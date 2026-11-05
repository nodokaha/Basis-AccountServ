using System;
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
        public const byte ResponseMessage = 128;
        public const int MaximumResponseBytes = 1024;
    }

    /// <summary>
    /// Client half of the AccountServ private server-message channel.
    /// This does not hook or modify the ordinary Basis chat pipeline.
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
        public static event Action<string> ResponseReceived;

        public static bool IsAvailable =>
            BasisClientMessageRegistry.IsPluginSupplied(AccountServMessageHandler.MessageName);

        public static bool Register(string accountName, string password) =>
            Send(AccountServClientProtocol.RequestRegister, writer =>
            {
                writer.Put(accountName ?? string.Empty);
                writer.Put(password ?? string.Empty);
            });

        public static bool Identify(string accountName, string password) =>
            Send(AccountServClientProtocol.RequestIdentify, writer =>
            {
                writer.Put(accountName ?? string.Empty);
                writer.Put(password ?? string.Empty);
            });

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
                operation != AccountServClientProtocol.ResponseMessage ||
                reader.AvailableBytes > AccountServClientProtocol.MaximumResponseBytes)
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
