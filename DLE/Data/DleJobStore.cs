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
    /// A job that was taken comes back as available at the origin office; the log says so.
    /// Host or singleplayer only.
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
                    Wage = def.initialWage,
                    BonusTime = def.timeLimitForJob,
                    SpawnTrackDisplay = def.spawnTrackDisplay,
                    IncludeLoadTask = def.includeLoadTask,
                    PlannedCars = def.plannedCarCount,
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
            if (payload?.Jobs == null || payload.Jobs.Count == 0) return;
            if (payload.SchemaVersion != SchemaVersion)
            {
                Main.Log($"[JobStore] job schema {payload.SchemaVersion} != {SchemaVersion}, skipping restore.");
                return;
            }

            // carGuid -> TrainCar for every car the vanilla save brought back.
            var byGuid = new Dictionary<string, TrainCar>(StringComparer.Ordinal);
            foreach (var kv in TrainCarRegistry.Instance.logicCarToTrainCar)
                if (kv.Key?.carGuid != null)
                    byGuid[kv.Key.carGuid] = kv.Value;

            int restored = 0;
            foreach (var snap in payload.Jobs)
            {
                try
                {
                    if (RestoreOne(snap, byGuid)) restored++;
                }
                catch (Exception ex)
                {
                    Main.LogAlways($"[JobStore] {snap.JobId}: restore failed ({ex.GetType().Name}: {ex.Message}); skipped.");
                }
            }
            // Any supply promised to a job that did not survive the load returns.
            Economy.EconomyState.Instance.ReleaseOrphanedReservations(
                id => Jobs.StaticDirectHaulJobDefinition.jobDefinitions.ContainsKey(id));

            Main.Log($"[JobStore] restored {restored}/{payload.Jobs.Count} Direct Haul job(s). " +
                     "Previously taken jobs are available again at the origin office.");
        }

        private static bool RestoreOne(JobSnapshot snap, Dictionary<string, TrainCar> byGuid)
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
                    Main.Log($"[JobStore] {snap.JobId}: only {cars.Count}/{snap.CarGuids.Count} cars found; skipped.");
                    return false;
                }
            }

            bool ok = DirectHaulGenerator.TryRebuild(origin, dest, cars, cargo,
                snap.JobId, snap.Wage, snap.BonusTime, snap.SpawnTrackDisplay,
                snap.IncludeLoadTask, snap.PlannedCars);
            if (ok) JobUtils.EnsureCounterPast(snap.JobId);
            return ok;
        }
    }
}
