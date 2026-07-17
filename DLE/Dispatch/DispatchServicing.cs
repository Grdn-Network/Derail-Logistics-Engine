using DLE.Economy;
using DLE.Jobs;
using DLE.Patches;
using DV.Logic.Job;
using DV.ThingTypes;
using DV.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DLE.Dispatch
{
    /// <summary>
    /// Dispatch-triggered load and unload servicing (#43). Two paths per action, decided
    /// by where the cars stand:
    /// - TERMINAL: every car is on the machine's warehouse track, so dispatch pulls the
    ///   machine's own lever remotely (StartLoadSequence / StartUnloadSequence): real
    ///   cargo logic, screens, sounds and vanilla pacing. The warehouse attach prefix
    ///   rides along exactly as if a crew pulled it.
    /// - STATION STAFF: cars parked elsewhere in the yard. Costs remoteSecondsPerCar per
    ///   car (config), needs the station to have staff for that action (remoteLoad /
    ///   remoteUnload), then moves the cargo directly and checks the warehouse task out
    ///   of the machine so it reads Done.
    /// Host or singleplayer only. Jobs must be taken first: dispatch can do that from the
    /// board too. Unloading never credits the economy by itself; the gated payout and
    /// stock credit belong to job turn-in.
    /// </summary>
    public static class DispatchServicing
    {
        public struct Result
        {
            public bool Ok;
            public string Message;
            public static Result Fail(string m) => new Result { Ok = false, Message = m };
            public static Result Done(string m) => new Result { Ok = true, Message = m };
        }

        public static Result LoadJob(string jobId, List<string> pickedCarIds = null)
        {
            if (!Main.IsHostOrSingleplayer()) return Result.Fail("host or singleplayer only");
            if (!StaticDirectHaulJobDefinition.jobDefinitions.TryGetValue(jobId, out var def) || def.LiveJob == null)
                return Result.Fail($"unknown job '{jobId}'");
            var job = def.LiveJob;
            if (job.State != JobState.InProgress)
                return Result.Fail($"job is {job.State}; take it first (Take button or booklet)");

            var originYard = def.chainData?.chainOriginYardId;
            var facility = originYard != null && EconomyState.Instance.Facilities.TryGetValue(originYard, out var f) ? f : null;
            if (facility != null && !facility.CanLoad)
                return Result.Fail($"{originYard} is unload-only");

            bool attached = def.carsToTransport != null && def.carsToTransport.Count > 0;
            List<Car> cars;
            if (attached)
            {
                if (pickedCarIds != null && pickedCarIds.Count > 0)
                    return Result.Fail("cars are already attached to this haul; loading works on those");
                cars = def.carsToTransport.ToList();
                if (cars.All(c => c.LoadedCargoAmount > 0f))
                    return Result.Fail("cargo is already loaded; haul it");

                // Staff only work their own station: attached cars must be back in the
                // origin yard before anyone loads them.
                var originSc = StationController.GetStationByYardID(originYard);
                var originTracks = StationTracks(originSc, def.loadMachine?.WarehouseTrack);
                var strays = cars.Where(c => c.CurrentTrack == null || !originTracks.Contains(c.CurrentTrack)).ToList();
                if (strays.Count > 0)
                    return Result.Fail($"{strays.Count}/{cars.Count} car(s) not at {originYard} ({string.Join(", ", strays.Take(4).Select(c => c.ID))})");
            }
            else
            {
                var sc = StationController.GetStationByYardID(originYard);
                if (sc == null) return Result.Fail($"origin {originYard} not loaded");
                int wanted = def.plannedCarCount > 0 ? def.plannedCarCount : def.displayCars?.Count ?? 0;
                if (wanted <= 0) return Result.Fail("job wants no cars");
                if (pickedCarIds != null && pickedCarIds.Count > 0)
                {
                    // Dispatcher-picked cars: exactly the booklet's count, every one a
                    // valid candidate (suitable, empty, jobless, unreserved, in the yard).
                    if (pickedCarIds.Count != wanted)
                        return Result.Fail($"job wants {wanted} car(s); {pickedCarIds.Count} picked");
                    var eligible = AllLoadCandidates(def, sc);
                    if (eligible == null)
                        return Result.Fail($"no car type carries {def.transportedCargo}");
                    var byId = eligible.ToDictionary(c => c.ID, c => c, StringComparer.Ordinal);
                    cars = new List<Car>();
                    foreach (var id in pickedCarIds)
                    {
                        if (!byId.TryGetValue(id, out var car))
                            return Result.Fail($"{id} is not a usable empty at {originYard} (moved, loaded, on a job, or wrong type)");
                        cars.Add(car);
                    }
                }
                else
                {
                    cars = CollectCandidates(def, sc, wanted, out var have);
                    if (cars == null)
                        return Result.Fail($"no car type carries {def.transportedCargo}");
                    if (cars.Count < wanted)
                        return Result.Fail($"only {have}/{wanted} suitable empties at {originYard}");
                }
            }

            var loadTrack = def.loadMachine?.WarehouseTrack;
            if (loadTrack != null && cars.All(c => c.CurrentTrack == loadTrack))
            {
                var controller = FindController(def.loadMachine);
                if (controller == null) return Result.Fail("load terminal not found");
                if (controller.LoadOrUnloadOngoing) return Result.Fail("the terminal is already running");
                controller.StartLoadSequence();
                Main.LogAlways($"[Servicing] {jobId}: terminal load started at {originYard}.");
                return Result.Done($"terminal loading at {originYard} track {loadTrack.ID?.FullDisplayID}");
            }

            if (facility == null || !facility.RemoteLoad)
                return Result.Fail($"{originYard} has no loading staff; bring the cars to {loadTrack?.ID?.FullDisplayID ?? "the loading track"}");

            float perCar = facility.RemoteSecondsPerCar;
            if (!DleDirectorBehaviour.TryRun(StaffLoadRoutine(def, job, cars, attached, perCar)))
                return Result.Fail("world not ready");
            return Result.Done($"station staff loading {cars.Count} car(s), one every {perCar:0}s (about {perCar * cars.Count:0}s)");
        }

        public static Result UnloadJob(string jobId)
        {
            if (!Main.IsHostOrSingleplayer()) return Result.Fail("host or singleplayer only");
            if (!StaticDirectHaulJobDefinition.jobDefinitions.TryGetValue(jobId, out var def) || def.LiveJob == null)
                return Result.Fail($"unknown job '{jobId}'");
            var job = def.LiveJob;
            if (job.State != JobState.InProgress)
                return Result.Fail($"job is {job.State}; take it first");

            var cars = def.carsToTransport;
            if (cars == null || cars.Count == 0)
                return Result.Fail("no cars attached yet; load the haul first");
            if (cars.All(c => c.LoadedCargoAmount == 0f))
                return Result.Fail("cargo is already unloaded; turn the haul in");

            var destYard = def.chainData?.chainDestinationYardId;
            var facility = destYard != null && EconomyState.Instance.Facilities.TryGetValue(destYard, out var f) ? f : null;
            if (facility != null && !facility.CanUnload)
                return Result.Fail($"{destYard} is load-only");

            var unloadTrack = def.unloadMachine?.WarehouseTrack;
            if (unloadTrack != null && cars.All(c => c.CurrentTrack == unloadTrack))
            {
                var controller = FindController(def.unloadMachine);
                if (controller == null) return Result.Fail("unload terminal not found");
                if (controller.LoadOrUnloadOngoing) return Result.Fail("the terminal is already running");
                controller.StartUnloadSequence();
                Main.LogAlways($"[Servicing] {jobId}: terminal unload started at {destYard}.");
                return Result.Done($"terminal unloading at {destYard} track {unloadTrack.ID?.FullDisplayID}");
            }

            var destSc = StationController.GetStationByYardID(destYard);
            var allowed = StationTracks(destSc, unloadTrack);
            var away = cars.Where(c => c.CurrentTrack == null || !allowed.Contains(c.CurrentTrack)).ToList();
            if (away.Count > 0)
                return Result.Fail($"{away.Count}/{cars.Count} car(s) not at {destYard} yet ({string.Join(", ", away.Take(4).Select(c => c.ID))})");

            if (facility == null || !facility.RemoteUnload)
                return Result.Fail($"{destYard} has no unloading staff; spot the cars on {unloadTrack?.ID?.FullDisplayID ?? "the unloading track"}");

            float perCar = facility.RemoteSecondsPerCar;
            if (!DleDirectorBehaviour.TryRun(StaffUnloadRoutine(def, job, cars.ToList(), perCar)))
                return Result.Fail("world not ready");
            return Result.Done($"station staff unloading {cars.Count} car(s), one every {perCar:0}s (about {perCar * cars.Count:0}s)");
        }

        // Helpers

        /// <summary>Every logic track belonging to a station's yard, plus an extra (the
        /// warehouse track, which may not be listed among the yard groups).</summary>
        internal static HashSet<Track> StationTracks(StationController sc, Track extra)
        {
            var set = new HashSet<Track>();
            var yard = sc?.logicStation?.yard;
            if (yard != null)
            {
                if (yard.StorageTracks != null) foreach (var t in yard.StorageTracks) set.Add(t);
                if (yard.TransferInTracks != null) foreach (var t in yard.TransferInTracks) set.Add(t);
                if (yard.TransferOutTracks != null) foreach (var t in yard.TransferOutTracks) set.Add(t);
            }
            if (extra != null) set.Add(extra);
            return set;
        }

        private static WarehouseMachineController FindController(WarehouseMachine machine)
        {
            if (machine == null) return null;
            foreach (var c in WarehouseMachineController.allControllers)
                if (c != null && c.warehouseMachine == machine) return c;
            return null;
        }

        /// <summary>
        /// Every suitable empty for a carless job anywhere in the origin yard: right car
        /// family, jobless, empty, not reserved for another haul; this job's reserved
        /// cars first, then cars already on the load track (cheapest to service). Null
        /// when no car type carries the cargo. The candidates API and the picker use the
        /// full list; auto-collection takes the head of it.
        /// </summary>
        internal static List<Car> AllLoadCandidates(StaticDirectHaulJobDefinition def, StationController sc)
        {
            if (!DV.Globals.G.Types.CargoType_to_v2.TryGetValue(def.transportedCargo, out var v2) ||
                !DV.Globals.G.Types.CargoToLoadableCarTypes.TryGetValue(v2, out var loadable))
                return null;

            var reservedElsewhere = new HashSet<string>(StringComparer.Ordinal);
            foreach (var other in StaticDirectHaulJobDefinition.jobDefinitions)
                if (other.Key != def.LiveJob.ID && other.Value?.reservedCarIds != null)
                    foreach (var rid in other.Value.reservedCarIds)
                        reservedElsewhere.Add(rid);

            var jobsManager = SingletonBehaviour<JobsManager>.Instance;
            var loadTrack = def.loadMachine?.WarehouseTrack;
            var pool = new List<Car>();
            foreach (var track in StationTracks(sc, loadTrack))
            {
                var cars = track.GetCarsFullyOnTrack();
                if (cars == null) continue;
                foreach (var car in cars)
                {
                    if (!loadable.Contains(car.carType.parentType)) continue;
                    if (car.playerSpawnedCar) continue; // never conscript a player's own cars
                    if (car.LoadedCargoAmount != 0f) continue;
                    if (jobsManager.GetJobOfCar(car) != null) continue;
                    if (reservedElsewhere.Contains(car.ID)) continue;
                    pool.Add(car);
                }
            }

            var reserved = def.reservedCarIds ?? new List<string>();
            return pool
                .OrderByDescending(c => reserved.Contains(c.ID))
                .ThenByDescending(c => loadTrack != null && c.CurrentTrack == loadTrack)
                .ToList();
        }

        private static List<Car> CollectCandidates(StaticDirectHaulJobDefinition def,
            StationController sc, int wanted, out int have)
        {
            var pool = AllLoadCandidates(def, sc);
            have = pool?.Count ?? 0;
            return pool?.Take(wanted).ToList();
        }

        /// <summary>
        /// Station staff work the cut the way the terminal does, one car per
        /// remoteSecondsPerCar, just on whatever track the cars stand: longer-loading
        /// pacing on any track. Everything is revalidated when
        /// each car comes up, so a job that dies or a consist that leaves mid-work stops
        /// the loading at that car instead of double-debiting or conjuring cargo.
        /// </summary>
        private static IEnumerator StaffLoadRoutine(StaticDirectHaulJobDefinition def, Job job,
            List<Car> cars, bool alreadyAttached, float perCar)
        {
            // The first staff member walks out to the cut.
            if (perCar > 0f) yield return new WaitForSeconds(perCar);

            // Attach commitment happens once, revalidated: without the recheck, a lever
            // attach mid-wait triggers a second CommitAttach (double stock debit,
            // overwritten consist).
            var originSc = StationController.GetStationByYardID(def.chainData?.chainOriginYardId);
            var originTracks = StationTracks(originSc, def.loadMachine?.WarehouseTrack);
            try
            {
                if (job.State != JobState.InProgress)
                {
                    Main.LogAlways($"[Servicing] {job.ID}: job is {job.State}; staff load aborted.");
                    yield break;
                }
                if (!alreadyAttached)
                {
                    if (def.carsToTransport != null && def.carsToTransport.Count > 0)
                    {
                        Main.LogAlways($"[Servicing] {job.ID}: cars attached by someone else during the staff walk-out; load aborted (debited once, not twice).");
                        yield break;
                    }
                    var jobsManager = SingletonBehaviour<JobsManager>.Instance;
                    cars = cars.Where(c => c != null && c.LoadedCargoAmount == 0f &&
                                           c.CurrentTrack != null && originTracks.Contains(c.CurrentTrack) &&
                                           jobsManager.GetJobOfCar(c) == null).ToList();
                    int wanted = def.plannedCarCount > 0 ? def.plannedCarCount : cars.Count;
                    if (cars.Count < wanted)
                    {
                        Main.LogAlways($"[Servicing] {job.ID}: cars were taken or moved during the staff walk-out; load aborted.");
                        yield break;
                    }
                    cars = cars.Take(wanted).ToList();
                    DleWarehouseLoadAttachPatch.CommitAttach(def, job, cars);
                }
            }
            catch (Exception ex)
            {
                Main.LogAlways($"[Servicing] {job.ID}: staff load failed: {ex.GetType().Name}: {ex.Message}");
                yield break;
            }

            // One car per interval, exactly like the terminal's paced loop but off-track.
            int loaded = 0;
            for (int i = 0; i < cars.Count; i++)
            {
                if (i > 0 && perCar > 0f) yield return new WaitForSeconds(perCar);
                try
                {
                    if (job.State != JobState.InProgress)
                    {
                        Main.LogAlways($"[Servicing] {job.ID}: job is {job.State}; staff load stopped at {loaded}/{cars.Count}.");
                        yield break;
                    }
                    var c = cars[i];
                    if (c == null || c.LoadedCargoAmount > 0f) continue;
                    if (c.CurrentTrack == null || !originTracks.Contains(c.CurrentTrack))
                    {
                        Main.LogAlways($"[Servicing] {job.ID}: {c.ID} left {def.chainData?.chainOriginYardId}; staff load stopped at {loaded}/{cars.Count}.");
                        yield break;
                    }
                    c.LoadCargo(c.capacity, def.transportedCargo);
                    loaded++;
                    def.loadedCarloads = Math.Max(def.loadedCarloads, loaded);
                }
                catch (Exception ex)
                {
                    Main.LogAlways($"[Servicing] {job.ID}: staff load of car {i + 1} failed: {ex.Message}");
                }
            }

            try
            {
                // Check the load task out of the machine so it reads Done; staff handled it.
                var loadTask = job.tasks.OfType<WarehouseTask>()
                    .FirstOrDefault(t => t.warehouseTaskType == WarehouseTaskType.Loading);
                if (loadTask != null) def.loadMachine?.RemoveWarehouseTask(loadTask);
                EconomyHistory.Record("loaded", def.chainData?.chainOriginYardId, def.transportedCargo.ToString(), loaded, job.ID);
                Main.LogAlways($"[Servicing] {job.ID}: staff loaded {loaded} {def.transportedCargo} at {def.chainData?.chainOriginYardId}.");
            }
            catch (Exception ex)
            {
                Main.LogAlways($"[Servicing] {job.ID}: staff load wrap-up failed: {ex.Message}");
            }
        }

        /// <summary>Mirror of the staff load: one car per interval, revalidated per car,
        /// so a consist that departs mid-work stops the unloading instead of having its
        /// cargo vaporized on the mainline.</summary>
        private static IEnumerator StaffUnloadRoutine(StaticDirectHaulJobDefinition def, Job job,
            List<Car> cars, float perCar)
        {
            var destSc = StationController.GetStationByYardID(def.chainData?.chainDestinationYardId);
            var allowed = StationTracks(destSc, def.unloadMachine?.WarehouseTrack);

            int unloaded = 0;
            for (int i = 0; i < cars.Count; i++)
            {
                if (perCar > 0f) yield return new WaitForSeconds(perCar);
                try
                {
                    if (job.State != JobState.InProgress)
                    {
                        Main.LogAlways($"[Servicing] {job.ID}: job is {job.State}; staff unload stopped at {unloaded}.");
                        yield break;
                    }
                    var c = cars[i];
                    if (c == null || c.LoadedCargoAmount <= 0f) continue;
                    if (c.CurrentTrack == null || !allowed.Contains(c.CurrentTrack))
                    {
                        Main.LogAlways($"[Servicing] {job.ID}: {c.ID} left {def.chainData?.chainDestinationYardId}; staff unload stopped at {unloaded}.");
                        yield break;
                    }
                    c.UnloadCargo(c.LoadedCargoAmount, c.CurrentCargoTypeInCar);
                    unloaded++;
                }
                catch (Exception ex)
                {
                    Main.LogAlways($"[Servicing] {job.ID}: staff unload of car {i + 1} failed: {ex.Message}");
                }
            }

            try
            {
                var unloadTask = job.tasks.OfType<WarehouseTask>()
                    .FirstOrDefault(t => t.warehouseTaskType == WarehouseTaskType.Unloading);
                if (unloadTask != null) def.unloadMachine?.RemoveWarehouseTask(unloadTask);
                EconomyHistory.Record("unloaded", def.chainData?.chainDestinationYardId, def.transportedCargo.ToString(), unloaded, job.ID);
                Main.LogAlways($"[Servicing] {job.ID}: staff unloaded {unloaded} car(s) at {def.chainData?.chainDestinationYardId}. Turn the haul in to get paid.");
            }
            catch (Exception ex)
            {
                Main.LogAlways($"[Servicing] {job.ID}: staff unload wrap-up failed: {ex.Message}");
            }
        }
    }
}
