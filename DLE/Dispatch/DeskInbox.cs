using DLE.Jobs;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DLE.Dispatch
{
    /// <summary>
    /// The Desk's fax tray (#225, Desk v20): reply faxes from consignee stations (and
    /// later, phone call slips) queue here with a delivery delay and print when the
    /// board polls. Verdicts are computed AT DELIVERY, not at request time, so a reply
    /// judges the yard as it is when the paper prints, not five seconds ago. Session
    /// paper only: never persisted; a world load clears the tray, and jobs left
    /// "pending" by a lost reply revert to unsigned on restore (the mail got lost).
    /// Host-only state, like everything the server serves.
    /// </summary>
    public static class DeskInbox
    {
        private const int Keep = 40;
        private const float ReplySeconds = 5f;

        public class Item
        {
            public int Id;
            public string Type;      // sign-reply (calls come later)
            public string JobId;
            public string Yard;      // who the paper is from
            public string Verdict;   // signed | refused | void
            public string Text;
            public float DueAt;      // realtimeSinceStartup
            public bool Matured;
            public bool Acked;
        }

        private static readonly List<Item> _items = new List<Item>();
        private static int _next = 1;

        public static void Clear()
        {
            lock (_items) { _items.Clear(); _next = 1; }
        }

        /// <summary>Queue the consignee's reply to a sign request. The verdict waits
        /// for delivery; this only posts the envelope.</summary>
        public static void QueueSignReply(string jobId, string destYard)
        {
            lock (_items)
            {
                _items.Add(new Item
                {
                    Id = _next++,
                    Type = "sign-reply",
                    JobId = jobId,
                    Yard = destYard,
                    DueAt = UnityEngine.Time.realtimeSinceStartup + ReplySeconds,
                });
                if (_items.Count > Keep * 2) _items.RemoveRange(0, _items.Count - Keep);
            }
        }

        /// <summary>Deliver every due envelope: resolve its verdict against the live
        /// world and stamp the job. Runs on the main thread (payload builds).</summary>
        public static void Mature()
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            lock (_items)
            {
                foreach (var it in _items)
                {
                    if (it.Matured || now < it.DueAt) continue;
                    it.Matured = true;
                    if (it.Type != "sign-reply") continue;
                    if (!StaticDirectHaulJobDefinition.jobDefinitions.TryGetValue(it.JobId, out var def) || def == null)
                    {
                        it.Verdict = "void";
                        it.Text = $"{it.JobId}: no longer on the board; the request died with it.";
                        continue;
                    }
                    var (signed, reason, by) = SignDesk.Resolve(def);
                    if (signed)
                    {
                        def.signState = "signed";
                        def.signedBy = by;
                        def.signReason = null;
                        it.Verdict = "signed";
                        it.Text = $"{it.JobId} SIGNED by {by}.";
                    }
                    else
                    {
                        def.signState = "refused";
                        def.signedBy = null;
                        def.signReason = reason;
                        it.Verdict = "refused";
                        it.Text = $"{it.JobId} NOT SIGNED by {it.Yard}: {reason}";
                    }
                    Main.LogAlways($"[SignDesk] {it.Text}");
                }
            }
        }

        public static int UnackedCount()
        {
            Mature();
            lock (_items) return _items.Count(i => i.Matured && !i.Acked);
        }

        public static object Payload()
        {
            Mature();
            lock (_items)
            {
                return _items.Where(i => i.Matured)
                    .OrderByDescending(i => i.Id)
                    .Take(Keep)
                    .Select(i => new { id = i.Id, type = i.Type, jobId = i.JobId, yard = i.Yard, verdict = i.Verdict, text = i.Text, acked = i.Acked })
                    .ToList();
            }
        }

        public static int Ack(int[] ids)
        {
            lock (_items)
            {
                int n = 0;
                foreach (var it in _items)
                    if (it.Matured && !it.Acked && (ids == null || ids.Length == 0 || Array.IndexOf(ids, it.Id) >= 0))
                    { it.Acked = true; n++; }
                return n;
            }
        }
    }

    /// <summary>
    /// The consignee's judgment on a sign request: REASONED first (is there actually
    /// room at the destination for what this paper sends), flavor RNG second and OFF
    /// by default (settings.stationDenyChance, owner has not ruled on paperwork RNG).
    /// </summary>
    internal static class SignDesk
    {
        public static (bool signed, string reason, string by) Resolve(StaticDirectHaulJobDefinition def)
        {
            var dest = def.chainData?.chainDestinationYardId;
            if (string.IsNullOrEmpty(dest)) return (false, "no consignee on the paper", null);
            var econ = Economy.EconomyState.Instance;

            // Room for every line of the manifest as it stands right now.
            var lines = new List<(DV.ThingTypes.CargoType cargo, int cars)>();
            if (def.manifest != null && def.manifest.Count > 0)
                foreach (var l in def.manifest)
                    lines.Add((l.Cargo, Math.Max(l.CarIds?.Count ?? 0, 1)));
            else
                lines.Add((def.transportedCargo, Math.Max(def.carsToTransport?.Count ?? 0, def.plannedCarCount)));
            foreach (var (cargo, cars) in lines)
            {
                float room = econ.GetRoom(dest, cargo);
                if (room < cars)
                    return (false, $"no room for {Economy.CargoCategories.DisplayName(cargo)} ({cars} carload(s), {Math.Floor(room)} free)", null);
            }

            float chance = Economy.RecipeProvider.Tuning.stationDenyChance;
            if (chance > 0f && UnityEngine.Random.value < chance)
                return (false, "request declined; resubmit later", null);

            return (true, null, $"{dest} stationmaster");
        }
    }
}
