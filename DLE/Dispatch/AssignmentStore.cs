using System;
using System.Collections.Generic;

namespace DLE.Dispatch
{
    /// <summary>
    /// Dispatcher assignments: which player a job is assigned to. Data only in honor mode;
    /// when the lock is enabled, unassigned DLE jobs cannot be taken (JobValidatorLockPatch).
    /// Persisted under DLE_Assignments_v1.
    /// </summary>
    public class AssignmentStore
    {
        private const string SaveKey = "DLE_Assignments_v1";
        private const int SchemaVersion = 1;

        public static readonly AssignmentStore Instance = new AssignmentStore();
        private AssignmentStore() { }

        [Serializable]
        public class Assignment
        {
            public string Player;
            public string AssignedBy;
            public string AtUtc;
        }

        [Serializable]
        private class SaveData
        {
            public int SchemaVersion;
            public bool LockEnabled;
            public Dictionary<string, Assignment> Assignments;
        }

        private Dictionary<string, Assignment> _assignments =
            new Dictionary<string, Assignment>(StringComparer.Ordinal);

        private bool _lockEnabled;

        // The setter is the single choke point for lock changes (API, save restore), so
        // every flip reaches DLE clients for their local paper sweep (#73). No-ops when
        // nothing changed or no MP session is up.
        public bool LockEnabled
        {
            get => _lockEnabled;
            set
            {
                if (_lockEnabled == value) return;
                _lockEnabled = value;
                DleMpChannel.NotifyLockChanged(value);
            }
        }

        public IReadOnlyDictionary<string, Assignment> All => _assignments;

        public Assignment Get(string jobId) =>
            _assignments.TryGetValue(jobId, out var a) ? a : null;

        public void Assign(string jobId, string player, string assignedBy)
        {
            _assignments[jobId] = new Assignment
            {
                Player = player,
                AssignedBy = assignedBy,
                AtUtc = DateTime.UtcNow.ToString("o"),
            };
            // Always-on: assign events are rare, and MP forensics need them in the default
            // log (issue #79: an assignment reported missing with no trace to check).
            Main.LogAlways($"[Dispatch] {jobId} assigned to {player} by {assignedBy}.");
        }

        public bool Unassign(string jobId)
        {
            var removed = _assignments.Remove(jobId);
            if (removed) Main.LogAlways($"[Dispatch] {jobId} unassigned.");
            return removed;
        }

        public void SaveTo(SaveGameData data)
        {
            data.SetObject(SaveKey, new SaveData
            {
                SchemaVersion = SchemaVersion,
                LockEnabled = LockEnabled,
                Assignments = _assignments,
            });
        }

        public void LoadFrom(SaveGameData data)
        {
            _assignments = new Dictionary<string, Assignment>(StringComparer.Ordinal);
            LockEnabled = false;
            SaveData payload = null;
            try { payload = data.GetObject<SaveData>(SaveKey); }
            catch (Exception ex) { Main.LogAlways($"[Dispatch] assignment save unreadable, starting empty: {ex.Message}"); }
            if (payload?.Assignments == null || payload.SchemaVersion != SchemaVersion) return;
            _assignments = payload.Assignments;
            LockEnabled = payload.LockEnabled;
        }
    }
}
