using DLE.Jobs;
using DV.Logic.Job;
using HarmonyLib;
using System.Collections.Generic;

namespace DLE.Patches
{
    /// <summary>
    /// Company Hauls outlive an empty station (owner ruling 2026-07-27).
    ///
    /// Vanilla wipes every available job at a station the moment the last player leaves
    /// its destroy zone: StationController.Update calls ExpireAllAvailableJobsInStation
    /// on that transition. That is right for vanilla, where paper only exists because a
    /// player is standing there, and wrong for DLE, where the board is the job market and
    /// a haul is an order the company placed. It also explains assignments appearing to
    /// vanish (#79): the assigned haul was expired while nobody was nearby, and the
    /// director later created a fresh one with a NEW id, which the board correctly showed
    /// as unassigned.
    ///
    /// Rather than reimplement the vanilla method (its overview cleanup is private), the
    /// managed jobs are lifted out of availableJobs for the duration of the call and put
    /// back afterwards, so vanilla expires exactly the jobs it should and never sees ours.
    /// Their office paper is still destroyed with everything else, which is correct: the
    /// station is empty, and vanilla clears processedNewJobs in the same pass, so the
    /// paper regenerates for the surviving hauls when a crew next arrives.
    /// </summary>
    [HarmonyPatch(typeof(StationController), nameof(StationController.ExpireAllAvailableJobsInStation))]
    public static class ManagedJobExpiryGuardPatch
    {
        [HarmonyPrefix]
        public static void Prefix(StationController __instance, out List<Job> __state)
        {
            __state = null;
            var available = __instance?.logicStation?.availableJobs;
            if (available == null || available.Count == 0) return;

            List<Job> managed = null;
            for (int i = available.Count - 1; i >= 0; i--)
            {
                var job = available[i];
                if (job?.ID == null || !JobUtils.ManagedJobIds.Contains(job.ID)) continue;
                (managed ?? (managed = new List<Job>())).Add(job);
                available.RemoveAt(i);
            }
            __state = managed;
        }

        [HarmonyPostfix]
        public static void Postfix(StationController __instance, List<Job> __state)
        {
            if (__state == null || __state.Count == 0) return;
            var available = __instance?.logicStation?.availableJobs;
            if (available == null)
            {
                Main.LogAlways($"[DirectHaul] {__state.Count} haul(s) could not be restored at " +
                               $"{__instance?.stationInfo?.YardID}; the station lost its job list mid-expiry.");
                return;
            }

            // Reinstate in the order they were listed: the removal pass walked backwards.
            for (int i = __state.Count - 1; i >= 0; i--)
                available.Add(__state[i]);

            Main.Log($"[DirectHaul] kept {__state.Count} Company Haul(s) alive at " +
                     $"{__instance?.stationInfo?.YardID} after the station emptied.");
        }
    }
}
