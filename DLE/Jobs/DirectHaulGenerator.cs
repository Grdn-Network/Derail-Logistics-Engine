using DV.Booklets;
using DV.Logic.Job;
using DV.ThingTypes;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DLE.Jobs
{
    /// <summary>
    /// Builds Company Hauls. Every haul is carless: it carries a load task and synthetic
    /// booklet cars; crews bring cars to the producer's loading track, the servicing patch
    /// attaches them (booklets printed after that show the real cars), and the producer's
    /// stock debits at that moment. Host or singleplayer only; the EconomyDirector gates
    /// creation on real stock. TryRebuild restores saved jobs, attached cars included.
    /// </summary>
    public static class DirectHaulGenerator
    {
        /// <summary>
        /// Length, tare and cargo capacity for a livery, read from its prefab's TrainCar
        /// exactly the way InitializeNewLogicCar builds real logic cars (length is the
        /// inter-coupler distance, tare is the parent type's mass). Cached per livery;
        /// falls back to the spawner's length math and capacity 1 if the prefab read
        /// fails, so the booklet degrades to approximate stats instead of dying.
        /// </summary>
        private static readonly Dictionary<TrainCarLivery, (float length, float tare, float capacity)>
            _liveryDisplayData = new Dictionary<TrainCarLivery, (float, float, float)>();

        internal static (float length, float tare, float capacity) LiveryDisplayData(TrainCarLivery livery)
        {
            if (_liveryDisplayData.TryGetValue(livery, out var cached)) return cached;
            float length = 0f, capacity = 1f;
            try
            {
                var proto = livery.prefab != null ? livery.prefab.GetComponent<TrainCar>() : null;
                if (proto != null)
                {
                    length = proto.InterCouplerDistance;
                    capacity = proto.cargoCapacity;
                }
            }
            catch { }
            if (length <= 0f)
            {
                try
                {
                    length = CarSpawner.Instance.GetTotalCarLiveriesLength(
                        new List<TrainCarLivery> { livery }, false);
                }
                catch { }
            }
            var data = (length, livery.parentType?.mass ?? 0f, capacity);
            _liveryDisplayData[livery] = data;
            return data;
        }

        /// <summary>
        /// Board estimates (#100): what a haul of this shape would weigh, measure and
        /// pay, using the same livery data and payment call the real generator uses.
        /// Returns false when the cargo has no loadable car type.
        /// </summary>
        public static bool EstimateHaul(
            StationController producer,
            StationController consumer,
            CargoType cargo,
            int carCount,
            out float tonnes,
            out float lengthMeters,
            out float pay)
        {
            tonnes = 0f; lengthMeters = 0f; pay = 0f;
            if (producer == null || consumer == null || carCount <= 0) return false;
            if (!DV.Globals.G.Types.CargoType_to_v2.TryGetValue(cargo, out var v2) ||
                !DV.Globals.G.Types.CargoToLoadableCarTypes.TryGetValue(v2, out var carTypes) ||
                carTypes.Count == 0)
                return false;
            var usableTypes = carTypes.Where(t => t.liveries != null && t.liveries.Count > 0).ToList();
            if (usableTypes.Count == 0) return false;

            var liveryCounts = new Dictionary<TrainCarLivery, int>();
            float massKg = 0f;
            for (int i = 0; i < carCount; i++)
            {
                var shownType = usableTypes[i % usableTypes.Count];
                var livery = shownType.liveries[i % shownType.liveries.Count];
                var (length, tare, capacity) = LiveryDisplayData(livery);
                lengthMeters += length;
                massKg += tare + capacity * v2.massPerUnit;
                liveryCounts.TryGetValue(livery, out var n);
                liveryCounts[livery] = n + 1;
            }
            tonnes = massKg / 1000f;

            float distance = JobPaymentCalculator.GetDistanceBetweenStations(producer, consumer);
            pay = JobPaymentCalculator.CalculateJobPayment(
                JobType.Transport, distance,
                new PaymentCalculationData(liveryCounts, new Dictionary<CargoType, int> { { cargo, carCount } }));
            return true;
        }

        public static string TryCreateCarless(
            StationController producer,
            StationController consumer,
            CargoType cargo,
            int carCount,
            List<string> reservedCarIds = null,
            bool unpaidMove = false)
        {
            if (producer == null || consumer == null || carCount <= 0) return null;

            var cargoList = new List<CargoType> { cargo };
            var loadMachine = producer.logicStation.yard
                .GetWarehouseMachinesThatSupportCargoTypes(cargoList).FirstOrDefault();
            var unloadMachine = consumer.logicStation.yard
                .GetWarehouseMachinesThatSupportCargoTypes(cargoList).FirstOrDefault();
            if (loadMachine == null || unloadMachine == null)
            {
                Main.Log($"[DirectHaul] carless: missing warehouse machine for {cargo}.");
                return null;
            }
            if (!DV.Globals.G.Types.CargoType_to_v2.TryGetValue(cargo, out var v2) ||
                !DV.Globals.G.Types.CargoToLoadableCarTypes.TryGetValue(v2, out var carTypes) ||
                carTypes.Count == 0)
            {
                Main.Log($"[DirectHaul] carless: no loadable car type for {cargo}.");
                return null;
            }
            var usableTypes = carTypes.Where(t => t.liveries != null && t.liveries.Count > 0).ToList();
            if (usableTypes.Count == 0)
            {
                Main.Log($"[DirectHaul] carless: no car type with a livery for {cargo}.");
                return null;
            }

            // Synthetic display cars: the booklet shows what to bring before cars attach.
            // Real length, tare and capacity make the booklet's length/mass/value stats
            // and the board's tonnage read true instead of zero.
            var displayCars = new List<Car_data>();
            var liveryCounts = new Dictionary<TrainCarLivery, int>();
            for (int i = 0; i < carCount; i++)
            {
                var shownType = usableTypes[i % usableTypes.Count];
                var livery = shownType.liveries[i % shownType.liveries.Count];
                var (length, tare, capacity) = LiveryDisplayData(livery);
                displayCars.Add(new Car_data("?", livery, false, false, length, tare, capacity));
                liveryCounts.TryGetValue(livery, out var n);
                liveryCounts[livery] = n + 1;
            }

            float distance = JobPaymentCalculator.GetDistanceBetweenStations(producer, consumer);
            float bonusTime = JobPaymentCalculator.CalculateHaulBonusTimeLimit(distance);
            // An unpaid move relocates imported goods: real work, no wage, per the
            // pay-once rule (goods pay when produced stock is delivered, never per bounce).
            float wage = unpaidMove ? 0f : JobPaymentCalculator.CalculateJobPayment(
                JobType.Transport, distance,
                new PaymentCalculationData(liveryCounts, new Dictionary<CargoType, int> { { cargo, carCount } }));

            var jobId = JobUtils.NextId(producer.stationInfo.YardID, consumer.stationInfo.YardID);
            bool ok = BuildChain(producer, consumer, new List<TrainCar>(), cargo,
                loadMachine, unloadMachine, jobId, wage, bonusTime,
                spawnTrackDisplay: $"warehouse {loadMachine.WarehouseTrack?.ID?.FullDisplayID ?? producer.stationInfo.YardID}",
                includeLoadTask: true, displayCarsOverride: displayCars, plannedCarCount: carCount);
            if (!ok) return null;

            if (reservedCarIds != null && reservedCarIds.Count > 0 &&
                StaticDirectHaulJobDefinition.jobDefinitions.TryGetValue(jobId, out var createdDef))
            {
                createdDef.reservedCarIds = new List<string>(reservedCarIds);
                Main.Log($"[DirectHaul] {jobId}: dispatcher reserved cars {string.Join(", ", reservedCarIds)}.");
            }

            // The job pre-allocates its supply; it comes back only if the booklet dies
            // before cars attach.
            Economy.EconomyState.Instance.Reserve(jobId, producer.stationInfo.YardID, cargo, carCount,
                paid: !unpaidMove);
            if (StaticDirectHaulJobDefinition.jobDefinitions.TryGetValue(jobId, out var newDef))
                newDef.unpaidMove = unpaidMove;

            Economy.EconomyHistory.Record("haul_created", producer.stationInfo.YardID, cargo.ToString(), carCount, jobId);
            // DLE clients get the cargo, count and pay their booklets cannot read from
            // the host-only definition.
            Dispatch.DleMpChannel.NotifyJobCreated(jobId, wage, unpaidMove, cargo.ToString(), carCount);
            Main.Log($"[DirectHaul] carless {jobId}: bring {carCount} empt{(carCount == 1 ? "y" : "ies")} " +
                     $"for {cargo} to {producer.stationInfo.YardID}.");
            return jobId;
        }

        /// <summary>
        /// Logistics run booklet: a real zero-pay vanilla EmptyHaul job over idle pool
        /// empties at the origin, so the run is paperwork a crew can hold and validate.
        /// Vanilla type on purpose: the game's own booklet code renders it (host AND
        /// modless DVMP clients), the vanilla save persists it, and DVMP syncs it natively.
        /// Basic license (coordination work, not licensed haulage), wage 0 by definition.
        /// Cars come from ONE track (the one holding the most suitable idle empties) so
        /// the booklet points at a real cut. Returns the job id, or null with a reason.
        /// </summary>
        public static string TryCreateLogiRun(
            StationController from,
            StationController to,
            CargoType? forCargo,
            int wanted,
            out string reason,
            out int boundCars)
        {
            reason = null;
            boundCars = 0;
            if (from == null || to == null || wanted <= 0) { reason = "bad request"; return null; }

            List<DV.ThingTypes.TrainCarType_v2> loadableTypes = null;
            if (forCargo.HasValue &&
                DV.Globals.G.Types.CargoType_to_v2.TryGetValue(forCargo.Value, out var v2) &&
                DV.Globals.G.Types.CargoToLoadableCarTypes.TryGetValue(v2, out var lt))
                loadableTypes = lt.ToList();

            var idle = Data.DleCarPool.CollectIdleEmpties(from, loadableTypes, int.MaxValue);
            if (idle.Count == 0)
            {
                reason = $"no idle suitable empties at {from.stationInfo.YardID}; posted as a coordination note only";
                return null;
            }

            // One cut, one track: the track holding the most suitable empties.
            var cut = idle
                .Where(tc => tc.logicCar?.CurrentTrack != null)
                .GroupBy(tc => tc.logicCar.CurrentTrack)
                .OrderByDescending(g => g.Count())
                .First()
                .Take(wanted)
                .ToList();
            var startTrack = cut[0].logicCar.CurrentTrack;

            // Somewhere to stand at the destination; longest storage track is a safe berth.
            var destTrack = to.logicStation?.yard?.StorageTracks?
                .OrderByDescending(t => t.length)
                .FirstOrDefault();
            if (destTrack == null) { reason = $"{to.stationInfo.YardID} has no storage track"; return null; }

            var logicCars = TrainCar.ExtractLogicCars(cut);
            if (logicCars == null || logicCars.Count == 0) { reason = "car extraction failed"; return null; }

            EmptyHaulJobProceduralGenerator.CalculateBonusTimeLimitAndWage(
                from, to, cut.Select(tc => tc.carLivery).ToList(), out var bonusTime, out _);

            var jcc = EmptyHaulJobProceduralGenerator.GenerateEmptyHaulChainController(
                from, to, startTrack, logicCars, destTrack, bonusTime,
                0f, JobLicenses.Basic);
            if (jcc == null) { reason = "the game refused to build the empty haul"; return null; }
            try
            {
                jcc.FinalizeSetupAndGenerateFirstJob(false);
            }
            catch (System.Exception ex)
            {
                Main.LogAlways($"[Logistics] booklet build failed ({ex.GetType().Name}): {ex.Message}");
                try { jcc.DestroyChain(); } catch { }
                reason = "booklet build failed; see game log";
                return null;
            }
            from.ProceduralJobsController.AddJobChainController(jcc);

            boundCars = cut.Count;
            var jobId = jcc.currentJobInChain?.ID;
            Main.LogAlways($"[Logistics] {jobId}: zero-pay run, {cut.Count} car(s) " +
                           $"{from.stationInfo.YardID} -> {to.stationInfo.YardID}" +
                           (forCargo.HasValue ? $" for {forCargo}" : "") + ".");
            return jobId;
        }

        /// <summary>
        /// Rebuild a saved job over cars that already exist in the world (save restore).
        /// The cars keep whatever cargo the vanilla save gave them.
        /// </summary>
        public static bool TryRebuild(
            StationController producer,
            StationController consumer,
            List<TrainCar> cars,
            CargoType cargo,
            string jobId,
            float wage,
            float bonusTime,
            string spawnTrackDisplay,
            bool includeLoadTask = false,
            int plannedCars = 0)
        {
            var cargoList = new List<CargoType> { cargo };
            var loadMachine = producer.logicStation.yard
                .GetWarehouseMachinesThatSupportCargoTypes(cargoList).FirstOrDefault();
            var unloadMachine = consumer.logicStation.yard
                .GetWarehouseMachinesThatSupportCargoTypes(cargoList).FirstOrDefault();
            if (unloadMachine == null)
            {
                Main.LogAlways($"[DirectHaul] rebuild {jobId}: {consumer?.stationInfo?.YardID} no longer unloads {cargo} (recipe/overlay change?); the saved haul is dropped.");
                return false;
            }

            List<Car_data> displayOverride = null;
            if (includeLoadTask && cars.Count == 0 && plannedCars > 0 &&
                DV.Globals.G.Types.CargoType_to_v2.TryGetValue(cargo, out var v2) &&
                DV.Globals.G.Types.CargoToLoadableCarTypes.TryGetValue(v2, out var carTypes) &&
                carTypes.Count > 0)
            {
                var usableTypes = carTypes.Where(t => t.liveries != null && t.liveries.Count > 0).ToList();
                displayOverride = new List<Car_data>();
                for (int i = 0; usableTypes.Count > 0 && i < plannedCars; i++)
                {
                    var shownType = usableTypes[i % usableTypes.Count];
                    displayOverride.Add(new Car_data("?",
                        shownType.liveries[i % shownType.liveries.Count], false, false, 0f, 0f, 0f));
                }
            }

            // A restored consist that already carries cargo has finished its load step:
            // rebuilding it WITH a load task re-checked that task into the origin machine
            // on re-take, where nothing could ever complete or remove it, so the job was
            // permanently un-turn-in-able.
            int loadedCars = cars.Count(c => c.logicCar != null && c.logicCar.LoadedCargoAmount > 0f);
            bool stillNeedsLoad = includeLoadTask && loadedCars == 0;

            bool ok = BuildChain(producer, consumer, cars, cargo, loadMachine, unloadMachine,
                jobId, wage, bonusTime, spawnTrackDisplay, stillNeedsLoad, displayOverride, plannedCars);
            if (ok)
            {
                if (StaticDirectHaulJobDefinition.jobDefinitions.TryGetValue(jobId, out var rebuilt))
                    rebuilt.loadedCarloads = loadedCars;
                Main.Log($"[DirectHaul] rebuilt {jobId} over {cars.Count} existing car(s)" +
                         (stillNeedsLoad && cars.Count == 0 ? " (carless, awaiting empties)" : "") +
                         (loadedCars > 0 ? $" ({loadedCars} loaded; load step done)" : "") + ".");
            }
            return ok;
        }

        private static bool BuildChain(
            StationController producer,
            StationController consumer,
            List<TrainCar> cars,
            CargoType cargo,
            WarehouseMachine loadMachine,
            WarehouseMachine unloadMachine,
            string jobId,
            float wage,
            float bonusTime,
            string spawnTrackDisplay,
            bool includeLoadTask = false,
            List<Car_data> displayCarsOverride = null,
            int plannedCarCount = 0)
        {
            // ExtractLogicCars returns NULL for an empty list (and warns "Passed null or
            // empty list of trainCars"), so a carless job must not go through it: the NRE
            // here killed every Company Haul at birth, which is why no carless booklet or
            // warehouse attach was ever seen live.
            var logicCars = cars != null && cars.Count > 0
                ? TrainCar.ExtractLogicCars(cars)
                : new List<Car>();
            if (logicCars == null)
            {
                Main.LogAlways($"[DirectHaul] {jobId}: could not extract logic cars; job not created.");
                return false;
            }
            int licenseCarCount = logicCars.Count > 0 ? logicCars.Count : plannedCarCount;
            var chainData = new StationsChainData(
                producer.stationInfo.YardID, consumer.stationInfo.YardID);
            var requiredLicenses =
                JobLicenseType_v2.ListToFlags(LicenseManager.Instance.GetRequiredLicensesForCargoTypes(new List<CargoType> { cargo }))
                | (LicenseManager.Instance.GetRequiredLicenseForNumberOfTransportedCars(licenseCarCount)?.v1 ?? JobLicenses.Basic);

            var go = new GameObject(
                $"ChainJob[Company Haul]: {chainData.chainOriginYardId} - {chainData.chainDestinationYardId}");
            go.transform.SetParent(producer.transform);

            var def = go.AddComponent<StaticDirectHaulJobDefinition>();
            def.carsToTransport   = logicCars;
            def.cargoAmountPerCar = logicCars.Select(c => c.capacity).ToList();
            def.loadMachine       = loadMachine;
            def.unloadMachine     = unloadMachine;
            def.transportedCargo  = cargo;
            def.includeLoadTask   = includeLoadTask;
            def.plannedCarCount   = plannedCarCount > 0 ? plannedCarCount : logicCars.Count;
            def.displayCars       = displayCarsOverride ?? logicCars.Select(c => new Car_data(c, false)).ToList();
            def.spawnTrackDisplay = spawnTrackDisplay;
            def.deliveryPayment   = wage;
            def.ForceJobId(jobId);
            // The booklet is faux: the vanilla job pays 0, and deliveryPayment is awarded on
            // the delivery unload instead (gated, so load and storage-unload never pay).
            def.PopulateBaseJobDefinition(producer.logicStation, bonusTime, 0f, chainData, requiredLicenses);

            var jcc = new JobChainController(go);
            jcc.carsForJobChain = logicCars;
            jcc.AddJobDefinitionToChain(def);
            try
            {
                jcc.FinalizeSetupAndGenerateFirstJob(false);
            }
            catch (System.Exception ex)
            {
                Main.LogAlways($"[DirectHaul] generation failed: {ex.GetType().Name}: {ex.Message}");
                try { jcc.DestroyChain(); } catch { }
                Object.Destroy(go);
                return false;
            }

            producer.ProceduralJobsController.AddJobChainController(jcc);
            return true;
        }
    }
}
