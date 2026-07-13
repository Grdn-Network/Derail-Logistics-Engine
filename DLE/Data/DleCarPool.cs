using DV.Logic.Job;
using DV.ThingTypes;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using JobTrack = DV.Logic.Job.Track;

namespace DLE.Data
{
    /// <summary>
    /// Finite/persistence mode: the pool of empty cars DLE has spawned into the world.
    /// Pool cars are protected from the unused-car deleter so the fleet is stable; players
    /// (directed by dispatch) move them to producers to fill carless jobs. Persisted under
    /// DLE_CarPool_v1.
    /// </summary>
    public class DleCarPool
    {
        private const string SaveKey = "DLE_CarPool_v1";
        private const int SchemaVersion = 1;

        public static readonly DleCarPool Instance = new DleCarPool();
        private DleCarPool() { }

        private HashSet<string> _guids = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Whether this save has ever received its one-time starter pools. Old saves that
        /// predate the finite world load as false and seed exactly once, same as a new
        /// game; after that, car counts only change through play, company.respawn or the
        /// empties API. Never an automatic top-up.
        /// </summary>
        public bool PoolsSeeded { get; private set; }

        public bool Contains(string carGuid) => carGuid != null && _guids.Contains(carGuid);
        public int Count => _guids.Count;

        /// <summary>
        /// One-time starter seeding for a save that has never had it, spread across frames.
        /// The seeded flag is set BEFORE spawning so any save that captures the new cars
        /// also captures the flag: otherwise a load without a later save re-seeds and stacks
        /// a second pool on top. Never an automatic top-up.
        /// </summary>
        public IEnumerator SeedOnceIfNeededRoutine()
        {
            if (PoolsSeeded || !Main.IsHostOrSingleplayer()) yield break;
            PoolsSeeded = true;
            int spawned = 0;
            yield return RespawnStationPoolsRoutine(deleteFirst: false, n => spawned = n);
            Main.LogAlways($"[CarPool] one-time starter pools seeded for this save ({spawned} car(s)).");
        }

        /// <summary>
        /// Spawn empty cars suited to a cargo on a free storage track at the station,
        /// varying the livery per car. Returns how many were spawned. Pass a per-sweep
        /// claimed-length ledger so repeated calls in one sweep spread across tracks:
        /// Track.OccupiedLength does not update within the frame, so without it consecutive
        /// spawns re-pick the same physically-free track and stack cars into a derail.
        /// </summary>
        public int SpawnEmpties(StationController station, CargoType cargo, int count,
            IDictionary<JobTrack, float> claimed = null)
        {
            if (station == null || count <= 0) return 0;

            if (!DV.Globals.G.Types.CargoType_to_v2.TryGetValue(cargo, out var v2) ||
                !DV.Globals.G.Types.CargoToLoadableCarTypes.TryGetValue(v2, out var carTypes) ||
                carTypes.Count == 0)
            {
                Main.LogAlways($"[CarPool] no car type carries {cargo}.");
                return 0;
            }

            // Vary the livery per car instead of a wall of identical wagons.
            var carType = carTypes[0];
            var liverySet = carType.liveries;
            var liveries = Enumerable.Range(0, count)
                .Select(_ => liverySet[UnityEngine.Random.Range(0, liverySet.Count)])
                .ToList();

            float length = CarSpawner.Instance.GetTotalCarLiveriesLength(liveries, true);

            // Physical occupancy decides, not the YardTracksOrganizer reservation ledger
            // (stale reservations from long-dead jobs report full tracks in an empty yard).
            // The per-sweep claimed ledger is subtracted on top so cars spawned earlier in
            // this same sweep, which OccupiedLength has not caught up to yet, still count.
            var fitting = station.logicStation.yard.StorageTracks
                .Where(t => t.length - t.OccupiedLength - ClaimedOn(claimed, t) > length)
                .ToList();
            if (fitting.Count == 0)
            {
                Main.LogAlways($"[CarPool] {station.stationInfo.YardID}: no storage track has {length:F0}m physically free for {count} empt{(count == 1 ? "y" : "ies")}.");
                return 0;
            }
            var track = fitting
                .OrderByDescending(t => t.length - t.OccupiedLength - ClaimedOn(claimed, t))
                .First();

            var railTrack = RailTrackRegistry.LogicToRailTrack[track];
            var spawned = CarSpawner.Instance
                .SpawnCarTypesOnTrackRandomOrientation(liveries, railTrack, true, applyHandbrakeOnLastCars: true);
            if (spawned == null || spawned.Count == 0)
            {
                Main.LogAlways($"[CarPool] spawn failed at {station.stationInfo.YardID}.");
                return 0;
            }

            foreach (var tc in spawned)
                if (tc.logicCar?.carGuid != null)
                    _guids.Add(tc.logicCar.carGuid);

            if (claimed != null)
                claimed[track] = ClaimedOn(claimed, track) + length;

            Main.Log($"[CarPool] spawned {spawned.Count} empty {carType.name} at " +
                     $"{station.stationInfo.YardID} track {track.ID?.FullDisplayID} (pool now {_guids.Count}).");
            return spawned.Count;
        }

