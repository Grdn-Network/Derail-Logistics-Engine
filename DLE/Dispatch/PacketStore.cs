using System;
using System.Collections.Generic;

namespace DLE.Dispatch
{
    /// <summary>
    /// Draft packets (Desk v10, #225): a packet is an ordered run of booklets. A filed
    /// booklet rides as its job id; an unfiled one rides as the board's own draft blob,
    /// which the server stores without reading: the skin is still moving, so the server
    /// holds state and stays out of the form's business. Grouping only: nothing here
    /// creates, takes or pays a job; filing stays with /api/v1/hauls. Persisted with
    /// the save under DLE_Packets_v1.
    /// </summary>
    public class PacketStore
    {
        private const string SaveKey = "DLE_Packets_v1";
        private const int SchemaVersion = 1;
        private const int MaxPackets = 64;
        private const int MaxEntries = 24;
        private const int MaxDraftChars = 8192;

        public static readonly PacketStore Instance = new PacketStore();
        private PacketStore() { }

        [Serializable]
        public class Packet
        {
            public string Id;
            public string Crew;
            public List<Entry> Entries = new List<Entry>();
        }

        [Serializable]
        public class Entry
        {
            // Exactly one side is set per entry.
            public string JobId;
            public string Draft;
        }

        [Serializable]
        private class SaveData
        {
            public int SchemaVersion;
            public int Next;
            public List<Packet> Packets;
        }

        private List<Packet> _packets = new List<Packet>();
        private int _next = 1;

        /// <summary>Tray order is creation order. Dead job refs fall out here (jobs
        /// complete and expire outside the store); empty packets stay: an empty folder
        /// in the tray is the dispatcher's to keep or delete, not the store's.</summary>
        public List<Packet> All(Func<string, bool> jobAlive)
        {
            foreach (var p in _packets)
                p.Entries?.RemoveAll(e => e == null ||
                    (!string.IsNullOrEmpty(e.JobId) && jobAlive != null && !jobAlive(e.JobId)));
            return _packets;
        }

        /// <summary>No id creates a packet; an id replaces that packet whole. Reorder,
        /// move-in and merge are all the client composing entries and posting the
        /// result, so the server needs exactly one write shape.</summary>
        public Packet Upsert(string id, string crew, List<Entry> entries, out string error)
        {
            error = null;
            entries = entries ?? new List<Entry>();
            if (entries.Count > MaxEntries) { error = $"a packet holds at most {MaxEntries} booklets"; return null; }
            foreach (var e in entries)
            {
                if (e == null || (string.IsNullOrEmpty(e.JobId) && string.IsNullOrEmpty(e.Draft)))
                { error = "every entry needs a jobId or a draft"; return null; }
                if (!string.IsNullOrEmpty(e.JobId) && !string.IsNullOrEmpty(e.Draft))
                { error = "an entry is a jobId or a draft, not both"; return null; }
                if (e.Draft != null && e.Draft.Length > MaxDraftChars)
                { error = $"draft too large (limit {MaxDraftChars} chars)"; return null; }
            }
            Packet p = null;
            if (!string.IsNullOrEmpty(id))
            {
                p = _packets.Find(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
                if (p == null) { error = $"unknown packet '{id}'"; return null; }
            }
            else
            {
                if (_packets.Count >= MaxPackets) { error = $"the tray holds at most {MaxPackets} packets"; return null; }
                p = new Packet { Id = "PK-" + _next++ };
                _packets.Add(p);
            }
            p.Crew = string.IsNullOrEmpty(crew) ? null : crew;
            p.Entries = entries;
            return p;
        }

        public bool Delete(string id) =>
            _packets.RemoveAll(x => string.Equals(x.Id, id ?? "", StringComparison.OrdinalIgnoreCase)) > 0;

        public void SaveTo(SaveGameData data) =>
            data.SetObject(SaveKey, new SaveData { SchemaVersion = SchemaVersion, Next = _next, Packets = _packets });

        public void LoadFrom(SaveGameData data)
        {
            _packets = new List<Packet>();
            _next = 1;
            SaveData payload = null;
            try { payload = data.GetObject<SaveData>(SaveKey); }
            catch (Exception ex) { Main.LogAlways($"[Packets] packet save unreadable, starting empty: {ex.Message}"); }
            if (payload?.Packets == null || payload.SchemaVersion != SchemaVersion) return;
            _packets = payload.Packets;
            _next = Math.Max(1, payload.Next);
        }
    }
}
