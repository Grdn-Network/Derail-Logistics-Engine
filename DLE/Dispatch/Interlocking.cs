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
    ///
    /// Every signal that mod places belongs to a JUNCTION and stands on one of its legs:
    /// the ids say so outright (W-0507-T on the trunk, W-0507:B1 and :B2 on the branches).
    /// So a signal is anchored to the junction it guards and to the leg it stands on, and
    /// the board draws it a fixed distance up that leg. An earlier build matched signals
    /// to the nearest sampled curve point instead, which left a quarter of them attached
    /// to nothing at all (127 of 473 on a live world) and, worse, let the board scatter
    /// the survivors along the rail to keep them from overlapping: a junction's three
    /// signals ended up strung out like a picket fence hundreds of metres from where they
    /// really stand.
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
            public int J = -1;           // junction index, -1 when it could not be placed
            public int Leg = -1;         // -1 the trunk, 0..n an out branch
            public bool Inbound = true;  // a move reads it running TOWARD the junction
            public int Slot;             // 0, 1, 2: two signals on one leg never stack
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
        private static readonly Dictionary<long, Sig> _inboundAt = new Dictionary<long, Sig>();
        private static readonly Dictionary<string, Route> _routes = new Dictionary<string, Route>(StringComparer.Ordinal);
        private static readonly Dictionary<Junction, int> _jIds = new Dictionary<Junction, int>();
        private static readonly List<Junction> _junctions = new List<Junction>();
        private static readonly HashSet<int> _inYard = new HashSet<int>();
        private static readonly Dictionary<int, List<object>> _stubs = new Dictionary<int, List<object>>();
        private const float StubMeters = 120f;
        private static string _builtHash;

        // A signal stands beside the junction it guards, a train length or so up its own
        // leg; on a live world the furthest sat 54m out, so this is generous rather than
        // tight, and the nearest junction wins anyway.
        private const float AnchorCell = 120f;
        private const float AnchorRadius = 150f;

        public static void Reset()
        {
            _signals.Clear(); _inboundAt.Clear(); _routes.Clear();
            _jIds.Clear(); _junctions.Clear(); _inYard.Clear(); _stubs.Clear(); _builtHash = null;
        }

        private static long LegKey(int junction, int leg) => ((long)junction << 8) ^ (uint)(leg + 8);

        /// <summary>
        /// Which of the Signals mod's types actually bound a block, and so belong on a
        /// dispatcher's panel. Distants only repeat what the next main signal is already
        /// saying, and shunting signals live inside yard limits this view hands to the
        /// station's own screen.
        /// </summary>
        private static bool IsBlockSignal(string type) =>
            string.Equals(type, "Mainline", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Which leg of its junction a signal stands on, straight off the id the Signals
        /// mod gives it: W-0507:B1 and :B2 are the branches, everything else is the trunk.
        /// </summary>
        private static int LegFromId(string id)
        {
            int c = id.LastIndexOf(':');
            if (c >= 0 && c + 2 < id.Length && (id[c + 1] == 'B' || id[c + 1] == 'b')
                && int.TryParse(id.Substring(c + 2), out var b) && b >= 1)
                return b - 1;
            return -1;
        }

        /// <summary>
        /// One scan per world: number the junctions, then hang every main signal off the
        /// junction and leg it belongs to. Junctions inside a station belong to that
        /// yard's own view, so they are numbered but not drawn.
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

            // Signal positions arrive already shifted from the bridge, junction positions
            // do not, so the yard test comes in both flavours rather than one of them
            // being quietly wrong.
            bool InYardShifted(float x, float z)
            {
                foreach (var s in stationPositions)
                {
                    float dx = x - s.x, dz = z - s.y;
                    if (dx * dx + dz * dz < yardRadius * yardRadius) return true;
                }
                return false;
            }

            var jgrid = new Dictionary<long, List<int>>();
            long GKey(float x, float z) =>
                ((long)Mathf.FloorToInt(x / AnchorCell) << 32) ^ (uint)Mathf.FloorToInt(z / AnchorCell);
            for (int i = 0; i < all.Length; i++)
            {
                var j = all[i];
                if (j == null) continue;
                var p = j.position - move;
                // Yard junctions keep their number, since a road can still run through
                // one, but they are not drawn: their station's own view owns them.
                if (InYardShifted(p.x, p.z)) _inYard.Add(_junctions.Count);
                var key = GKey(p.x, p.z);
                if (!jgrid.TryGetValue(key, out var l)) jgrid[key] = l = new List<int>();
                l.Add(_junctions.Count);
                _jIds[j] = _junctions.Count;
                _junctions.Add(j);
            }
            int NearestJunction(float x, float z, out float dist)
            {
                int best = -1; float bestD = float.MaxValue;
                int cx = Mathf.FloorToInt(x / AnchorCell), cz = Mathf.FloorToInt(z / AnchorCell);
                for (int dx = -1; dx <= 1; dx++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (!jgrid.TryGetValue(((long)(cx + dx) << 32) ^ (uint)(cz + dz), out var bucket)) continue;
                        foreach (var idx in bucket)
                        {
                            var p = _junctions[idx].position - move;
                            float ddx = p.x - x, ddz = p.z - z;
                            float d = ddx * ddx + ddz * ddz;
                            if (d < bestD) { bestD = d; best = idx; }
                        }
                    }
                dist = bestD < float.MaxValue ? Mathf.Sqrt(bestD) : float.MaxValue;
                return best;
            }

            SignalsLink.TryInit();
            int placed = 0, skipped = 0, loose = 0;
            var dirs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var used = new Dictionary<long, int>();
            foreach (var info in SignalsLink.All())
            {
                if (info?.Id == null) continue;
                // Main signals only (owner ruling). Distants merely repeat the signal
                // ahead and form no block of their own, and shunting signals govern
                // moves inside yard limits, which this view does not draw. Carrying them
                // would clutter the panel and, worse, stop a road short at something
                // that never was a block boundary.
                if (!IsBlockSignal(info.Type)) { skipped++; continue; }
                // A signal standing inside a station belongs to that yard's own view,
                // exactly like the switches there. Drawing it here piles marks onto the
                // bubble and mangles the station's own name.
                if (InYardShifted(info.X, info.Z)) { skipped++; continue; }

                var key = info.Direction ?? "None";
                dirs.TryGetValue(key, out var dc); dirs[key] = dc + 1;

                var sig = new Sig { Id = info.Id, Info = info, Leg = LegFromId(info.Id) };
                int jidx = NearestJunction(info.X, info.Z, out var dist);
                if (jidx >= 0 && dist <= AnchorRadius) { sig.J = jidx; placed++; }
                else loose++;
                _signals.Add(sig);
            }
            // A signal at a junction guards it: it stands on one leg and a train reads it
            // running toward the points. That is what the world shows, confirmed at
            // SM-SUB-N where the mast faces a train coming up the trunk. The mod's own
            // Direction field does not separate the two cases (it reports Out for every
            // trunk signal and None for every branch one), so it is not used for facing:
            // trusting it pointed the trunk signals backwards and greened the face a
            // driver passes on the back. Only a leg carrying a SECOND signal has one
            // governing the way out, and slots keep those two marks a step apart.
            foreach (var sig in _signals)
            {
                if (sig.J < 0) continue;
                var k = LegKey(sig.J, sig.Leg);
                used.TryGetValue(k, out var n);
                used[k] = n + 1;
                sig.Slot = n;
                sig.Inbound = n == 0;
                if (sig.Inbound && !_inboundAt.ContainsKey(k)) _inboundAt[k] = sig;
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
            var dirText = string.Join(", ", dirs.Select(kv => kv.Key + "=" + kv.Value).ToArray());
            Main.LogAlways($"[Interlocking] {_signals.Count} main signal(s) kept ({placed} standing at a known switch, "
                + $"{loose} loose), {skipped} skipped as distant/shunting/other, {_junctions.Count} junction(s) numbered; "
                + $"the mod reports direction {dirText}.");
            // A world reload rebuilds this list, so a railway left under CTC has to be
            // put back to stop or half of it would quietly go automatic again.
            if (Ctc && _signals.Count > 0) HoldEverything();
        }

        /// <summary>
        /// CTC (#176): every main signal held at stop until a dispatcher clears a road
        /// through it. Off, signals run on the Signals mod's own automatic logic and a
        /// crew can work the railway without dispatch; on, nothing moves that the board
        /// has not authorised, which is the whole point of the seat.
        /// </summary>
        public static bool Ctc { get; private set; }

        public static (bool ok, string message) SetCtc(bool on)
        {
            if (!SignalsLink.Available)
                return (false, "the DV Signals mod is not loaded, so there are no signals to hold");
            if (Ctc == on) return (true, on ? "CTC is already on" : "CTC is already off");
            Ctc = on;
            if (!on)
            {
                int freed = 0;
                foreach (var s in _signals)
                {
                    if (_routes.ContainsKey(s.Id)) continue;
                    try { SignalsLink.SetAutomaticFn?.Invoke(s.Id); freed++; } catch { }
                }
                Main.LogAlways($"[Interlocking] CTC off: {freed} signal(s) handed back to automatic.");
                return (true, $"CTC off; {freed} signal(s) back on automatic");
            }
            var (held, marked) = HoldEverything();
            return (true, $"CTC on; {held} signal(s) held at stop"
                + (marked > 0 ? $", {marked} of them showing the manual marker" : ""));
        }

        /// <summary>
        /// Put every signal not already carrying a road to stop. The aspect is tried on
        /// its own first, because taking a signal to manual is what lights the white
        /// marker lamp under the head and the owner would rather not see it; only the
        /// ones that will not hold without it get switched to manual.
        /// </summary>
        private static (int held, int marked) HoldEverything()
        {
            int held = 0, marked = 0;
            foreach (var s in _signals)
            {
                if (_routes.ContainsKey(s.Id)) continue;
                if (HoldStop(s.Id)) held++;
            }
            var now = new Dictionary<string, SignalsLink.SignalInfo>(StringComparer.Ordinal);
            foreach (var i in SignalsLink.All()) now[i.Id] = i;
            foreach (var s in _signals)
            {
                if (_routes.ContainsKey(s.Id)) continue;
                if (now.TryGetValue(s.Id, out var i)
                    && string.Equals(i.Aspect, SignalsLink.AspectStop, StringComparison.Ordinal)) continue;
                try { SignalsLink.SetManualFn?.Invoke(s.Id); } catch { }
                if (HoldStop(s.Id)) { marked++; if (held < _signals.Count) held++; }
            }
            Main.LogAlways($"[Interlocking] CTC on: {held} signal(s) held at stop; "
                + $"{marked} would not hold on aspect alone and went to manual (white marker lit).");
            return (held, marked);
        }

        private static bool HoldStop(string id)
        {
            try { return SignalsLink.SetAspectFn?.Invoke(id, SignalsLink.AspectStop) ?? false; }
            catch { return false; }
        }

        /// <summary>Give a signal back once its road ends: to the mod's own logic
        /// normally, or straight back to stop while CTC holds the railway.</summary>
        private static void Release(string id)
        {
            try
            {
                if (Ctc) HoldStop(id);
                else SignalsLink.SetAutomaticFn?.Invoke(id);
            }
            catch (Exception ex) { Main.Log($"[Interlocking] releasing {id} failed: {ex.Message}"); }
        }

        public static object Payload()
        {
            var move = WorldMover.currentMove;
            // Aspects are read live so the board shows what the world actually shows,
            // including changes the Signals mod makes on its own.
            var live = new Dictionary<string, SignalsLink.SignalInfo>(StringComparer.Ordinal);
            foreach (var i in SignalsLink.All()) live[i.Id] = i;
            var sigs = new List<object>();
            foreach (var s in _signals)
            {
                live.TryGetValue(s.Id, out var now);
                var info = now ?? s.Info;
                sigs.Add(new
                {
                    id = s.Id,
                    // The board draws a placed signal on its own leg, a fixed step off the
                    // junction, so these are only the fallback for a loose one.
                    x = info.X,
                    z = info.Z,
                    jid = s.J,
                    leg = s.Leg,
                    slot = s.Slot,
                    inbound = s.Inbound,
                    aspect = info.Aspect,
                    on = info.IsOn,
                    manual = info.Manual,
                    type = info.Type,
                    dir = info.Direction,
                    road = _routes.ContainsKey(s.Id),
                    routable = s.J >= 0,
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
            return new { signals = sigs, junctions = jn, routes = rts, ctc = Ctc };
        }

        /// <summary>Throw a switch from the board. The game's own event carries it to
        /// every client; a junction held by a cleared route refuses to move.</summary>
        public static (bool ok, string message) Throw(int junctionId)
        {
            if (junctionId < 0 || junctionId >= _junctions.Count) return (false, "no such switch");
            var j = _junctions[junctionId];
            if (j == null) return (false, "that switch is gone");
            if (Held(junctionId, out var by)) return (false, by + "; drop that road first");
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
        /// Clear the road from a signal: leave its leg, run through the junction the way
        /// the switches are actually set, and carry on until the next signal facing the
        /// move or the end of the line. Every junction crossed locks behind it.
        /// </summary>
        public static (bool ok, string message) Clear(string signalId)
        {
            var s = _signals.FirstOrDefault(x => x.Id == signalId);
            if (s == null) return (false, "no such signal");
            if (_routes.ContainsKey(signalId)) return (false, "that signal is already off");
            if (s.J < 0 || s.J >= _junctions.Count)
                return (false, "that signal is not standing at a switch this board knows, so no road can be set from it");
            var j0 = _junctions[s.J];
            if (j0 == null) return (false, "that switch is gone");
            var startLeg = BranchOf(j0, s.Leg);
            if (startLeg?.track == null) return (false, "that signal's own track is missing");

            var route = new Route { SignalId = signalId };
            var occupied = OccupiedTracks();
            var seen = new HashSet<int>();
            RailTrack track;
            bool towardOut;

            if (s.Inbound)
            {
                // Read from its own leg: into the junction and out the far side.
                route.Path.Add(startLeg.track);
                seen.Add(startLeg.track.GetInstanceID());
                var exit = ExitFrom(j0, s.Leg, out var why);
                if (exit?.track == null) return (false, why ?? "that switch has nowhere to go");
                if (Held(s.J, out var by0)) return (false, by0);
                route.Locked.Add(s.J);
                track = exit.track; towardOut = exit.first;
            }
            else
            {
                // A departure: the move comes off whichever leg is set and runs out along
                // this one, so the switch has to be facing this way already.
                if (s.Leg >= 0 && j0.selectedBranch != s.Leg)
                    return (false, $"switch {s.J} is set the other way; throw it first");
                if (Held(s.J, out var by1)) return (false, by1);
                route.Locked.Add(s.J);
                track = startLeg.track; towardOut = startLeg.first;
            }

            for (int step = 0; step < 60; step++)
            {
                if (track == null || !seen.Add(track.GetInstanceID())) break;
                route.Path.Add(track);
                if (occupied.Contains(track)) return (false, "the road ahead is occupied");
                var nj = towardOut ? track.outJunction : track.inJunction;
                if (nj == null) break;                         // buffer stop or plain end
                if (!_jIds.TryGetValue(nj, out var njid)) break;
                int arrive = LegOf(nj, track);
                if (arrive == -2) break;
                // A signal facing this move ends the road, exactly like a real green.
                if (_inboundAt.TryGetValue(LegKey(njid, arrive), out var stop) && stop.Id != signalId) break;
                var exit = ExitFrom(nj, arrive, out _);
                if (exit?.track == null) break;                // set against the move
                if (Held(njid, out var by)) return (false, by);
                route.Locked.Add(njid);
                track = exit.track; towardOut = exit.first;
            }
            if (route.Path.Count == 0) return (false, "nothing to set from that signal");
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
            return (true, $"{signalId} off: {route.Path.Count} track(s), {route.Locked.Count} switch(es) locked"
                + (shown ? "" : " (the signal itself would not clear; check the Signals mod)"));
        }

        public static (bool ok, string message) Cancel(string signalId)
        {
            if (!_routes.Remove(signalId)) return (false, "that signal is already on");
            Release(signalId);
            return (true, Ctc
                ? $"{signalId} back to stop; switches released"
                : $"{signalId} back on automatic; switches released");
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
                    Release(id);
                    Main.Log($"[Interlocking] road off {id} released; the train is through.");
                }
            }
        }

        private static bool Held(int junctionId, out string message)
        {
            foreach (var r in _routes.Values)
                if (r.Locked.Contains(junctionId))
                {
                    message = $"switch {junctionId} is locked by the road off {r.SignalId}";
                    return true;
                }
            message = null;
            return false;
        }

        /// <summary>The branch on a given leg: -1 is the trunk, 0..n an out branch.</summary>
        private static Junction.Branch BranchOf(Junction j, int leg)
        {
            if (leg < 0) return j.inBranch;
            var outs = j.outBranches;
            return outs != null && leg < outs.Count ? outs[leg] : null;
        }

        /// <summary>Which leg of a junction a track hangs off, or -2 when it does not.</summary>
        private static int LegOf(Junction j, RailTrack t)
        {
            if (j.inBranch != null && ReferenceEquals(j.inBranch.track, t)) return -1;
            var outs = j.outBranches;
            if (outs != null)
                for (int i = 0; i < outs.Count; i++)
                    if (outs[i] != null && ReferenceEquals(outs[i].track, t)) return i;
            return -2;
        }

        /// <summary>
        /// Where a move entering on one leg comes out, given how the switch is set right
        /// now. Null means the switch is against it, which is exactly when a real road
        /// refuses rather than quietly moving the points under a train.
        /// </summary>
        private static Junction.Branch ExitFrom(Junction j, int leg, out string why)
        {
            why = null;
            var outs = j.outBranches;
            if (leg < 0)
            {
                if (outs == null || j.selectedBranch >= outs.Count) { why = "that switch has nowhere to go"; return null; }
                return outs[j.selectedBranch];
            }
            if (j.selectedBranch != leg)
            {
                _jIds.TryGetValue(j, out var id);
                why = $"switch {id} is set the other way; throw it first";
                return null;
            }
            if (j.inBranch?.track == null) { why = "that switch has nowhere to go"; return null; }
            return j.inBranch;
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
        /// A short piece of every leg out of a junction, the trunk included, cached at
        /// build because the geometry never moves. The board paints the leg the switch is
        /// set to solid and the others grey, so a dispatcher can see where a switch IS
        /// pointing and where it COULD point without clicking anything (owner ruling).
        /// Each stub starts AT the junction and runs outward, which is also what lets the
        /// board stand a signal a fixed step up its own leg.
        /// </summary>
        private static void BuildStubs(Junction j, List<object> into)
        {
            var outs = j.outBranches;
            if (j.inBranch != null) AddStub(j.inBranch, -1, into);
            if (outs == null) return;
            for (int b = 0; b < outs.Count; b++) AddStub(outs[b], b, into);
        }

        private static void AddStub(Junction.Branch br, int leg, List<object> into)
        {
            if (br?.track?.curve == null) return;
            var move = WorldMover.currentMove;
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
                    branch = leg,
                    side = TrackMap.SideOfTrack(TrackIdOf(br.track)),
                    pts = pts.ToArray(),
                });
        }
    }
}
