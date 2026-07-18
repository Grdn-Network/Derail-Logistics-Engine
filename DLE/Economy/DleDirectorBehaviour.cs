using DLE.Dispatch;
using System.Collections;
using UnityEngine;

namespace DLE.Economy
{
    /// <summary>
    /// The job generator machine: on the host, tick the economy and keep the map stocked
    /// with hauls up to the configured caps. One haul per tick spreads the spawn cost; an
    /// initial burst shortly after world load fills the board for the session.
    /// </summary>
    public class DleDirectorBehaviour : MonoBehaviour
    {
        private static GameObject _host;

        // The save this director was born into. If SaveGameManager swaps to a different
        // save (the player loaded another world in the same session), this director is
        // stale: its loops belong to the old world's economy and must stop before they
        // generate jobs or tick production into the newly loading world. StartOnHost only
        // destroys it at the NEW world's LoadingFinished, which is after streaming begins.
        private object _bornData;

        private bool IsStale() =>
            !ReferenceEquals(_bornData, SaveGameManager.Instance?.data);

        public static void StartOnHost()
        {
            // A fresh world load gets a fresh director: the old TickLoop belongs to the
            // previous save (its one-time seeding and initial fill already ran) and dies
            // with its host object. Without this, the second save loaded in one session
            // never seeded and never got an initial fill.
            if (_host != null) Destroy(_host);
            _host = new GameObject("DLE_Director");
            DontDestroyOnLoad(_host);
            _host.AddComponent<DleDirectorBehaviour>();
        }

        /// <summary>Run a coroutine on the host director (used by console commands).</summary>
        public static bool TryRun(IEnumerator routine)
        {
            if (_host == null || routine == null) return false;
            var beh = _host.GetComponent<DleDirectorBehaviour>();
            if (beh == null) return false;
            beh.StartCoroutine(routine);
            return true;
        }

        private void Start()
        {
            _bornData = SaveGameManager.Instance?.data;
            // Fresh world, fresh sweep state: a coroutine that died mid-sweep in the old
            // world must not wedge the single-flight guard forever.
            Data.DleCarPool.SweepInFlight = false;
            StartCoroutine(TickLoop());
            StartCoroutine(OverviewSweepLoop());
        }

        /// <summary>
        /// While the assignment lock is ON, Company Haul paperwork stops existing at
        /// station offices: crews take and turn in from the board instead. The game keeps
        /// respawning overviews for available jobs, so this sweeps them every few seconds
        /// rather than patching the spawner.
        /// </summary>
        private IEnumerator OverviewSweepLoop()
        {
            var wait = new WaitForSeconds(3f);
            while (true)
            {
                yield return wait;
                if (IsStale()) yield break; // a different world loaded; this director is done
                if (!Main.IsHostOrSingleplayer()) continue;

                // Paper is just paperwork: a haul whose cargo stands unloaded at the
                // destination closes itself, pays, and frees the consumer to consume.
                // Covers staff, terminal and hand unloading alike; the board's Turn in
                // button stays as a manual fallback.
                try { AutoCloseDelivered(); }
                catch (System.Exception ex) { Main.LogAlways($"[Director] auto-close sweep failed: {ex.GetType().Name}: {ex.Message}"); }

                try { DespawnManagedOverviews(Dispatch.AssignmentStore.Instance.LockEnabled); }
                catch (System.Exception ex) { Main.LogAlways($"[Director] overview sweep failed: {ex.GetType().Name}: {ex.Message}"); }

                try { AutoCloseLogiRuns(); }
                catch (System.Exception ex) { Main.LogAlways($"[Director] logistics sweep failed: {ex.GetType().Name}: {ex.Message}"); }
            }
        }

        private static void AutoCloseDelivered()
        {
            var ids = new System.Collections.Generic.List<string>();
            foreach (var kv in Jobs.StaticDirectHaulJobDefinition.jobDefinitions)
            {
                var def = kv.Value;
                var job = def?.LiveJob;
                if (job == null || job.State != DV.ThingTypes.JobState.InProgress) continue;
                if (def.carsToTransport == null || def.carsToTransport.Count == 0) continue;
                if (def.loadedCarloads <= 0) continue;
                bool anyLoaded = false;
                foreach (var car in def.carsToTransport)
                    if (car != null && car.LoadedCargoAmount > 0f) { anyLoaded = true; break; }
                if (anyLoaded) continue;
                ids.Add(kv.Key);
            }
            foreach (var id in ids)
            {
                // CompleteJob re-validates position (all cars in the destination yard)
                // and refuses anything not truly delivered; a refusal here just means
                // the cut has not arrived yet.
                var r = Dispatch.DispatchLifecycle.CompleteJob(id);
                if (r.Ok) Main.LogAlways($"[Dispatch] {id} closed automatically: cargo delivered.");
            }
        }

