using System;
using System.Collections.Generic;

namespace BasisAccountServ
{
    /// <summary>
    /// Sliding-window request limiter keyed by authenticated identity. Registration rewrites the
    /// account database under a global lock, so unbounded request rates from one identity could
    /// otherwise stall every login. Has no Basis dependencies so it can be unit tested alone.
    /// </summary>
    internal static class AccountServRateLimiter
    {
        internal const int MaximumRequestsPerWindow = 30;
        internal static readonly TimeSpan Window = TimeSpan.FromSeconds(60);
        private const int PruneThreshold = 4096;

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, Queue<DateTime>> Recent =
            new Dictionary<string, Queue<DateTime>>(StringComparer.OrdinalIgnoreCase);

        internal static bool TryAcquire(string identity, DateTime nowUtc)
        {
            if (string.IsNullOrEmpty(identity)) return false;

            lock (Gate)
            {
                if (Recent.Count > PruneThreshold)
                {
                    var stale = new List<string>();
                    foreach (KeyValuePair<string, Queue<DateTime>> entry in Recent)
                    {
                        if (entry.Value.Count == 0 || nowUtc - entry.Value.Peek() > Window)
                            stale.Add(entry.Key);
                    }
                    foreach (string key in stale) Recent.Remove(key);
                }

                if (!Recent.TryGetValue(identity, out Queue<DateTime> queue))
                {
                    queue = new Queue<DateTime>();
                    Recent[identity] = queue;
                }

                while (queue.Count > 0 && nowUtc - queue.Peek() > Window)
                    queue.Dequeue();
                if (queue.Count >= MaximumRequestsPerWindow)
                    return false;

                queue.Enqueue(nowUtc);
                return true;
            }
        }

        internal static void Reset()
        {
            lock (Gate) Recent.Clear();
        }
    }
}
