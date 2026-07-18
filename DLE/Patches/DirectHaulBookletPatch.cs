using DLE.Jobs;
using DV.Booklets;
using DV.Localization;
using DV.RenderTextureSystem.BookletRender;
using DV.ThingTypes;
using DV.ThingTypes.TransitionHelpers;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DLE.Patches
{
    /// <summary>
    /// Vanilla booklet creators switch on JobType and know nothing about
    /// ComplexTransport: they log "Unsupported format of job" and return null, which
    /// then NREs in BookletCreator every frame. These prefixes render Direct Haul jobs
    /// instead: overview front page, full booklet (cover, front, load task, unload
    /// task, validate), expired report and missing-license report.
    /// Booklet layout adapted from Chump_the_Lump's SelfShunter, used with permission.
    /// </summary>
    internal static class DirectHaulBooklet
    {
        // Player-facing job name (booklets, overview). Internal identifiers keep the
        // Direct Haul working name.
        public const string DIRECT_HAUL_NAME = "Company Haul";

        // Distinct from the vanilla job colors (haul orange, shunting yellow/green);
        // a muted violet marks Direct Haul paperwork.
        public static readonly Color DIRECT_HAUL_COLOR = new Color(0.557f, 0.369f, 0.635f, 1f);

        /// <summary>
        /// Collect the cars and cargo to draw. Prefers the live task data on the job;
        /// falls back to the snapshot the generator stored on the definition (task data
        /// can be empty before the load task runs).
        /// </summary>
        public static void GetDisplayData(Job_data job, out List<Car_data> cars,
            out List<CargoType> cargoTypePerCar, out string cargoName)
        {
            StaticDirectHaulJobDefinition.jobDefinitions.TryGetValue(job.ID, out var def);

            var taskCars = FirstTaskCars(job);
            cars = taskCars != null && taskCars.Count > 0
                ? taskCars
                : (def?.displayCars ?? new List<Car_data>());

            // No definition (a DVMP client: the def is host state) and no attached cars:
            // rebuild the display from the SYNCED task data. Every warehouse task carries
            // its cargo, and a carless task's cargoAmount carries the planned car count,
            // so the client's paper can show "4 loads of Scrap Metal" instead of 0 of
            // nothing. Same livery pick as the host's synthetic display cars.
            if (def == null && cars.Count == 0 && job.tasksData != null)
            {
                var syncedCargo = CargoType.None;
                int plannedFromSync = 0;
                foreach (var task in job.tasksData)
                {
                    if (syncedCargo == CargoType.None && task.cargoTypePerCar != null)
                        foreach (var c in task.cargoTypePerCar)
                            if (c != CargoType.None) { syncedCargo = c; break; }
                    int hint = (int)task.totalCargoAmount;
                    if (hint > plannedFromSync && hint <= 99) plannedFromSync = hint;
                }
                if (syncedCargo != CargoType.None && plannedFromSync > 0 &&
                    DV.Globals.G.Types.CargoType_to_v2.TryGetValue(syncedCargo, out var v2) &&
                    DV.Globals.G.Types.CargoToLoadableCarTypes.TryGetValue(v2, out var carTypes))
                {
                    var usable = carTypes.Where(t => t.liveries != null && t.liveries.Count > 0).ToList();
                    if (usable.Count > 0)
                    {
                        var rebuilt = new List<Car_data>();
                        for (int i = 0; i < plannedFromSync; i++)
                        {
                            var shownType = usable[i % usable.Count];
                            var livery = shownType.liveries[i % shownType.liveries.Count];
                            var (length, tare, capacity) = DLE.Jobs.DirectHaulGenerator.LiveryDisplayData(livery);
                            rebuilt.Add(new Car_data("?", livery, false, false, length, tare, capacity));
                        }
                        cars = rebuilt;
                    }
                }
                if (syncedCargo != CargoType.None && cars.Count > 0)
                {
                    cargoTypePerCar = cars.Select(_ => syncedCargo).ToList();
                    cargoName = CargoDisplayName(syncedCargo);
                    return;
                }
            }

            var taskCargo = FirstTaskCargo(job);
            if (taskCargo != null && taskCargo.Count == cars.Count && taskCargo.Count > 0)
            {
                cargoTypePerCar = taskCargo;
            }
            else if (def != null)
            {
                cargoTypePerCar = cars.Select(_ => def.transportedCargo).ToList();
            }
            else
            {
                cargoTypePerCar = taskCargo ?? new List<CargoType>();
            }

            cargoName = cargoTypePerCar.Count > 0 ? CargoDisplayName(cargoTypePerCar[0]) : string.Empty;
        }

        private static string CargoDisplayName(CargoType cargo)
        {
            try { return LocalizationAPI.L(cargo.ToV2().localizationKeyFull); }
            catch (Exception) { return cargo.ToString(); }
        }

        private static List<Car_data> FirstTaskCars(Job_data job) =>
            job.tasksData != null && job.tasksData.Length > 0 ? job.tasksData[0].cars : null;

        private static List<CargoType> FirstTaskCargo(Job_data job) =>
            job.tasksData != null && job.tasksData.Length > 0 ? job.tasksData[0].cargoTypePerCar : null;

        /// <summary>
        /// Format the stat strings the same way the vanilla transport booklet does,
        /// using DV's own helpers in DV.Booklets.C.
        /// </summary>
        public static void GetStats(Job_data job, List<Car_data> cars, List<CargoType> cargoTypePerCar,
            out string timeLimit, out string value, out string mass, out string length)
        {
            timeLimit = job.timeLimit > 0f
                ? Mathf.FloorToInt(job.timeLimit / 60f) + " min"
                : C.NO_BONUS_TIME_LIMIT_STR;

            try
            {
                length = C.GetCarsTotalLength(cars).ToString("N2", LocalizationAPI.CC) + " m";
                mass = (C.GetCarsTotalMass(cars, cargoTypePerCar) / 1000f)
                    .ToString("N2", LocalizationAPI.CC) + " t";
                value = "$" + (C.GetTrainValue(cars, cargoTypePerCar) / 1000000f)
                    .ToString("N2", LocalizationAPI.CC) + "m";
            }
            catch (Exception ex)
            {
                // Never let a stats miscount kill the whole booklet.
                Main.Log($"[DirectHaul] Booklet stats fallback: {ex.Message}");
                length = cars.Count + " cars";
                mass = string.Empty;
                value = string.Empty;
            }
        }

        public static FrontPageTemplatePaperData BuildFrontPage(Job_data job,
            string pageNumber, string totalPages)
        {
            GetDisplayData(job, out var cars, out var cargoTypePerCar, out var cargoName);
            GetStats(job, cars, cargoTypePerCar,
                out var timeLimit, out var value, out var mass, out var length);

            // Tell the player where the loaded consist is sitting. Falls back silently when
            // the definition is not registered (e.g. on a DVMP client).
            StaticDirectHaulJobDefinition.jobDefinitions.TryGetValue(job.ID, out var defForTrack);

            // A carless job can reach the booklet before chainOriginStationInfo/Destination
            // are populated, and rendering with them null NREs the whole booklet. Fall back
            // to resolving the station info from the definition's yard ids.
            var origin = job.chainOriginStationInfo
                ?? StationController.GetStationByYardID(defForTrack?.chainData?.chainOriginYardId)?.stationInfo;
            var destination = job.chainDestinationStationInfo
                ?? StationController.GetStationByYardID(defForTrack?.chainData?.chainDestinationYardId)?.stationInfo;
            if (origin == null || destination == null)
            {
                // Rendering on would NRE below (the exact crash this fallback exists to
                // prevent); the caller degrades to a page without the front sheet.
                Main.LogAlways($"[DirectHaul] {job.ID}: booklet station info unresolved " +
                               $"(origin null={origin == null}, dest null={destination == null}); front page skipped.");
                return null;
            }

            // A carless job waiting for its empties has no cars sitting anywhere; a
            // "Cars on track ..." line would point crews at a consist that does not exist.
            string pickup;
            bool awaitingEmpties = defForTrack != null && defForTrack.includeLoadTask &&
                                   (defForTrack.carsToTransport?.Count ?? 0) == 0;
            if (awaitingEmpties)
            {
                var loadTrack = GetTaskTrackDisplay(job, 0);
                pickup = string.IsNullOrEmpty(loadTrack)
                    ? " Bring empty cars to the loading track."
                    : $" Bring empty cars to loading track {loadTrack}.";
            }
            else
            {
                pickup = string.IsNullOrEmpty(defForTrack?.spawnTrackDisplay)
                    ? string.Empty
                    : $" Cars on track {defForTrack.spawnTrackDisplay}.";
            }

            return new FrontPageTemplatePaperData(
                DIRECT_HAUL_NAME, string.Empty, job.ID, DIRECT_HAUL_COLOR,
                $"Transport {cars.Count} loads of {cargoName}.{pickup}",
                job.requiredLicenses,
                cargoTypePerCar.Distinct().ToList(), cargoTypePerCar,
                string.Empty, string.Empty, TemplatePaperData.NOT_USED_COLOR,
                LocalizationAPI.L(origin.LocalizationKey), origin.Type, origin.StationColor,
                LocalizationAPI.L(destination.LocalizationKey), destination.Type, destination.StationColor,
                cars, length, mass, value, timeLimit,
                // The vanilla payment is zeroed (booklets are faux); the paper shows the
                // real on-delivery pay so crews can see what a haul is worth. On a DVMP
                // client the definition is host state, so the pay comes from DLE's own
                // packet channel instead.
                (defForTrack != null && defForTrack.deliveryPayment > 0f
                    ? defForTrack.deliveryPayment
                    : Dispatch.DleMpChannel.ClientJobPay.TryGetValue(job.ID, out var syncedPay) && syncedPay > 0f
                        ? syncedPay
                        : job.basePayment).ToString("N0", LocalizationAPI.CC),
                pageNumber, totalPages);
        }

        /// <summary>
        /// Track label for the load (task 0) or unload (task 1) warehouse task,
        /// e.g. "B3L", or empty when the task data is not there.
        /// </summary>
        public static string GetTaskTrackDisplay(Job_data job, int taskIndex)
        {
            if (job.tasksData == null || job.tasksData.Length <= taskIndex)
                return string.Empty;
            var trackId = job.tasksData[taskIndex].destinationTrackID;
            return trackId != null ? trackId.TrackPartOnly : string.Empty;
        }

        /// <summary>Localize with a fallback so a missing key cannot blank a booklet.</summary>
        public static string SafeL(string key, string fallback)
        {
            try
            {
                var s = LocalizationAPI.L(key);
                return string.IsNullOrEmpty(s) || s == key ? fallback : s;
            }
            catch { return fallback; }
        }
    }

    [HarmonyPatch(typeof(BookletCreator_JobOverview), nameof(BookletCreator_JobOverview.GetJobOverviewTemplateData))]
    internal static class DirectHaulJobOverviewPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Job_data job, ref List<TemplatePaperData> __result)
        {
            if (job == null || job.type != JobType.ComplexTransport)
                return true;

            var front = DirectHaulBooklet.BuildFrontPage(job, string.Empty, string.Empty);
            __result = new List<TemplatePaperData>
            {
                // Unresolvable station info degrades to a titled placeholder instead of
                // an NRE that kills the render.
                front ?? (TemplatePaperData)new JobExpiredTemplatePaperData(
                    DirectHaulBooklet.DIRECT_HAUL_NAME, string.Empty, job.ID,
                    DirectHaulBooklet.DIRECT_HAUL_COLOR)
            };
            return false;
        }
    }

    [HarmonyPatch(typeof(BookletCreator_Job), "GetBookletTemplateData")]
    internal static class DirectHaulJobBookletPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Job_data job, ref List<TemplatePaperData> __result)
        {
            if (job == null || job.type != JobType.ComplexTransport)
                return true;

            DirectHaulBooklet.GetDisplayData(job, out var cars, out _, out _);

            StaticDirectHaulJobDefinition.jobDefinitions.TryGetValue(job.ID, out var def);
            var origin = job.chainOriginStationInfo
                ?? StationController.GetStationByYardID(def?.chainData?.chainOriginYardId)?.stationInfo;
            var destination = job.chainDestinationStationInfo
                ?? StationController.GetStationByYardID(def?.chainData?.chainDestinationYardId)?.stationInfo;

            // A carless Company Haul carries a leading load task; the booklet mirrors it.
            // Jobs whose cars ship pre-loaded (single unload task) keep the 4-page layout, and the
            // unload page reads its track from the right task either way (task 0 is the
            // LOAD track on a carless job, not the unload track).
            bool hasLoadStep = def?.includeLoadTask
                ?? (job.tasksData != null && job.tasksData.Length > 1);
            string total = hasLoadStep ? "5" : "4";

            var pages = new List<TemplatePaperData>
            {
                new CoverPageTemplatePaperData(job.ID, DirectHaulBooklet.DIRECT_HAUL_NAME, "1", total),
            };
            var frontPage = DirectHaulBooklet.BuildFrontPage(job, "2", total);
            if (frontPage != null) pages.Add(frontPage);

            if (hasLoadStep)
            {
                pages.Add(new TaskTemplatePaperData(
                    "1",
                    DirectHaulBooklet.SafeL("job/task_type_load", "LOAD"),
                    "Bring empty cars to the loading track, unless dispatch has already loaded them remotely.",
                    origin?.YardID ?? string.Empty,
                    origin?.StationColor ?? DirectHaulBooklet.DIRECT_HAUL_COLOR,
                    DirectHaulBooklet.GetTaskTrackDisplay(job, 0), C.TRACK_COLOR,
                    string.Empty, string.Empty, TemplatePaperData.NOT_USED_COLOR,
                    cars, null, "3", total));
            }

            pages.Add(new TaskTemplatePaperData(
                hasLoadStep ? "2" : "1",
                DirectHaulBooklet.SafeL("job/task_type_unload", "UNLOAD"),
                DirectHaulBooklet.SafeL("job/task_desc_unload", "Unload the cars at the destination warehouse."),
                destination?.YardID ?? string.Empty,
                destination?.StationColor ?? DirectHaulBooklet.DIRECT_HAUL_COLOR,
                DirectHaulBooklet.GetTaskTrackDisplay(job, hasLoadStep ? 1 : 0), C.TRACK_COLOR,
                string.Empty, string.Empty, TemplatePaperData.NOT_USED_COLOR,
                cars, null, hasLoadStep ? "4" : "3", total));

            pages.Add(new ValidateJobTaskTemplatePaperData(
                hasLoadStep ? "3" : "2", hasLoadStep ? "5" : "4", total));

            __result = pages;
            return false;
        }
    }

    [HarmonyPatch(typeof(BookletCreator_JobExpiredReport), "GetJobExpiredTemplateData")]
    internal static class DirectHaulJobExpiredPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Job_data job, ref List<TemplatePaperData> __result)
        {
            if (job == null || job.type != JobType.ComplexTransport)
                return true;

            __result = new List<TemplatePaperData>
            {
                new JobExpiredTemplatePaperData(
                    DirectHaulBooklet.DIRECT_HAUL_NAME, string.Empty, job.ID,
                    DirectHaulBooklet.DIRECT_HAUL_COLOR)
            };
            return false;
        }
    }

    [HarmonyPatch(typeof(BookletCreator_JobMissingLicense), "GetMissingLicenseTemplateData")]
    internal static class DirectHaulJobMissingLicensePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Job_data job, bool isJobLicenseMissing,
            ref List<TemplatePaperData> __result)
        {
            if (job == null || job.type != JobType.ComplexTransport)
                return true;

            var licensesData = new List<MissingLicensesPageTemplatePaperData.LicensePrintData>();
            try
            {
                if (isJobLicenseMissing)
                {
                    // Same walk as vanilla: every non-basic job license that is either
                    // required-and-missing or already acquired for this job.
                    var required = JobLicenseType_v2.ToV2List(job.requiredLicenses);
                    var missing = LicenseManager.Instance.GetMissingLicensesForJob(required);
                    var acquired = LicenseManager.Instance.GetAcquiredLicensesForJob(required);
                    foreach (var license in DV.Globals.G.Types.jobLicenses)
                    {
                        if (license.v1 == JobLicenses.Basic)
                            continue;
                        bool isAcquired = acquired.Contains(license);
                        if (!isAcquired && !missing.Contains(license))
                            continue;
                        licensesData.Add(new MissingLicensesPageTemplatePaperData.LicensePrintData(
                            LocalizationAPI.L(license.localizationKey), license.icon, isAcquired));
                    }
                }
                else
                {
                    var license = LicenseManager.Instance.GetMissingConcurrentJobsLicense();
                    if (license != null)
                    {
                        licensesData.Add(new MissingLicensesPageTemplatePaperData.LicensePrintData(
                            LocalizationAPI.L(license.localizationKey), license.icon, false));
                    }
                }
            }
            catch (Exception ex)
            {
                // A minimal page without license rows still beats the vanilla NRE.
                Main.Log($"[DirectHaul] Missing-license page fallback: {ex.Message}");
            }

            __result = new List<TemplatePaperData>
            {
                new MissingLicensesPageTemplatePaperData(
                    DirectHaulBooklet.DIRECT_HAUL_NAME, string.Empty, job.ID,
                    DirectHaulBooklet.DIRECT_HAUL_COLOR, licensesData)
            };
            return false;
        }
    }
}