        /// <summary>
        /// A logistics run's zero-pay EmptyHaul closes itself on arrival: when its
        /// transport task reads Done, the sweep completes the job and marks the order
        /// Done, so the run needs no turn-in trip (paper is paperwork here too).
        /// </summary>
        private static void AutoCloseLogiRuns()
        {
            var jobsManager = DV.Utils.SingletonBehaviour<DV.Logic.Job.JobsManager>.Instance;
            if (jobsManager == null) return;
            foreach (var order in Dispatch.LogisticsBoard.Instance.All)
            {
                if (string.IsNullOrEmpty(order.JobId) || order.Status == "Done") continue;
                DV.Logic.Job.Job job = null;
                foreach (var j in jobsManager.jobToJobCars.Keys)
                    if (j != null && j.ID == order.JobId) { job = j; break; }
                if (job == null) continue; // expired or already cleaned up; the order stays a note
                if (job.State == DV.ThingTypes.JobState.Completed)
                {
                    Dispatch.LogisticsBoard.Instance.SetStatus(order.Id, "Done");
                    continue;
                }
                if (job.State != DV.ThingTypes.JobState.InProgress) continue;
                bool allDone = true;
                foreach (var t in job.tasks)
                    if (t.state != DV.Logic.Job.TaskState.Done) { allDone = false; break; }
                if (!allDone) continue;
                var state = jobsManager.TryToCompleteAJob(job);
                if (state == DV.ThingTypes.JobState.Completed)
                {
                    Dispatch.LogisticsBoard.Instance.SetStatus(order.Id, "Done");
                    Main.LogAlways($"[Logistics] {order.Id} ({order.JobId}) arrived; run closed automatically.");
                }
            }
        }

        private static void DespawnManagedOverviews(bool lockOn)
        {
            if (StationController.allStations == null) return;
            int removed = 0;
            foreach (var sc in StationController.allStations)
            {
                var overviews = sc?.spawnedJobOverviews;
                if (overviews == null) continue;
                for (int i = overviews.Count - 1; i >= 0; i--)
                {
                    var ov = overviews[i];
                    // Unity's overloaded == catches destroyed components; `ov?.` would not,
                    // and DestroyJobOverview does NOT remove itself from this list (vanilla
                    // callers remove first), so prune AND remove here, or the next pass throws
                    // MissingReferenceException and aborts the sweep.
                    if (ov == null)
                    {
                        overviews.RemoveAt(i);
                        continue;
                    }
                    var id = ov.job?.ID;
                    if (id == null || !Jobs.JobUtils.ManagedJobIds.Contains(id)) continue;
                    // Lock ON sweeps every Company Haul paper. Unpaid moves are dispatch
                    // work regardless of the lock: their paper never sits in an office;
                    // dispatch assigns and faxes them instead.
                    bool unpaid = Jobs.StaticDirectHaulJobDefinition.jobDefinitions.TryGetValue(id, out var d) && d.unpaidMove;
                    if (!lockOn && !unpaid) continue;
                    overviews.RemoveAt(i);
                    ov.DestroyJobOverview();
                    removed++;
                }
            }
            if (removed > 0)
                Main.Log($"[Director] assignment lock: removed {removed} Company Haul office paper(s).");
        }

        private IEnumerator TickLoop()
        {
            // Let world streaming settle before the initial fill: a slow load can otherwise
            // skip the fill entirely, so wait for the stations (up to a minute).
            yield return new WaitForSeconds(15f);
            for (int i = 0; i < 45 && !WorldReady(); i++)
                yield return new WaitForSeconds(1f);
            if (!WorldReady())
            {
                Main.LogAlways("[Director] world never became ready; no seeding or generation this session.");
                yield break;
            }

            // One-time starter pools happen here, not at LoadingFinished: car spawning
            // needs the world fully live (the same reason this loop waits). Runs as a
            // nested coroutine so the fill spreads across frames.
            yield return Data.DleCarPool.Instance.SeedOnceIfNeededRoutine();

            // A tick that throws must not kill the whole generation loop for the session
            // (an unhandled NRE in job creation would end all generation silently).
            bool SafeTick()
            {
                try { return DispatcherBrain.Current.TickOnce(); }
                catch (System.Exception ex)
                {
                    Main.LogAlways($"[Director] tick failed: {ex.GetType().Name}: {ex.Message}");
                    return false;
                }
            }

            Main.LogAlways("[Director] initial fill starting.");
            int created = 0;
            while (Main.IsHostOrSingleplayer() && WorldReady() &&
                   SafeTick() && created++ < 40)
                yield return new WaitForSeconds(1.5f); // one spawn per frame-slice, no hitching
            Main.LogAlways($"[Director] initial fill done: {created} haul(s) created." +
                (created == 0 ? " Nothing shippable: check available supply at /api/v1/options (stock may be drained or fully reserved)." : ""));

            Main.Log("[Director] initial fill done; ticking.");
            float productionAccumulator = 0f;
            while (true)
            {
                float tick = Mathf.Max(15, RecipeProvider.Tuning.directorTickSeconds);
                yield return new WaitForSeconds(tick);
                if (IsStale()) yield break; // a different world loaded; do not tick into it
                if (!Main.IsHostOrSingleplayer() || !WorldReady()) continue;

                // Cargo enters the world at the sources on a slow clock.
                productionAccumulator += tick / 60f;
                float interval = Mathf.Max(1, RecipeProvider.Tuning.sourceProductionMinutes);
                if (productionAccumulator >= interval)
                {
                    EconomyState.Instance.TickSourceProduction((int)(productionAccumulator / interval));
                    productionAccumulator %= interval;
                }

                SafeTick();
            }
        }

        private static bool WorldReady() =>
            StationController.allStations != null && StationController.allStations.Count > 0;
    }
}
