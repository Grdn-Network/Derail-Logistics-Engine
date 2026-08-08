using Signals.API;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace DLE.Dispatch
{
    /// <summary>
    /// The only DLE code that touches Signals.API. It lives in its own assembly so the
    /// core mod loads perfectly well without the DV Signals mod installed; DLE loads this
    /// by reflection once that API is confirmed present, and it hands the core plain data
    /// and delegates in return.
    ///
    /// Setting a road puts a signal into Manual and clears it; cancelling gives it back to
    /// the mod's own automatic logic rather than leaving a stop aspect pinned by us.
    /// </summary>
    public static class SignalsBridge
    {
        // The full read is EXPENSIVE (1154 signals through the API with position math),
        // and the websocket push was paying it every second on the game thread: 43ms a
        // tick on the owner's lag meter. The list is built once and kept fresh by the
        // Signals mod's own change events instead, so a read is a cached list and an
        // aspect change is an O(1) update.
        private static List<SignalsLink.SignalInfo> _cache;
        private static Dictionary<string, SignalsLink.SignalInfo> _byId;
        private static bool _hooked;

        public static void Init()
        {
            SignalsLink.GetAllFn = GetAll;
            SignalsLink.SetAspectFn = SetAspect;
            SignalsLink.SetAutomaticFn = id => SetMode(id, SignalMode.Automatic);
            SignalsLink.SetManualFn = id => SetMode(id, SignalMode.Manual);
            try
            {
                SignalsAPI.Loaded += () => { _cache = null; Hook(); };
                SignalsAPI.Unloaded += () => { _cache = null; _hooked = false; };
                if (SignalsAPI.Instance != null) Hook();
            }
            catch { }
        }

        private static void Hook()
        {
            if (_hooked || SignalsAPI.Instance == null) return;
            _hooked = true;
            SignalsAPI.Instance.SignalAspectChanged += st =>
            {
                if (st == null || _byId == null || !_byId.TryGetValue(st.Id, out var si)) return;
                si.Aspect = st.CurrentAspectId;
                si.IsOn = st.IsOn;
                si.Manual = st.Mode == SignalMode.Manual;
                SignalsLink.Version++;
            };
            SignalsAPI.Instance.SignalModeChanged += (id, mode) =>
            {
                if (_byId == null || !_byId.TryGetValue(id, out var si)) return;
                si.Manual = mode == SignalMode.Manual;
                SignalsLink.Version++;
            };
        }

        private static List<SignalsLink.SignalInfo> GetAll()
        {
            if (_cache != null) return _cache;
            var outp = new List<SignalsLink.SignalInfo>();
            var byId = new Dictionary<string, SignalsLink.SignalInfo>(StringComparer.Ordinal);
            IReadOnlyList<SignalState> list;
            try { list = SignalsAPI.GetAllSignals(); }
            catch { return outp; }
            if (list == null) return outp;
            var move = WorldMover.currentMove;
            foreach (var s in list)
            {
                if (s == null) continue;
                var p = s.Position - move;
                var si = new SignalsLink.SignalInfo
                {
                    Id = s.Id,
                    X = (float)Math.Round(p.x, 1),
                    Z = (float)Math.Round(p.z, 1),
                    Aspect = s.CurrentAspectId,
                    IsOn = s.IsOn,
                    Manual = s.Mode == SignalMode.Manual,
                    Type = s.Type.ToString(),
                    Direction = s.Direction.ToString(),
                    TrackId = s.TrackId,
                    YardId = s.YardId,
                    JunctionId = Convert.ToString(s.JunctionId),
                };
                outp.Add(si);
                if (!byId.ContainsKey(si.Id)) byId[si.Id] = si;
            }
            _cache = outp;
            _byId = byId;
            Hook();
            SignalsLink.Version++;
            return outp;
        }

        private static bool SetAspect(string id, string aspect)
        {
            try { return SignalsAPI.SetSignalAspect(id, aspect); }
            catch { return false; }
        }

        private static bool SetMode(string id, SignalMode mode)
        {
            try { return SignalsAPI.SetSignalMode(id, mode); }
            catch { return false; }
        }
    }
}
