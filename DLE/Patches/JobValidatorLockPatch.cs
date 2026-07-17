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
            if (!JobUtils.ManagedJobIds.Contains(job.ID)) return true;

            // Accept-time supply check (#67): paper whose supply went to other hauls
            // since printing is stale; it expires in the crew's hand instead of taking.
            if (job.State == DV.ThingTypes.JobState.Available &&
                !Economy.EconomyState.Instance.HardenReservation(job.ID))
            {
                Main.LogAlways($"[Dispatch] {job.ID} rejected at validator: stale paper, supply is gone; expiring it.");
                try { job.ExpireJob(); } catch { }
                return false;
            }

            // Unpaid moves are dispatch-run whatever the lock says: no assignment, no take.
            bool unpaidMove = Jobs.StaticDirectHaulJobDefinition.jobDefinitions.TryGetValue(job.ID, out var mdef) && mdef.unpaidMove;
            if (unpaidMove && AssignmentStore.Instance.Get(job.ID) == null)
            {
                Main.Log($"[Dispatch] {job.ID} rejected at validator: unpaid moves are assigned by dispatch.");
                return false;
            }

            if (!AssignmentStore.Instance.LockEnabled) return true;
            if (AssignmentStore.Instance.Get(job.ID) == null)
            {
                Main.Log($"[Dispatch] {job.ID} rejected at validator: lock is on and the job is unassigned.");
                return false; // swallow; the overview stays printed, nothing happens
            }
            return true;
        }
    }
}
