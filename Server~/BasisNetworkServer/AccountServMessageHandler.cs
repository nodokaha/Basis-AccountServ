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
        public const byte ResponseMessage = 128;
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
                string response;

                switch (operation)
                {
                    case RequestRegister:
                    {
                        if (!TryReadTwoStrings(reader, out string account, out string password))
                        {
                            response = "Invalid registration request.";
                            break;
                        }
                        response = AccountServService.Register(uuid, account, password, out string error)
                            ? "Account registered and identified as " + account.Trim() + "."
                            : "Registration failed: " + error;
                        break;
                    }
                    case RequestIdentify:
                    {
                        if (!TryReadTwoStrings(reader, out string account, out string password))
                        {
                            response = "Invalid identify request.";
                            break;
                        }
                        response = AccountServService.Identify(uuid, account, password, out string error)
                            ? "Identified as " + AccountServService.GetIdentifiedAccount(uuid) + "."
                            : "Identify failed: " + error;
                        break;
                    }
                    case RequestLogout:
                        response = RequireNoTrailingData(reader)
                            ? Logout(uuid)
                            : "Invalid logout request.";
                        break;

                    case RequestStatus:
                        response = RequireNoTrailingData(reader)
                            ? Status(uuid)
                            : "Invalid status request.";
                        break;

                    case RequestSetRole:
                    {
                        if (!TryReadTwoStrings(reader, out string targetUuid, out string role))
                        {
                            response = "Invalid role request.";
                            break;
                        }
                        response = AccountServService.SetOperatorRole(uuid, targetUuid, role, out string error)
                            ? "Role for " + targetUuid.Trim() + " set to " + role.Trim().ToLowerInvariant() + "."
                            : "Role change failed: " + error;
                        break;
                    }

                    default:
                        response = "Unknown AccountServ request.";
                        break;
                }

                Reply(peer, response);
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
            catch
            {
                return false;
            }
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
    }
}
