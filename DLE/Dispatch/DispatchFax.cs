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
        private static Type _npType;
        private static bool _npTypeSearched;

        public static System.Collections.Generic.List<string> GetPlayerNames()
        {
            if (_rosterCache != null && Time.realtimeSinceStartup - _rosterCacheAt < 10f)
                return _rosterCache;
            var names = new System.Collections.Generic.List<string>();
            _rosterCache = names;
            _rosterCacheAt = Time.realtimeSinceStartup;
            try
            {
                if (!_npTypeSearched)
                {
                    _npTypeSearched = true;
                    _npType = AppDomain.CurrentDomain.GetAssemblies()
                        .Select(a => a.GetType("Multiplayer.Components.Networking.Player.NetworkedPlayer"))
                        .FirstOrDefault(t => t != null);
                }
                var type = _npType;
                if (type == null) return names;
                var usernameProp = type.GetProperty("Username");
                foreach (var obj in UnityEngine.Object.FindObjectsOfType(type))
                    if (usernameProp?.GetValue(obj) is string username && !string.IsNullOrEmpty(username))
                        names.Add(username);
                var self = LocalPlayerName();
                if (!string.IsNullOrEmpty(self) &&
                    !names.Contains(self, StringComparer.OrdinalIgnoreCase))
                    names.Add(self);
                names.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Main.Log($"[Fax] player roster lookup failed: {ex.Message}");
            }
            return names;
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
        private static string LocalPlayerName()
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
