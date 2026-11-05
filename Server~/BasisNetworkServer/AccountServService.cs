using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using BasisPermissions;

namespace BasisAccountServ
{
    /// <summary>
    /// Minimal account backend. Callers must supply the UUID obtained from Basis'
    /// authenticated peer identity API; never pass a UUID received from a client.
    /// </summary>
    public static class AccountServService
    {
        private const int SaltSize = 16;
        private const int HashSize = 32;
        private const int Pbkdf2Iterations = 210000;
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, AccountRecord> Accounts =
            new Dictionary<string, AccountRecord>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, SessionRecord> Sessions =
            new Dictionary<string, SessionRecord>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, FailureRecord> Failures =
            new Dictionary<string, FailureRecord>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, ChallengeRecord> Challenges =
            new Dictionary<string, ChallengeRecord>(StringComparer.OrdinalIgnoreCase);

        private static string _databasePath = "config/accountserv.db";
        private static int _minimumPasswordLength = 12;
        private static int _maximumFailedLogins = 5;
        private static int _lockoutSeconds = 300;
        private static int _sessionLifetimeMinutes = 1440;
        private static bool _initialized;

        public static void Initialize(
            string databasePath,
            int minimumPasswordLength = 12,
            int maximumFailedLogins = 5,
            int lockoutSeconds = 300,
            int sessionLifetimeMinutes = 1440)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
                throw new ArgumentException("Database path is required.", nameof(databasePath));

            lock (Gate)
            {
                _databasePath = databasePath;
                _minimumPasswordLength = Math.Max(8, minimumPasswordLength);
                _maximumFailedLogins = Math.Max(1, maximumFailedLogins);
                _lockoutSeconds = Math.Max(1, lockoutSeconds);
                _sessionLifetimeMinutes = Math.Max(1, sessionLifetimeMinutes);

                Accounts.Clear();
                Sessions.Clear();
                Failures.Clear();
                Challenges.Clear();

                string directory = Path.GetDirectoryName(_databasePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                if (File.Exists(_databasePath))
                {
                    string json = File.ReadAllText(_databasePath);
                    AccountDatabase database = JsonSerializer.Deserialize<AccountDatabase>(json);
                    if (database != null && database.Accounts != null)
                    {
                        foreach (AccountRecord account in database.Accounts)
                        {
                            if (account != null && !string.IsNullOrWhiteSpace(account.Name))
                                Accounts[NormalizeName(account.Name)] = account;
                        }
                    }
                }

                _initialized = true;
            }
        }

        public static bool Register(string authenticatedUuid, string accountName, string password, out string error)
        {
            error = null;
            if (!IsValidIdentity(authenticatedUuid)) { error = "A verified Basis identity is required."; return false; }
            if (!IsValidAccountName(accountName)) { error = "Account name must be 3-24 characters (letters, digits, underscore, or hyphen)."; return false; }
            if (password == null || password.Length < _minimumPasswordLength || password.Length > 256) { error = "Password length must be within the configured minimum and 256 characters."; return false; }

            lock (Gate)
            {
                EnsureInitialized();
                string key = NormalizeName(accountName);
                if (Accounts.ContainsKey(key)) { error = "Account name is already registered."; return false; }

                byte[] salt = new byte[SaltSize];
                using (RandomNumberGenerator random = RandomNumberGenerator.Create())
                    random.GetBytes(salt);
                DeriveCredentialKeys(password, salt, out byte[] storedKey, out _);
                Accounts.Add(key, new AccountRecord
                {
                    Name = accountName.Trim(),
                    OwnerUuid = authenticatedUuid.Trim(),
                    Salt = Convert.ToBase64String(salt),
                    PasswordHash = Convert.ToBase64String(storedKey),
                    CredentialVersion = 2,
                    CreatedUtc = DateTime.UtcNow
                });
                Save_NoLock();
                Sessions[authenticatedUuid.Trim()] = NewSession(accountName);
                return true;
            }
        }


