using System;
using System.IO;
using System.Text.Json;
using Basis.Network.Core;
using BasisNetworkServer;

[assembly: BasisNetworkPackage(typeof(BasisAccountServ.AccountServPackage))]

namespace BasisAccountServ
{
    /// <summary>
    /// Basis Server package entry point. Registers the AccountServ backend and its
    /// private plugin-message endpoint without touching the ordinary chat channel.
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
            BNL.Log("[AccountServ] Package initialized.");
        }

        private static void OnServerStarted(Configuration configuration)
        {
            const string configPath = "config/accountserv.json";
            if (!File.Exists(configPath))
            {
                BNL.Log("[AccountServ] config/accountserv.json not found; service remains disabled. Copy config/accountserv.example.json to enable it.");
                return;
            }

            try
            {
                using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(configPath)))
                {
                    JsonElement root = document.RootElement;
                    if (!root.TryGetProperty("enabled", out JsonElement enabled) || !enabled.GetBoolean())
                    {
                        BNL.Log("[AccountServ] Disabled by configuration.");
                        return;
                    }

                    string database = "config/accountserv.db";
                    if (root.TryGetProperty("database", out JsonElement databaseElement) &&
                        databaseElement.ValueKind == JsonValueKind.String)
                        database = databaseElement.GetString();

                    int minimumPasswordLength = 12;
                    int maximumFailedLogins = 5;
                    int lockoutSeconds = 300;
                    int sessionLifetimeMinutes = 1440;

                    if (root.TryGetProperty("security", out JsonElement security))
                    {
                        ReadInt(security, "minimumPasswordLength", ref minimumPasswordLength);
                        ReadInt(security, "maximumFailedLogins", ref maximumFailedLogins);
                        ReadInt(security, "lockoutSeconds", ref lockoutSeconds);
                        ReadInt(security, "sessionLifetimeMinutes", ref sessionLifetimeMinutes);
                    }

                    AccountServService.Initialize(database, minimumPasswordLength,
                        maximumFailedLogins, lockoutSeconds, sessionLifetimeMinutes);
                    AccountServMessageHandler.Register();
                    BNL.Log("[AccountServ] Account service and private DM endpoint initialized.");
                }
            }
            catch (Exception exception)
            {
                BNL.Log("[AccountServ] Failed to initialize: " + exception.Message);
            }
        }

        private static void ReadInt(JsonElement element, string name, ref int value)
        {
            if (element.TryGetProperty(name, out JsonElement property) &&
                property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out int parsed))
                value = parsed;
        }

        private static void OnServerStopping()
        {
            BNL.Log("[AccountServ] Server stopping.");
        }
    }
}
