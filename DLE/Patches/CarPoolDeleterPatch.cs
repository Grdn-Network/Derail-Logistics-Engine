using DLE.Data;
using HarmonyLib;

namespace DLE.Patches
{
    /// <summary>
    /// Pool cars never despawn: the finite fleet is the point of persistence mode. Guarded
    /// with Prepare so a game update that renames the method skips the patch instead of
    /// failing the whole mod load.
    /// </summary>
    [HarmonyPatch(typeof(UnusedTrainCarDeleter), "AreDeleteConditionsFulfilled")]
    public static class CarPoolDeleterPatch
    {
        [HarmonyPrepare]
        public static bool Prepare()
        {
            var found = AccessTools.Method(typeof(UnusedTrainCarDeleter), "AreDeleteConditionsFulfilled") != null;
            if (!found) Main.LogAlways("[CarPool] deleter method not found; pool cars are NOT protected.");
            return found;
        }

        [HarmonyPrefix]
        public static bool Prefix(TrainCar trainCar, ref bool __result)
        {
            // Client-installed DLE (booklet rendering support): the pool is host state.
            // A client must not arm a pool from its own local save or influence deletion
            // in a synced world; the host is authoritative over every car.
            if (!Main.IsHostOrSingleplayer()) return true;

            // The first delete decisions run during world load, before OnWorldLoaded has
            // handed DLE its save data; arm the pool from the save on demand so the guard
            // is never consulted unarmed (an unarmed guard condemned the whole restored
            // fleet on every load).
            DleCarPool.Instance.EnsureLoaded();
            if (trainCar?.logicCar?.carGuid != null && DleCarPool.Instance.Contains(trainCar.logicCar.carGuid))
            {
                __result = false;
                return false;
            }
            return true;
        }
    }
}
