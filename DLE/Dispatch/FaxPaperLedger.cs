using System;
using System.Collections.Generic;
using UnityEngine;

namespace DLE.Dispatch
{
    /// <summary>
    /// Papers DLE itself printed, per job: a re-fax destroys the live old copy first,
    /// so with "fax = assign" the machine refreshes paper instead of stacking
    /// duplicates, and reassigning a job recalls the previous crew's copy. The ledger
    /// is local to each machine: the host tracks what it printed (local player and
    /// world prints for modless crews), each DLE client tracks its own inventory
    /// prints. Booklets a crew took at an office are vanilla paper and stay untouched.
    /// Destroying a tracked booklet is the same operation vanilla runs when a booklet
    /// self-destroys on job completion.
    /// </summary>
    internal static class FaxPaperLedger
    {
        private static readonly Dictionary<string, List<WeakReference<GameObject>>> _printed =
            new Dictionary<string, List<WeakReference<GameObject>>>(StringComparer.Ordinal);

        public static void Remember(string jobId, GameObject paper)
        {
            if (string.IsNullOrEmpty(jobId) || paper == null) return;
            if (!_printed.TryGetValue(jobId, out var list))
                _printed[jobId] = list = new List<WeakReference<GameObject>>();
            list.RemoveAll(w => !w.TryGetTarget(out var go) || go == null); // Unity fake-null prunes too
            list.Add(new WeakReference<GameObject>(paper));
        }

        /// <summary>Destroy every live paper this machine printed for the job.
        /// Returns how many were recalled.</summary>
        public static int DestroyLive(string jobId)
        {
            if (string.IsNullOrEmpty(jobId) || !_printed.TryGetValue(jobId, out var list)) return 0;
            int n = 0;
            foreach (var w in list)
                if (w.TryGetTarget(out var go) && go != null)
                {
                    try { UnityEngine.Object.Destroy(go); n++; }
                    catch { }
                }
            _printed.Remove(jobId);
            if (n > 0) Main.Log($"[Fax] {jobId}: recalled {n} old cop{(n == 1 ? "y" : "ies")} before reprinting.");
            return n;
        }

        public static void Clear() => _printed.Clear();
    }
}
