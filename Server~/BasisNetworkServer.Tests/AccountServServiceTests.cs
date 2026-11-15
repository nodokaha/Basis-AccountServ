using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BasisAccountServ;
using BasisPermissions;
using Xunit;

namespace BasisAccountServ.Server.Tests
{
    /// <summary>
    /// The service is a static class with global state, so every test re-Initializes it against a
    /// fresh temporary database. All tests live in one class so xunit runs them serially.
    /// </summary>
    public sealed class AccountServServiceTests : IDisposable
    {
        private const string Alice = "did:key:z6MkAliceAliceAliceAliceAliceAliceAliceAlice1";
        private const string Bob = "did:key:z6MkBobBobBobBobBobBobBobBobBobBobBobBobBob2";
        private const string GoodPassword = "correct horse battery staple";

        private readonly string _directory;
        private readonly string _database;

        public AccountServServiceTests()
        {
            _directory = Path.Combine(Path.GetTempPath(), "accountserv-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            _database = Path.Combine(_directory, "accountserv.db");
            PermissionManager.PermissionIntegration.Manager = new FakePermissionManager();
            AccountServRateLimiter.Reset();
            AccountServService.Initialize(_database);
        }

        public void Dispose()
        {
            try { Directory.Delete(_directory, true); } catch { }
        }

        // ---- client-side derivation, mirroring AccountServClient.DeriveCredentialKeys / proof ----

        private static void Derive(string password, byte[] salt, out byte[] storedKey, out byte[] clientKey)
        {
            byte[] salted = Rfc2898DeriveBytes.Pbkdf2(password, salt, 210000, HashAlgorithmName.SHA256, 32);
            using (var hmac = new HMACSHA256(salted))
                clientKey = hmac.ComputeHash(Encoding.UTF8.GetBytes("Client Key"));
            storedKey = SHA256.HashData(clientKey);
        }

        private static string Proof(string password, string saltBase64, string account, string nonceBase64)
        {
            Derive(password, Convert.FromBase64String(saltBase64), out byte[] storedKey, out byte[] clientKey);
            byte[] input = Encoding.UTF8.GetBytes(account.Trim().ToLowerInvariant() + AccountServService.ProofSeparator + nonceBase64);
            byte[] signature;
            using (var hmac = new HMACSHA256(storedKey))
                signature = hmac.ComputeHash(input);
            byte[] proof = new byte[32];
            for (int i = 0; i < 32; i++) proof[i] = (byte)(clientKey[i] ^ signature[i]);
            return Convert.ToBase64String(proof);
        }

        private static bool Register(string uuid, string account, string password, out string error)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            Derive(password, salt, out byte[] storedKey, out _);
            return AccountServService.RegisterVerifier(
                uuid, account, Convert.ToBase64String(salt), Convert.ToBase64String(storedKey),
                password.Length, out error);
        }

        private static bool Login(string uuid, string account, string password, out string error)
        {
            if (!AccountServService.BeginIdentifyChallenge(uuid, account, out string salt, out string nonce, out error))
                return false;
            return AccountServService.CompleteIdentifyChallenge(
                uuid, account, nonce, Proof(password, salt, account, nonce), out error);
        }

        // ---- protocol ----

        [Fact]
        public void ProofSeparatorIsBackslashAndLetterN()
        {
            // The Unity client uses the identical literal; a real newline here would break login.
            Assert.Equal(2, AccountServService.ProofSeparator.Length);
            Assert.Equal('\\', AccountServService.ProofSeparator[0]);
            Assert.Equal('n', AccountServService.ProofSeparator[1]);
        }

        [Fact]
        public void RegisterThenChallengeLoginRoundTrips()
        {
            Assert.True(Register(Alice, "alice_01", GoodPassword, out string error), error);
            Assert.True(AccountServService.IsIdentified(Alice));

            AccountServService.Logout(Alice);
            Assert.False(AccountServService.IsIdentified(Alice));

            Assert.True(Login(Alice, "Alice_01", GoodPassword, out error), error);
            Assert.Equal("alice_01", AccountServService.GetIdentifiedAccount(Alice));
        }