        /// <summary>
        /// Registers a client-generated SCRAM-style stored key. The password itself never crosses
        /// the network; the client derives the stored key from its password and random salt.
        /// </summary>
        public static bool RegisterVerifier(
            string authenticatedUuid,
            string accountName,
            string saltBase64,
            string storedKeyBase64,
            int claimedPasswordLength,
            out string error)
        {
            error = null;
            if (!IsValidIdentity(authenticatedUuid)) { error = "A verified Basis identity is required."; return false; }
            if (!IsValidAccountName(accountName)) { error = "Account name must be 3-24 characters (letters, digits, underscore, or hyphen)."; return false; }
            if (claimedPasswordLength < _minimumPasswordLength || claimedPasswordLength > 256)
            {
                error = "Password length must be within the configured minimum and 256 characters.";
                return false;
            }

            byte[] salt;
            byte[] verifier;
            try
            {
                salt = Convert.FromBase64String(saltBase64 ?? string.Empty);
                verifier = Convert.FromBase64String(storedKeyBase64 ?? string.Empty);
            }
            catch
            {
                error = "Invalid registration verifier.";
                return false;
            }
            if (salt.Length != SaltSize || verifier.Length != HashSize)
            {
                error = "Invalid registration verifier.";
                return false;
            }

            lock (Gate)
            {
                EnsureInitialized();
                string key = NormalizeName(accountName);
                if (Accounts.ContainsKey(key)) { error = "Account name is already registered."; return false; }

                Accounts.Add(key, new AccountRecord
                {
                    Name = accountName.Trim(),
                    OwnerUuid = authenticatedUuid.Trim(),
                    Salt = Convert.ToBase64String(salt),
                    PasswordHash = Convert.ToBase64String(verifier),
                    CredentialVersion = 2,
                    CreatedUtc = DateTime.UtcNow
                });
                Save_NoLock();
                Sessions[authenticatedUuid.Trim()] = NewSession(accountName);
                return true;
            }
        }

        /// <summary>
        /// Starts a one-time challenge for password identification. The server returns only
        /// the stored salt and a random nonce; it never asks the client to transmit its password.
        /// </summary>
        public static bool BeginIdentifyChallenge(
            string authenticatedUuid,
            string accountName,
            out string saltBase64,
            out string nonceBase64,
            out string error)
        {
            saltBase64 = null;
            nonceBase64 = null;
            error = null;
            if (!IsValidIdentity(authenticatedUuid) || string.IsNullOrWhiteSpace(accountName))
            {
                error = "Invalid account or password.";
                return false;
            }

            lock (Gate)
            {
                EnsureInitialized();
                string uuid = authenticatedUuid.Trim();
                if (IsLocked_NoLock(uuid))
                {
                    error = "Login temporarily locked. Try again later.";
                    return false;
                }

                if (!Accounts.TryGetValue(NormalizeName(accountName), out AccountRecord account) ||
                    !string.Equals(account.OwnerUuid, uuid, StringComparison.OrdinalIgnoreCase))
                {
                    RecordFailure_NoLock(uuid);
                    error = "Invalid account or password.";
                    return false;
                }

                if (!UpgradeLegacyCredential_NoLock(account))
                {
                    error = "Stored account credentials are invalid. Contact the server administrator.";
                    return false;
                }

                byte[] nonce = new byte[32];
                using (RandomNumberGenerator random = RandomNumberGenerator.Create())
                    random.GetBytes(nonce);

                nonceBase64 = Convert.ToBase64String(nonce);
                saltBase64 = account.Salt;
                Challenges[uuid] = new ChallengeRecord
                {
                    AccountName = NormalizeName(accountName),
                    Nonce = nonceBase64,
                    ExpiresUtc = DateTime.UtcNow.AddMinutes(1)
                };
                return true;
            }
        }

