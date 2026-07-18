using System;
using System.Collections.Generic;
using System.Linq;

namespace DLE.Dispatch
{
    /// <summary>
    /// Dispatcher coordination board for logistics runs: unpaid, bookletless movements
    /// (typically repositioning empty pool cars to a producer). Pure data; the mod does not
    /// enforce anything about them. Persisted under DLE_Logistics_v1.
    /// </summary>
    public class LogisticsBoard
    {
        private const string SaveKey = "DLE_Logistics_v1";
        private const int SchemaVersion = 1;

        public static readonly LogisticsBoard Instance = new LogisticsBoard();
        private LogisticsBoard() { }

        [Serializable]
        public class Order
        {
            public string Id;
            public string FromYardId;
            public string ToYardId;
            public int CarCount;
            public string Cargo;      // optional: what the cars are FOR (picks car type)
            public string Note;       // free text from the dispatcher
            public string Status;     // Open, InProgress, Done
            public string CreatedUtc;
            // The zero-pay EmptyHaul booklet backing this run, when idle empties existed
            // to bind at creation; null keeps the run a pure coordination note. Additive
            // field: older saves deserialize it as null.
            public string JobId;
        }

        private readonly Dictionary<string, Order> _orders =
            new Dictionary<string, Order>(StringComparer.Ordinal);
        private int _nextId = 1;

        public IReadOnlyCollection<Order> All => _orders.Values;

        public Order Create(string fromYard, string toYard, int carCount, string cargo, string note)
        {
            var order = new Order
            {
                Id = $"LOG-{_nextId++:D3}",
                FromYardId = fromYard,
                ToYardId = toYard,
                CarCount = carCount,
                Cargo = cargo,
                Note = note,
                Status = "Open",
                CreatedUtc = DateTime.UtcNow.ToString("o"),
            };
            _orders[order.Id] = order;
            Main.Log($"[Logistics] {order.Id}: {carCount} car(s) {fromYard} -> {toYard}" +
                     (string.IsNullOrEmpty(cargo) ? "" : $" for {cargo}") + ".");
            return order;
        }

        public bool SetStatus(string id, string status)
        {
            if (!_orders.TryGetValue(id, out var order)) return false;
            order.Status = status;
            Main.Log($"[Logistics] {id} -> {status}.");
            return true;
        }

        public bool Delete(string id) => _orders.Remove(id);

        [Serializable]
        private class SaveData
        {
            public int SchemaVersion;
            public int NextId;
            public List<Order> Orders;
        }

        public void SaveTo(SaveGameData data) =>
            data.SetObject(SaveKey, new SaveData
            {
                SchemaVersion = SchemaVersion,
                NextId = _nextId,
                Orders = _orders.Values.ToList(),
            });

        public void LoadFrom(SaveGameData data)
        {
            _orders.Clear();
            _nextId = 1;
            SaveData payload = null;
            try { payload = data.GetObject<SaveData>(SaveKey); }
            catch (Exception ex) { Main.LogAlways($"[Logistics] save unreadable, starting empty: {ex.Message}"); }
            if (payload?.Orders == null || payload.SchemaVersion != SchemaVersion) return;
            foreach (var o in payload.Orders) _orders[o.Id] = o;
            _nextId = Math.Max(1, payload.NextId);
        }
    }
}
