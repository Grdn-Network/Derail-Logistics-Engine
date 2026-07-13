using DLE.Economy;
using DLE.Jobs;
using DV.Logic.Job;
using DV.ServicePenalty;
using DV.ThingTypes;
using DV.ThingTypes.TransitionHelpers;
using DV.Utils;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DLE.Patches
{
    /// <summary>
    /// Finite/persistence mode: warehouse servicing for carless Direct Hauls, adapted from
    /// Chump_the_Lump's SelfShunter (used with permission) but SCOPED to DLE jobs so vanilla
    /// warehouse behavior is untouched. When a player brings suitable empty cars to the
    /// producer warehouse and starts the load sequence, the cars are attached to the oldest
    /// waiting DLE job, debt is registered, plates update, and the producer stockpile is
    /// debited (stock leaves the ledger the moment it is committed to cars).
    /// </summary>
    [HarmonyPatch(typeof(WarehouseTask), nameof(WarehouseTask.UpdateTaskState))]
    public static class DleWarehouseTaskStatePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(WarehouseTask __instance, ref TaskState __result)
        {
            // Only DLE jobs; everything else keeps vanilla task logic.
            var jobId = __instance.Job?.ID;
            if (jobId == null || !JobUtils.ManagedJobIds.Contains(jobId)) return true;

            __instance.readyForMachine = true;

            var machineTasks = __instance.warehouseMachine?.currentTasks;
            SetState(__instance, machineTasks != null && machineTasks.Contains(__instance)
                ? TaskState.InProgress
                : TaskState.Done);

            __result = __instance.state;
            return false;
        }

        private static void SetState(WarehouseTask task, TaskState newState)
        {
            if (task.state == newState) return;
            task.taskFinishTime = newState == TaskState.Done
                ? SingletonBehaviour<JobsManager>.Instance.Time
                : 0f;
            task.state = newState;
        }
    }

    [HarmonyPatch(typeof(JobDebtController), nameof(JobDebtController.RegisterGeneratedJob))]
    public static class DleDebtRegistrationGuardPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Job job, List<Car> cars)
        {
            // A carless DLE job has nothing to insure yet; debt registers at attach time.
            if (job?.ID != null && JobUtils.ManagedJobIds.Contains(job.ID))
                return cars != null && cars.Count > 0;
            return true;
        }
    }

    [HarmonyPatch(typeof(WarehouseMachineController), "StartLoadSequence")]
    public static class DleWarehouseLoadAttachPatch
    {
        [HarmonyPrefix]
        public static void Prefix(WarehouseMachineController __instance)
        {
            try
            {
                AttachEmptiesToWaitingJobs(__instance);
            }
            catch (Exception ex)
            {
                Main.Log($"[LoadServicing] attach failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void AttachEmptiesToWaitingJobs(WarehouseMachineController controller)
        {
            var machine = controller.warehouseMachine;
            if (machine == null) return;

            foreach (var jobData in machine.GetCurrentLoadUnloadData(WarehouseTaskType.Loading))
            {
                if (jobData.tasksAvailableToProcess.Count == 0) continue;
                var task = jobData.tasksAvailableToProcess[0];
                if (task.cars.Count != 0) continue; // already has cars
                if (!StaticDirectHaulJobDefinition.jobDefinitions.TryGetValue(jobData.id, out var def)) continue;

                int wanted = def.plannedCarCount > 0 ? def.plannedCarCount : def.displayCars?.Count ?? 0;
                if (wanted <= 0) continue;

                if (!DV.Globals.G.Types.CargoType_to_v2.TryGetValue(task.cargoType, out var v2)) continue;
                var loadable = DV.Globals.G.Types.CargoToLoadableCarTypes[v2];

                // Suitable = on the warehouse track, right car family, jobless and empty.
                var jobsManager = SingletonBehaviour<JobsManager>.Instance;
                var valid = new List<Car>();
                foreach (var car in machine.WarehouseTrack.GetCarsFullyOnTrack())
                {
                    if (valid.Count >= wanted) break;
                    if (!loadable.Contains(car.carType.parentType)) continue;
                    if (jobsManager.GetJobOfCar(car) != null) continue;
                    if (car.LoadedCargoAmount != 0) continue;
                    valid.Add(car);
                }
                if (valid.Count != wanted) continue; // bring the full cut before it attaches

                // Attach to every warehouse task of the job (load and unload).
                foreach (var t in task.Job.tasks)
                {
                    if (!(t is WarehouseTask wt)) continue;
                    wt.cars.Clear();
                    float totalCapacity = 0f;
                    foreach (var car in valid)
                    {
                        MakeJobEligible(car);
                        wt.cars.Add(car);
                        totalCapacity += car.capacity;
                        car.TrainCar().UpdateJobIdOnCarPlates(wt.Job.ID);
                    }
                    // cargoAmount is public but readonly; set it the way SelfShunter does.
                    Traverse.Create(wt).Field(nameof(WarehouseTask.cargoAmount)).SetValue(totalCapacity);
                }

                def.carsToTransport = new List<Car>(valid);
                def.cargoAmountPerCar = valid.Select(c => c.capacity).ToList();

                jobsManager.jobToJobCars[task.Job] = new HashSet<Car>(valid);
                JobDebtController.Instance.RegisterGeneratedJob(task.Job, valid);
                JobDebtController.Instance.OnJobTaken(task.Job, false);

                // Stock leaves the ledger the moment it is committed to cars.
                EconomyState.Instance.Debit(def.chainData.chainOriginYardId, task.cargoType, valid.Count);

                // Cleanup guards: if the job dies with cargo aboard, dump it.
                task.Job.JobAbandoned += DumpJobCargo;
                task.Job.JobCompleted += DumpPlates;

                Main.Log($"[LoadServicing] {jobData.id}: attached {valid.Count} car(s), loading {task.cargoType}. " +
                         $"{def.chainData.chainOriginYardId} stock debited.");
            }
        }

        private static void MakeJobEligible(Car car)
        {
            if (!car.playerSpawnedCar) return;
            // Both flags are readonly; set them the way SelfShunter does.
            Traverse.Create(car).Field(nameof(Car.playerSpawnedCar)).SetValue(false);
            var tc = car.TrainCar();
            if (tc != null)
            {
                Traverse.Create(tc).Field("playerSpawnedCar").SetValue(false);
                tc.GetComponent<CarDebtController>()?.SetDebtTracker(tc.CarDamage, tc.CargoDamage);
            }
        }

        private static void DumpJobCargo(Job job)
        {
            if (!(job.tasks.FirstOrDefault(t => t is WarehouseTask) is WarehouseTask wt)) return;
            foreach (var c in wt.cars)
            {
                if (c.LoadedCargoAmount > 0f) c.UnloadCargo(c.LoadedCargoAmount, c.CurrentCargoTypeInCar);
                c.TrainCar()?.UpdateJobIdOnCarPlates(string.Empty);
            }
        }

        private static void DumpPlates(Job job)
        {
            if (!(job.tasks.FirstOrDefault(t => t is WarehouseTask) is WarehouseTask wt)) return;
            foreach (var c in wt.cars)
                c.TrainCar()?.UpdateJobIdOnCarPlates(string.Empty);
        }
    }
}
