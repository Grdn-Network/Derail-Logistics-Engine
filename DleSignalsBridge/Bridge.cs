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
        public static void Init()
        {
            SignalsLink.GetAllFn = GetAll;
            SignalsLink.SetAspectFn = SetAspect;
            SignalsLink.SetAutomaticFn = id => SetMode(id, SignalMode.Automatic);
            SignalsLink.SetManualFn = id => SetMode(id, SignalMode.Manual);
        }

        private static List<SignalsLink.SignalInfo> GetAll()
        {
            var outp = new List<SignalsLink.SignalInfo>();
            IReadOnlyList<SignalState> list;
            try { list = SignalsAPI.GetAllSignals(); }
            catch { return outp; }
            if (list == null) return outp;
            var move = WorldMover.currentMove;
            foreach (var s in list)
            {
                if (s == null) continue;
                var p = s.Position - move;
                outp.Add(new SignalsLink.SignalInfo
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
                });
            }
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
