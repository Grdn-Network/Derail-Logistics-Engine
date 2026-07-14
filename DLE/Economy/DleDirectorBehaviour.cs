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

        public static void StartOnHost()
        {
            if (_host != null) return;
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
                if (!Main.IsHostOrSingleplayer()) continue;
                if (!Dispatch.AssignmentStore.Instance.LockEnabled) continue;
                try { DespawnManagedOverviews(); }
                catch (System.Exception ex) { Main.Log($"[Director] overview sweep failed: {ex.Message}"); }
            }
        }

        private static void DespawnManagedOverviews()
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
                    var id = ov?.job?.ID;
                    if (id == null || !Jobs.JobUtils.ManagedJobIds.Contains(id)) continue;
                    ov.DestroyJobOverview();
                    removed++;
                }
            }
            if (removed > 0)
                Main.Log($"[Director] assignment lock: removed {removed} Company Haul office paper(s).");
        }

        private IEnumerator TickLoop()
        {
            // Let world streaming settle before the initial fill. Previously a slow load
            // could skip the fill entirely; now we wait for the stations (up to a minute).
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
            // (an NRE in job creation used to end all generation silently).
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
                float tick = Mathf.Max(15, Main.Settings?.DirectorTickSeconds ?? 60);
                yield return new WaitForSeconds(tick);
                if (!Main.IsHostOrSingleplayer() || !WorldReady()) continue;

                // Cargo enters the world at the sources on a slow clock.
                productionAccumulator += tick / 60f;
                float interval = Mathf.Max(1, Main.Settings?.SourceProductionMinutes ?? 10);
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
