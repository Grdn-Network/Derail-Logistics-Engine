using DLE.Economy;
using DLE.Jobs;
using DV.InventorySystem;
using DV.Logic.Job;
using DV.Utils;
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

            // Only cargo that was actually LOADED can be delivered: an attached but
            // never-loaded cut used to mint stock and pay here.
            int total = def.carsToTransport?.Count ?? 0;
            int deliverable = System.Math.Min(total, def.loadedCarloads);
            if (deliverable <= 0)
            {
                Main.LogAlways($"[Economy] {lastJobInChain.ID} completed with no cargo ever loaded; nothing credited or paid.");
                return;
            }
            int accepted = EconomyState.Instance.OnDelivered(dest, def.transportedCargo, deliverable);

            // Faux booklet paid nothing; the wallet is paid here, and only for the cargo the
            // destination actually accepted (nothing when it is full). This is the single
            // gated payout: loading and storage-unloads never reach it.
            if (accepted > 0 && def.deliveryPayment > 0f)
            {
                double pay = def.deliveryPayment * (double)accepted / System.Math.Max(1, total);
                var inv = SingletonBehaviour<Inventory>.Instance;
                if (inv != null)
                {
                    inv.SetMoney(inv.PlayerMoney + pay);
                    Main.Log($"[Economy] delivery paid {pay:0} for {accepted}/{total} {def.transportedCargo} at {dest}.");
                }
                else
                {
                    Main.LogAlways($"[Economy] {dest}: delivery complete but Inventory.Instance null; not paid.");
                }
            }
        }
    }
}