        [Fact]
        public void WrongPasswordIsRejected()
        {
            Assert.True(Register(Alice, "alice_01", GoodPassword, out _));
            AccountServService.Logout(Alice);

            Assert.False(Login(Alice, "alice_01", "another password entirely", out string error));
            Assert.Contains("Invalid", error);
            Assert.False(AccountServService.IsIdentified(Alice));
        }

        [Fact]
        public void ChallengeCannotBeReplayed()
        {
            Assert.True(Register(Alice, "alice_01", GoodPassword, out _));
            AccountServService.Logout(Alice);

            Assert.True(AccountServService.BeginIdentifyChallenge(Alice, "alice_01", out string salt, out string nonce, out _));
            string proof = Proof(GoodPassword, salt, "alice_01", nonce);
            Assert.True(AccountServService.CompleteIdentifyChallenge(Alice, "alice_01", nonce, proof, out _));

            AccountServService.Logout(Alice);
            Assert.False(AccountServService.CompleteIdentifyChallenge(Alice, "alice_01", nonce, proof, out _));
            Assert.False(AccountServService.IsIdentified(Alice));
        }

        [Fact]
        public void AccountCannotBeUsedFromAnotherIdentity()
        {
            Assert.True(Register(Alice, "alice_01", GoodPassword, out _));
            Assert.False(AccountServService.BeginIdentifyChallenge(Bob, "alice_01", out _, out _, out _));
        }

        [Fact]
        public void RepeatedFailuresLockTheIdentityOut()
        {
            AccountServService.Initialize(_database, maximumFailedLogins: 3, lockoutSeconds: 300);
            Assert.True(Register(Alice, "alice_01", GoodPassword, out _));
            AccountServService.Logout(Alice);

            for (int i = 0; i < 3; i++)
                Assert.False(Login(Alice, "alice_01", "wrong password " + i, out _));

            Assert.False(Login(Alice, "alice_01", GoodPassword, out string error));
            Assert.Contains("locked", error, StringComparison.OrdinalIgnoreCase);
        }

        // ---- account policy ----

        [Theory]
        [InlineData("ab")]                       // too short
        [InlineData("has space")]
        [InlineData("semi;colon")]
        [InlineData("\u0410dmin01")]             // Cyrillic A: look-alike of Latin A
        [InlineData("\u65e5\u672c\u8a9e\u540d")] // non-ASCII letters
        public void InvalidOrLookAlikeNamesAreRejected(string name)
        {
            Assert.False(Register(Alice, name, GoodPassword, out string error));
            Assert.Contains("ASCII", error);
        }

        [Fact]
        public void OneIdentityCannotRegisterUnlimitedAccounts()
        {
            Assert.True(Register(Alice, "alice_01", GoodPassword, out _));
            Assert.False(Register(Alice, "alice_02", GoodPassword, out string error));
            Assert.Contains("maximum", error);
            Assert.True(Register(Bob, "bob_01", GoodPassword, out _));
            Assert.Single(AccountServService.ListAccounts().Where(n => n.StartsWith("alice")));
        }

        [Fact]
        public void AccountLimitIsConfigurable()
        {
            AccountServService.Initialize(_database, maximumAccountsPerIdentity: 2);
            Assert.True(Register(Alice, "alice_01", GoodPassword, out _));
            Assert.True(Register(Alice, "alice_02", GoodPassword, out _));
            Assert.False(Register(Alice, "alice_03", GoodPassword, out _));
        }

        [Fact]
        public void DuplicateNamesAreCaseInsensitive()
        {
            Assert.True(Register(Alice, "Alice_01", GoodPassword, out _));
            Assert.False(Register(Bob, "ALICE_01", GoodPassword, out string error));
            Assert.Contains("already", error);
        }

        // ---- persistence ----

        [Fact]
        public void AccountsSurviveRestartButSessionsDoNot()
        {
            Assert.True(Register(Alice, "alice_01", GoodPassword, out _));
            Assert.True(AccountServService.IsIdentified(Alice));

            AccountServService.Initialize(_database); // simulates a server restart
            Assert.False(AccountServService.IsIdentified(Alice));
            Assert.True(Login(Alice, "alice_01", GoodPassword, out string error), error);
            Assert.False(File.Exists(_database + ".tmp"));
        }

