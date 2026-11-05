using System;
using System.Security.Cryptography;
using System.Text;
using Basis.Network.Core;
using Basis.Scripts.Networking;

[assembly: BasisNetworkPackage(typeof(BasisAccountServ.AccountServClientPackage))]

namespace BasisAccountServ
{
    internal static class AccountServClientProtocol
    {
        public const string MessageName = "com.nodokaha.accountserv.dm";
        public const byte RequestRegister = 1;
        public const byte RequestIdentify = 2;
        public const byte RequestLogout = 3;
        public const byte RequestStatus = 4;
        public const byte RequestSetRole = 5;
        public const byte RequestCompleteIdentify = 6;
        public const byte ResponseMessage = 128;
        public const byte ResponseIdentifyChallenge = 129;
        public const int MaximumResponseBytes = 1024;
        public const int Pbkdf2Iterations = 210000;
        public const int SaltSize = 16;
        public const int VerifierSize = 32;
        public const int MinimumPasswordLength = 12;
    }

    /// <summary>
    /// Client half of the AccountServ private server-message channel.
    /// Passwords are processed locally; only a salted verifier or a one-time HMAC proof
    /// is sent to the server. This never hooks or modifies ordinary Basis chat.
    /// </summary>
    public sealed class AccountServClientPackage : IBasisNetworkPackage
    {
        public void Initialize()
        {
            BasisClientMessageRegistry.RegisterClientPlugin(
                AccountServClientProtocol.MessageName,
                AccountServClient.HandleServerMessage);
        }
    }

    public static class AccountServClient
    {
        private static readonly object PendingIdentifyLock = new object();
        private static string pendingAccountName;
        private static string pendingPassword;

        public static event Action<string> ResponseReceived;

        public static bool IsAvailable =>
            BasisClientMessageRegistry.IsPluginSupplied(AccountServClientProtocol.MessageName);

        public static bool Register(string accountName, string password)
        {
            if (password == null || password.Length < AccountServClientProtocol.MinimumPasswordLength || password.Length > 256)
            {
                ResponseReceived?.Invoke("Registration requires a password of 12-256 characters.");
                return false;
            }

            byte[] salt = new byte[AccountServClientProtocol.SaltSize];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create())
                random.GetBytes(salt);
            byte[] verifier = DeriveVerifier(password, salt);

            return Send(AccountServClientProtocol.RequestRegister, writer =>
            {
                writer.Put(accountName ?? string.Empty);
                writer.Put(Convert.ToBase64String(salt));
                writer.Put(Convert.ToBase64String(verifier));
                writer.Put(password.Length);
            });
        }

        public static bool Identify(string accountName, string password)
        {
            if (string.IsNullOrWhiteSpace(accountName) || string.IsNullOrEmpty(password))
            {
                ResponseReceived?.Invoke("Account name and password are required.");
                return false;
            }

            lock (PendingIdentifyLock)
            {
                pendingAccountName = accountName;
                pendingPassword = password;
            }

            bool sent = Send(AccountServClientProtocol.RequestIdentify, writer =>
                writer.Put(accountName));
            if (!sent)
            {
                ClearPendingIdentify();
                return false;
            }
            return true;
        }

        public static bool Logout() =>
            Send(AccountServClientProtocol.RequestLogout, null);

        public static bool GetStatus() =>
            Send(AccountServClientProtocol.RequestStatus, null);

        public static bool SetRole(string targetUuid, string role) =>
            Send(AccountServClientProtocol.RequestSetRole, writer =>
            {
                writer.Put(targetUuid ?? string.Empty);
                writer.Put(role ?? string.Empty);
            });

        private static bool Send(byte operation, Action<NetDataWriter> write)
        {
            return BasisClientMessageRegistry.Send(AccountServClientProtocol.MessageName, writer =>
            {
                writer.Put(operation);
                write?.Invoke(writer);
            });
        }

        internal static void HandleServerMessage(
            NetPeer peer,
            NetPacketReader reader,
            byte channel,
            DeliveryMethod deliveryMethod)
        {
            if (!reader.TryGetByte(out byte operation) ||
                reader.AvailableBytes > AccountServClientProtocol.MaximumResponseBytes)
                return;

            if (operation == AccountServClientProtocol.ResponseIdentifyChallenge)
            {
                HandleIdentifyChallenge(reader);
                return;
            }

            if (operation != AccountServClientProtocol.ResponseMessage)
                return;

            string response;
            try
            {
                response = reader.GetString();
            }
            catch
            {
                return;
            }

            ClearPendingIdentify();
            if (!string.IsNullOrWhiteSpace(response))
                ResponseReceived?.Invoke(response);
        }

        private static void HandleIdentifyChallenge(NetPacketReader reader)
        {
            try
            {
                string accountName = reader.GetString();
                string saltBase64 = reader.GetString();
                string nonceBase64 = reader.GetString();
                if (reader.AvailableBytes != 0)
                {
                    ClearPendingIdentify();
                    return;
                }

                string password;
                string expectedAccount;
                lock (PendingIdentifyLock)
                {
                    password = pendingPassword;
                    expectedAccount = pendingAccountName;
                }

                if (string.IsNullOrEmpty(password) ||
                    !string.Equals(accountName, expectedAccount, StringComparison.OrdinalIgnoreCase))
                {
                    ClearPendingIdentify();
                    ResponseReceived?.Invoke("Identify challenge did not match the pending request.");
                    return;
                }

                byte[] salt = Convert.FromBase64String(saltBase64);
                byte[] nonce = Convert.FromBase64String(nonceBase64);
                if (salt.Length != AccountServClientProtocol.SaltSize || nonce.Length != 32)
                {
                    ClearPendingIdentify();
                    ResponseReceived?.Invoke("Server returned an invalid identify challenge.");
                    return;
                }

                byte[] verifier = DeriveVerifier(password, salt);
                string normalizedAccount = accountName.Trim().ToLowerInvariant();
                byte[] proofInput = Encoding.UTF8.GetBytes(normalizedAccount + "\\n" + nonceBase64);
                byte[] proof;
                using (var hmac = new HMACSHA256(verifier))
                    proof = hmac.ComputeHash(proofInput);

                ClearPendingIdentify();
                Send(AccountServClientProtocol.RequestCompleteIdentify, writer =>
                {
                    writer.Put(accountName);
                    writer.Put(nonceBase64);
                    writer.Put(Convert.ToBase64String(proof));
                });
            }
            catch
            {
                ClearPendingIdentify();
                ResponseReceived?.Invoke("Unable to complete the identify challenge.");
            }
        }

        private static byte[] DeriveVerifier(string password, byte[] salt)
        {
            using (var derive = new Rfc2898DeriveBytes(
                password,
                salt,
                AccountServClientProtocol.Pbkdf2Iterations,
                HashAlgorithmName.SHA256))
            {
                return derive.GetBytes(AccountServClientProtocol.VerifierSize);
            }
        }

        private static void ClearPendingIdentify()
        {
            lock (PendingIdentifyLock)
            {
                pendingPassword = null;
                pendingAccountName = null;
            }
        }
    }
}
