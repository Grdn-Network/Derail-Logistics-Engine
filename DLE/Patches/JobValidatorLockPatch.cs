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
            // Client-installed DLE (booklet rendering support): lock and reservation state
            // live on the host, and DVMP round-trips client validation through the host's
            // validator anyway; the client-side prefix must stay out of the way.
            if (!Main.IsHostOrSingleplayer()) return true;
            if (!JobUtils.ManagedJobIds.Contains(job.ID)) return true;

            // Rejection checks come BEFORE hardening. Hardening the hold and THEN refusing
            // the take (unpaid, or locked-unassigned) left a permanent hard hold on a job
            // that stayed Available, shrinking the pile for every other booklet until the
            // next world load and falsely expiring their paper as stale.

            // Unpaid moves are dispatch-run whatever the lock says: no assignment, no take.
            bool unpaidMove = Jobs.StaticDirectHaulJobDefinition.jobDefinitions.TryGetValue(job.ID, out var mdef) && mdef.unpaidMove;
            if (unpaidMove && AssignmentStore.Instance.Get(job.ID) == null)
            {
                Main.Log($"[Dispatch] {job.ID} rejected at validator: unpaid moves are assigned by dispatch.");
                return false;
            }

            if (AssignmentStore.Instance.LockEnabled && AssignmentStore.Instance.Get(job.ID) == null)
            {
                Main.Log($"[Dispatch] {job.ID} rejected at validator: lock is on and the job is unassigned.");
                return false; // swallow; the overview stays printed, nothing happens
            }

            // Accept-time supply check (#67): paper whose supply went to other hauls since
            // printing is stale; it expires in the crew's hand instead of taking. Done last,
            // so a hold only hardens when the take will actually proceed.
            if (job.State == DV.ThingTypes.JobState.Available &&
                !Economy.EconomyState.Instance.HardenReservation(job.ID))
            {
                Main.LogAlways($"[Dispatch] {job.ID} rejected at validator: stale paper, supply is gone; expiring it.");
                try { job.ExpireJob(); }
                catch (System.Exception ex) { Main.LogAlways($"[Dispatch] {job.ID} stale-paper expire threw: {ex.GetType().Name}: {ex.Message}"); }
                return false;
            }

            return true;
        }
    }
}
