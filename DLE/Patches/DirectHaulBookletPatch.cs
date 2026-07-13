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
        public const string DIRECT_HAUL_NAME = "Direct Haul";

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

            cargoName = string.Empty;
            if (cargoTypePerCar.Count > 0)
            {
                try
                {
                    cargoName = LocalizationAPI.L(cargoTypePerCar[0].ToV2().localizationKeyFull);
                }
                catch (Exception)
                {
                    cargoName = cargoTypePerCar[0].ToString();
                }
            }
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

            var origin = job.chainOriginStationInfo;
            var destination = job.chainDestinationStationInfo;

            return new FrontPageTemplatePaperData(
                DIRECT_HAUL_NAME, string.Empty, job.ID, DIRECT_HAUL_COLOR,
                $"Transport {cars.Count} loads of {cargoName}",
                job.requiredLicenses,
                cargoTypePerCar.Distinct().ToList(), cargoTypePerCar,
                string.Empty, string.Empty, TemplatePaperData.NOT_USED_COLOR,
                LocalizationAPI.L(origin.LocalizationKey), origin.Type, origin.StationColor,
                LocalizationAPI.L(destination.LocalizationKey), destination.Type, destination.StationColor,
                cars, length, mass, value, timeLimit,
                job.basePayment.ToString("N0", LocalizationAPI.CC),
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
    }

    [HarmonyPatch(typeof(BookletCreator_JobOverview), nameof(BookletCreator_JobOverview.GetJobOverviewTemplateData))]
    internal static class DirectHaulJobOverviewPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Job_data job, ref List<TemplatePaperData> __result)
        {
            if (job == null || job.type != JobType.ComplexTransport)
                return true;

            __result = new List<TemplatePaperData>
            {
                DirectHaulBooklet.BuildFrontPage(job, string.Empty, string.Empty)
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

            var origin = job.chainOriginStationInfo;
            var destination = job.chainDestinationStationInfo;

            var cover = new CoverPageTemplatePaperData(
                job.ID, DirectHaulBooklet.DIRECT_HAUL_NAME, "1", "5");

            var frontPage = DirectHaulBooklet.BuildFrontPage(job, "2", "5");

            var loadPage = new TaskTemplatePaperData(
                "1",
                LocalizationAPI.L("job/task_type_load"),
                LocalizationAPI.L("job/task_desc_load"),
                origin.YardID, origin.StationColor,
                DirectHaulBooklet.GetTaskTrackDisplay(job, 0), C.TRACK_COLOR,
                string.Empty, string.Empty, TemplatePaperData.NOT_USED_COLOR,
                cars, null, "3", "5");

            var unloadPage = new TaskTemplatePaperData(
                "2",
                LocalizationAPI.L("job/task_type_unload"),
                LocalizationAPI.L("job/task_desc_unload"),
                destination.YardID, destination.StationColor,
                DirectHaulBooklet.GetTaskTrackDisplay(job, 1), C.TRACK_COLOR,
                string.Empty, string.Empty, TemplatePaperData.NOT_USED_COLOR,
                cars, null, "4", "5");

            var validatePage = new ValidateJobTaskTemplatePaperData("3", "5", "5");

            __result = new List<TemplatePaperData>
            {
                cover, frontPage, loadPage, unloadPage, validatePage
            };
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
