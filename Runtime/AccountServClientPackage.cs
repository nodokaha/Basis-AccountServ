using System;
using Basis.Network.Core;
using Basis.Scripts.Networking;

[assembly: BasisNetworkPackage(typeof(BasisAccountServ.AccountServClientPackage))]

namespace BasisAccountServ
{
    /// <summary>
    /// Client half of the AccountServ private server-message channel.
    /// This does not hook or modify the ordinary Basis chat pipeline.
    /// </summary>
    public sealed class AccountServClientPackage : IBasisNetworkPackage
    {
        public void Initialize()
        {
            BasisClientMessageRegistry.RegisterClientPlugin(
                AccountServMessageHandler.MessageName,
                AccountServClient.HandleServerMessage);
        }
    }

    public static class AccountServClient
    {
        public static event Action<string> ResponseReceived;

        public static bool IsAvailable =>
            BasisClientMessageRegistry.IsPluginSupplied(AccountServMessageHandler.MessageName);

        public static bool Register(string accountName, string password) =>
            Send(AccountServMessageHandler.RequestRegister, writer =>
            {
                writer.Put(accountName ?? string.Empty);
                writer.Put(password ?? string.Empty);
            });

        public static bool Identify(string accountName, string password) =>
            Send(AccountServMessageHandler.RequestIdentify, writer =>
            {
                writer.Put(accountName ?? string.Empty);
                writer.Put(password ?? string.Empty);
            });

        public static bool Logout() =>
            Send(AccountServMessageHandler.RequestLogout, null);

        public static bool GetStatus() =>
            Send(AccountServMessageHandler.RequestStatus, null);

        public static bool SetRole(string targetUuid, string role) =>
            Send(AccountServMessageHandler.RequestSetRole, writer =>
            {
                writer.Put(targetUuid ?? string.Empty);
                writer.Put(role ?? string.Empty);
            });

        private static bool Send(byte operation, Action<NetDataWriter> write)
        {
            return BasisClientMessageRegistry.Send(AccountServMessageHandler.MessageName, writer =>
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
                operation != AccountServMessageHandler.ResponseMessage ||
                reader.AvailableBytes > AccountServMessageHandler.MaximumResponseBytes)
            {
                return;
            }

            string response;
            try
            {
                response = reader.GetString();
            }
            catch
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(response))
                ResponseReceived?.Invoke(response);
        }
    }
}
