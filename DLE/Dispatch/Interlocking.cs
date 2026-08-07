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
            public List<object> Poly = new List<object>();
            public bool WasOccupied;
        }

        private static readonly List<Sig> _signals = new List<Sig>();
        private static readonly Dictionary<string, Sig> _byEnd = new Dictionary<string, Sig>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Route> _routes = new Dictionary<string, Route>(StringComparer.Ordinal);
        private static readonly Dictionary<Junction, int> _jIds = new Dictionary<Junction, int>();
        private static readonly List<Junction> _junctions = new List<Junction>();
        private static readonly HashSet<int> _inYard = new HashSet<int>();
        private static readonly Dictionary<int, List<object>> _stubs = new Dictionary<int, List<object>>();
        private const float StubMeters = 90f;
        private static string _builtHash;

        // A signal stands beside its rail, not on the centreline, so the match allows a
        // few metres; the cell only has to be at least that big for the 3x3 search.
        private const float MatchCell = 30f;
        private const float MatchRadius = 25f;

        public static void Reset()
        {
            _signals.Clear(); _byEnd.Clear(); _routes.Clear();
            _jIds.Clear(); _junctions.Clear(); _inYard.Clear(); _stubs.Clear(); _builtHash = null;
        }

        private static string EndKey(RailTrack t, bool first) => t.GetInstanceID() + (first ? ":0" : ":1");

        /// <summary>
        /// Which of the Signals mod's types actually bound a block, and so belong on a
        /// dispatcher's panel. Distants only repeat what the next main signal is already
        /// saying, and shunting signals live inside yard limits this view hands to the
        /// station's own screen.
        /// </summary>
        private static bool IsBlockSignal(string type) =>
            string.Equals(type, "Mainline", StringComparison.OrdinalIgnoreCase);

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
                // Yard junctions keep their number, since a road can still run through
                // one, but they are not drawn: their station's own view owns them.
                if (InYard(j.position)) _inYard.Add(_junctions.Count);
                _jIds[j] = _junctions.Count;
                _junctions.Add(j);
            }

            // Signals are the Signals mod's, and it names them after junctions
            // (W-0000-T for the trunk, W-0000:B1 and :B2 for the branches), so its
            // TrackId is no use as a key into our rails: matching on it resolved nothing
            // at all on a live world. Match on POSITION instead, which cannot care what
            // anybody names anything: the rail whose centreline passes nearest the signal
            // is the rail it stands on.
            SignalsLink.TryInit();
            var grid = new Dictionary<long, List<(RailTrack t, Vector3 p)>>();
            long GKey(float x, float z) =>
                ((long)Mathf.FloorToInt(x / MatchCell) << 32) ^ (uint)Mathf.FloorToInt(z / MatchCell);
            try
            {
                foreach (var rt in SingletonBehaviour<RailTrackRegistryBase>.Instance.OrderedRailtracks)
                {
                    if (rt?.curve == null) continue;
                    for (int i = 0; i < rt.curve.pointCount; i++)
                    {
                        var bp = rt.curve[i];
                        if (bp == null) continue;
                        var q = bp.position - move;
                        var key = GKey(q.x, q.z);
                        if (!grid.TryGetValue(key, out var l)) grid[key] = l = new List<(RailTrack, Vector3)>();
                        l.Add((rt, q));
                    }
                }
            }
            catch { }
            RailTrack NearestRail(float x, float z, out float dist)
            {
                RailTrack best = null; float bestD = float.MaxValue;
                int cx = Mathf.FloorToInt(x / MatchCell), cz = Mathf.FloorToInt(z / MatchCell);
                for (int dx = -1; dx <= 1; dx++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (!grid.TryGetValue(((long)(cx + dx) << 32) ^ (uint)(cz + dz), out var bucket)) continue;
                        foreach (var (t, q) in bucket)
                        {
                            float ddx = q.x - x, ddz = q.z - z;
                            float d = ddx * ddx + ddz * ddz;
                            if (d < bestD) { bestD = d; best = t; }
                        }
                    }
                dist = bestD < float.MaxValue ? Mathf.Sqrt(bestD) : float.MaxValue;
                return best;
            }
            int resolved = 0, skipped = 0;
            foreach (var info in SignalsLink.All())
            {
                if (info?.Id == null) continue;
                // Main signals only (owner ruling). Distants merely repeat the signal
                // ahead and form no block of their own, and shunting signals govern
                // moves inside yard limits, which this view does not draw. Carrying them
                // would clutter the panel and, worse, stop a road short at something
                // that never was a block boundary.
                if (!IsBlockSignal(info.Type)) { skipped++; continue; }
                var track = NearestRail(info.X, info.Z, out var dist);
                if (dist > MatchRadius) track = null;
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
            // Leg geometry never moves, so it is worked out once here and only the
            // selected index changes from poll to poll.
            for (int i = 0; i < _junctions.Count; i++)
            {
                if (_inYard.Contains(i)) continue;
                var list = new List<object>();
                try { BuildStubs(_junctions[i], list); } catch { }
                if (list.Count > 0) _stubs[i] = list;
            }
            Main.LogAlways($"[Interlocking] {_signals.Count} main signal(s) kept ({resolved} matched to a rail by position), "
                + $"{skipped} skipped as distant/shunting/other, {_junctions.Count} junction(s) numbered.");
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
                int sside = 0; float sdx = 1f, sdz = 0f;
                if (s.Approach != null)
                {
                    sside = TrackMap.SideOfTrack(info.TrackId ?? s.Info.TrackId);
                    Heading(s.Approach, !s.TowardOut, out sdx, out sdz);
                }
                sigs.Add(new
                {
                    id = s.Id,
                    x = info.X,
                    z = info.Z,
                    side = sside,
                    dx = (float)Math.Round(sdx, 3),
                    dz = (float)Math.Round(sdz, 3),
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
                if (j == null || _inYard.Contains(i)) continue;
                var p = j.position - move;
                // Stand the mark on the same side its rail is drawn, or a crossover puts
                // two switches on one spot between the tracks.
                var approach = j.inBranch?.track;
                int side = 0; float dx = 1f, dz = 0f;
                if (approach != null)
                {
                    side = TrackMap.SideOfTrack(TrackIdOf(approach));
                    Heading(approach, j.inBranch.first, out dx, out dz);
                }
                jn.Add(new
                {
                    id = i,
                    x = (float)Math.Round(p.x, 1),
                    z = (float)Math.Round(p.z, 1),
                    branch = (int)j.selectedBranch,
                    branches = j.outBranches?.Count ?? 0,
                    locked = locked.Contains(i),
                    side,
                    dx = (float)Math.Round(dx, 3),
                    dz = (float)Math.Round(dz, 3),
                    legs = _stubs.TryGetValue(i, out var st) ? st : null,
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

        /// <summary>The rail's id as the map keys it, for the fan-side lookup.</summary>
        private static string TrackIdOf(RailTrack t)
        {
            try
            {
                foreach (var kv in RailTrackRegistry.LogicToRailTrack)
                    if (ReferenceEquals(kv.Value, t)) return kv.Key?.ID?.FullDisplayID;
            }
            catch { }
            return null;
        }

        /// <summary>Unit heading of a rail at the end in question, so a mark standing on
        /// it can be pushed square to the way it runs.</summary>
        private static void Heading(RailTrack t, bool atFirstEnd, out float dx, out float dz)
        {
            dx = 1f; dz = 0f;
            try
            {
                var c = t.curve;
                int n = c.pointCount;
                if (n < 2) return;
                var a = atFirstEnd ? c[0].position : c[n - 2].position;
                var b = atFirstEnd ? c[1].position : c[n - 1].position;
                float ex = b.x - a.x, ez = b.z - a.z;
                float len = Mathf.Sqrt(ex * ex + ez * ez);
                if (len > 0.001f) { dx = ex / len; dz = ez / len; }
            }
            catch { }
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

        /// <summary>
        /// The road piece by piece, one entry per rail it runs along, each carrying that
        /// rail's fan side. The board recolours the rail itself green (owner ruling), so
        /// the green has to sit exactly where the rail is drawn, offset and all.
        /// </summary>
        private static List<object> PathPolyline(List<RailTrack> path)
        {
            var move = WorldMover.currentMove;
            var outp = new List<object>();
            foreach (var t in path)
            {
                if (t?.curve == null) continue;
                var pts = new List<float>();
                float lx = 0, lz = 0; bool have = false;
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
                if (pts.Count >= 4)
                    outp.Add(new { side = TrackMap.SideOfTrack(TrackIdOf(t)), pts = pts.ToArray() });
            }
            return outp;
        }

        /// <summary>
        /// A short piece of each leg out of a junction, cached at build because the
        /// geometry never moves. The board paints the leg the switch is set to solid and
        /// the others grey, so a dispatcher can see where a switch IS pointing and where
        /// it COULD point without clicking anything (owner ruling).
        /// </summary>
        private static void BuildStubs(Junction j, List<object> into)
        {
            var move = WorldMover.currentMove;
            var outs = j.outBranches;
            if (outs == null) return;
            for (int b = 0; b < outs.Count; b++)
            {
                var br = outs[b];
                if (br?.track?.curve == null) continue;
                var c = br.track.curve;
                int n = c.pointCount;
                var pts = new List<float>();
                float run = 0f;
                Vector3 prev = default; bool have = false;
                for (int k = 0; k < n && run < StubMeters; k++)
                {
                    // Walk outward from the end that touches the junction.
                    var bp = br.first ? c[k] : c[n - 1 - k];
                    if (bp == null) continue;
                    var q = bp.position - move;
                    if (have) run += Vector3.Distance(prev, q);
                    prev = q; have = true;
                    pts.Add((float)Math.Round(q.x, 1));
                    pts.Add((float)Math.Round(q.z, 1));
                }
                if (pts.Count >= 4)
                    into.Add(new
                    {
                        branch = b,
                        side = TrackMap.SideOfTrack(TrackIdOf(br.track)),
                        pts = pts.ToArray(),
                    });
            }
        }
    }
}
