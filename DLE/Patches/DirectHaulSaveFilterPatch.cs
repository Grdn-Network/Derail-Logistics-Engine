using DLE.Jobs;
using DV.Logic.Job;
using HarmonyLib;

namespace DLE.Patches
{
    /// <summary>
    /// Phase 1: keep DLE's Direct Haul chains out of the vanilla save. The save collector
    /// drops any chain whose GetJobChainSaveData returns null, so we skip the original for
    /// our own chains. This stops the vanilla serialiser from writing a ComplexTransport
    /// chain it cannot reload. DLE gains its own job persistence in a later commit.
    /// </summary>
    [HarmonyPatch(typeof(JobChainController), "GetJobChainSaveData")]
    public static class DirectHaulSaveFilterPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(JobChainController __instance, ref JobChainSaveData __result)
        {
            var job = __instance?.currentJobInChain;
            if (job != null && JobUtils.ManagedJobIds.Contains(job.ID))
            {
                __result = null;
                return false; // skip original; collector drops the null entry
            }
            return true;
        }
    }
}
