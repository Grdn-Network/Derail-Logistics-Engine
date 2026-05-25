using System.Collections.Generic;
using Newtonsoft.Json;

namespace GRDNInterchange.Data
{
    /// <summary>
    /// Per-car metadata stamped at feeder job creation time.
    /// Survives save/load via SaveGameData.SetObject.
    /// </summary>
    public class CarDestRecord
    {
        public string TrueDestYardId { get; set; }
        public string TrueOriginYardId { get; set; }
        public string AssignedHubYardId { get; set; }
        /// <summary>Phase this car is currently in within the interchange chain.</summary>
        public InterchangePhase Phase { get; set; } = InterchangePhase.Feeder;
    }

    public enum InterchangePhase
    {
        Feeder,      // origin → hub (inbound leg, in progress)
        SortAtHub,   // waiting for / in shunt-sort job at origin hub
        BlockHaul,   // hub → hub (mainline block train)
        BreakAtHub,  // waiting for / in breakdown shunt at receiving hub
        FinalMile,   // hub → true final destination
        Delivered,   // complete — entry can be pruned on next save
    }

    public class CarDestinationStore
    {
        private const string SAVE_KEY = "GRDNInterchange_v1";

        public static readonly CarDestinationStore Instance = new CarDestinationStore();
        private CarDestinationStore() { }

        private Dictionary<string, CarDestRecord> _store =
            new Dictionary<string, CarDestRecord>();

        // ── Write ──────────────────────────────────────────────────────────────────

        public void Register(string carGuid, string trueDestYardId,
                             string trueOriginYardId, string hubYardId)
        {
            _store[carGuid] = new CarDestRecord
            {
                TrueDestYardId    = trueDestYardId,
                TrueOriginYardId  = trueOriginYardId,
                AssignedHubYardId = hubYardId,
                Phase             = InterchangePhase.Feeder,
            };
            Main.Log($"[Store] Registered {carGuid}: {trueOriginYardId}→{trueDestYardId} via {hubYardId}");
        }

        public void SetPhase(string carGuid, InterchangePhase phase)
        {
            if (_store.TryGetValue(carGuid, out var rec))
                rec.Phase = phase;
        }

        public void Remove(string carGuid) => _store.Remove(carGuid);

        // ── Read ───────────────────────────────────────────────────────────────────

        public CarDestRecord Get(string carGuid) =>
            _store.TryGetValue(carGuid, out var r) ? r : null;

        public bool IsInterchangeCar(string carGuid) => _store.ContainsKey(carGuid);

        /// <summary>Returns a snapshot of all tracked car records (for debug output).</summary>
        public IReadOnlyDictionary<string, CarDestRecord> GetAll() => _store;

        // ── Persistence ────────────────────────────────────────────────────────────

        public void SaveTo(SaveGameData data)
        {
            // Prune delivered entries before saving
            var toRemove = new List<string>();
            foreach (var kv in _store)
                if (kv.Value.Phase == InterchangePhase.Delivered)
                    toRemove.Add(kv.Key);
            foreach (var k in toRemove)
                _store.Remove(k);

            data.SetObject(SAVE_KEY, _store);
            Main.Log($"[Store] Saved {_store.Count} car records");
        }

        public void LoadFromSave(SaveGameData data)
        {
            var loaded = data.GetObject<Dictionary<string, CarDestRecord>>(SAVE_KEY);
            _store = loaded ?? new Dictionary<string, CarDestRecord>();
            Main.Log($"[Store] Loaded {_store.Count} car records");
        }
    }
}
