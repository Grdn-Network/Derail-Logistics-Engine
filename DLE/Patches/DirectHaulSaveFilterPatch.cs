using DLE.Jobs;
using DV.Logic.Job;
using HarmonyLib;
using System;
using System.Linq;

namespace DLE.Patches
{
    /// <summary>
    /// Keeps DLE's Direct Haul chains out of the vanilla save: the vanilla serialiser
    /// cannot reload a ComplexTransport chain, so GetJobChainSaveData returns null for
    /// managed chains. The vanilla collector adds that null to the saved array
    /// unfiltered; DirectHaulSaveNullScrubPatch below strips it. DLE persists these
    /// jobs in its own store and rebuilds them on load.
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

        /// <summary>
        /// DVMP can leave ghost chain controllers behind that throw
        /// "Uninitialized chain controller!" during save collection, which aborts the
        /// whole autosave. Swallow exactly that exception (the collector drops the null
        /// entry) and rethrow anything else.
        /// </summary>
        [HarmonyFinalizer]
        public static Exception Finalizer(Exception __exception, ref JobChainSaveData __result)
        {
            if (__exception != null && __exception.Message != null &&
                __exception.Message.Contains("Uninitialized"))
            {
                Main.LogAlways($"[DirectHaul] Suppressed save exception for ghost chain: {__exception.Message}");
                __result = null;
                return null;
            }
            return __exception;
        }
    }

    /// <summary>
    /// JobSaveManager.GetJobsSaveGameData adds every chain's save data to its list with
    /// no null check, so a chain that serialises to null (a DLE chain, or a ghost chain
    /// hit by the finalizer above) becomes a null entry in the saved jobChains array.
    /// JobSaveManager.LoadJobChain dereferences each entry immediately, and one null
    /// there makes the loader delete every car and job in the world as its cleanup
    /// response. Nulls are stripped on save, and stripped again on load so saves
    /// already written with null entries load instead of resetting the world.
    /// </summary>
    [HarmonyPatch(typeof(JobSaveManager))]
    public static class DirectHaulSaveNullScrubPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch("GetJobsSaveGameData")]
        public static void ScrubOnSave(JobsSaveGameData __result) => Scrub(__result, "save");

        [HarmonyPrefix]
        [HarmonyPatch("LoadJobSaveGameData")]
        public static void ScrubOnLoad(JobsSaveGameData saveData) => Scrub(saveData, "load");

        private static void Scrub(JobsSaveGameData data, string where)
        {
            if (data?.jobChains == null || data.jobChains.Length == 0) return;
            var clean = data.jobChains.Where(c => c != null).ToArray();
            if (clean.Length != data.jobChains.Length)
            {
                Main.LogAlways($"[DirectHaul] Scrubbed {data.jobChains.Length - clean.Length} null job chain entries on {where}.");
                data.jobChains = clean;
            }
        }
    }
}
