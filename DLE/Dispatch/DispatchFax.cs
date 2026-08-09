using DLE.Jobs;
using DV.Booklets;
using DV.InventorySystem;
using DV.Utils;
using System;
using System.Linq;
using UnityEngine;

namespace DLE.Dispatch
{
    /// <summary>
    /// Fax a Company Haul booklet to a player (#33): every loco has a fax machine. The
    /// paper goes into the local player's inventory when it fits, otherwise it prints in
    /// front of the target; remote crews always get it printed in front of them, since
    /// their inventories live on their own machines and the world item syncs over DVMP.
    /// Faxed paper is a JobBooklet, not an office overview, so the bookletless lock's
    /// sweep never eats it: dispatch sent it on purpose.
    /// </summary>
    public static class DispatchFax
    {
        public struct Result
        {
            public bool Ok;
            public string Message;
            public static Result Fail(string m) => new Result { Ok = false, Message = m };
            public static Result Done(string m) => new Result { Ok = true, Message = m };
        }

        public static Result Fax(string jobId, string player)
        {
            if (!Main.IsHostOrSingleplayer()) return Result.Fail("host or singleplayer only");

            // Any live job faxes, not just Company Hauls: a logi move is a vanilla
            // EmptyHaul and the vanilla booklet code renders it natively.
            DV.Logic.Job.Job job = null;
            if (StaticDirectHaulJobDefinition.jobDefinitions.TryGetValue(jobId, out var def) && def.LiveJob != null)
                job = def.LiveJob;
            else
            {
                var jm = SingletonBehaviour<DV.Logic.Job.JobsManager>.Instance;
                if (jm != null)
                    foreach (var jj in jm.jobToJobCars.Keys)
                        if (jj != null && jj.ID == jobId) { job = jj; break; }
            }
            if (job == null) return Result.Fail($"unknown job '{jobId}'");

            // No name given: the assigned crew is the natural target; only an
            // unassigned job faxes to the local player.
            bool viaAssignment = false;
            if (string.IsNullOrEmpty(player))
            {
                var assignment = AssignmentStore.Instance.Get(jobId);
                if (!string.IsNullOrEmpty(assignment?.Player))
                {
                    player = assignment.Player;
                    viaAssignment = true;
                }
            }

            // Assign-by-loco (#118 wave 4): a crew slot naming a live locomotive means
            // "whoever is running that engine". The fax resolves the loco to the nearest
            // player aboard at SEND time, so the paper follows the seat, not the name.
            if (!string.IsNullOrEmpty(player) && TryFindLoco(player, out var loco))
            {
                var crewName = NearestPlayerTo(loco.transform.position, 30f);
                if (crewName == null)
                    return Result.Fail($"nobody is aboard {loco.ID} (within 30 m) to fax");
                Main.LogAlways($"[Fax] {jobId}: {loco.ID} resolved to {(crewName.Length == 0 ? "the local player" : crewName)}.");
                player = crewName; // empty string = the local player
                viaAssignment = true; // keep the loco assignment; do not overwrite with the person
            }

            Transform target;
            bool isLocal;
            string name;
            if (string.IsNullOrEmpty(player))
            {
                target = PlayerManager.PlayerTransform;
                isLocal = true;
                name = "you";
            }
            else if (IsLocalPlayerName(player))
            {
                // The local player (host) is never a NetworkedPlayer avatar, so a typed or
                // assigned name matching our own username resolves to the local transform
                // and the paper goes to the local inventory like a blank-name fax.
                target = PlayerManager.PlayerTransform;
                isLocal = true;
                name = player;
            }
            else
            {
                // A crew running DLE gets the clean fax: their own mod prints the paper
                // straight into their inventory. The world-print below stays the fallback
                // for modless crews (the paper syncs poorly, but it is all DVMP offers).
                if (DleMpChannel.NotifyFax(player, jobId))
                {
                    AssignFaxTarget(jobId, player, viaAssignment);
                    Main.LogAlways($"[Fax] {jobId} faxed to {player}'s inventory via the DLE channel.");
                    return Result.Done($"{jobId} faxed to {player}'s inventory");
                }

                target = FindNetworkedPlayer(player, out name);
                isLocal = false;
                if (target == null)
                    return Result.Fail(viaAssignment
                        ? $"assigned crew '{player}' is not in this session; type a name to fax someone else"
                        : $"player '{player}' not found in this session");
            }
            if (target == null) return Result.Fail("no player to fax to");

            var pos = target.position + target.forward * 0.6f + Vector3.up * 1.1f;
            var rot = Quaternion.LookRotation(target.forward);

            // The Job overload assigns the job to the JobBooklet component (the Job_data
            // overload renders pages only, leaving the item named [NO JOB] and exempt from
            // completion cleanup). Parenting to the origin shift keeps a world-printed
            // paper in place when the world moves; storage registration is deferred until
            // the paper is known to stay in the world.
            GameObject booklet;
            try
            {
                booklet = BookletCreator_Job.Create(job, pos, rot,
                    WorldMover.OriginShiftParent, addToWorldStorage: false)?.gameObject;
            }
            catch (Exception ex)
            {
                return Result.Fail($"fax jammed: {ex.GetType().Name}: {ex.Message}");
            }
            if (booklet == null) return Result.Fail("fax jammed: no booklet came out");

            if (isLocal)
            {
                try
                {
                    var inv = SingletonBehaviour<Inventory>.Instance;
                    if (inv != null && inv.CanAddItem(booklet))
                    {
                        int slot = inv.AddItemToInventory(booklet, false);
                        if (slot >= 0)
                        {
                            Main.LogAlways($"[Fax] {jobId} faxed to the local player's inventory.");
                            return Result.Done($"{jobId} faxed to your inventory");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Main.Log($"[Fax] inventory stash failed ({ex.Message}); leaving the paper in the world.");
                }
            }

            try { SingletonBehaviour<StorageController>.Instance?.AddItemToWorldStorageAfterOneFrame(booklet); }
            catch (Exception ex) { Main.Log($"[Fax] world storage registration failed: {ex.Message}"); }

            // Handing someone paper is handing them the work: a fax to a named crew
            // assigns the job if nothing else has.
            if (!isLocal && !viaAssignment && AssignmentStore.Instance.Get(jobId) == null)
            {
                AssignmentStore.Instance.Assign(jobId, name, "fax");
                Main.Log($"[Fax] {jobId} assigned to {name} by fax.");
            }

            Main.LogAlways($"[Fax] {jobId} faxed; printed in front of {name}.");
            return Result.Done($"{jobId} faxed; printing in front of {name}");
        }

        /// <summary>Fax-to-crew implies assignment when nothing else set one.</summary>
        private static void AssignFaxTarget(string jobId, string player, bool viaAssignment)
        {
            if (!viaAssignment && AssignmentStore.Instance.Get(jobId) == null)
                AssignmentStore.Instance.Assign(jobId, player, "fax");
        }

        /// <summary>A live locomotive whose plate matches the given name, else false. This
        /// is what lets an assignment slot hold an engine instead of a person.</summary>
        private static bool TryFindLoco(string name, out TrainCar loco)
        {
            loco = null;
            try
            {
                foreach (var kv in TrainCarRegistry.Instance.logicCarToTrainCar)
                    if (kv.Key?.ID != null && kv.Value != null && kv.Value.IsLoco &&
                        string.Equals(kv.Key.ID, name, StringComparison.OrdinalIgnoreCase))
                    { loco = kv.Value; return true; }
            }
            catch (Exception ex)
            {
                Main.Log($"[Fax] loco lookup failed: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// The player standing closest to a position within range: the local player
        /// (returned as the empty string, the fax code's marker for "us") or a DVMP
        /// avatar's username. Null when nobody is close enough.
        /// </summary>
        private static string NearestPlayerTo(Vector3 pos, float range)
        {
            string best = null;
            float bestD = range;
            try
            {
                var lp = PlayerManager.PlayerTransform;
                if (lp != null)
                {
                    float d = Vector3.Distance(lp.position, pos);
                    if (d < bestD) { bestD = d; best = LocalPlayerName() ?? string.Empty; }
                }
                var type = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType("Multiplayer.Components.Networking.Player.NetworkedPlayer"))
                    .FirstOrDefault(t => t != null);
                if (type != null)
                {
                    var usernameProp = type.GetProperty("Username");
                    foreach (var obj in UnityEngine.Object.FindObjectsOfType(type))
                    {
                        var comp = obj as Component;
                        var username = usernameProp?.GetValue(obj) as string;
                        if (comp == null || string.IsNullOrEmpty(username)) continue;
                        float d = Vector3.Distance(comp.transform.position, pos);
                        if (d < bestD) { bestD = d; best = username; }
                    }
                }
            }
            catch (Exception ex)
            {
                Main.Log($"[Fax] nearest-player lookup failed: {ex.Message}");
            }
            return best;
        }

        /// <summary>Every connected crew name: DVMP avatars are the REMOTE players only,
        /// so the local player's own username (from the server player list, which the
        /// avatars never include) joins the roster too; the host can assign and fax
        /// themselves. Empty in singleplayer; the board uses it as suggestions.</summary>
        // The roster used to rescan every loaded assembly and the whole scene on EVERY
        // call, and the board polls it every 5 seconds: the lag meter measured about
        // 100 ms of main thread per request, a hitch machine. Ten seconds of cache is
        // fresher than anyone joins or leaves.
        private static System.Collections.Generic.List<string> _rosterCache;
        private static float _rosterCacheAt = -999f;

        public static System.Collections.Generic.List<string> GetPlayerNames()
        {
            if (_rosterCache != null && Time.realtimeSinceStartup - _rosterCacheAt < 10f)
                return _rosterCache;
            // MPAPI or nothing (owner ruling): the server player list is a plain list
            // read. No scene-scan fallback; it fired in singleplayer, where there is
            // nobody to list, and cost ~80ms a pass for nothing.
            var mp = MpApiPlayerNames() ?? new System.Collections.Generic.List<string>();
            mp.Sort(StringComparer.OrdinalIgnoreCase);
            _rosterCache = mp;
            _rosterCacheAt = Time.realtimeSinceStartup;
            return mp;
        }

        private static bool IsLocalPlayerName(string player)
        {
            var self = LocalPlayerName();
            return !string.IsNullOrEmpty(self) &&
                   string.Equals(self, player, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The local player's own username via the MPAPI server player list (the entry
        /// flagged IsHost is us when we host). Reflection keeps the zero compile-time
        /// dependency on DVMP; null in singleplayer or when no server runs.
        /// </summary>
        /// <summary>
        /// Every connected username via the MPAPI server player list: no scene scan.
        /// Null when DVMP/MPAPI is absent (singleplayer) or the list is unreadable.
        /// </summary>
        internal static System.Collections.Generic.List<string> MpApiPlayerNames()
        {
            try
            {
                var mpApiType = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "MultiplayerAPI")
                    ?.GetType("MPAPI.MultiplayerAPI");
                var server = mpApiType?.GetProperty("Server")?.GetValue(null);
                if (server == null) return null;
                if (!(server.GetType().GetProperty("Players")?.GetValue(server)
                        is System.Collections.IEnumerable players)) return null;
                var names = new System.Collections.Generic.List<string>();
                foreach (var p in players)
                    if (p.GetType().GetProperty("Username")?.GetValue(p) is string u
                        && !string.IsNullOrEmpty(u) && !names.Contains(u))
                        names.Add(u);
                return names;
            }
            catch { return null; }
        }

        /// <summary>
        /// Remote player positions via MPAPI, in the same shifted frame as car and
        /// station transforms (dv-mp compares WorldPosition against car transforms
        /// directly). Host and still-loading entries are skipped: the local player is
        /// read from PlayerManager by the caller, and a loading player's position is
        /// not yet meaningful. Null when MPAPI is absent.
        /// </summary>
        // MPAPI reflection handles, resolved once (#211): the assembly set cannot
        // change inside a session, and the 5s dormancy sweep was re-scanning every
        // loaded assembly to re-find the same type. Absence is not latched, so a
        // late-arming DVMP (a client joining after load) is still found later.
        private static System.Reflection.PropertyInfo _mpServerProp;
        private static System.Reflection.PropertyInfo _mpPlayersProp;

        private static System.Collections.IEnumerable MpApiPlayers()
        {
            try
            {
                if (_mpServerProp == null)
                {
                    var mpApiType = AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(a => a.GetName().Name == "MultiplayerAPI")
                        ?.GetType("MPAPI.MultiplayerAPI");
                    _mpServerProp = mpApiType?.GetProperty("Server");
                    if (_mpServerProp == null) return null;
                }
                var server = _mpServerProp.GetValue(null);
                if (server == null) return null;
                if (_mpPlayersProp == null)
                    _mpPlayersProp = server.GetType().GetProperty("Players");
                return _mpPlayersProp?.GetValue(server) as System.Collections.IEnumerable;
            }
            catch { return null; }
        }

        internal static System.Collections.Generic.List<Vector3> MpApiPlayerPositions()
        {
            try
            {
                var players = MpApiPlayers();
                if (players == null) return null;
                var list = new System.Collections.Generic.List<Vector3>();
                foreach (var p in players)
                {
                    var pt = p.GetType();
                    if (pt.GetProperty("IsHost")?.GetValue(p) is bool h && h) continue;
                    if (pt.GetProperty("IsLoaded")?.GetValue(p) is bool l && !l) continue;
                    if (pt.GetProperty("Position")?.GetValue(p) is Vector3 v) list.Add(v);
                }
                return list;
            }
            catch { return null; }
        }

        /// <summary>
        /// Who is sitting in which car, via MPAPI OccupiedCar: usernames to car ids
        /// (L-049 and the like). Loco-reference assignment and the crew-loco display
        /// both hang off this. Null without MPAPI (singleplayer).
        /// </summary>
        internal static System.Collections.Generic.Dictionary<string, string> MpApiPlayerCars()
        {
            try
            {
                var players = MpApiPlayers();
                if (players == null) return null;
                var map = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in players)
                {
                    var pt = p.GetType();
                    if (!(pt.GetProperty("Username")?.GetValue(p) is string u) || string.IsNullOrEmpty(u)) continue;
                    var car = pt.GetProperty("OccupiedCar")?.GetValue(p);
                    if (car == null) continue;
                    string id = null;
                    try { id = car.GetType().GetProperty("ID")?.GetValue(car) as string; } catch { }
                    if (id == null) { try { id = car.GetType().GetField("ID")?.GetValue(car) as string; } catch { } }
                    if (!string.IsNullOrEmpty(id)) map[u] = id;
                }
                return map;
            }
            catch { return null; }
        }

        /// <summary>
        /// Owner ask: typing a loco on the assign line (49, 049, L049, L-049, L(049),
        /// however people write it) assigns whoever is SITTING in that loco. Returns
        /// true when the text reads as a loco reference at all; player comes back null
        /// when the cab is empty. Anything wordier than an L and digits is a crew name
        /// and none of this method's business.
        /// </summary>
        internal static bool TryResolveLocoAssignee(string text, out string player, out string locoId)
        {
            player = null; locoId = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var digits = new string(text.Where(char.IsDigit).ToArray());
            var letters = new string(text.Where(char.IsLetter).ToArray()).ToUpperInvariant();
            if (digits.Length < 1 || digits.Length > 4 || (letters.Length != 0 && letters != "L")) return false;
            if (!int.TryParse(digits, out var want)) return false;
            locoId = "L-" + digits.PadLeft(3, '0');
            var cars = MpApiPlayerCars();
            if (cars == null) return true;
            foreach (var kv in cars)
            {
                var cid = kv.Value;
                if (string.IsNullOrEmpty(cid) || char.ToUpperInvariant(cid[0]) != 'L') continue;
                var cdig = new string(cid.Where(char.IsDigit).ToArray());
                if (int.TryParse(cdig, out var have) && have == want)
                { player = kv.Key; locoId = cid; return true; }
            }
            return true;
        }

        private static string LocalPlayerName()
        {
            try
            {
                var players = MpApiPlayers();
                if (players == null) return null;
                foreach (var p in players)
                {
                    var pt = p.GetType();
                    if (pt.GetProperty("IsHost")?.GetValue(p) is bool isHost && isHost)
                        return pt.GetProperty("Username")?.GetValue(p) as string;
                }
            }
            catch (Exception ex)
            {
                Main.Log($"[Fax] local player name lookup failed: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Remote crews exist in the host's world as DVMP player avatars; find one by
        /// username via reflection so DLE keeps zero compile-time dependency on the
        /// Multiplayer mod.
        /// </summary>
        private static Transform FindNetworkedPlayer(string player, out string resolved)
        {
            resolved = player;
            try
            {
                var type = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType("Multiplayer.Components.Networking.Player.NetworkedPlayer"))
                    .FirstOrDefault(t => t != null);
                if (type == null) return null;

                var usernameProp = type.GetProperty("Username");
                foreach (var obj in UnityEngine.Object.FindObjectsOfType(type))
                {
                    var comp = obj as Component;
                    var username = usernameProp?.GetValue(obj) as string;
                    if (comp == null || username == null) continue;
                    if (string.Equals(username, player, StringComparison.OrdinalIgnoreCase))
                    {
                        resolved = username;
                        return comp.transform;
                    }
                }
            }
            catch (Exception ex)
            {
                Main.Log($"[Fax] player lookup failed: {ex.Message}");
            }
            return null;
        }
    }
}
