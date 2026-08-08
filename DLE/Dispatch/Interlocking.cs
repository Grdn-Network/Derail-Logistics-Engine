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
        private static readonly Dictionary<int, List<object>> _stubs = new Dictionary<int, List<object>>();

        /// <summary>Everything about a drawn switch that the world decides once: where it
        /// stands, which way its approach runs, and which side of a double track it is on.
        /// Held in world coordinates, because the game shifts the world origin under us and
        /// only the live subtraction stays honest across that.</summary>
        private class JGeo { public Vector3 World; public float Dx, Dz; public int Side; }
        private static readonly Dictionary<int, JGeo> _jGeo = new Dictionary<int, JGeo>();
        private static readonly Dictionary<RailTrack, string> _trackIds = new Dictionary<RailTrack, string>();
        // How far each leg of a switch is drawn. This is the ONLY thing on the board that
        // says which way a switch is set, so it has to be long enough to read without
        // winding the zoom up: 120m is seventeen pixels at the default scale, which is
        // nothing. Half a kilometre reads at a glance and still stops well short of
        // claiming the whole section.
        private const float StubMeters = 500f;
        private const float StubThin = 12f;      // drop points closer together than this
        // How many switches one road may hold. Stretches of this railway run junction after
        // junction with no signal between them, and a road that runs to the next signal can
        // therefore lock a very long way; one dispatcher's click should not take the
        // railway away from everybody else.
        private const int MaxLocked = 10;
        private static string _builtHash;
        private static string _facingReport = "";

        // A signal stands beside the junction it guards, a train length or so up its own
        // leg; on a live world the furthest sat 54m out, so this is generous rather than
        // tight, and the nearest junction wins anyway.
        private const float AnchorCell = 120f;
        private const float AnchorRadius = 150f;

        public static void Reset()
        {
            _signals.Clear(); _inboundAt.Clear(); _routes.Clear();
            _jIds.Clear(); _junctions.Clear(); _stubs.Clear();
            _jGeo.Clear(); _trackIds.Clear(); _builtHash = null;
        }

        private static long LegKey(int junction, int leg) => ((long)junction << 8) ^ (uint)(leg + 8);

        /// <summary>Unity flags a destroyed object as fake-null, so a dead first junction
        /// means the whole set belongs to a world that has been unloaded.</summary>
        private static bool JunctionsAlive() => _junctions.Count == 0 || _junctions[0] != null;

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
        /// <summary>The tail of a signal id, which is how the mod says what it is:
        /// T on the trunk toward the junction, F on the trunk away from it, B1 and B2
        /// on the branches.</summary>
        private static string SuffixOf(string id)
        {
            int c = id.LastIndexOf(':');
            if (c >= 0) return id.Substring(c + 1);
            int h = id.LastIndexOf('-');
            return h >= 0 ? id.Substring(h + 1) : "?";
        }

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
        /// junction and leg it belongs to. Yards included: main signals show everywhere
        /// (owner ruling), and there is no separate station view any more.
        /// </summary>
        public static void Build()
        {
            string hash = null;
            try { hash = SingletonBehaviour<RailTrackRegistryBase>.Instance?.TracksHash; } catch { }
            // Loading a second save in one session destroys every Junction and then builds
            // the SAME railway again, so the track hash is identical and the hash alone
            // would wave through a set of references to objects that no longer exist. The
            // map payload has always tested this; this did not, and would have gone on
            // holding dead switches until the game was restarted.
            if (_builtHash == hash && _signals.Count > 0 && JunctionsAlive()) return;
            Reset();
            _builtHash = hash;

            var move = WorldMover.currentMove;
            Junction[] all;
            try { all = RailTrackRegistry.Instance.TrackRootParent.GetComponentsInChildren<Junction>(); }
            catch { return; }

            var jgrid = new Dictionary<long, List<int>>();
            long GKey(float x, float z) =>
                ((long)Mathf.FloorToInt(x / AnchorCell) << 32) ^ (uint)Mathf.FloorToInt(z / AnchorCell);
            for (int i = 0; i < all.Length; i++)
            {
                var j = all[i];
                if (j == null) continue;
                var p = j.position - move;
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
                var key = info.Direction ?? "None";
                dirs.TryGetValue(key, out var dc); dirs[key] = dc + 1;

                var sig = new Sig { Id = info.Id, Info = info, Leg = LegFromId(info.Id) };
                int jidx = NearestJunction(info.X, info.Z, out var dist);
                if (jidx >= 0 && dist <= AnchorRadius) { sig.J = jidx; placed++; }
                else loose++;
                _signals.Add(sig);
            }
            // Which way a signal faces, which decides both the arrow and the road.
            //
            // The mod's own Direction field cannot tell the cases apart: it reports Out for
            // every trunk signal in the world and None for every branch one. So the id
            // decides, and the id says To or From. A trunk signal named -T guards the
            // junction and is read by a train running toward the points, which the world
            // confirms at SM-SUB-N where that mast faces a train coming up the trunk. One
            // named -F governs the move the other way, out of the junction and away along
            // the trunk, which is what a section signal beyond a junction does. Branch
            // signals guard the junction like -T does.
            //
            // Nothing here is guesswork that stays guesswork: the log prints the split by
            // suffix, so a report of a signal greening backwards can be traced to a group
            // rather than hunted one mast at a time.
            var facing = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var sig in _signals)
            {
                if (sig.J < 0) continue;
                var k = LegKey(sig.J, sig.Leg);
                used.TryGetValue(k, out var n);
                used[k] = n + 1;
                sig.Slot = n;
                // A second signal on one leg necessarily faces the other way, whatever it
                // is called; that is four legs in the whole world. The -F suffix was once
                // read as a departure signal facing out, but that was inference, and the
                // owner's screenshots show those masts facing the junction like every
                // other: a junction signal guards its junction, whatever it is named.
                sig.Inbound = n == 0;
                var fk = SuffixOf(sig.Id) + (sig.Inbound ? " inbound" : " outbound");
                facing.TryGetValue(fk, out var fc); facing[fk] = fc + 1;
                if (sig.Inbound && !_inboundAt.ContainsKey(k)) _inboundAt[k] = sig;
            }
            _facingReport = string.Join(", ", facing.OrderBy(kv => kv.Key)
                .Select(kv => kv.Key + "=" + kv.Value).ToArray());

            // A rail's display id is looked up by object once here. Finding it used to mean
            // walking the whole track registry, and the live payload did that for every
            // drawn junction on every poll.
            try
            {
                foreach (var kv in RailTrackRegistry.LogicToRailTrack)
                    if (kv.Value != null && kv.Key?.ID != null) _trackIds[kv.Value] = kv.Key.ID.FullDisplayID;
            }
            catch { }

            // Leg geometry never moves, so it is worked out once here and only the
            // selected index changes from poll to poll.
            // Every junction, yards included (owner ruling): main signals and switches
            // show everywhere, and the yard IS the map now.
            for (int i = 0; i < _junctions.Count; i++)
            {
                var j = _junctions[i];
                if (j == null) continue;
                var list = new List<object>();
                try { BuildStubs(j, list); } catch { }
                if (list.Count > 0) _stubs[i] = list;

                // Stand the mark on the same side its rail is drawn, or a crossover puts
                // two switches on one spot between the tracks.
                // ABSOLUTE coordinates, fixed at build. The game shifts the world
                // origin under a running session, and caching a frame-relative position
                // here while subtracting the CURRENT origin at poll time slid every
                // switch disc off the railway a little further with each shift (owner
                // screenshot: discs and dots floating in open ground while the lines,
                // arms and signals, all absolute, stayed put).
                var geo = new JGeo { World = j.position - move, Dx = 1f, Dz = 0f };
                var approach = j.inBranch?.track;
                if (approach != null)
                {
                    Heading(approach, j.inBranch.first, out var hdx, out var hdz);
                    geo.Dx = (float)Math.Round(hdx, 3);
                    geo.Dz = (float)Math.Round(hdz, 3);
                }
                _jGeo[i] = geo;
            }
            var dirText = string.Join(", ", dirs.Select(kv => kv.Key + "=" + kv.Value).ToArray());
            Main.LogAlways($"[Interlocking] {_signals.Count} main signal(s) kept ({placed} standing at a known switch, "
                + $"{loose} loose), {skipped} skipped as distant/shunting/other, {_junctions.Count} junction(s) numbered; "
                + $"the mod reports direction {dirText}.");
            Main.LogAlways($"[Interlocking] signal facing by id: {_facingReport}. "
                + "A signal reported as greening the wrong way should name its group here.");
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

        /// <summary>
        /// The legs of every drawn switch, which is pure geometry and never changes once a
        /// world is built. It rides the memoized map payload rather than the live one:
        /// half a kilometre of leg on six hundred junctions is hundreds of kilobytes, and
        /// re-serializing that on the game's own thread every five seconds to say nothing
        /// new is exactly the kind of cost the board is supposed to avoid.
        /// </summary>
        public static object LegsPayload()
        {
            var outp = new List<object>();
            foreach (var kv in _stubs)
                outp.Add(new { id = kv.Key, legs = kv.Value });
            return outp;
        }

        public static object Payload()
        {
            // A world reload leaves this holding switches that no longer exist. Rebuilding
            // the map rebuilds this with it, and the map's own guard makes the call cheap
            // when nothing has changed, so the board heals itself instead of showing an
            // empty railway until somebody reloads the page.
            if (!JunctionsAlive()) { try { TrackMap.GeometryBytes(); } catch { } }
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
                    // True position, always (full RD ruling): the board draws a signal
                    // where it stands and nudges only for click room.
                    x = info.X,
                    z = info.Z,
                    jid = s.J,
                    leg = s.Leg,
                    slot = s.Slot,
                    inbound = s.Inbound,
                    aspect = info.Aspect,
                    on = info.IsOn,
                    manual = info.Manual,
                    road = _routes.ContainsKey(s.Id),
                });
            }
            var locked = new HashSet<int>();
            foreach (var r in _routes.Values) foreach (var j in r.Locked) locked.Add(j);
            var jn = new List<object>();
            // Where a switch stands, which way its approach runs and which side of a
            // double track it sits on are all fixed when the world is built. Only the set
            // branch and the lock change from poll to poll, so only those are read here.
            // This used to work all of it out every poll, and finding a rail's id meant
            // walking the whole track registry, so a single poll did 221 full registry
            // scans to re-answer questions whose answers cannot change.
            foreach (var kv in _jGeo)
            {
                int i = kv.Key;
                var g = kv.Value;
                var j = _junctions[i];
                if (j == null) continue;
                var p = g.World;
                jn.Add(new
                {
                    id = i,
                    x = (float)Math.Round(p.x, 1),
                    z = (float)Math.Round(p.z, 1),
                    branch = (int)j.selectedBranch,
                    branches = j.outBranches?.Count ?? 0,
                    locked = locked.Contains(i),
                    side = g.Side,
                    dx = g.Dx,
                    dz = g.Dz,
                });
            }
            var rts = _routes.Values.Select(r => new { signal = r.SignalId, poly = r.Poly }).ToList();
            // The map is fetched once and kept, so it needs a way to know it has gone
            // stale. The track hash cannot do it: loading a second save rebuilds the SAME
            // railway, hash and all, but the game may place the world at a different
            // origin, and the map bakes that origin in. This counts rebuilds instead.
            return new { signals = sigs, junctions = jn, routes = rts, ctc = Ctc, epoch = TrackMap.Epoch };
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
            bool capped = false;
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
                // A road runs signal to signal, but stretches of this railway carry no
                // signal for junction after junction, and one click should never lock half
                // the map away from everybody else. The road ends here instead.
                if (route.Locked.Count >= MaxLocked) { capped = true; break; }
                var exit = ExitFrom(nj, arrive, out _);
                if (exit?.track == null) break;                // set against the move
                if (Held(njid, out var by)) return (false, by);
                route.Locked.Add(njid);
                track = exit.track; towardOut = exit.first;
            }
            if (route.Path.Count == 0) return (false, "nothing to set from that signal");
            // The approach the signal stands on belongs in Path so the release logic
            // sees the train coming, but not in the PICTURE: painting the track behind
            // the signal green made every cleared road look like it ran backwards
            // (owner screenshot: a kilometre of green trailing away behind the mast).
            route.Poly = PathPolyline(s.Inbound && route.Path.Count > 1
                ? route.Path.GetRange(1, route.Path.Count - 1)
                : route.Path);
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
                + (capped ? $" (stopped at {MaxLocked}, the limit for one road)" : "")
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
                    // Say what to do about it. A dispatcher reading this needs the way out,
                    // not just the diagnosis.
                    message = $"switch {junctionId} is locked by the road off {r.SignalId}; "
                        + $"click {r.SignalId} to drop that road first";
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
            if (t == null) return null;
            if (_trackIds.TryGetValue(t, out var id)) return id;
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
                // The final curve point always goes in: wye connectors are shorter
                // than the thinning distance, and without this their piece of a cleared
                // road had one point and drew NOTHING. The light went green and the
                // board showed no road into the wye (owner report).
                var endBp = t.curve[t.curve.pointCount - 1];
                if (endBp != null && have)
                {
                    var ep = endBp.position - move;
                    if ((ep.x - lx) * (ep.x - lx) + (ep.z - lz) * (ep.z - lz) > 0.01f)
                    {
                        pts.Add((float)Math.Round(ep.x, 1));
                        pts.Add((float)Math.Round(ep.z, 1));
                    }
                }
                if (pts.Count >= 4)
                    outp.Add(new { pts = pts.ToArray() });
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
            Vector3 prev = default, kept = default; bool have = false;
            for (int k = 0; k < n && run < StubMeters; k++)
            {
                // Walk outward from the end that touches the junction.
                var bp = br.first ? c[k] : c[n - 1 - k];
                if (bp == null) continue;
                var q = bp.position - move;
                if (have) run += Vector3.Distance(prev, q);
                prev = q;
                // The first point IS the junction and always goes in; the rest are thinned,
                // because half a kilometre of raw curve points on every leg of six hundred
                // junctions rides in the payload on every poll.
                if (have && Vector3.Distance(kept, q) < StubThin) continue;
                kept = q; have = true;
                pts.Add((float)Math.Round(q.x, 1));
                pts.Add((float)Math.Round(q.z, 1));
            }
            if (pts.Count >= 4)
            {
                into.Add(new
                {
                    branch = leg,
                    pts = pts.ToArray(),
                });
            }
        }
    }
}
