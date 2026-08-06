using DV.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DLE.Dispatch
{
    /// <summary>
    /// Switches and signals, the Rail Route way (#131). There is no pathfinding here on
    /// purpose: a route is simply wherever the switches already point. Click a signal and
    /// the road greens along the CURRENT alignment until the next signal; every junction
    /// under that green locks until the train has passed or the dispatcher drops it.
    ///
    /// Signals come from the DV Signals mod through its own API (owner ruling): they are
    /// real objects in the world that drivers read, so DLE reads them rather than
    /// inventing its own. Setting a road takes one to Manual and clears it; cancelling
    /// hands it back to that mod's automatic logic. No Signals mod means no signals here.
    /// A through-station move needs no special case, because a throat's switches resting
    /// in their normal position ARE the through route.
    ///
    /// Host-side only, and throwing a switch goes through the game's own Junction.Switch,
    /// which fires the Switched event that the Multiplayer mod already broadcasts, so a
    /// throw from the board reaches every client with no packet of ours involved.
    /// </summary>
    internal static class Interlocking
    {
        private class Sig
        {
            public string Id;            // the Signals mod's own id
            public RailTrack Approach;   // the track a train sits on when reading it
            public bool TowardOut;       // the way a move off this signal travels
            public SignalsLink.SignalInfo Info;
        }

        private class Route
        {
            public string SignalId;
            public List<RailTrack> Path = new List<RailTrack>();
            public List<int> Locked = new List<int>();
            public float[] Poly = Array.Empty<float>();
            public bool WasOccupied;
        }

        private static readonly List<Sig> _signals = new List<Sig>();
        private static readonly Dictionary<string, Sig> _byEnd = new Dictionary<string, Sig>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Route> _routes = new Dictionary<string, Route>(StringComparer.Ordinal);
        private static readonly Dictionary<Junction, int> _jIds = new Dictionary<Junction, int>();
        private static readonly List<Junction> _junctions = new List<Junction>();
        private static string _builtHash;

        public static void Reset()
        {
            _signals.Clear(); _byEnd.Clear(); _routes.Clear();
            _jIds.Clear(); _junctions.Clear(); _builtHash = null;
        }

        private static string EndKey(RailTrack t, bool first) => t.GetInstanceID() + (first ? ":0" : ":1");

        /// <summary>
        /// One scan per world: number the junctions, and hang a signal off each one's
        /// facing approach. Junctions inside a station belong to that yard's own view,
        /// so they get no signal here.
        /// </summary>
        public static void Build(IReadOnlyList<Vector2> stationPositions, float yardRadius)
        {
            string hash = null;
            try { hash = SingletonBehaviour<RailTrackRegistryBase>.Instance?.TracksHash; } catch { }
            if (_builtHash == hash && _signals.Count > 0) return;
            Reset();
            _builtHash = hash;

            var move = WorldMover.currentMove;
            Junction[] all;
            try { all = RailTrackRegistry.Instance.TrackRootParent.GetComponentsInChildren<Junction>(); }
            catch { return; }

            bool InYard(Vector3 world)
            {
                var p = world - move;
                foreach (var s in stationPositions)
                {
                    float dx = p.x - s.x, dz = p.z - s.y;
                    if (dx * dx + dz * dz < yardRadius * yardRadius) return true;
                }
                return false;
            }

            for (int i = 0; i < all.Length; i++)
            {
                var j = all[i];
                if (j == null) continue;
                _jIds[j] = _junctions.Count;
                _junctions.Add(j);
            }

            // Signals are the Signals mod's. Match each to the track it stands on so a
            // road can be walked from it; one that will not resolve still draws, it just
            // cannot set a road.
            SignalsLink.TryInit();
            var byTrackId = new Dictionary<string, RailTrack>(StringComparer.Ordinal);
            try
            {
                foreach (var kv in RailTrackRegistry.LogicToRailTrack)
                    if (kv.Key?.ID?.FullDisplayID is string tid && kv.Value != null)
                        byTrackId[tid] = kv.Value;
            }
            catch { }
            int resolved = 0;
            foreach (var info in SignalsLink.All())
            {
                if (info?.Id == null) continue;
                byTrackId.TryGetValue(info.TrackId ?? "", out var track);
                bool towardOut = !string.Equals(info.Direction, "In", StringComparison.OrdinalIgnoreCase);
                var sig = new Sig { Id = info.Id, Approach = track, TowardOut = towardOut, Info = info };
                _signals.Add(sig);
                if (track != null)
                {
                    resolved++;
                    var key = EndKey(track, !towardOut);
                    if (!_byEnd.ContainsKey(key)) _byEnd[key] = sig;
                }
            }
            Main.LogAlways($"[Interlocking] {_signals.Count} signal(s) from the Signals mod ({resolved} matched to track), "
                + $"{_junctions.Count} junction(s) numbered.");
        }

        public static object Payload()
        {
            var move = WorldMover.currentMove;
            // Aspects are read live so the board shows what the world actually shows,
            // including changes the Signals mod makes on its own.
            var live = SignalsLink.All().ToDictionary(i => i.Id, i => i, StringComparer.Ordinal);
            var sigs = new List<object>();
            foreach (var s in _signals)
            {
                live.TryGetValue(s.Id, out var now);
                var info = now ?? s.Info;
                sigs.Add(new
                {
                    id = s.Id,
                    x = info.X,
                    z = info.Z,
                    aspect = info.Aspect,
                    on = info.IsOn,
                    manual = info.Manual,
                    type = info.Type,
                    road = _routes.ContainsKey(s.Id),
                    routable = s.Approach != null,
                });
            }
            var locked = new HashSet<int>();
            foreach (var r in _routes.Values) foreach (var j in r.Locked) locked.Add(j);
            var jn = new List<object>();
            for (int i = 0; i < _junctions.Count; i++)
            {
                var j = _junctions[i];
                if (j == null) continue;
                var p = j.position - move;
                jn.Add(new
                {
                    id = i,
                    x = (float)Math.Round(p.x, 1),
                    z = (float)Math.Round(p.z, 1),
                    branch = (int)j.selectedBranch,
                    branches = j.outBranches?.Count ?? 0,
                    locked = locked.Contains(i),
                });
            }
            var rts = _routes.Values.Select(r => new { signal = r.SignalId, poly = r.Poly }).ToList();
            return new { signals = sigs, junctions = jn, routes = rts };
        }

        /// <summary>Throw a switch from the board. The game's own event carries it to
        /// every client; a junction held by a cleared route refuses to move.</summary>
        public static (bool ok, string message) Throw(int junctionId)
        {
            if (junctionId < 0 || junctionId >= _junctions.Count) return (false, "no such switch");
            var j = _junctions[junctionId];
            if (j == null) return (false, "that switch is gone");
            foreach (var r in _routes.Values)
                if (r.Locked.Contains(junctionId))
                    return (false, $"locked by the road off {r.SignalId}; drop that road first");
            int n = j.outBranches?.Count ?? 0;
            if (n < 2) return (false, "that junction has nothing to throw");
            try
            {
                byte next = (byte)((j.selectedBranch + 1) % n);
                j.Switch(Junction.SwitchMode.REGULAR, next);
                return (true, $"switch {junctionId} set to branch {next + 1} of {n}");
            }
            catch (Exception ex) { return (false, $"throw failed: {ex.Message}"); }
        }

        /// <summary>
        /// Clear the road from a signal: walk the rails exactly as the switches are set
        /// until the next signal or the end of the line, then lock what was crossed.
        /// </summary>
        public static (bool ok, string message) Clear(string signalId)
        {
            var s = _signals.FirstOrDefault(x => x.Id == signalId);
            if (s == null) return (false, "no such signal");
            if (_routes.ContainsKey(signalId)) return (false, "that signal is already off");
            if (s.Approach == null) return (false, "that signal is not matched to a track, so no road can be set from it");
            var route = new Route { SignalId = signalId };
            var occupied = OccupiedTracks();
            var seen = new HashSet<int>();

            var track = s.Approach;
            bool towardOut = s.TowardOut;
            for (int step = 0; step < 60; step++)
            {
                if (track == null || !seen.Add(track.GetInstanceID())) break;
                route.Path.Add(track);
                var j = towardOut ? track.outJunction : track.inJunction;
                if (j == null) break;                       // buffer stop or plain end
                if (!_jIds.TryGetValue(j, out var jid)) break;
                foreach (var other in _routes.Values)
                    if (other.Locked.Contains(jid))
                        return (false, $"switch {jid} is already locked by the road off {other.SignalId}");
                Junction.Branch next;
                if (j.inBranch != null && j.inBranch.track == track)
                {
                    var outs = j.outBranches;
                    if (outs == null || j.selectedBranch >= outs.Count) break;
                    next = outs[j.selectedBranch];
                }
                else
                {
                    int idx = j.outBranches?.FindIndex(b => b.track == track) ?? -1;
                    if (idx < 0 || idx != j.selectedBranch) break;  // set against a trailing move
                    next = j.inBranch;
                }
                if (next?.track == null) break;
                route.Locked.Add(jid);
                track = next.track;
                towardOut = next.first;
                if (occupied.Contains(track))
                    return (false, "the road ahead is occupied");
                // Stop at the next signal facing this way, exactly like a real green.
                if (_byEnd.TryGetValue(EndKey(track, !towardOut), out var stop) && stop.Id != signalId)
                {
                    route.Path.Add(track);
                    break;
                }
            }
            if (route.Locked.Count == 0) return (false, "nothing to set from that signal");
            route.Poly = PathPolyline(route.Path);
            _routes[signalId] = route;

            // The signal itself belongs to the Signals mod: take it to manual and clear
            // it so drivers see a real green, not just a line on the dispatcher's board.
            bool shown = false;
            try
            {
                SignalsLink.SetManualFn?.Invoke(signalId);
                shown = SignalsLink.SetAspectFn?.Invoke(signalId, SignalsLink.AspectClear) ?? false;
            }
            catch (Exception ex) { Main.Log($"[Interlocking] aspect set failed: {ex.Message}"); }
            return (true, $"{signalId} off: {route.Locked.Count} switch(es) locked"
                + (shown ? "" : " (the signal itself would not clear; check the Signals mod)"));
        }

        public static (bool ok, string message) Cancel(string signalId)
        {
            if (!_routes.Remove(signalId)) return (false, "that signal is already on");
            // Hand it straight back to the Signals mod rather than pinning a stop of ours.
            try { SignalsLink.SetAutomaticFn?.Invoke(signalId); }
            catch (Exception ex) { Main.Log($"[Interlocking] handing {signalId} back failed: {ex.Message}"); }
            return (true, $"{signalId} back on automatic; switches released");
        }

        /// <summary>Release a road once a train has run through it, the way a real one
        /// clears behind the tail. Called from the director tick.</summary>
        public static void Tick()
        {
            if (_routes.Count == 0) return;
            HashSet<RailTrack> occupied;
            try { occupied = OccupiedTracks(); }
            catch { return; }
            foreach (var id in _routes.Keys.ToList())
            {
                var r = _routes[id];
                bool any = r.Path.Any(t => t != null && occupied.Contains(t));
                if (any) { r.WasOccupied = true; continue; }
                if (r.WasOccupied)
                {
                    _routes.Remove(id);
                    try { SignalsLink.SetAutomaticFn?.Invoke(id); } catch { }
                    Main.Log($"[Interlocking] road off {id} released; the train is through.");
                }
            }
        }

        private static HashSet<RailTrack> OccupiedTracks()
        {
            var set = new HashSet<RailTrack>();
            foreach (var kv in TrainCarRegistry.Instance.logicCarToTrainCar)
            {
                var tc = kv.Value;
                if (tc?.Bogies == null) continue;
                foreach (var b in tc.Bogies)
                    if (b != null && b.track != null) set.Add(b.track);
            }
            return set;
        }

        private static float[] PathPolyline(List<RailTrack> path)
        {
            var move = WorldMover.currentMove;
            var pts = new List<float>();
            float lx = 0, lz = 0; bool have = false;
            foreach (var t in path)
            {
                if (t?.curve == null) continue;
                for (int i = 0; i < t.curve.pointCount; i++)
                {
                    var bp = t.curve[i];
                    if (bp == null) continue;
                    var p = bp.position - move;
                    if (have && (p.x - lx) * (p.x - lx) + (p.z - lz) * (p.z - lz) < 100f) continue;
                    pts.Add((float)Math.Round(p.x, 1));
                    pts.Add((float)Math.Round(p.z, 1));
                    lx = p.x; lz = p.z; have = true;
                }
            }
            return pts.ToArray();
        }
    }
}
