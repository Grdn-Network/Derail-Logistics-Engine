using DV.Logic.Job;
using DV.ThingTypes;
using GRDNInterchange.Data;
using GRDNInterchange.Jobs;
using HarmonyLib;
using UnityEngine;

namespace GRDNInterchange.Patches
{
    /// <summary>
    /// Blocks SL/SU job generation at spoke stations (and optionally at hubs).
    ///
    /// WHY PREFIX INSTEAD OF POSTFIX:
    /// A postfix runs after FinalizeSetupAndGenerateFirstJob, by which point the job is
    /// already registered with the station and DVMP has already broadcast it to all clients.
    /// Calling DestroyChain() in a postfix removes it, but DV immediately sees an empty slot
    /// and regenerates the same job — causing an infinite flicker loop on the board.
    ///
    /// A prefix fires BEFORE finalization, so the job is never registered anywhere. DV never
    /// sees a "slot filled then removed" cycle and stops trying to regenerate.
    ///
    /// HOW STATION IS DETERMINED IN PREFIX:
    /// responsibleStationForJobChain is set by StationProceduralJobsController before it calls
    /// FinalizeSetupAndGenerateFirstJob, so it is available in the prefix. Falls back to
    /// chainOriginYardId if the field is null (uncommon but safe).
    ///
    /// Settings:
    ///   GlobalShunting (false) — suppress SL/SU at spokes. Spokes only show GRDN feeders.
    ///   HubShunting    (true)  — allow SL/SU at hubs (local harbour cargo, etc.).
    /// </summary>
    [HarmonyPatch(typeof(JobChainController), nameof(JobChainController.FinalizeSetupAndGenerateFirstJob))]
    public static class GlobalShuntingPatch
    {
        private static readonly System.Reflection.FieldInfo _responsibleStationField =
            typeof(JobChainController).GetField(
                "responsibleStationForJobChain",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        [HarmonyPrefix]
        public static bool Prefix(JobChainController __instance)
        {
            if (!Main.IsHostOrSingleplayer()) return true;

            var registry = HubRegistry.Instance;
            if (registry == null || !registry.IsReady) return true;

            // Determine job type from the GO components BEFORE finalization.
            // currentJobInChain is not yet set at prefix time.
            var go = __instance.jobChainGO;
            if (go == null) return true;

            bool isSL = go.GetComponent<StaticShuntingLoadJobDefinition>() != null;
            bool isSU = go.GetComponent<StaticShuntingUnloadJobDefinition>() != null;
            if (!isSL && !isSU) return true; // not SL/SU — let NewJobChainInterceptPatch handle it

            // responsibleStationForJobChain is set by the owning StationProceduralJobsController
            // before it calls FinalizeSetupAndGenerateFirstJob, so it is available here.
            var responsibleStation =
                _responsibleStationField?.GetValue(__instance) as StationController;

            // Fall back: find station by checking which station owns this JCC
            // (chainOriginYardId is unreliable for multi-step chains)
            string originYardId = responsibleStation?.stationInfo.YardID;

            if (string.IsNullOrEmpty(originYardId))
            {
                // Last resort: try to read from a SL definition's starting track
                if (isSL)
                {
                    var def = go.GetComponent<StaticShuntingLoadJobDefinition>();
                    if (def?.carsPerStartingTrack?.Count > 0)
                    {
                        var group = def.carsPerStartingTrack[0];
                        if (group?.cars?.Count > 0 && group.cars[0]?.CurrentTrack != null)
                            originYardId = TrackClassifier.GetYardId(group.cars[0].CurrentTrack);
                    }
                }
                if (string.IsNullOrEmpty(originYardId)) return true; // can't determine station
            }

            if (Main.Config.ExcludedYardIds.Contains(originYardId)) return true;

            bool isHub = registry.IsHub(originYardId);

            if (isHub)
            {
                if (!Main.Settings.HubShunting)
                {
                    Main.Log($"[GlobalShunting] Blocking {(isSL ? "SL" : "SU")} at hub {originYardId} before registration");
                    Object.Destroy(go);
                    return false; // skip FinalizeSetupAndGenerateFirstJob entirely
                }
                return true;
            }

            // Spoke station
            if (!Main.Settings.GlobalShunting)
            {
                Main.Log($"[GlobalShunting] Blocking {(isSL ? "SL" : "SU")} at spoke {originYardId} before registration");
                Object.Destroy(go);
                return false; // skip — job never registered, no slot freed, no regeneration cycle
            }

            return true;
        }
    }
}
