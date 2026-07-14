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

        public static Result LoadJob(string jobId)
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
                cars = CollectCandidates(def, sc, wanted, out var have);
                if (cars == null)
                    return Result.Fail($"no car type carries {def.transportedCargo}");
                if (cars.Count < wanted)
                    return Result.Fail($"only {have}/{wanted} suitable empties at {originYard}");
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

            float penalty = (facility.RemoteSecondsPerCar) * cars.Count;
            if (!DleDirectorBehaviour.TryRun(StaffLoadRoutine(def, job, cars, attached, penalty)))
                return Result.Fail("world not ready");
            return Result.Done($"station staff loading {cars.Count} car(s), about {penalty:0}s");
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

            float penalty = facility.RemoteSecondsPerCar * cars.Count;
            if (!DleDirectorBehaviour.TryRun(StaffUnloadRoutine(def, job, cars.ToList(), penalty)))
                return Result.Fail("world not ready");
            return Result.Done($"station staff unloading {cars.Count} car(s), about {penalty:0}s");
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
        /// Suitable empties for a carless job anywhere in the origin yard: right car
        /// family, jobless, empty, not reserved for another haul; this job's reserved
        /// cars first, then cars already on the load track (cheapest to service).
        /// </summary>
        private static List<Car> CollectCandidates(StaticDirectHaulJobDefinition def,
            StationController sc, int wanted, out int have)
        {
            have = 0;
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
            have = pool.Count;

            var reserved = def.reservedCarIds ?? new List<string>();
            return pool
                .OrderByDescending(c => reserved.Contains(c.ID))
                .ThenByDescending(c => loadTrack != null && c.CurrentTrack == loadTrack)
                .Take(wanted)
                .ToList();
        }

        private static IEnumerator StaffLoadRoutine(StaticDirectHaulJobDefinition def, Job job,
            List<Car> cars, bool alreadyAttached, float penalty)
        {
            if (penalty > 0f) yield return new WaitForSeconds(penalty);
            try
            {
                // The world moved on during the wait; revalidate EVERYTHING. A lever
                // attach mid-wait used to trigger a second CommitAttach here (double
                // stock debit, overwritten consist), and an abandoned job got cargo
                // loaded into jobless pool cars.
                if (job.State != JobState.InProgress)
                {
                    Main.LogAlways($"[Servicing] {job.ID}: job is {job.State} after the staff wait; load aborted.");
                    yield break;
                }
                if (!alreadyAttached && def.carsToTransport != null && def.carsToTransport.Count > 0)
                {
                    Main.LogAlways($"[Servicing] {job.ID}: cars attached by someone else during the staff wait; load aborted (already debited once, not twice).");
                    yield break;
                }

                var originSc = StationController.GetStationByYardID(def.chainData?.chainOriginYardId);
                var originTracks = StationTracks(originSc, def.loadMachine?.WarehouseTrack);
                var jobsManager = SingletonBehaviour<JobsManager>.Instance;
                cars = cars.Where(c => c != null && c.LoadedCargoAmount == 0f &&
                                       c.CurrentTrack != null && originTracks.Contains(c.CurrentTrack) &&
                                       (alreadyAttached || jobsManager.GetJobOfCar(c) == null)).ToList();
                if (!alreadyAttached)
                {
                    int wanted = def.plannedCarCount > 0 ? def.plannedCarCount : cars.Count;
                    if (cars.Count < wanted)
                    {
                        Main.LogAlways($"[Servicing] {job.ID}: cars were taken or moved during the staff wait; load aborted.");
                        yield break;
                    }
                    cars = cars.Take(wanted).ToList();
                    DleWarehouseLoadAttachPatch.CommitAttach(def, job, cars);
                }
                else if (cars.Count == 0)
                {
                    Main.LogAlways($"[Servicing] {job.ID}: the consist left {def.chainData?.chainOriginYardId} during the staff wait; load aborted.");
                    yield break;
                }

                foreach (var c in cars)
                    c.LoadCargo(c.capacity, def.transportedCargo);
                def.loadedCarloads = Math.Max(def.loadedCarloads, cars.Count);

                // Check the load task out of the machine so it reads Done; staff handled it.
                var loadTask = job.tasks.OfType<WarehouseTask>()
                    .FirstOrDefault(t => t.warehouseTaskType == WarehouseTaskType.Loading);
                if (loadTask != null) def.loadMachine?.RemoveWarehouseTask(loadTask);

                Main.LogAlways($"[Servicing] {job.ID}: staff loaded {cars.Count} {def.transportedCargo} at {def.chainData?.chainOriginYardId}.");
            }
            catch (Exception ex)
            {
                Main.LogAlways($"[Servicing] {job.ID}: staff load failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static IEnumerator StaffUnloadRoutine(StaticDirectHaulJobDefinition def, Job job,
            List<Car> cars, float penalty)
        {
            if (penalty > 0f) yield return new WaitForSeconds(penalty);
            try
            {
                // Revalidate after the wait: unloading a train that departed mid-wait
                // vaporized its cargo on the mainline and force-completed the task.
                if (job.State != JobState.InProgress)
                {
                    Main.LogAlways($"[Servicing] {job.ID}: job is {job.State} after the staff wait; unload aborted.");
                    yield break;
                }
                var destSc = StationController.GetStationByYardID(def.chainData?.chainDestinationYardId);
                var allowed = StationTracks(destSc, def.unloadMachine?.WarehouseTrack);
                var away = cars.Where(c => c == null || c.CurrentTrack == null || !allowed.Contains(c.CurrentTrack)).ToList();
                if (away.Count > 0)
                {
                    Main.LogAlways($"[Servicing] {job.ID}: the consist left {def.chainData?.chainDestinationYardId} during the staff wait; unload aborted.");
                    yield break;
                }

                int unloaded = 0;
                foreach (var c in cars)
                {
                    if (c == null || c.LoadedCargoAmount <= 0f) continue;
                    c.UnloadCargo(c.LoadedCargoAmount, c.CurrentCargoTypeInCar);
                    unloaded++;
                }

                var unloadTask = job.tasks.OfType<WarehouseTask>()
                    .FirstOrDefault(t => t.warehouseTaskType == WarehouseTaskType.Unloading);
                if (unloadTask != null) def.unloadMachine?.RemoveWarehouseTask(unloadTask);

                Main.LogAlways($"[Servicing] {job.ID}: staff unloaded {unloaded} car(s) at {def.chainData?.chainDestinationYardId}. Turn the haul in to get paid.");
            }
            catch (Exception ex)
            {
                Main.LogAlways($"[Servicing] {job.ID}: staff unload failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
