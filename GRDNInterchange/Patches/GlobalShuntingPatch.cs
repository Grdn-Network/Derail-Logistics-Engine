using DV.Logic.Job;
using GRDNInterchange.Data;
using GRDNInterchange.Jobs;
using HarmonyLib;

namespace GRDNInterchange.Patches
{
    /// <summary>
    /// When Settings.GlobalShunting = false (the default), suppresses vanilla
    /// ShuntingLoad and ShuntingUnload job generation at non-hub (spoke) stations.
    ///
    /// Effect: spokes only ever expose GRDN feeder jobs to the player. Empty-car
    /// shunting at origin yards is removed — the GRDN network operates on already-
    /// loaded freight cars intercepted from vanilla FreightHaul jobs.
    ///
    /// Runs as a second postfix on the same FinalizeSetupAndGenerateFirstJob hook as
    /// NewJobChainInterceptPatch. HubRegistry guard ensures we do nothing before
    /// OnWorldLoaded fires (so saved SL/SU jobs are not destroyed on load).
    /// </summary>
    [HarmonyPatch(typeof(JobChainController), nameof(JobChainController.FinalizeSetupAndGenerateFirstJob))]
    public static class GlobalShuntingPatch
    {
        [HarmonyPostfix]
        public static void Postfix(JobChainController __instance)
        {
            // Feature off — vanilla shunting is allowed everywhere
            if (Main.Settings.GlobalShunting) return;

            if (!Main.IsHostOrSingleplayer()) return;

            var registry = HubRegistry.Instance;
            // HubRegistry not ready (world still loading) — leave saved jobs alone
            if (registry == null || !registry.IsReady) return;

            var job = __instance.currentJobInChain;
            if (job == null) return;

            // Only suppress SL and SU — leave FH, EmptyHaul, etc. to NewJobChainInterceptPatch
            if (job.jobType != JobType.ShuntingLoad && job.jobType != JobType.ShuntingUnload)
                return;

            // Skip jobs we spawned (sort jobs are Transport, so this is an extra guard)
            if (job.ID != null && JobUtils.ManagedJobIds.Contains(job.ID)) return;

            var originYardId = job.chainData?.chainOriginYardId;
            if (string.IsNullOrEmpty(originYardId)) return;

            // Hub SL/SU is fine — hub sort jobs are Transport type anyway, but guard defensively
            if (registry.IsHub(originYardId)) return;

            // Excluded yards (military chain etc.) are never touched
            if (Main.Config.ExcludedYardIds.Contains(originYardId)) return;

            Main.Log($"[GlobalShunting] Suppressing {job.jobType} at spoke {originYardId} (GlobalShunting=false)");
            __instance.DestroyChain();
        }
    }
}
