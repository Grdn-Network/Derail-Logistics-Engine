using DV.Logic.Job;
using HarmonyLib;

namespace DLE.Patches
{
    /// <summary>
    /// Fresh paper without the walk of shame: vanilla prints a station's job overview
    /// papers when the player ENTERS generation range, so a job added while someone is
    /// already standing in the office shows no paper until they leave and come back:
    /// in MP that meant "reload the station", which is not a real instruction. Arming
    /// attemptJobOverviewGeneration (publicized) after every AddJobToStation makes the
    /// station's own Update print the new job's paper on the spot while the zone is
    /// live; out of range it changes nothing, zone entry already handles that.
    /// Deliberately ungated: overviews are per-peer objects (each client prints its
    /// own), and dv-mp routes synced jobs through this same method on clients. The
    /// lock sweep still eats DLE paper in lock mode exactly as before, and the spawn
    /// path itself skips jobs that already have paper, so re-arming is idempotent.
    /// </summary>
    [HarmonyPatch(typeof(Station), nameof(Station.AddJobToStation))]
    public static class JobPaperOnAddPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Station __instance)
        {
            try
            {
                var sc = StationController.GetStationByYardID(__instance?.ID);
                if (sc != null) sc.attemptJobOverviewGeneration = true;
            }
            catch { }
        }
    }
}
