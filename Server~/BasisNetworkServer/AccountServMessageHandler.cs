using System;
using Basis.Network.Core;
using BasisNetworkServer;

namespace BasisAccountServ
{
    /// <summary>
    /// Server-side private request/response endpoint for the AccountServ client panel.
    /// The peer identity is always resolved from Basis authentication; no client-supplied
    /// UUID is accepted. Ordinary ChatChannel traffic is not inspected or modified.
    /// </summary>
    public static class AccountServMessageHandler
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
        private const int MaximumRequestBytes = 1024;

        private static bool _registered;

        public static void Register()
        {
            if (_registered) return;
            BasisServerMessageRegistry.RegisterServerPlugin(
                MessageName,
                DeliveryMethod.ReliableOrdered,
                Handle);
            _registered = true;
            BNL.Log("[AccountServ] Private DM endpoint registered.");
        }

        private static void Handle(
            NetPeer peer,
            NetPacketReader reader,
            byte channel,
            DeliveryMethod deliveryMethod)
        {
            try
            {
                if (peer == null || reader.AvailableBytes < 1 ||
                    reader.AvailableBytes > MaximumRequestBytes)
                    return;

                if (NetworkServer.AuthIdentity == null ||
                    !NetworkServer.AuthIdentity.NetIDToUUID(peer, out string uuid) ||
                    string.IsNullOrWhiteSpace(uuid))
                {
                    Reply(peer, "AccountServ: authenticated Basis identity required.");
                    return;
                }

                byte operation = reader.GetByte();
                switch (operation)
                {
                    case RequestRegister:
                    {
                        if (!TryReadRegistration(reader, out string account, out string salt, out string verifier, out int passwordLength))
                        {
                            Reply(peer, "Invalid registration request.");
                            break;
                        }
                        bool registered = AccountServService.RegisterVerifier(
                            uuid, account, salt, verifier, passwordLength, out string error);
                        Reply(peer, registered
                            ? "Account registered and identified as " + account.Trim() + "."
                            : "Registration failed: " + error);
                        break;
                    }

                    case RequestIdentify:
                    {
                        if (!TryReadOneString(reader, out string account))
                        {
                            Reply(peer, "Invalid identify request.");
                            break;
                        }
                        if (AccountServService.BeginIdentifyChallenge(
                            uuid, account, out string salt, out string nonce, out string error))
                        {
                            ReplyIdentifyChallenge(peer, account.Trim(), salt, nonce);
                        }
                        else
                        {
                            Reply(peer, "Identify failed: " + error);
                        }
                        break;
                    }

                    case RequestCompleteIdentify:
                    {
                        if (!TryReadThreeStrings(reader, out string account, out string nonce, out string proof))
                        {
                            Reply(peer, "Invalid identify proof.");
                            break;
                        }
                        bool identified = AccountServService.CompleteIdentifyChallenge(
                            uuid, account, nonce, proof, out string error);
                        Reply(peer, identified
                            ? "Identified as " + AccountServService.GetIdentifiedAccount(uuid) + "."
                            : "Identify failed: " + error);
                        break;
                    }

                    case RequestLogout:
                        Reply(peer, RequireNoTrailingData(reader)
                            ? Logout(uuid)
                            : "Invalid logout request.");
                        break;

                    case RequestStatus:
                        Reply(peer, RequireNoTrailingData(reader)
                            ? Status(uuid)
                            : "Invalid status request.");
                        break;

                    case RequestSetRole:
                    {
                        if (!TryReadTwoStrings(reader, out string targetUuid, out string role))
                        {
                            Reply(peer, "Invalid role request.");
                            break;
                        }
                        bool changed = AccountServService.SetOperatorRole(
                            uuid, targetUuid, role, out string error);
                        Reply(peer, changed
                            ? "Role for " + targetUuid.Trim() + " set to " + role.Trim().ToLowerInvariant() + "."
                            : "Role change failed: " + error);
                        break;
                    }

                    default:
                        Reply(peer, "Unknown AccountServ request.");
                        break;
                }
            }
            catch (Exception exception)
            {
                BNL.LogError("[AccountServ] Private request failed: " + exception.Message);
                Reply(peer, "AccountServ: request failed due to a server error.");
            }
            finally
            {
                reader.Recycle();
            }
        }

        private static bool TryReadOneString(NetPacketReader reader, out string first)
        {
            first = null;
            try
            {
                if (reader.AvailableBytes < 2) return false;
                first = reader.GetString();
                return reader.AvailableBytes == 0 && first != null && first.Length <= 256;
            }
            catch { return false; }
        }

        private static bool TryReadTwoStrings(NetPacketReader reader, out string first, out string second)
        {
            first = null;
            second = null;
            try
            {
                if (reader.AvailableBytes < 4) return false;
                first = reader.GetString();
                second = reader.GetString();
                return reader.AvailableBytes == 0 &&
                    first != null && second != null &&
                    first.Length <= 256 && second.Length <= 256;
            }
            catch { return false; }
        }

        private static bool TryReadThreeStrings(NetPacketReader reader, out string first, out string second, out string third)
        {
            first = null;
            second = null;
            third = null;
            try
            {
                if (reader.AvailableBytes < 6) return false;
                first = reader.GetString();
                second = reader.GetString();
                third = reader.GetString();
                return reader.AvailableBytes == 0 &&
                    first != null && second != null && third != null &&
                    first.Length <= 256 && second.Length <= 128 && third.Length <= 128;
            }
            catch { return false; }
        }

        private static bool TryReadRegistration(
            NetPacketReader reader,
            out string account,
            out string salt,
            out string verifier,
            out int passwordLength)
        {
            account = salt = verifier = null;
            passwordLength = 0;
            try
            {
                if (reader.AvailableBytes < 10) return false;
                account = reader.GetString();
                salt = reader.GetString();
                verifier = reader.GetString();
                passwordLength = reader.GetInt();
                return reader.AvailableBytes == 0 &&
                    account != null && salt != null && verifier != null &&
                    account.Length <= 24 && salt.Length <= 64 && verifier.Length <= 64;
            }
            catch { return false; }
        }

        private static bool RequireNoTrailingData(NetPacketReader reader) =>
            reader.AvailableBytes == 0;

        private static string Logout(string uuid)
        {
            AccountServService.Logout(uuid);
            return "AccountServ session ended.";
        }

        private static string Status(string uuid)
        {
            string account = AccountServService.GetIdentifiedAccount(uuid);
            return string.IsNullOrEmpty(account)
                ? "This Basis identity is not identified to an AccountServ account."
                : "Identified as " + account + ".";
        }

        private static void Reply(NetPeer peer, string message)
        {
            if (peer == null) return;
            string safeMessage = string.IsNullOrWhiteSpace(message)
                ? "AccountServ: no response."
                : message.Length > 512 ? message.Substring(0, 512) : message;

            BasisServerMessageRegistry.SendToPeer(peer, MessageName, writer =>
            {
                writer.Put(ResponseMessage);
                writer.Put(safeMessage);
            });
        }

        private static void ReplyIdentifyChallenge(NetPeer peer, string account, string salt, string nonce)
        {
            BasisServerMessageRegistry.SendToPeer(peer, MessageName, writer =>
            {
                writer.Put(ResponseIdentifyChallenge);
                writer.Put(account);
                writer.Put(salt);
                writer.Put(nonce);
            });
        }
    }
}
