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
        /// Two guards keep it exactly once without ever leaving the world empty:
        /// - if a prior session already put a pool in the world (its cars persisted even if
        ///   the flag did not), adopt it and spawn nothing, so reloads never stack a second
        ///   pool (never an automatic top-up);
        /// - the seeded flag latches only after cars actually spawn, so an interrupted or
        ///   failed seed retries next load instead of locking the world empty forever.
        /// </summary>
        public IEnumerator SeedOnceIfNeededRoutine()
        {
            if (PoolsSeeded || !Main.IsHostOrSingleplayer()) yield break;

            bool adopted = false;
            try { adopted = TryAdoptExistingPool(); }
            catch (Exception ex)
            {
                // This runs as a child of the director's TickLoop; an escaping exception
                // would kill the whole loop for the session. Fail toward seeding.
                Main.LogAlways($"[CarPool] pool adoption check failed ({ex.GetType().Name}: {ex.Message}); seeding instead.");
            }
            if (adopted)
            {
                PoolsSeeded = true;
                yield break;
            }

            int spawned = 0;
            yield return RespawnStationPoolsRoutine(deleteFirst: false, n => spawned = n);
            if (spawned > 0)
            {
                PoolsSeeded = true;
                Main.LogAlways($"[CarPool] one-time starter pools seeded for this save ({spawned} car(s)).");
            }
            else
            {
                Main.LogAlways("[CarPool] seeding produced no cars; will retry next load.");
            }
        }

        /// <summary>
        /// A save whose cars persisted but whose DLE state did not still holds a fleet;
        /// adopt it instead of stacking a second pool on top. Adoption REGISTERS the cars
        /// in the pool: unregistered cars are unprotected from the unused-car deleter and
        /// invisible to the cap accounting. Only a
        /// clearly-seeded world (a full pool's worth of idle empties) is adopted; a handful
        /// of stray leftovers is not, and seeding proceeds.
        /// </summary>
        private bool TryAdoptExistingPool()
        {
            if (_guids.Count > 0) return true;
            var found = new List<TrainCar>();
            foreach (var facility in Economy.EconomyState.Instance.Facilities.Values)
            {
                var sc = StationController.GetStationByYardID(facility.YardId);
                if (sc == null) continue;
                found.AddRange(CollectIdleEmpties(sc, null, int.MaxValue));
            }
            // A single haul's worth of leftovers is not a pool: expired vanilla jobs leave
            // that much lying around. Require several hauls' worth before adopting.
            int threshold = 18; // three typical hauls' worth
            if (found.Count < threshold) return false;

            foreach (var tc in found)
                if (tc.logicCar?.carGuid != null)
                    _guids.Add(tc.logicCar.carGuid);
            Main.LogAlways($"[CarPool] adopted an existing pool of {found.Count} idle car(s); they are protected and counted, nothing spawned.");
            return true;
        }

        /// <summary>
        /// Spawn empty cars suited to a cargo on a free storage track at the station,
        /// varying the livery per car. Returns how many were spawned. Pass a per-sweep
        /// claimed-length ledger so repeated calls in one sweep spread across tracks:
        /// Track.OccupiedLength does not update within the frame, so without it consecutive
        /// spawns re-pick the same physically-free track and stack cars into a derail.
        /// </summary>
        public int SpawnEmpties(StationController station, CargoType cargo, int count,
            IDictionary<JobTrack, float> claimed = null, List<TrainCar> collector = null)
        {
            if (station == null || count <= 0) return 0;

            // Standalone empties request (not part of a sweep, which prunes for itself):
            // drop guids whose cars died since load first, so the cap is measured against
            // the live fleet, not phantoms left by radio-clears or MP despawns.
            if (collector == null) PruneDeadGuids();

            // Hard fleet cap: no code path (seeding, respawn, empties API) may push the
            // pool past it. An unbounded pool degrades physics, saves and MP sync; the cap
            // is the backstop if a bug ever compounds the fleet.
            int cap = Math.Max(0, Economy.RecipeProvider.Tuning.maxPoolCars);
            int room = cap - _guids.Count;
            if (room <= 0)
            {
                Main.LogAlways($"[CarPool] pool is at its cap ({cap}); refusing to spawn more. Raise MaxPoolCars in settings if this is intended.");
                return 0;
            }
            if (count > room)
            {
                Main.LogAlways($"[CarPool] trimming spawn from {count} to {room} car(s) to respect the {cap}-car pool cap.");
                count = room;
            }

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

            // One cut per EMPTY track. The physics overlap check (Strict) is blind at
            // stations whose cells are not streamed in, so the only streaming-proof
            // guarantee against stacking is the logic layer: a track with zero cars on it
            // and no claim from this sweep cannot hold anything to stack onto. Stations
            // with more outputs than free storage tracks shortfall and say so.
            System.Collections.Generic.List<Track> FittingTracks(float len) =>
                station.logicStation.yard.StorageTracks
                    .Where(t => (t.GetCarsFullyOnTrack()?.Count ?? 0) == 0)
                    .Where(t => ClaimedOn(claimed, t) <= 0f)
                    .Where(t => t.length - 10.0 > len)
                    .ToList();

            var fitting = FittingTracks(length);

            // A station whose remaining empty tracks are all short loses the whole output
            // when the full cut cannot fit anywhere; half a cut on a short track beats
            // zero cars for that cargo.
            if (fitting.Count == 0 && count > 2)
            {
                int half = Math.Max(2, count / 2);
                var halfLiveries = liveries.Take(half).ToList();
                float halfLength = CarSpawner.Instance.GetTotalCarLiveriesLength(halfLiveries, true);
                var halfFitting = FittingTracks(halfLength);
                if (halfFitting.Count > 0)
                {
                    Main.Log($"[CarPool] {station.stationInfo.YardID}: no track fits {count} cars ({length:F0}m); spawning {half} instead.");
                    count = half;
                    liveries = halfLiveries;
                    length = halfLength;
                    fitting = halfFitting;
                }
            }

            if (fitting.Count == 0)
            {
                Main.LogAlways($"[CarPool] {station.stationInfo.YardID}: no empty storage track fits {count} empt{(count == 1 ? "y" : "ies")} ({length:F0}m); nothing spawned.");
                return 0;
            }
            var track = fitting
                .OrderByDescending(t => t.length)
                .First();

            var railTrack = RailTrackRegistry.LogicToRailTrack[track];

            // Spawn STRICT: it physically overlap-checks every car (IsBoxOverlapping) and
            // refuses with Blocked/CannotFitOnTrack instead of placing. The middle-based
            // spawn the pool used before centers every cut on the track midpoint with no
            // overlap check at all, which is what piled cuts on top of each other. A
            // Blocked anchor just means try the next spot along the track.
            List<TrainCar> spawned = null;
            double margin = 5.0;
            double maxStart = track.length - length - margin;
            if (maxStart >= margin)
            {
                for (int attempt = 0; attempt < 5 && (spawned == null || spawned.Count == 0); attempt++)
                {
                    double startSpan = margin + (maxStart - margin) * attempt / 4.0;
                    spawned = CarSpawner.Instance.SpawnCarTypesOnTrackStrict(
                        liveries, railTrack, true, true, startSpan, false, true, false);
                }
            }
            if (spawned == null || spawned.Count == 0)
            {
                Main.LogAlways($"[CarPool] {station.stationInfo.YardID}: no clear spot for {count} car(s) on {track.ID?.FullDisplayID}; nothing spawned.");
                return 0;
            }

            foreach (var tc in spawned)
                if (tc.logicCar?.carGuid != null)
                    _guids.Add(tc.logicCar.carGuid);

            if (claimed != null)
                claimed[track] = ClaimedOn(claimed, track) + length;

            if (collector != null)
                collector.AddRange(spawned);
            else
                // One-off spawn (empties API): quarantine-check this cut on its own.
                Economy.DleDirectorBehaviour.TryRun(ValidateAndSleepRoutine(new List<TrainCar>(spawned), null));

            Main.Log($"[CarPool] spawned {spawned.Count} empty {carType.name} at " +
                     $"{station.stationInfo.YardID} track {track.ID?.FullDisplayID} (pool now {_guids.Count}).");
            return spawned.Count;
        }

        /// <summary>
        /// Bad-spawn quarantine plus instant sleep. A car that spawns derailed never
        /// becomes stationary, never sleeps, drags the framerate down and corrupts the
        /// save it is written into (corrupted consists hang save loading), so after physics settles the
        /// cut, anything derailed is deleted on the spot. Healthy cars are force-slept
        /// through the game's own optimizer API instead of waiting out its stationary
        /// timer; the TrainsOptimizer wakes them again when live traffic reaches them.
        /// </summary>
        private IEnumerator ValidateAndSleepRoutine(List<TrainCar> cars, Action<int> onDeleted)
        {
            yield return new WaitForSeconds(1f);

            var bad = new List<TrainCar>();
            int slept = 0;
            List<KeyValuePair<TrainCar, Vector3>> snapshot;
            try { snapshot = SnapshotAllCars(); }
            catch (Exception ex)
            {
                Main.LogAlways($"[CarPool] validation snapshot failed ({ex.Message}); sleeping cars unvalidated.");
                snapshot = new List<KeyValuePair<TrainCar, Vector3>>();
            }
            foreach (var tc in cars)
            {
                if (tc == null) continue;
                try
                {
                    // Wrecked (derailed or impossibly tilted) OR interpenetrating another
                    // car: sleeping it just plants a mine that detonates when it wakes.
                    if (IsWreck(tc) || OverlapsAnotherCar(tc, snapshot)) { bad.Add(tc); continue; }
                    tc.ForceSleep(true);
                    slept++;
                }
                catch (Exception ex)
                {
                    bad.Add(tc);
                    Main.LogAlways($"[CarPool] {tc.ID}: validation failed ({ex.GetType().Name}); quarantining it.");
                }
            }

            if (bad.Count > 0)
            {
                int gone = 0;
                var single = new List<TrainCar>(1);
                foreach (var tc in bad)
                {
                    try
                    {
                        if (tc.logicCar?.carGuid != null)
                            _guids.Remove(tc.logicCar.carGuid);
                        single.Clear();
                        single.Add(tc);
                        CarSpawner.Instance.DeleteTrainCars(single, true);
                        gone++;
                    }
                    catch (Exception ex)
                    {
                        Main.LogAlways($"[CarPool] could not delete quarantined {tc?.ID}: {ex.Message}");
                    }
                }
                Main.LogAlways($"[CarPool] quarantined {gone}/{bad.Count} car(s) that spawned derailed or overlapping; they are deleted, not saved.");
            }
            Main.LogAlways($"[CarPool] {slept} spawned car(s) put to sleep.");
            onDeleted?.Invoke(bad.Count);
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
        /// A car that is physically wrong even though the derail flag never tripped:
        /// either the game says it derailed, or it is tilted harder than any DV track
        /// could tilt it (steepest grades are ~4 degrees; a jobless car pitched past 8 is
        /// resting ON something, the frozen ramp piles). Sleep freezes these mid-crash, so
        /// the flag alone misses them.
        /// </summary>
        private static bool IsWreck(TrainCar tc)
        {
            if (tc == null) return false;
            if (tc.derailed) return true;
            return Vector3.Angle(tc.transform.up, Vector3.up) > 8f;
        }

        /// <summary>
        /// True when a car is coupled into a consist that contains a locomotive: a crew is
        /// actively hauling it, so recovery must leave it alone even though no job is
        /// attached (carless hauls attach only on arrival at the producer).
        /// </summary>
        private static bool InPlayerConsist(TrainCar tc)
        {
            var set = tc?.trainset;
            if (set?.cars == null) return false;
            foreach (var c in set.cars)
                if (c != null && c.IsLoco) return true;
            return false;
        }

        /// <summary>
        /// Interpenetration distance: coupled cars on one track sit ~10m apart center to
        /// center and true stacked spawns sit near 0-2m, while parallel tracks (DoubleTrack
        /// lays them at 4.0-4.5m centers) put healthy cars abreast just past 3.5m. 5m used
        /// to false-positive on those neighbors and delete healthy stock.
        /// </summary>
        private const float OverlapDistance = 3.5f;
        private const float OverlapDistanceSq = OverlapDistance * OverlapDistance;

        /// <summary>
        /// Cars frozen inside each other are physics mines that fire apart on wake; sleep
        /// hides them from every derail flag, so overlap is detected geometrically against
        /// a position snapshot (one native transform fetch per car instead of per pair).
        /// </summary>
        private static List<KeyValuePair<TrainCar, Vector3>> SnapshotAllCars()
        {
            var list = new List<KeyValuePair<TrainCar, Vector3>>();
            foreach (var other in TrainCarRegistry.Instance.logicCarToTrainCar.Values)
                if (other != null)
                    list.Add(new KeyValuePair<TrainCar, Vector3>(other, other.transform.position));
            return list;
        }

        private static bool OverlapsAnotherCar(TrainCar tc, List<KeyValuePair<TrainCar, Vector3>> snapshot)
        {
            if (tc == null) return false;
            var pos = tc.transform.position;
            foreach (var kv in snapshot)
            {
                if (ReferenceEquals(kv.Key, tc)) continue;
                if ((kv.Value - pos).sqrMagnitude < OverlapDistanceSq)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Recovery bulldozer for company.respawn: idle empties in every economy yard,
        /// PLUS any derailed jobless empty non-player freight car anywhere in the world
        /// (wreckage flies off its track, so the yard sweep cannot see it), PLUS any pair
        /// of such cars interpenetrating anywhere (sleeping mines from the stacked-spawn
        /// era; they detonate on wake, so they go now instead). Deletion is one car at a
        /// time inside try/catch: a consist the game itself cannot split (the
        /// Trainset.Split ArgumentOutOfRangeException, which corrupts saves and would kill
        /// this whole routine mid-delete) costs that single car, and the
        /// recovery carries on.
        /// </summary>
        private void DeleteRecoverableCars()
        {
            var targets = new HashSet<TrainCar>();

            foreach (var facility in Economy.EconomyState.Instance.Facilities.Values)
            {
                var sc = StationController.GetStationByYardID(facility.YardId);
                if (sc == null) continue;
                foreach (var tc in CollectIdleEmpties(sc, null, int.MaxValue))
                    targets.Add(tc);
            }

            // Cars a live carless haul reserved but has not yet attached: they ride to the
            // producer before CommitAttach, so GetJobOfCar is null the whole trip. Deleting
            // them mid-run would annihilate the exact dispatched move this mod exists to
            // create.
            var reservedByLiveJobs = new HashSet<string>(StringComparer.Ordinal);
            foreach (var kv in Jobs.StaticDirectHaulJobDefinition.jobDefinitions)
                if (kv.Value?.LiveJob != null && kv.Value.reservedCarIds != null)
                    foreach (var rid in kv.Value.reservedCarIds)
                        reservedByLiveJobs.Add(rid);

            // Every jobless empty non-player freight car on the map, wherever it stands.
            var jobsManager = DV.Utils.SingletonBehaviour<JobsManager>.Instance;
            var freeCars = new List<TrainCar>();
            foreach (var kv in TrainCarRegistry.Instance.logicCarToTrainCar)
            {
                var car = kv.Key;
                var tc = kv.Value;
                if (car == null || tc == null) continue;
                if (tc.IsLoco || car.playerSpawnedCar) continue;
                if (car.LoadedCargoAmount > 0f) continue;
                if (jobsManager.GetJobOfCar(car) != null) continue;
                if (reservedByLiveJobs.Contains(car.ID)) continue;
                // Coupled into a consist that has a locomotive: a crew is hauling this cut
                // right now (empties on their way to a producer, say). Never delete it out
                // from under a moving train.
                if (InPlayerConsist(tc)) continue;
                freeCars.Add(tc);
                if (IsWreck(tc)) targets.Add(tc);

                // Pool-registered cars are DLE's own fleet: a jobless empty pool car is
                // recoverable wherever it stands, not only in a yard. Strays left outside
                // yards otherwise survive every respawn and eat the pool cap while the
                // repack shortfalls. Loaded cars and cars on jobs never reach this line.
                if (car.carGuid != null && _guids.Contains(car.carGuid)) targets.Add(tc);
            }

            // Mine sweep: interpenetrating pairs among the free cars. Positions are
            // snapshotted once (not re-fetched per pair), and the threshold sits below
            // parallel-track spacing (DoubleTrack lays neighbors at 4.0-4.5m centers) so
            // healthy cars standing abreast are never mistaken for a stack.
            int mines = 0;
            var positions = new Vector3[freeCars.Count];
            for (int i = 0; i < freeCars.Count; i++)
                positions[i] = freeCars[i].transform.position;
            for (int i = 0; i < freeCars.Count; i++)
                for (int j = i + 1; j < freeCars.Count; j++)
                {
                    if ((positions[i] - positions[j]).sqrMagnitude >= OverlapDistanceSq)
                        continue;
                    if (targets.Add(freeCars[i])) mines++;
                    if (targets.Add(freeCars[j])) mines++;
                }
            if (mines > 0)
                Main.LogAlways($"[CarPool] found {mines} overlapping car(s) waiting to detonate; deleting them.");

            int deleted = 0, stubborn = 0;
            var single = new List<TrainCar>(1);
            foreach (var tc in targets)
            {
                if (tc == null) continue;
                try
                {
                    var guid = tc.logicCar?.carGuid;
                    if (guid != null) _guids.Remove(guid);
                    single.Clear();
                    single.Add(tc);
                    CarSpawner.Instance.DeleteTrainCars(single, true);
                    deleted++;
                }
                catch (Exception ex)
                {
                    stubborn++;
                    Main.LogAlways($"[CarPool] could not delete {tc.ID}: {ex.GetType().Name}: {ex.Message}. Rerail or remove it by hand.");
                }
            }
            Main.LogAlways($"[CarPool] recovery cleared {deleted} car(s)" +
                (stubborn > 0 ? $"; {stubborn} refused to delete (broken consists, see log)." : "."));
        }

        /// <summary>
        /// Give every producer a working pool, spread one station per frame so a large fill
        /// neither hitches nor stacks. When deleteFirst is set, clears the idle jobless
        /// empties in each economy yard first (company.respawn recovery) and waits for the
        /// deletions to settle. A per-sweep claimed-length ledger keeps consecutive spawns
        /// off the same track. Company.respawn and new-game seeding both come through here.
        /// Host or singleplayer only.
        /// </summary>
        /// <summary>
        /// True while a pool sweep (seed or respawn) is running. Two interleaved sweeps
        /// each see the same tracks as free with separate ledgers and stack cuts, and the
        /// second sweep's bulldozer eats the first's unslept cars; single-flight only.
        /// Reset by the director on every world load so a dead coroutine cannot wedge it.
        /// </summary>
        internal static bool SweepInFlight;

        public IEnumerator RespawnStationPoolsRoutine(bool deleteFirst, Action<int> onDone = null)
        {
            if (!Main.IsHostOrSingleplayer()) { onDone?.Invoke(0); yield break; }
            if (SweepInFlight)
            {
                Main.LogAlways("[CarPool] a pool sweep is already running; ignoring this one.");
                onDone?.Invoke(0);
                yield break;
            }
            SweepInFlight = true;
            int totalSpawned = 0;
            var claimed = new Dictionary<JobTrack, float>();

            if (deleteFirst)
            {
                DeleteRecoverableCars();
                // Let the deletions settle so freed track length is visible before respawn.
                yield return new WaitForSeconds(0.5f);
            }

            // Guids of cars that died since load (any cause) otherwise count against the
            // cap and shrink the repack by exactly that many phantom cars.
            PruneDeadGuids();

            // Each producer offers its empty storage tracks, longest first; each track is
            // packed with a random mix of the cargos that producer ships. Consumers with
            // no outputs offer nothing and stay clear for deliveries.
            var offers = new List<(StationController sc, Economy.FacilityDef facility, Queue<JobTrack> tracks)>();
            foreach (var facility in Economy.EconomyState.Instance.Facilities.Values)
            {
                if (facility.Outputs.Count == 0) continue;
                var sc = StationController.GetStationByYardID(facility.YardId);
                if (sc == null) continue;
                var empties = sc.logicStation.yard.StorageTracks
                    .Where(t => (t.GetCarsFullyOnTrack()?.Count ?? 0) == 0)
                    .OrderByDescending(t => t.length)
                    .ToList();
                if (empties.Count > 0)
                    offers.Add((sc, facility, new Queue<JobTrack>(empties)));
            }

            // One track per station per round, so the pool cap lands evenly across the
            // map instead of starving whichever stations iterate last.
            var spawnedCars = new List<TrainCar>();
            int cap = Math.Max(0, Economy.RecipeProvider.Tuning.maxPoolCars);
            bool capHit = false;
            for (bool any = offers.Count > 0; any && !capHit;)
            {
                any = false;
                foreach (var offer in offers)
                {
                    if (offer.tracks.Count == 0) continue;
                    any = true;
                    var track = offer.tracks.Dequeue();

                    // One failed station must not kill the whole map's respawn: a silent
                    // coroutine death here is exactly what left stacked cuts unvalidated.
                    try { totalSpawned += PackTrack(offer.sc, offer.facility, track, claimed, spawnedCars); }
                    catch (Exception ex)
                    {
                        Main.LogAlways($"[CarPool] {offer.facility.YardId}: packing {track.ID?.FullDisplayID} failed: {ex.GetType().Name}: {ex.Message}");
                    }

                    if (_guids.Count >= cap)
                    {
                        Main.LogAlways($"[CarPool] pool cap ({cap}) reached mid-respawn; remaining tracks stay empty.");
                        capHit = true;
                        break;
                    }

                    // One track per frame-slice: spread the cost, let physics settle.
                    yield return null;
                }
            }

            // Quarantine anything that spawned derailed and sleep the healthy cars.
            int deleted = 0;
            yield return ValidateAndSleepRoutine(spawnedCars, n => deleted = n);
            totalSpawned -= deleted;

            SweepInFlight = false;
            Main.LogAlways($"[CarPool] station pools respawned: {totalSpawned} car(s) total.");
            onDone?.Invoke(totalSpawned);
        }

        /// <summary>
        /// Fill one empty storage track with a random mix of empty cars matching the
        /// producer's outputs, up to the configured fill fraction of the track and the
        /// pool cap. The track was empty when collected and is re-checked here; the cut
        /// spawns Strict from the low anchor, trimming itself shorter if the spawner
        /// refuses, so a partially blocked track yields a shorter cut instead of nothing.
        /// </summary>
        private int PackTrack(StationController station, Economy.FacilityDef facility,
            JobTrack track, IDictionary<JobTrack, float> claimed, List<TrainCar> collector)
        {
            int cap = Math.Max(0, Economy.RecipeProvider.Tuning.maxPoolCars);
            int room = cap - _guids.Count;
            if (room < 2) return 0;
            if ((track.GetCarsFullyOnTrack()?.Count ?? 0) != 0 || ClaimedOn(claimed, track) > 0f)
                return 0;

            // Livery pools per output cargo; outputs nothing can carry are skipped.
            var perCargo = new List<System.Collections.Generic.IList<TrainCarLivery>>();
            foreach (var cargo in facility.Outputs)
                if (DV.Globals.G.Types.CargoType_to_v2.TryGetValue(cargo, out var v2) &&
                    DV.Globals.G.Types.CargoToLoadableCarTypes.TryGetValue(v2, out var carTypes) &&
                    carTypes.Count > 0 && carTypes[0].liveries != null && carTypes[0].liveries.Count > 0)
                    perCargo.Add(carTypes[0].liveries);
            if (perCargo.Count == 0) return 0;

            int fillPercent = Mathf.Clamp(Economy.RecipeProvider.Tuning.poolTrackFillPercent, 10, 100);
            double usable = track.length * fillPercent / 100.0 - 10.0;
            if (usable <= 0) return 0;

            // Draw random cars until the next one no longer fits the fill target.
            var liveries = new List<TrainCarLivery>();
            while (liveries.Count < room)
            {
                var set = perCargo[UnityEngine.Random.Range(0, perCargo.Count)];
                liveries.Add(set[UnityEngine.Random.Range(0, set.Count)]);
                if (CarSpawner.Instance.GetTotalCarLiveriesLength(liveries, true) > usable)
                {
                    liveries.RemoveAt(liveries.Count - 1);
                    break;
                }
            }

            // Spawn Strict, trimming the cut when the spawner refuses: a physically
            // blocked stretch costs cars, not the whole track.
            const double margin = 5.0;
            List<TrainCar> spawned = null;
            var railTrack = RailTrackRegistry.LogicToRailTrack[track];
            while (liveries.Count >= 2)
            {
                float length = CarSpawner.Instance.GetTotalCarLiveriesLength(liveries, true);
                double maxStart = track.length - length - margin;
                if (maxStart >= margin)
                {
                    for (int attempt = 0; attempt < 5 && (spawned == null || spawned.Count == 0); attempt++)
                    {
                        double startSpan = margin + (maxStart - margin) * attempt / 4.0;
                        spawned = CarSpawner.Instance.SpawnCarTypesOnTrackStrict(
                            liveries, railTrack, true, true, startSpan, false, true, false);
                    }
                    if (spawned != null && spawned.Count > 0) break;
                }
                liveries.RemoveRange(liveries.Count - Math.Max(1, liveries.Count / 4), Math.Max(1, liveries.Count / 4));
            }
            if (spawned == null || spawned.Count == 0)
            {
                Main.Log($"[CarPool] {station.stationInfo.YardID}: no clear spot on {track.ID?.FullDisplayID}; track skipped.");
                return 0;
            }

            foreach (var tc in spawned)
                if (tc.logicCar?.carGuid != null)
                    _guids.Add(tc.logicCar.carGuid);
            claimed[track] = ClaimedOn(claimed, track) + CarSpawner.Instance.GetTotalCarLiveriesLength(
                spawned.Select(tc => tc.carLivery).ToList(), true);
            collector?.AddRange(spawned);

            Main.Log($"[CarPool] packed {spawned.Count} empties at {station.stationInfo.YardID} " +
                     $"track {track.ID?.FullDisplayID} (pool now {_guids.Count}).");
            return spawned.Count;
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

        /// <summary>
        /// Drop guids whose car no longer exists (deleted via comms radio, MP despawn,
        /// game cleanup): they are dead weight that only counts against MaxPoolCars, and
        /// left alone the counted fleet creeps to the cap while the real one shrinks.
        /// Runs at load, when the registry already holds every restored car.
        /// </summary>
        public void PruneDeadGuids()
        {
            if (_guids.Count == 0) return;
            var alive = new HashSet<string>(StringComparer.Ordinal);
            foreach (var kv in TrainCarRegistry.Instance.logicCarToTrainCar)
                if (kv.Key?.carGuid != null)
                    alive.Add(kv.Key.carGuid);
            int before = _guids.Count;
            _guids.RemoveWhere(g => !alive.Contains(g));
            if (_guids.Count != before)
                Main.LogAlways($"[CarPool] pruned {before - _guids.Count} dead pool guid(s); pool now {_guids.Count}.");
        }

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
            else if (payload?.Guids != null)
            {
                // Schema mismatch (a mod downgrade against a newer save): the guid list is
                // dropped, so the restored fleet is unprotected from the deleter until
                // adoption re-registers the idle empties. Say so unconditionally.
                Main.LogAlways($"[CarPool] pool save schema {payload.SchemaVersion} != {SchemaVersion}; pool starts empty and re-adopts idle cars on seed. Run company.respawn if cars go missing.");
            }
            _loadedFrom = data;
        }

        private SaveGameData _loadedFrom;

        /// <summary>
        /// Arm the pool from the current save if it has not been read yet. The game
        /// decides which jobless cars to delete DURING world load, before OnWorldLoaded
        /// fires; a guard that waits for OnWorldLoaded is armed after the kill list is
        /// already made and the whole fleet is condemned on every load. Reference
        /// comparison keeps the fast path allocation-free; a new world means a new
        /// SaveGameData instance and triggers a fresh read.
        /// </summary>
        public void EnsureLoaded()
        {
            var data = SaveGameManager.Instance?.data;
            if (data == null || ReferenceEquals(_loadedFrom, data)) return;
            LoadFrom(data);
            Main.LogAlways($"[CarPool] pool armed early from save data: {_guids.Count} guid(s).");
        }
    }
}
