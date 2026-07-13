using DLE.Economy;
using HarmonyLib;

namespace DLE.Patches
{
    /// <summary>
    /// The economy owns freight job generation: vanilla procedural jobs no longer spawn at
    /// economy stations (on host AND client; the host drives all real generation, and
    /// clients must not spawn their own private cars). Excluded yards (military) keep
    /// vanilla generation since they are outside the economy.
    /// </summary>
    [HarmonyPatch(typeof(StationProceduralJobsController), nameof(StationProceduralJobsController.TryToGenerateJobs))]
    public static class VanillaGenerationSuppressorPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(StationProceduralJobsController __instance)
        {
            var yardId = __instance?.stationController?.stationInfo?.YardID;
            if (string.IsNullOrEmpty(yardId)) return true;
            if (RecipeProvider.ExcludedYards.Contains(yardId)) return true;

            if (Main.Settings?.VerboseLogging == true)
                Main.Log($"[Suppressor] vanilla job generation blocked at {yardId} (economy owns it).");
            return false;
        }
    }
}
