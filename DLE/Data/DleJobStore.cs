using DLE.Jobs;
using DV.ThingTypes;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DLE.Data
{
    /// <summary>
    /// DLE owns persistence for its Direct Haul jobs (they are filtered out of the vanilla
    /// job save, which cannot reload a ComplexTransport chain). On save, every live job is
    /// snapshotted; on world load the cars are found again by GUID (cars and their cargo
    /// persist through the vanilla save) and the same job is rebuilt with the same ID.
    /// A job that was taken is re-taken after the rebuild (#94): restoring it as open
    /// paper made a crew's loaded haul indistinguishable from un-taken booklets, and the
    /// lock-on purge legally expired it. Host or singleplayer only.
    /// </summary>
    public static class DleJobStore
    {
        private const string SaveKey = "DLE_Jobs_v1";
        private const int SchemaVersion = 1;

        [Serializable]
        public class JobSnapshot
        {
            public string JobId;
            public string OriginYardId;
            public string DestYardId;
            public string Cargo;
            public List<string> CarGuids;
            public float Wage;
            public float BonusTime;
            public string SpawnTrackDisplay;
            public bool IncludeLoadTask;
            public int PlannedCars;
            public int LoadedCarloads;
            public bool UnpaidMove;
            // Additive since 0.43.000: absent in older saves, defaulting to false, which
            // restores the job as open paper exactly like those saves expect.
            public bool WasTaken;
        }

        [Serializable]
        private class SaveData
        {
            public int SchemaVersion;
            public List<JobSnapshot> Jobs;
        }

        public static void SaveTo(SaveGameData data)
        {
            var snapshots = new List<JobSnapshot>();
            foreach (var kv in StaticDirectHaulJobDefinition.jobDefinitions)
            {
                var def = kv.Value;
                if (def == null || def.carsToTransport == null) continue;
                // Completed jobs unregister via OnDestroy; anything still registered is live.
                snapshots.Add(new JobSnapshot
                {
                    JobId = kv.Key,
                    OriginYardId = def.chainData?.chainOriginYardId,
                    DestYardId = def.chainData?.chainDestinationYardId,
                    Cargo = def.transportedCargo.ToString(),
                    CarGuids = def.carsToTransport.Select(c => c.carGuid).ToList(),
                    Wage = def.deliveryPayment,
                    BonusTime = def.timeLimitForJob,
                    SpawnTrackDisplay = def.spawnTrackDisplay,
                    IncludeLoadTask = def.includeLoadTask,
                    PlannedCars = def.plannedCarCount,
                    LoadedCarloads = def.loadedCarloads,
                    UnpaidMove = def.unpaidMove,
                    WasTaken = def.LiveJob?.State == JobState.InProgress,
                });
            }
            data.SetObject(SaveKey, new SaveData { SchemaVersion = SchemaVersion, Jobs = snapshots });
            Main.Log($"[JobStore] saved {snapshots.Count} Direct Haul job(s).");
        }

        public static void RestoreFrom(SaveGameData data)
        {
            SaveData payload = null;
            try { payload = data.GetObject<SaveData>(SaveKey); }
            catch (Exception ex) { Main.LogAlways($"[JobStore] job save unreadable, skipping: {ex.Message}"); }
            if (payload?.Jobs == null || payload.Jobs.Count == 0) { ReconcileReservations(); return; }
            if (payload.SchemaVersion != SchemaVersion)
            {
                Main.LogAlways($"[JobStore] job schema {payload.SchemaVersion} != {SchemaVersion}; dropping every saved Direct Haul job. Taken hauls will be gone after this load.");
                ReconcileReservations();
                return;
            }

            // carGuid -> TrainCar for every car the vanilla save brought back.
            var byGuid = new Dictionary<string, TrainCar>(StringComparer.Ordinal);
            foreach (var kv in TrainCarRegistry.Instance.logicCarToTrainCar)
                if (kv.Key?.carGuid != null)
                    byGuid[kv.Key.carGuid] = kv.Value;

            int restored = 0, retaken = 0;
            foreach (var snap in payload.Jobs)
            {
                try
                {
                    if (RestoreOne(snap, byGuid, ref retaken)) restored++;
                }
                catch (Exception ex)
                {
                    Main.LogAlways($"[JobStore] {snap.JobId}: restore failed ({ex.GetType().Name}: {ex.Message}); skipped.");
                }
            }
            ReconcileReservations();

            // Always logged: this one line was the forensic key to #94 (it proved every
            // job had been demoted to Available before the purge ran).
            Main.LogAlways($"[JobStore] restored {restored}/{payload.Jobs.Count} Direct Haul job(s); " +
                     $"{retaken} re-taken (in progress at save), the rest are open at their origin offices.");
        }

        /// <summary>
        /// After any restore outcome (including none): supply promised to jobs that did
        /// not survive returns. Surviving OPEN paper drops its hold to soft (taking it
        /// re-hardens through the normal accept gate); re-taken jobs keep hard holds.
        /// </summary>
        private static void ReconcileReservations()
        {
            Economy.EconomyState.Instance.ReleaseOrphanedReservations(
                id => StaticDirectHaulJobDefinition.jobDefinitions.ContainsKey(id));
            Economy.EconomyState.Instance.SyncReservationTiers(id =>
                StaticDirectHaulJobDefinition.jobDefinitions.TryGetValue(id, out var d) &&
                (d.LiveJob == null || d.LiveJob.State == DV.ThingTypes.JobState.Available));
        }

        private static bool RestoreOne(JobSnapshot snap, Dictionary<string, TrainCar> byGuid, ref int retaken)
        {
            if (string.IsNullOrEmpty(snap.JobId) || snap.CarGuids == null) return false;
            if (StaticDirectHaulJobDefinition.jobDefinitions.ContainsKey(snap.JobId)) return false;

            var origin = StationController.GetStationByYardID(snap.OriginYardId);
            var dest = StationController.GetStationByYardID(snap.DestYardId);
            if (origin == null || dest == null || !Enum.TryParse<CargoType>(snap.Cargo, out var cargo))
            {
                Main.LogAlways($"[JobStore] {snap.JobId}: station or cargo no longer resolves; skipped.");
                return false;
            }

            var cars = new List<TrainCar>();
            foreach (var guid in snap.CarGuids)
                if (byGuid.TryGetValue(guid, out var tc)) cars.Add(tc);
            if (cars.Count != snap.CarGuids.Count)
            {
                // A carless (finite mode) job legitimately has zero cars until players
                // bring empties; anything else with missing cars is unrecoverable.
                if (!(snap.IncludeLoadTask && snap.CarGuids.Count == 0))
                {
                    // Advance the route counter past this dropped ID even though it did not
                    // rebuild, so a future job cannot be minted with the same ID and inherit
                    // this one's still-persisted crew assignment.
                    JobUtils.EnsureCounterPast(snap.JobId);
                    Main.LogAlways($"[JobStore] {snap.JobId}: only {cars.Count}/{snap.CarGuids.Count} cars found after load; job dropped. The crew's booklet is gone.");
                    return false;
                }
            }

            bool ok = DirectHaulGenerator.TryRebuild(origin, dest, cars, cargo,
                snap.JobId, snap.Wage, snap.BonusTime, snap.SpawnTrackDisplay,
                snap.IncludeLoadTask, snap.PlannedCars);
            if (ok)
            {
                JobUtils.EnsureCounterPast(snap.JobId);
                if (StaticDirectHaulJobDefinition.jobDefinitions.TryGetValue(snap.JobId, out var rebuilt))
                {
                    // The saved tally outranks the rebuild's inference (cars may have been
                    // unloaded at the destination before the save, leaving them empty but
                    // legitimately delivered-in-progress).
                    if (snap.LoadedCarloads > 0 && rebuilt.loadedCarloads < snap.LoadedCarloads)
                        rebuilt.loadedCarloads = snap.LoadedCarloads;
                    rebuilt.unpaidMove = snap.UnpaidMove;

                    // #94: a haul that was in progress at save comes back in progress, the
                    // same way the vanilla save restores taken jobs. The rebuild leaves it
                    // Available, so a crew's running (even loaded) haul looked exactly like
                    // un-taken paper and the lock-on purge expired it among the open booklets.
                    if (snap.WasTaken && rebuilt.LiveJob != null &&
                        rebuilt.LiveJob.State == JobState.Available)
                    {
                        DV.Utils.SingletonBehaviour<DV.Logic.Job.JobsManager>.Instance
                            .TakeJob(rebuilt.LiveJob, true);
                        retaken++;
                        Main.LogAlways($"[JobStore] {snap.JobId} restored as TAKEN; the crew's haul survives the restart.");
                    }
                }
            }
            return ok;
        }
    }
}
