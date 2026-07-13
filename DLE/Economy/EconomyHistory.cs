using System;
using System.Collections.Generic;
using System.Linq;

namespace DLE.Economy
{
    /// <summary>
    /// Session telemetry: a ring buffer of economy events (hauls created, deliveries,
    /// conversions, source production) served at /api/v1/history for dispatch charting.
    /// In-memory only; a session record, not a save file.
    /// </summary>
    public static class EconomyHistory
    {
        private const int Capacity = 600;

        public class Entry
        {
            public string Utc;
            public string Type;   // haul_created, delivered, converted, production
            public string Yard;
            public string Cargo;
            public float Amount;
            public string JobId;
        }

        private static readonly Queue<Entry> _entries = new Queue<Entry>(Capacity);
        private static readonly object _lock = new object();

        public static void Record(string type, string yard, string cargo, float amount, string jobId = null)
        {
            lock (_lock)
            {
                if (_entries.Count >= Capacity) _entries.Dequeue();
                _entries.Enqueue(new Entry
                {
                    Utc = DateTime.UtcNow.ToString("o"),
                    Type = type,
                    Yard = yard,
                    Cargo = cargo,
                    Amount = amount,
                    JobId = jobId,
                });
            }
        }

        public static List<Entry> Snapshot(int limit)
        {
            lock (_lock)
            {
                var all = _entries.ToList();
                return limit > 0 && all.Count > limit
                    ? all.Skip(all.Count - limit).ToList()
                    : all;
            }
        }
    }
}
