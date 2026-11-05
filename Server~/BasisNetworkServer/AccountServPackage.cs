using System;
using Basis.Network.Core;
using BasisNetworkServer;

[assembly: BasisNetworkPackage(typeof(BasisAccountServ.AccountServPackage))]

namespace BasisAccountServ
{
    /// <summary>
    /// Basis Server package entry point. The package is compiled into BasisNetworkServer,
    /// which gives it access to the server lifecycle and server-side identity/permission APIs.
    /// </summary>
    public sealed class AccountServPackage : IBasisNetworkPackage
    {
        private static bool _initialized;

        public void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            NetworkServer.ServerStarted += OnServerStarted;
            NetworkServer.ServerStopping += OnServerStopping;
            BNL.Log("[AccountServ] Package initialized. Chat command adapter is not enabled until the Basis chat hook is available.");
        }

        private static void OnServerStarted(Configuration configuration)
        {
            BNL.Log("[AccountServ] Server started; AccountServ storage initialization is pending configuration.");
        }

        private static void OnServerStopping()
        {
            BNL.Log("[AccountServ] Server stopping.");
        }
    }
}
