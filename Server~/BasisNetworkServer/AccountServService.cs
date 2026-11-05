using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
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
                byte[] hash = DeriveHash(password, salt);
                Accounts.Add(key, new AccountRecord
                {
                    Name = accountName.Trim(),
                    OwnerUuid = authenticatedUuid.Trim(),
                    Salt = Convert.ToBase64String(salt),
                    PasswordHash = Convert.ToBase64String(hash),
                    CreatedUtc = DateTime.UtcNow
                });
                Save_NoLock();
                Sessions[authenticatedUuid] = NewSession(accountName);
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
                if (IsLocked_NoLock(authenticatedUuid)) { error = "Login temporarily locked. Try again later."; return false; }

                if (!Accounts.TryGetValue(NormalizeName(accountName), out AccountRecord account) ||
                    !string.Equals(account.OwnerUuid, authenticatedUuid.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    RecordFailure_NoLock(authenticatedUuid);
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

                byte[] actual = DeriveHash(password, salt);
                if (!CryptographicOperations.FixedTimeEquals(actual, expected))
                {
                    RecordFailure_NoLock(authenticatedUuid);
                    error = "Invalid account name or password.";
                    return false;
                }

                Failures.Remove(authenticatedUuid);
                Sessions[authenticatedUuid] = NewSession(account.Name);
                return true;
            }
        }

        public static bool IsIdentified(string authenticatedUuid)
        {
            if (!IsValidIdentity(authenticatedUuid)) return false;
            lock (Gate)
            {
                EnsureInitialized();
                if (!Sessions.TryGetValue(authenticatedUuid, out SessionRecord session))
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
                if (!PermissionIntegration.Manager.IsInGroup(actorUuid, "admin"))
                {
                    error = "Only existing Basis administrators can manage AccountServ roles.";
                    return false;
                }

                string normalizedRole = (role ?? "").Trim().ToLowerInvariant();
                if (normalizedRole == "admin")
                    PermissionIntegration.Manager.AddUserToGroup(targetUuid, "admin");
                else if (normalizedRole == "moderator")
                {
                    PermissionIntegration.Manager.RemoveUserFromGroup(targetUuid, "admin");
                    PermissionIntegration.Manager.AddUserToGroup(targetUuid, "moderator");
                }
                else if (normalizedRole == "none")
                {
                    PermissionIntegration.Manager.RemoveUserFromGroup(targetUuid, "admin");
                    PermissionIntegration.Manager.RemoveUserFromGroup(targetUuid, "moderator");
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

        private static byte[] DeriveHash(string password, byte[] salt)
        {
            using (var derive = new Rfc2898DeriveBytes(password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256))
                return derive.GetBytes(HashSize);
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
            public List<AccountRecord> Accounts { get; set; } = new List<AccountRecord>();
        }

        private sealed class AccountRecord
        {
            public string Name { get; set; }
            public string OwnerUuid { get; set; }
            public string Salt { get; set; }
            public string PasswordHash { get; set; }
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
    }
}
