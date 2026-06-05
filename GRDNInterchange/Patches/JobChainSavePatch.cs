using DV.Logic.Job;
using HarmonyLib;
using System;

namespace GRDNInterchange.Patches
{
    /// <summary>
    /// Suppresses "Uninitialized chain controller!" exceptions that fire on every
    /// save/autosave when DVMP is active.
    ///
    /// DVMP creates client-side shadow JCCs for every network job via its own packet
    /// deserialization path. These shadows never go through FinalizeSetupAndGenerateFirstJob,
    /// so currentJobInChain remains null. DV's save manager iterates all JCCs and calls
    /// GetJobChainSaveData() on each — the shadow JCCs throw because they're uninitialized.
    ///
    /// The HarmonyFinalizer catches the exception only for JCCs with a null job (i.e. the
    /// DVMP ghosts) and swallows it, allowing the save to complete. Real JCC exceptions
    /// (non-null job) are still rethrown.
    /// </summary>
    [HarmonyPatch(typeof(JobChainController), "GetJobChainSaveData")]
    public static class JobChainSavePatch
    {
        [HarmonyFinalizer]
        public static Exception Finalizer(JobChainController __instance, Exception __exception)
        {
            if (__exception != null && __instance.currentJobInChain == null)
            {
                Main.Log("[JobChainSavePatch] Suppressed uninitialized JCC save exception (DVMP ghost)");
                return null; // swallow — method returns default(T) = null, which GetJobsSaveGameData handles
            }
            return __exception; // rethrow anything from a real JCC
        }
    }
}
