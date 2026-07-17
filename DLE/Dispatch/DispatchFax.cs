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
            if (!StaticDirectHaulJobDefinition.jobDefinitions.TryGetValue(jobId, out var def) || def.LiveJob == null)
                return Result.Fail($"unknown job '{jobId}'");

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

            Transform target;
            bool isLocal;
            string name;
            if (string.IsNullOrEmpty(player))
            {
                target = PlayerManager.PlayerTransform;
                isLocal = true;
                name = "you";
            }
            else
            {
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
                booklet = BookletCreator_Job.Create(def.LiveJob, pos, rot,
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

        /// <summary>Every connected crew name (DVMP avatars via reflection). Empty in
        /// singleplayer; the board uses it as assignment suggestions.</summary>
        public static System.Collections.Generic.List<string> GetPlayerNames()
        {
            var names = new System.Collections.Generic.List<string>();
            try
            {
                var type = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType("Multiplayer.Components.Networking.Player.NetworkedPlayer"))
                    .FirstOrDefault(t => t != null);
                if (type == null) return names;
                var usernameProp = type.GetProperty("Username");
                foreach (var obj in UnityEngine.Object.FindObjectsOfType(type))
                    if (usernameProp?.GetValue(obj) is string username && !string.IsNullOrEmpty(username))
                        names.Add(username);
                names.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Main.Log($"[Fax] player roster lookup failed: {ex.Message}");
            }
            return names;
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