        [Fact]
        public void LegacyPbkdf2OnlyAccountsAreUpgradedOnFirstChallenge()
        {
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            byte[] legacyHash = Rfc2898DeriveBytes.Pbkdf2(GoodPassword, salt, 210000, HashAlgorithmName.SHA256, 32);
            string json = "{\"Accounts\":[{\"Name\":\"legacy_01\",\"OwnerUuid\":\"" + Alice + "\"," +
                          "\"Salt\":\"" + Convert.ToBase64String(salt) + "\"," +
                          "\"PasswordHash\":\"" + Convert.ToBase64String(legacyHash) + "\"," +
                          "\"CredentialVersion\":1,\"CreatedUtc\":\"2025-01-01T00:00:00Z\"}]}";
            File.WriteAllText(_database, json);
            AccountServService.Initialize(_database);

            Assert.True(Login(Alice, "legacy_01", GoodPassword, out string error), error);
            Assert.Contains("\"CredentialVersion\": 2", File.ReadAllText(_database));
        }

        // ---- operator roles ----

        [Fact]
        public void NonAdminsCannotChangeRoles()
        {
            Assert.False(AccountServService.SetOperatorRole(Alice, Bob, "moderator", out string error));
            Assert.Contains("administrators", error, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void AdminCanAssignRolesToAnotherIdentity()
        {
            var manager = PermissionManager.PermissionIntegration.Manager;
            manager.AddUserToGroup(Alice, "admin");

            Assert.True(AccountServService.SetOperatorRole(Alice, Bob, "moderator", out string error), error);
            Assert.True(manager.IsInGroup(Bob, "moderator"));

            Assert.True(AccountServService.SetOperatorRole(Alice, Bob, "admin", out error), error);
            Assert.True(manager.IsInGroup(Bob, "admin"));

            Assert.True(AccountServService.SetOperatorRole(Alice, Bob, "none", out error), error);
            Assert.False(manager.IsInGroup(Bob, "admin"));
            Assert.False(manager.IsInGroup(Bob, "moderator"));
        }

        [Fact]
        public void AdminCannotRemoveTheirOwnAdminRole()
        {
            var manager = PermissionManager.PermissionIntegration.Manager;
            manager.AddUserToGroup(Alice, "admin");

            Assert.False(AccountServService.SetOperatorRole(Alice, Alice, "none", out string error));
            Assert.Contains("own admin", error);
            Assert.False(AccountServService.SetOperatorRole(Alice, Alice, "moderator", out _));
            Assert.True(manager.IsInGroup(Alice, "admin"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("has space")]
        [InlineData("semi;colon")]
        [InlineData("new\nline")]
        public void MalformedRoleTargetsAreRejected(string target)
        {
            PermissionManager.PermissionIntegration.Manager.AddUserToGroup(Alice, "admin");
            Assert.False(AccountServService.SetOperatorRole(Alice, target, "moderator", out _));
        }

        [Fact]
        public void UnknownRolesAreRejected()
        {
            PermissionManager.PermissionIntegration.Manager.AddUserToGroup(Alice, "admin");
            Assert.False(AccountServService.SetOperatorRole(Alice, Bob, "superuser", out string error));
            Assert.Contains("admin, moderator, or none", error);
        }

        // ---- rate limiter ----

        [Fact]
        public void RateLimiterBlocksBurstsAndRecoversAfterTheWindow()
        {
            DateTime now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            for (int i = 0; i < AccountServRateLimiter.MaximumRequestsPerWindow; i++)
                Assert.True(AccountServRateLimiter.TryAcquire(Alice, now));

            Assert.False(AccountServRateLimiter.TryAcquire(Alice, now));
            Assert.True(AccountServRateLimiter.TryAcquire(Bob, now)); // per identity

            Assert.True(AccountServRateLimiter.TryAcquire(Alice, now + AccountServRateLimiter.Window + TimeSpan.FromSeconds(1)));
        }
    }
}
