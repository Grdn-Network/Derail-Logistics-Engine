using DV.Logic.Job;
using DV.ThingTypes;
using System;
using System.Collections.Generic;
using System.Linq;

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

        public bool Contains(string carGuid) => carGuid != null && _guids.Contains(carGuid);
        public int Count => _guids.Count;

        /// <summary>
        /// Spawn empty cars suited to a cargo on a free storage track at the station.
        /// Returns how many were spawned.
        /// </summary>
        public int SpawnEmpties(StationController station, CargoType cargo, int count)
        {
            if (station == null || count <= 0) return 0;

            if (!DV.Globals.G.Types.CargoType_to_v2.TryGetValue(cargo, out var v2) ||
                !DV.Globals.G.Types.CargoToLoadableCarTypes.TryGetValue(v2, out var carTypes) ||
                carTypes.Count == 0)
            {
                Main.Log($"[CarPool] no car type carries {cargo}.");
                return 0;
            }
            var livery = carTypes[0].liveries[0];
            var liveries = Enumerable.Repeat(livery, count).ToList();

            float length = CarSpawner.Instance.GetTotalCarLiveriesLength(liveries, true);
            var candidates = station.logicStation.yard.StorageTracks.Where(t => t.IsFree()).ToList();
            var track = YardTracksOrganizer.Instance
                .FilterOutTracksWithoutRequiredFreeSpace(candidates, length)
                .FirstOrDefault();
            if (track == null)
            {
                Main.Log($"[CarPool] {station.stationInfo.YardID}: no free storage track for {count} empt{(count == 1 ? "y" : "ies")}.");
                return 0;
            }

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

            Main.Log($"[CarPool] spawned {spawned.Count} empty {livery.name} at " +
                     $"{station.stationInfo.YardID} track {track.ID?.FullDisplayID} (pool now {_guids.Count}).");
            return spawned.Count;
        }

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
        /// Give every producer a working pool: optionally clear the idle jobless empties
        /// standing in each economy yard first (derailment recovery), then spawn a fresh
        /// pool of empties per output cargo. Company.respawn and new-game seeding both
        /// come through here. Host or singleplayer only.
        /// </summary>
        public int RespawnStationPools(bool deleteFirst)
        {
            if (!Main.IsHostOrSingleplayer()) return 0;
            int totalSpawned = 0;
            int perOutput = Math.Max(1, Main.Settings?.MaxCarsPerHaul ?? 6);

            foreach (var facility in Economy.EconomyState.Instance.Facilities.Values)
            {
                var sc = StationController.GetStationByYardID(facility.YardId);
                if (sc == null) continue;

                if (deleteFirst)
                {
                    var idle = CollectIdleEmpties(sc, null, int.MaxValue);
                    if (idle.Count > 0)
                    {
                        foreach (var tc in idle)
                            if (tc.logicCar?.carGuid != null)
                                _guids.Remove(tc.logicCar.carGuid);
                        CarSpawner.Instance.DeleteTrainCars(idle, true);
                        Main.Log($"[CarPool] {facility.YardId}: cleared {idle.Count} idle empt{(idle.Count == 1 ? "y" : "ies")}.");
                    }
                }

                foreach (var cargo in facility.Outputs)
                    totalSpawned += SpawnEmpties(sc, cargo, perOutput);
            }
            Main.Log($"[CarPool] station pools respawned: {totalSpawned} car(s) total.");
            return totalSpawned;
        }

        [Serializable]
        private class SaveData
        {
            public int SchemaVersion;
            public List<string> Guids;
        }

        public void SaveTo(SaveGameData data) =>
            data.SetObject(SaveKey, new SaveData { SchemaVersion = SchemaVersion, Guids = _guids.ToList() });

        public void LoadFrom(SaveGameData data)
        {
            _guids = new HashSet<string>(StringComparer.Ordinal);
            SaveData payload = null;
            try { payload = data.GetObject<SaveData>(SaveKey); }
            catch (Exception ex) { Main.LogAlways($"[CarPool] save unreadable, starting empty: {ex.Message}"); }
            if (payload?.Guids != null && payload.SchemaVersion == SchemaVersion)
                _guids = new HashSet<string>(payload.Guids, StringComparer.Ordinal);
        }
    }
}
