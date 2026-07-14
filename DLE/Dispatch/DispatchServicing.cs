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

namespace DLE.Dispatch
{
    /// <summary>
    /// Dispatch-triggered load servicing: instead of a crew running the warehouse machine,
    /// dispatch loads a haul's cars from the board or API. Terminal service (cars already on
    /// the load track) is immediate; station-staff service (cars parked anywhere in the
    /// origin yard) costs a per-car time penalty and needs the station to have loading staff.
    /// Gated by the per-station role and staff config. Host or singleplayer only. The unload
    /// side follows in a later step.
    /// </summary>
    public static class DispatchServicing
    {
        public struct Result
        {
            public bool Ok;
            public string Message;
            public static Result Fail(string m) => new Result { Ok = false, Message = m };
            public static Result Started(string m) => new Result { Ok = true, Message = m };
        }

        public static Result LoadJob(string jobId)
        {
            if (!Main.IsHostOrSingleplayer()) return Result.Fail("host or singleplayer only");
            if (!StaticDirectHaulJobDefinition.jobDefinitions.TryGetValue(jobId, out var def) || def.LiveJob == null)
                return Result.Fail($"unknown job '{jobId}'");
            if (def.carsToTransport != null && def.carsToTransport.Count > 0)
                return Result.Fail("job already has its cars");

            var originYard = def.chainData?.chainOriginYardId;
            if (string.IsNullOrEmpty(originYard)) return Result.Fail("job has no origin");
            var facility = EconomyState.Instance.Facilities.TryGetValue(originYard, out var f) ? f : null;
            if (facility != null && !facility.CanLoad) return Result.Fail($"{originYard} is unload-only");

            var sc = StationController.GetStationByYardID(originYard);
            if (sc == null) return Result.Fail($"origin {originYard} not loaded");

            int wanted = def.plannedCarCount > 0 ? def.plannedCarCount : def.displayCars?.Count ?? 0;
            if (wanted <= 0) return Result.Fail("job wants no cars");

            if (!DV.Globals.G.Types.CargoType_to_v2.TryGetValue(def.transportedCargo, out var v2) ||
                !DV.Globals.G.Types.CargoToLoadableCarTypes.TryGetValue(v2, out var loadable))
                return Result.Fail($"no car type carries {def.transportedCargo}");

            // Cars reserved for other jobs are off-limits.
            var reservedElsewhere = new HashSet<string>(StringComparer.Ordinal);
            foreach (var other in StaticDirectHaulJobDefinition.jobDefinitions)
                if (other.Key != jobId && other.Value?.reservedCarIds != null)
                    foreach (var rid in other.Value.reservedCarIds)
                        reservedElsewhere.Add(rid);

            var jobsManager = SingletonBehaviour<JobsManager>.Instance;
            var loadTrack = def.loadMachine?.WarehouseTrack;
            var onTrack = new HashSet<Car>(loadTrack?.GetCarsFullyOnTrack() ?? new List<Car>());

            var pool = CollectOriginEmpties(sc, loadable, jobsManager, reservedElsewhere);
            // Prefer this job's reserved cars, then cars already on the load track (cheaper).
            var reserved = def.reservedCarIds ?? new List<string>();
            var ordered = pool
                .OrderByDescending(c => reserved.Contains(c.ID))
                .ThenByDescending(c => onTrack.Contains(c))
                .ToList();
            if (ordered.Count < wanted)
                return Result.Fail($"only {ordered.Count}/{wanted} suitable empties at {originYard}");
            var valid = ordered.Take(wanted).ToList();

            bool allOnTrack = loadTrack != null && valid.All(c => onTrack.Contains(c));
            if (!allOnTrack && (facility == null || !facility.RemoteLoad))
                return Result.Fail($"{originYard} has no loading staff; bring the cars to the load track");

            float penalty = allOnTrack ? 0f : (facility?.RemoteSecondsPerCar ?? 45f) * valid.Count;

            if (!DleDirectorBehaviour.TryRun(LoadRoutine(def, valid, penalty)))
                return Result.Fail("world not ready");
            return Result.Started(allOnTrack
                ? $"loading {valid.Count} car(s) at the terminal"
                : $"station staff loading {valid.Count} car(s) in about {penalty:0}s");
        }

        private static List<Car> CollectOriginEmpties(StationController sc, List<TrainCarType_v2> loadable,
            JobsManager jobsManager, HashSet<string> reservedElsewhere)
        {
            var result = new List<Car>();
            var yard = sc.logicStation?.yard;
            if (yard == null) return result;
            var tracks = new List<DV.Logic.Job.Track>();
            if (yard.StorageTracks != null) tracks.AddRange(yard.StorageTracks);
            if (yard.TransferInTracks != null) tracks.AddRange(yard.TransferInTracks);
            if (yard.TransferOutTracks != null) tracks.AddRange(yard.TransferOutTracks);
            foreach (var track in tracks)
            {
                var cars = track.GetCarsFullyOnTrack();
                if (cars == null) continue;
                foreach (var car in cars)
                {
                    if (!loadable.Contains(car.carType.parentType)) continue;
                    if (car.LoadedCargoAmount != 0f) continue;
                    if (jobsManager.GetJobOfCar(car) != null) continue;
                    if (reservedElsewhere.Contains(car.ID)) continue;
                    result.Add(car);
                }
            }
            return result;
        }

        private static IEnumerator LoadRoutine(StaticDirectHaulJobDefinition def, List<Car> valid, float penalty)
        {
            if (penalty > 0f) yield return new UnityEngine.WaitForSeconds(penalty);

            // Cars may have moved or been taken during the wait; re-check before committing.
            valid = valid.Where(c => c != null && c.LoadedCargoAmount == 0f).ToList();
            if (def.LiveJob == null || valid.Count == 0)
            {
                Main.LogAlways($"[Servicing] {def.LiveJob?.ID}: nothing loadable left after the wait; aborted.");
                yield break;
            }

            DleWarehouseLoadAttachPatch.CommitAttach(def, def.LiveJob, valid);
            foreach (var car in valid)
                car.LoadCargo(car.capacity, def.transportedCargo);

            Main.LogAlways($"[Servicing] {def.LiveJob.ID}: dispatch loaded {valid.Count} " +
                           $"{def.transportedCargo} at {def.chainData?.chainOriginYardId}.");
        }
    }
}
