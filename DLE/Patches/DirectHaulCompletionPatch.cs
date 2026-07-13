using DLE.Economy;
using DLE.Jobs;
using DV.Logic.Job;
using HarmonyLib;

namespace DLE.Patches
{
    /// <summary>
    /// When a Direct Haul chain finishes (cars unloaded at the destination), credit the
    /// destination stockpile and let the economy convert. Host/singleplayer only; on a
    /// client the chain controller does not exist, so this never runs there.
    /// </summary>
    [HarmonyPatch(typeof(JobChainController), "OnLastJobInChainCompleted")]
    public static class DirectHaulCompletionPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Job lastJobInChain)
        {
            if (lastJobInChain == null) return;
            if (!Main.IsHostOrSingleplayer()) return;
            if (!StaticDirectHaulJobDefinition.jobDefinitions.TryGetValue(lastJobInChain.ID, out var def))
                return;

            var dest = def.chainData?.chainDestinationYardId;
            if (string.IsNullOrEmpty(dest)) return;

            EconomyState.Instance.OnDelivered(dest, def.transportedCargo, def.carsToTransport?.Count ?? 0);
        }
    }
}