        private static float ClaimedOn(IDictionary<JobTrack, float> claimed, JobTrack t) =>
            (claimed != null && claimed.TryGetValue(t, out var v)) ? v : 0f;

        /// <summary>
        /// Idle empties standing anywhere in a station's yard that can carry the cargo:
        /// jobless, empty, not player-owned. Delivered cuts from earlier hauls are exactly
        /// this, so the economy naturally recycles them into the next outbound job.
        /// Pass null loadableTypes to match any freight car.
        /// </summary>
        public static List<TrainCar> CollectIdleEmpties(
            StationController station,
            List<TrainCarType_v2> loadableTypes,
            int wanted)
        {
            var result = new List<TrainCar>();
            var yard = station.logicStation?.yard;
            if (yard == null) return result;

            var jobsManager = DV.Utils.SingletonBehaviour<JobsManager>.Instance;
            var tracks = new List<DV.Logic.Job.Track>();
            if (yard.TransferOutTracks != null) tracks.AddRange(yard.TransferOutTracks);
            if (yard.TransferInTracks != null) tracks.AddRange(yard.TransferInTracks);
            if (yard.StorageTracks != null) tracks.AddRange(yard.StorageTracks);

            foreach (var track in tracks)
            {
                var cars = track.GetCarsFullyOnTrack();
                if (cars == null) continue;
                foreach (var car in cars)
                {
                    if (result.Count >= wanted) return result;
                    if (car.playerSpawnedCar) continue;
                    if (car.LoadedCargoAmount > 0f) continue;
                    if (loadableTypes != null && !loadableTypes.Contains(car.carType.parentType)) continue;
                    if (jobsManager.GetJobOfCar(car) != null) continue;

                    var trainCar = car.TrainCar();
                    if (trainCar == null) continue;
                    result.Add(trainCar);
                }
            }
            return result;
        }

        /// <summary>
        /// Give every producer a working pool, spread one station per frame so a large fill
        /// neither hitches nor stacks. When deleteFirst is set, clears the idle jobless
        /// empties in each economy yard first (company.respawn recovery) and waits for the
        /// deletions to settle. A per-sweep claimed-length ledger keeps consecutive spawns
        /// off the same track. Company.respawn and new-game seeding both come through here.
        /// Host or singleplayer only.
        /// </summary>
        public IEnumerator RespawnStationPoolsRoutine(bool deleteFirst, Action<int> onDone = null)
        {
            if (!Main.IsHostOrSingleplayer()) { onDone?.Invoke(0); yield break; }
            int totalSpawned = 0;
            int perOutput = Math.Max(1, Main.Settings?.MaxCarsPerHaul ?? 6);
            var claimed = new Dictionary<JobTrack, float>();

            if (deleteFirst)
            {
                foreach (var facility in Economy.EconomyState.Instance.Facilities.Values)
                {
                    var sc = StationController.GetStationByYardID(facility.YardId);
                    if (sc == null) continue;
                    var idle = CollectIdleEmpties(sc, null, int.MaxValue);
                    if (idle.Count == 0) continue;
                    foreach (var tc in idle)
                        if (tc.logicCar?.carGuid != null)
                            _guids.Remove(tc.logicCar.carGuid);
                    CarSpawner.Instance.DeleteTrainCars(idle, true);
                    Main.Log($"[CarPool] {facility.YardId}: cleared {idle.Count} idle empt{(idle.Count == 1 ? "y" : "ies")}.");
                }
                // Let the deletions settle so freed track length is visible before respawn.
                yield return new WaitForSeconds(0.5f);
            }

            foreach (var facility in Economy.EconomyState.Instance.Facilities.Values)
            {
                var sc = StationController.GetStationByYardID(facility.YardId);
                if (sc == null) continue;

                foreach (var cargo in facility.Outputs)
                    totalSpawned += SpawnEmpties(sc, cargo, perOutput, claimed);

                // One station per frame-slice: spread the cost, let physics settle.
                yield return null;
            }

            Main.LogAlways($"[CarPool] station pools respawned: {totalSpawned} car(s) total.");
            onDone?.Invoke(totalSpawned);
        }

        [Serializable]
        private class SaveData
        {
            public int SchemaVersion;
            public bool PoolsSeeded;
            public List<string> Guids;
        }

        public void SaveTo(SaveGameData data) =>
            data.SetObject(SaveKey, new SaveData
            {
                SchemaVersion = SchemaVersion,
                PoolsSeeded = PoolsSeeded,
                Guids = _guids.ToList(),
            });

        public void LoadFrom(SaveGameData data)
        {
            _guids = new HashSet<string>(StringComparer.Ordinal);
            PoolsSeeded = false;
            SaveData payload = null;
            try { payload = data.GetObject<SaveData>(SaveKey); }
            catch (Exception ex) { Main.LogAlways($"[CarPool] save unreadable, starting empty: {ex.Message}"); }
            if (payload?.Guids != null && payload.SchemaVersion == SchemaVersion)
            {
                _guids = new HashSet<string>(payload.Guids, StringComparer.Ordinal);
                // Older saves have no flag and deserialize as false: they seed once, by design.
                PoolsSeeded = payload.PoolsSeeded;
            }
        }
    }
}
