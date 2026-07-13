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
            if (!found) Main.Log("[CarPool] deleter method not found; pool cars are NOT protected.");
            return found;
        }

        [HarmonyPrefix]
        public static bool Prefix(TrainCar trainCar, ref bool __result)
        {
            if (trainCar?.logicCar?.carGuid != null && DleCarPool.Instance.Contains(trainCar.logicCar.carGuid))
            {
                __result = false;
                return false;
            }
            return true;
        }
    }
}