        /// <summary>
        /// Verifies a SCRAM-style client proof derived from the client's PBKDF2 credential. Each challenge is
        /// single-use and expires after one minute.
        /// </summary>
        public static bool CompleteIdentifyChallenge(
            string authenticatedUuid,
            string accountName,
            string nonceBase64,
            string proofBase64,
            out string error)
        {
            error = null;
            if (!IsValidIdentity(authenticatedUuid) || string.IsNullOrWhiteSpace(accountName))
            {
                error = "Invalid account or password.";
                return false;
            }

            lock (Gate)
            {
                EnsureInitialized();
                string uuid = authenticatedUuid.Trim();
                if (IsLocked_NoLock(uuid))
                {
                    Challenges.Remove(uuid);
                    error = "Login temporarily locked. Try again later.";
                    return false;
                }

                if (!Challenges.TryGetValue(uuid, out ChallengeRecord challenge))
                {
                    error = "Login challenge expired. Please try again.";
                    return false;
                }
                Challenges.Remove(uuid);

                if (challenge.ExpiresUtc <= DateTime.UtcNow ||
                    !string.Equals(challenge.AccountName, NormalizeName(accountName), StringComparison.Ordinal) ||
                    !string.Equals(challenge.Nonce, nonceBase64, StringComparison.Ordinal) ||
                    !Accounts.TryGetValue(challenge.AccountName, out AccountRecord account) ||
                    !string.Equals(account.OwnerUuid, uuid, StringComparison.OrdinalIgnoreCase))
                {
                    RecordFailure_NoLock(uuid);
                    error = "Invalid account or password.";
                    return false;
                }

                byte[] storedKey;
                byte[] suppliedProof;
                byte[] nonce;
                try
                {
                    storedKey = Convert.FromBase64String(account.PasswordHash);
                    suppliedProof = Convert.FromBase64String(proofBase64 ?? string.Empty);
                    nonce = Convert.FromBase64String(nonceBase64 ?? string.Empty);
                }
                catch
                {
                    RecordFailure_NoLock(uuid);
                    error = "Invalid account or password.";
                    return false;
                }

                if (storedKey.Length != HashSize || suppliedProof.Length != HashSize || nonce.Length != 32)
                {
                    RecordFailure_NoLock(uuid);
                    error = "Invalid account or password.";
                    return false;
                }

                byte[] proofInput = Encoding.UTF8.GetBytes(challenge.AccountName + "\\n" + challenge.Nonce);
                byte[] signature;
                using (var hmac = new HMACSHA256(storedKey))
                    signature = hmac.ComputeHash(proofInput);

                byte[] clientKey = new byte[HashSize];
                for (int i = 0; i < HashSize; i++)
                    clientKey[i] = (byte)(suppliedProof[i] ^ signature[i]);

                byte[] computedStoredKey;
                using (SHA256 sha = SHA256.Create())
                    computedStoredKey = sha.ComputeHash(clientKey);

                if (!CryptographicOperations.FixedTimeEquals(computedStoredKey, storedKey))
                {
                    RecordFailure_NoLock(uuid);
                    error = "Invalid account or password.";
                    return false;
                }

                Failures.Remove(uuid);
                Sessions[uuid] = NewSession(account.Name);
                return true;
            }
        }

