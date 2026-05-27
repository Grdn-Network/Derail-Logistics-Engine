using DV.Logic.Job;
using DV.ThingTypes;
using GRDNInterchange.Data;
using GRDNInterchange.Jobs;
using HarmonyLib;

namespace GRDNInterchange.Patches
{
    /// <summary>
    /// Controls vanilla ShuntingLoad / ShuntingUnload job generation.
    ///
    /// Two independent settings:
    ///   GlobalShunting (default false) — when false, SL/SU at spoke stations are suppressed.
    ///     Spokes only ever expose GRDN feeder jobs. Set true to allow vanilla SL/SU at spokes.
    ///
    ///   HubShunting (default true) — when true, vanilla SL/SU at hub stations are allowed
    ///     (e.g. harbour inbound shunting for local-destined cargo).
    ///     Set false to suppress hub SL/SU as well, leaving hub operations entirely to GRDN sort jobs.
    ///
    /// The two settings are independent: you can allow spokes but suppress hubs, or vice-versa.
    /// </summary>
    [HarmonyPatch(typeof(JobChainController), nameof(JobChainController.FinalizeSetupAndGenerateFirstJob))]
    public static class GlobalShuntingPatch
    {
        [HarmonyPostfix]
        public static void Postfix(JobChainController __instance)
        {
            if (!Main.IsHostOrSingleplayer()) return;

            var registry = HubRegistry.Instance;
            // HubRegistry not ready (world still loading) — leave saved jobs alone
            if (registry == null || !registry.IsReady) return;

            var job = __instance.currentJobInChain;
            if (job == null) return;

            // Only suppress SL and SU — FH/EmptyHaul etc. are handled by NewJobChainInterceptPatch
            if (job.jobType != JobType.ShuntingLoad && job.jobType != JobType.ShuntingUnload)
                return;

            // Skip jobs we spawned
            if (job.ID != null && JobUtils.ManagedJobIds.Contains(job.ID)) return;

            var originYardId = job.chainData?.chainOriginYardId;
            if (string.IsNullOrEmpty(originYardId)) return;

            // Excluded yards (military chain etc.) are never touched
            if (Main.Config.ExcludedYardIds.Contains(originYardId)) return;

            bool isHub = registry.IsHub(originYardId);

            if (isHub)
            {
                // Hub SL/SU: controlled by HubShunting (true = allow, false = suppress)
                if (!Main.Settings.HubShunting)
                {
                    Main.Log($"[GlobalShunting] Suppressing {job.jobType} at hub {originYardId} (HubShunting=false)");
                    __instance.DestroyChain();
                }
                return;
            }

            // Spoke SL/SU: controlled by GlobalShunting (false = suppress, true = allow)
            if (!Main.Settings.GlobalShunting)
            {
                Main.Log($"[GlobalShunting] Suppressing {job.jobType} at spoke {originYardId} (GlobalShunting=false)");
                __instance.DestroyChain();
            }
        }
    }
}
