using System.Collections.Generic;

// Minimal stand-ins for the Basis types AccountServService touches. The real members are
// supplied by the Basis Server at build time; only the call shapes used by AccountServ matter.
namespace Basis.Network.Core
{
    public static class BNL
    {
        public static readonly List<string> Messages = new List<string>();
        public static void Log(string message) { lock (Messages) Messages.Add(message); }
        public static void LogError(string message) { lock (Messages) Messages.Add(message); }
    }
}

namespace BasisPermissions
{
    public sealed class FakePermissionManager
    {
        private readonly Dictionary<string, HashSet<string>> _groups =
            new Dictionary<string, HashSet<string>>();

        public bool IsInGroup(string uuid, string group) =>
            _groups.TryGetValue(group, out HashSet<string> members) && members.Contains(uuid);

        public void AddUserToGroup(string uuid, string group)
        {
            if (!_groups.TryGetValue(group, out HashSet<string> members))
                _groups[group] = members = new HashSet<string>();
            members.Add(uuid);
        }

        public void RemoveUserFromGroup(string uuid, string group)
        {
            if (_groups.TryGetValue(group, out HashSet<string> members))
                members.Remove(uuid);
        }
    }

    public static class PermissionManager
    {
        public static class PermissionIntegration
        {
            public static FakePermissionManager Manager { get; set; } = new FakePermissionManager();
        }
    }
}