        public static bool Identify(string authenticatedUuid, string accountName, string password, out string error)
        {
            error = null;
            if (!IsValidIdentity(authenticatedUuid)) { error = "A verified Basis identity is required."; return false; }
            if (string.IsNullOrWhiteSpace(accountName) || password == null) { error = "Account name and password are required."; return false; }

            lock (Gate)
            {
                EnsureInitialized();
                if (IsLocked_NoLock(authenticatedUuid.Trim())) { error = "Login temporarily locked. Try again later."; return false; }

                if (!Accounts.TryGetValue(NormalizeName(accountName), out AccountRecord account) ||
                    !string.Equals(account.OwnerUuid, authenticatedUuid.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    RecordFailure_NoLock(authenticatedUuid.Trim());
                    error = "Invalid account name or password.";
                    return false;
                }

                byte[] salt;
                byte[] expected;
                try
                {
                    salt = Convert.FromBase64String(account.Salt);
                    expected = Convert.FromBase64String(account.PasswordHash);
                }
                catch
                {
                    error = "Stored account credentials are invalid.";
                    return false;
                }

                byte[] actual;
                if (account.CredentialVersion < 2)
                    actual = DeriveHash(password, salt);
                else
                    DeriveCredentialKeys(password, salt, out actual, out _);

                if (!CryptographicOperations.FixedTimeEquals(actual, expected))
                {
                    RecordFailure_NoLock(authenticatedUuid);
                    error = "Invalid account name or password.";
                    return false;
                }

                if (account.CredentialVersion < 2)
                {
                    DeriveCredentialKeys(password, salt, out byte[] upgradedStoredKey, out _);
                    account.PasswordHash = Convert.ToBase64String(upgradedStoredKey);
                    account.CredentialVersion = 2;
                    Save_NoLock();
                }

                Failures.Remove(authenticatedUuid);
                Sessions[authenticatedUuid.Trim()] = NewSession(account.Name);
                return true;
            }
        }

        public static bool IsIdentified(string authenticatedUuid)
        {
            if (!IsValidIdentity(authenticatedUuid)) return false;
            lock (Gate)
            {
                EnsureInitialized();
                if (!Sessions.TryGetValue(authenticatedUuid.Trim(), out SessionRecord session))
                    return false;
                if (session.ExpiresUtc <= DateTime.UtcNow)
                {
                    Sessions.Remove(authenticatedUuid);
                    return false;
                }
                return true;
            }
        }

        public static string GetIdentifiedAccount(string authenticatedUuid)
        {
            if (!IsValidIdentity(authenticatedUuid)) return null;
            lock (Gate)
            {
                if (!Sessions.TryGetValue(authenticatedUuid, out SessionRecord session))
                    return null;
                if (session.ExpiresUtc <= DateTime.UtcNow)
                {
                    Sessions.Remove(authenticatedUuid);
                    return null;
                }
                return session.AccountName;
            }
        }

        public static void Logout(string authenticatedUuid)
        {
            if (string.IsNullOrWhiteSpace(authenticatedUuid)) return;
            lock (Gate) Sessions.Remove(authenticatedUuid.Trim());
        }

        /// <summary>
        /// Operator role changes are only allowed to a UUID already in Basis' admin group.
        /// Roles are stored in Basis' existing permission manager, not a parallel ACL.
        /// </summary>
        public static bool SetOperatorRole(string actorUuid, string targetUuid, string role, out string error)
        {
            error = null;
            if (!IsValidIdentity(actorUuid) || !IsValidIdentity(targetUuid))
            {
                error = "Verified actor and target identities are required.";
                return false;
            }

            lock (Gate)
            {
                EnsureInitialized();
                if (!PermissionManager.PermissionIntegration.Manager.IsInGroup(actorUuid, "admin"))
                {
                    error = "Only existing Basis administrators can manage AccountServ roles.";
                    return false;
                }

                string normalizedRole = (role ?? "").Trim().ToLowerInvariant();
                if (normalizedRole == "admin")
                    PermissionManager.PermissionIntegration.Manager.AddUserToGroup(targetUuid, "admin");
                else if (normalizedRole == "moderator")
                {
                    PermissionManager.PermissionIntegration.Manager.RemoveUserFromGroup(targetUuid, "admin");
                    PermissionManager.PermissionIntegration.Manager.AddUserToGroup(targetUuid, "moderator");
                }
                else if (normalizedRole == "none")
                {
                    PermissionManager.PermissionIntegration.Manager.RemoveUserFromGroup(targetUuid, "admin");
                    PermissionManager.PermissionIntegration.Manager.RemoveUserFromGroup(targetUuid, "moderator");
                }
                else
                {
                    error = "Role must be admin, moderator, or none.";
                    return false;
                }
                return true;
            }
        }

        public static IReadOnlyCollection<string> ListAccounts()
        {
            lock (Gate)
            {
                EnsureInitialized();
                return Accounts.Values.Select(a => a.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
            }
        }

        private static SessionRecord NewSession(string accountName) => new SessionRecord
        {
            AccountName = accountName,
            ExpiresUtc = DateTime.UtcNow.AddMinutes(_sessionLifetimeMinutes)
        };

        private static bool UpgradeLegacyCredential_NoLock(AccountRecord account)
        {
            if (account.CredentialVersion >= 2) return true;
            try
            {
                byte[] legacyPbkdf2 = Convert.FromBase64String(account.PasswordHash ?? string.Empty);
                if (legacyPbkdf2.Length != HashSize) return false;
                byte[] clientKey;
                using (var hmac = new HMACSHA256(legacyPbkdf2))
                    clientKey = hmac.ComputeHash(Encoding.UTF8.GetBytes("Client Key"));
                byte[] storedKey;
                using (SHA256 sha = SHA256.Create())
                    storedKey = sha.ComputeHash(clientKey);
                account.PasswordHash = Convert.ToBase64String(storedKey);
                account.CredentialVersion = 2;
                Save_NoLock();
                Array.Clear(clientKey, 0, clientKey.Length);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static byte[] DeriveHash(string password, byte[] salt)
        {
            using (var derive = new Rfc2898DeriveBytes(password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256))
                return derive.GetBytes(HashSize);
        }

        private static void DeriveCredentialKeys(string password, byte[] salt, out byte[] storedKey, out byte[] clientKey)
        {
            byte[] saltedPassword = DeriveHash(password, salt);
            using (var hmac = new HMACSHA256(saltedPassword))
                clientKey = hmac.ComputeHash(Encoding.UTF8.GetBytes("Client Key"));
            using (SHA256 sha = SHA256.Create())
                storedKey = sha.ComputeHash(clientKey);
            Array.Clear(saltedPassword, 0, saltedPassword.Length);
        }

        private static bool IsLocked_NoLock(string uuid)
        {
            if (!Failures.TryGetValue(uuid, out FailureRecord failure)) return false;
            if (failure.LockedUntilUtc <= DateTime.UtcNow)
            {
                Failures.Remove(uuid);
                return false;
            }
            return true;
        }

        private static void RecordFailure_NoLock(string uuid)
        {
            if (!Failures.TryGetValue(uuid, out FailureRecord failure))
                failure = new FailureRecord();
            failure.Count++;
            if (failure.Count >= _maximumFailedLogins)
                failure.LockedUntilUtc = DateTime.UtcNow.AddSeconds(_lockoutSeconds);
            Failures[uuid] = failure;
        }

        private static void Save_NoLock()
        {
            var database = new AccountDatabase { Accounts = Accounts.Values.ToList() };
            string json = JsonSerializer.Serialize(database, new JsonSerializerOptions { WriteIndented = true });
            string temporaryPath = _databasePath + ".tmp";
            File.WriteAllText(temporaryPath, json);
            if (File.Exists(_databasePath)) File.Delete(_databasePath);
            File.Move(temporaryPath, _databasePath);
        }

        private static void EnsureInitialized()
        {
            if (!_initialized) throw new InvalidOperationException("AccountServService.Initialize must be called first.");
        }

        private static bool IsValidIdentity(string uuid) => !string.IsNullOrWhiteSpace(uuid) && uuid.Trim().Length <= 128;

        private static bool IsValidAccountName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            name = name.Trim();
            if (name.Length < 3 || name.Length > 24) return false;
            foreach (char c in name)
            {
                if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-')) return false;
            }
            return true;
        }

        private static string NormalizeName(string name) => name.Trim().ToLowerInvariant();

        private sealed class AccountDatabase
        {
            public AccountDatabase() { }
            public List<AccountRecord> Accounts { get; set; } = new List<AccountRecord>();
        }

        private sealed class AccountRecord
        {
            public AccountRecord() { }
            public string Name { get; set; }
            public string OwnerUuid { get; set; }
            public string Salt { get; set; }
            public string PasswordHash { get; set; }
            public int CredentialVersion { get; set; }
            public DateTime CreatedUtc { get; set; }
        }

        private sealed class SessionRecord
        {
            public string AccountName { get; set; }
            public DateTime ExpiresUtc { get; set; }
        }

        private sealed class FailureRecord
        {
            public int Count { get; set; }
            public DateTime LockedUntilUtc { get; set; }
        }

        private sealed class ChallengeRecord
        {
            public string AccountName { get; set; }
            public string Nonce { get; set; }
            public DateTime ExpiresUtc { get; set; }
        }
    }
}
