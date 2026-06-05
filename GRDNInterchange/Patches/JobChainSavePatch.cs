using DV.Logic.Job;
using HarmonyLib;
using System;

namespace GRDNInterchange.Patches
{
    /// <summary>
    /// Suppresses "Uninitialized chain controller!" exceptions that fire on every
    /// save/autosave when DVMP is active.
    ///
    /// DVMP creates client-side shadow JCCs via its own packet deserialization path.
    /// These shadows never go through FinalizeSetupAndGenerateFirstJob so they throw
    /// when DV's save manager calls GetJobChainSaveData() on them.
    ///
    /// The Finalizer does NOT access __instance because the ghost JCC may be in a state
    /// where reading any field itself throws — crashing the Finalizer and letting the
    /// original exception propagate anyway. Checking the message string is safe.
    /// </summary>
    [HarmonyPatch(typeof(JobChainController), "GetJobChainSaveData")]
    public static class JobChainSavePatch
    {
        [HarmonyFinalizer]
        public static Exception Finalizer(Exception __exception)
        {
            if (__exception?.Message?.Contains("Uninitialized") == true)
            {
                Main.Log("[JobChainSavePatch] Suppressed uninitialized JCC save exception (DVMP ghost)");
                return null; // swallow — GetJobsSaveGameData receives null and skips it
            }
            return __exception; // rethrow anything from a real JCC
        }
    }
}
