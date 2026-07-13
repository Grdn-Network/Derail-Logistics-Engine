using DLE.Dispatch;
using DLE.Jobs;
using HarmonyLib;

namespace DLE.Patches
{
    /// <summary>
    /// Dispatch lock (hybrid enforcement, minimal 0.1 form): when the dispatcher enables
    /// the lock, a DLE job with NO assignment cannot be accepted at the validator. Honor
    /// system otherwise. Runs on the host, and client acceptance round-trips through the
    /// host's validator under DVMP, so this covers clients too. Full per-player identity
    /// matching (only the assignee may accept) is tracked in issue #11.
    /// </summary>
    [HarmonyPatch(typeof(JobValidator), "ProcessJobOverview")]
    public static class JobValidatorLockPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(JobOverview jobOverview)
        {
            var job = jobOverview?.job;
            if (job == null) return true;
            if (!AssignmentStore.Instance.LockEnabled) return true;
            if (!JobUtils.ManagedJobIds.Contains(job.ID)) return true;

            if (AssignmentStore.Instance.Get(job.ID) == null)
            {
                Main.Log($"[Dispatch] {job.ID} rejected at validator: lock is on and the job is unassigned.");
                return false; // swallow; the overview stays printed, nothing happens
            }
            return true;
        }
    }
}
