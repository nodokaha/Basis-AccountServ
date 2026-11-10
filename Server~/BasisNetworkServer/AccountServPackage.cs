using System;
using System.Globalization;
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
            string configPath = Environment.GetEnvironmentVariable("ACCOUNTSERV_CONFIG_FILE");
            if (string.IsNullOrWhiteSpace(configPath))
                configPath = "config/accountserv.json";

            try
            {
                JsonDocument document = File.Exists(configPath)
                    ? JsonDocument.Parse(File.ReadAllText(configPath))
                    : null;

                try
                {
                    JsonElement root = document == null ? default(JsonElement) : document.RootElement;
                    if (document == null)
                        BNL.Log("[AccountServ] " + configPath + " not found; using environment variables and defaults.");

                    bool enabled = ReadBool(root, "enabled", false, "ACCOUNTSERV_ENABLED");
                    if (!enabled)
                    {
                        BNL.Log("[AccountServ] Disabled by configuration.");
                        return;
                    }

                    string database = ReadString(root, "database",
                        "config/accountserv.db", "ACCOUNTSERV_DATABASE");
                    int minimumPasswordLength = ReadInt(root, "security", "minimumPasswordLength",
                        12, "ACCOUNTSERV_SECURITY_MINIMUM_PASSWORD_LENGTH");
                    int maximumFailedLogins = ReadInt(root, "security", "maximumFailedLogins",
                        5, "ACCOUNTSERV_SECURITY_MAXIMUM_FAILED_LOGINS");
                    int lockoutSeconds = ReadInt(root, "security", "lockoutSeconds",
                        300, "ACCOUNTSERV_SECURITY_LOCKOUT_SECONDS");
                    int sessionLifetimeMinutes = ReadInt(root, "security", "sessionLifetimeMinutes",
                        1440, "ACCOUNTSERV_SECURITY_SESSION_LIFETIME_MINUTES");

                    AccountServService.Initialize(database, minimumPasswordLength,
                        maximumFailedLogins, lockoutSeconds, sessionLifetimeMinutes);
                    AccountServMessageHandler.Register();
                    BNL.Log("[AccountServ] Account service and private DM endpoint initialized. Database: " + database);
                }
                finally
                {
                    if (document != null)
                        document.Dispose();
                }
            }
            catch (Exception exception)
            {
                BNL.Log("[AccountServ] Failed to initialize: " + exception.Message);
            }
        }

        // Environment variables take precedence over JSON; JSON takes precedence over defaults.
        private static bool ReadBool(JsonElement root, string name, bool defaultValue, string environmentName)
        {
            string environmentValue = Environment.GetEnvironmentVariable(environmentName);
            if (!string.IsNullOrWhiteSpace(environmentValue))
            {
                bool parsedEnvironment;
                if (bool.TryParse(environmentValue, out parsedEnvironment))
                    return parsedEnvironment;
                BNL.Log("[AccountServ] Invalid boolean in " + environmentName + "; falling back to JSON/default.");
            }

            JsonElement property;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty(name, out property) &&
                (property.ValueKind == JsonValueKind.True || property.ValueKind == JsonValueKind.False))
                return property.GetBoolean();

            return defaultValue;
        }

        private static string ReadString(JsonElement root, string name, string defaultValue, string environmentName)
        {
            string environmentValue = Environment.GetEnvironmentVariable(environmentName);
            if (!string.IsNullOrWhiteSpace(environmentValue))
                return environmentValue;

            JsonElement property;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty(name, out property) &&
                property.ValueKind == JsonValueKind.String)
            {
                string configuredValue = property.GetString();
                if (!string.IsNullOrWhiteSpace(configuredValue))
                    return configuredValue;
            }

            return defaultValue;
        }

        private static int ReadInt(
            JsonElement root,
            string sectionName,
            string name,
            int defaultValue,
            string environmentName)
        {
            string environmentValue = Environment.GetEnvironmentVariable(environmentName);
            if (!string.IsNullOrWhiteSpace(environmentValue))
            {
                int parsedEnvironment;
                if (int.TryParse(environmentValue, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out parsedEnvironment))
                    return parsedEnvironment;
                BNL.Log("[AccountServ] Invalid integer in " + environmentName + "; falling back to JSON/default.");
            }

            JsonElement section;
            JsonElement property;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty(sectionName, out section) &&
                section.ValueKind == JsonValueKind.Object &&
                section.TryGetProperty(name, out property) &&
                property.ValueKind == JsonValueKind.Number)
            {
                int parsedJson;
                if (property.TryGetInt32(out parsedJson))
                    return parsedJson;
            }

            return defaultValue;
        }

        private static void OnServerStopping()
        {
            BNL.Log("[AccountServ] Server stopping.");
        }
    }
}
