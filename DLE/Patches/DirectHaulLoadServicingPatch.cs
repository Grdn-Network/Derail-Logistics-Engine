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
                Main.LogAlways($"[LoadServicing] attach failed: {ex.GetType().Name}: {ex.Message}");
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
                if (!DV.Globals.G.Types.CargoToLoadableCarTypes.TryGetValue(v2, out var loadable)) continue;

                // Suitable = on the warehouse track, right car family, jobless and empty.
                // Dispatcher-reserved cars are taken first, then anything else suitable.
                var jobsManager = SingletonBehaviour<JobsManager>.Instance;

                // Cars another live job has reserved are off-limits here, so this job never
                // grabs the cut a dispatcher set aside for a different haul.
                var reservedElsewhere = new HashSet<string>(StringComparer.Ordinal);
                foreach (var other in StaticDirectHaulJobDefinition.jobDefinitions)
                    if (other.Key != jobData.id && other.Value?.reservedCarIds != null)
                        foreach (var rid in other.Value.reservedCarIds)
                            reservedElsewhere.Add(rid);

                var candidates = new List<Car>();
                foreach (var car in machine.WarehouseTrack.GetCarsFullyOnTrack())
                {
                    if (!loadable.Contains(car.carType.parentType)) continue;
                    if (jobsManager.GetJobOfCar(car) != null) continue;
                    if (car.LoadedCargoAmount != 0) continue;
                    if (reservedElsewhere.Contains(car.ID)) continue; // reserved for another job
                    candidates.Add(car);
                }
                var reserved = def.reservedCarIds;
                if (reserved != null && reserved.Count > 0)
                    candidates.Sort((a, b) => reserved.Contains(b.ID).CompareTo(reserved.Contains(a.ID)));
                var valid = candidates.Count > wanted ? candidates.GetRange(0, wanted) : candidates;
                if (valid.Count != wanted) continue; // bring the full cut before it attaches

                CommitAttach(def, task.Job, valid);

                Main.Log($"[LoadServicing] {jobData.id}: attached {valid.Count} car(s), loading {task.cargoType}. " +
                         $"{def.chainData.chainOriginYardId} stock debited.");
            }
        }

        /// <summary>
        /// Attach a chosen set of cars to a Direct Haul: fill its warehouse tasks, take the
        /// cars out of the free pool, register debt, consume the reserved supply, wire the
        /// cleanup guards, and redraw any in-hand booklet. Shared by the player-triggered
        /// warehouse attach and by dispatch servicing.
        /// </summary>
        internal static void CommitAttach(StaticDirectHaulJobDefinition def, Job job, List<Car> valid)
        {
            var jobsManager = SingletonBehaviour<JobsManager>.Instance;
            foreach (var t in job.tasks)
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

            jobsManager.jobToJobCars[job] = new HashSet<Car>(valid);
            JobDebtController.Instance.RegisterGeneratedJob(job, valid);
            JobDebtController.Instance.OnJobTaken(job, false);

            // The promised supply physically leaves the stockpile now.
            EconomyState.Instance.ConsumeReservation(job.ID,
                def.chainData.chainOriginYardId, def.transportedCargo, valid.Count);

            // Cleanup guards: if the job dies with cargo aboard, dump it.
            job.JobAbandoned += DumpJobCargo;
            job.JobCompleted += DumpPlates;

            // Booklets already in someone's hand redraw with the real consist.
            RefreshInHandBooklets(job);
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

        /// <summary>
        /// Redraw any printed booklet for this job in place: render a fresh hidden booklet
        /// from the updated Job_data and swap its page textures and materials into the one
        /// the player holds (SelfShunter's technique, used with permission). Failure just
        /// leaves the old pages; a re-printed booklet is always correct anyway.
        /// </summary>
        private static void RefreshInHandBooklets(Job job)
        {
            try
            {
                foreach (var book in JobBooklet.allExistingJobBooklets.ToArray())
                {
                    if (book.job != job) continue;

                    var pb = book.GetComponent<PageBook>();
                    if (pb == null) continue;

                    var tempBook = DV.Booklets.BookletCreator_Job.Create(
                        new DV.Booklets.Job_data(job),
                        book.transform.position, book.transform.rotation).gameObject;
                    var tempPb = tempBook.GetComponent<PageBook>();
                    if (tempPb == null)
                    {
                        // No page book to swap from; drop the temp so it does not leak.
                        UnityEngine.Object.Destroy(tempBook);
                        continue;
                    }

                    tempPb.PageBookGenerated += () =>
                    {
                        try
                        {
                            pb.pageTextures = tempPb.pageTextures;
                            for (int i = 0; i < tempPb.pages.Count && i < pb.pages.Count; i++)
                            {
                                UnityEngine.Object.Destroy(pb.pages[i].renderer.material);
                                UnityEngine.Object.Destroy(pb.pages[i].pageMaterial);
                                pb.pages[i].renderer.material = tempPb.pages[i].renderer.material;
                                pb.pages[i].pageMaterial = tempPb.pages[i].pageMaterial;
                                tempPb.pages[i].renderer.material = null;
                                tempPb.pages[i].pageMaterial = null;
                            }

                            var texturesField = AccessTools.Field(
                                typeof(DV.Booklets.Rendered.RenderedTexturesBooklet), "textures");
                            var tempRendered = tempBook.GetComponent<DV.Booklets.Rendered.RenderedTexturesBooklet>();
                            var liveRendered = book.GetComponent<DV.Booklets.Rendered.RenderedTexturesBooklet>();
                            if (texturesField != null && tempRendered != null && liveRendered != null)
                            {
                                var fresh = texturesField.GetValue(tempRendered);
                                var stale = texturesField.GetValue(liveRendered);
                                texturesField.SetValue(liveRendered, fresh);
                                texturesField.SetValue(tempRendered, stale);
                            }

                            UnityEngine.Object.Destroy(pb.coverMaterial);
                            pb.coverMaterial = tempPb.coverMaterial;
                            tempPb.coverMaterial = null;
                        }
                        catch (Exception ex)
                        {
                            Main.LogAlways($"[LoadServicing] booklet redraw swap failed: {ex.Message}");
                        }
                        UnityEngine.Object.Destroy(tempBook);
                    };
                    Main.Log($"[LoadServicing] {job.ID}: in-hand booklet redrawn.");
                }
            }
            catch (Exception ex)
            {
                Main.LogAlways($"[LoadServicing] booklet redraw failed: {ex.Message}");
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
