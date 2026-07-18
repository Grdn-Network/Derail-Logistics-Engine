using DLE.Jobs;
using DV.Booklets;
using DV.Logic.Job;
using DV.Utils;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace DLE.Dispatch
{
    /// <summary>
    /// DLE's host-to-client data channel over DVMP's mod packet API. It exists because
    /// dv-mp serializes a job's cars and tasks exactly once, in the creation packet, and
    /// its periodic update carries only state/time/item data: DLE's attach-on-arrival
    /// model changes a job's cars mid-life and no vanilla packet can say so. This channel
    /// carries three things to clients that run DLE: the delivery payment (booklet
    /// display), the attached car list (plates and consists), and fax-print signals
    /// (paper into the client's own inventory).
    ///
    /// Split on purpose: THIS class holds pure game/BCL state and is safe to touch from
    /// anywhere (singleplayer included). DleMpTransport touches MultiplayerAPI types in
    /// method bodies only, so the soft-referenced MultiplayerAPI.dll is loaded the first
    /// time one of those methods RUNS, which only happens when DVMP is present.
    ///
    /// Safety: packets go only to clients that sent the DLE hello, so modless clients
    /// receive nothing. A DLE client greeting a non-DLE host costs that host one logged
    /// LiteNetLib parse warning (dv-mp catches ParseException); nothing breaks.
    /// </summary>
    public static class DleMpChannel
    {
        // CLIENT side: meta the booklet render reads for jobs whose definitions live on
        // the host. Keyed by job ID.
        public static readonly Dictionary<string, float> ClientJobPay =
            new Dictionary<string, float>(StringComparer.Ordinal);
        public static readonly HashSet<string> ClientUnpaid =
            new HashSet<string>(StringComparer.Ordinal);
        public static readonly Dictionary<string, string> ClientJobCargo =
            new Dictionary<string, string>(StringComparer.Ordinal);
        public static readonly Dictionary<string, int> ClientJobCars =
            new Dictionary<string, int>(StringComparer.Ordinal);

        // Attach packets that arrived before DVMP delivered the job itself; retried on
        // each later packet (attaches trail creations by minutes, so this is a corner).
        private static readonly Dictionary<string, string> _pendingAttaches =
            new Dictionary<string, string>(StringComparer.Ordinal);

        // CLIENT side: the host's assignment lock, synced at hello and on every toggle.
        // Drives the local paper sweep (#73): when the lock is on, Company Haul papers
        // leave THIS client's station offices the same way they leave the host's.
        public static bool ClientLockOn;

        public static bool TransportUp { get; internal set; }

        /// <summary>
        /// Arm the transport. Callable repeatedly (mod load AND world load): assemblies
        /// load LAZILY in .NET, so at DLE's load time DVMP may not have touched its own
        /// API yet and MultiplayerAPI.dll is absent from the AppDomain even though the
        /// mod is installed; this forces it in from the Multiplayer mod's own folder.
        /// Silently does nothing when DVMP is not installed. Never throws outward.
        /// </summary>
        public static void TryInit()
        {
            if (TransportArmed) return;
            try
            {
                var domain = AppDomain.CurrentDomain.GetAssemblies();
                if (!domain.Any(a => a.GetName().Name == "MultiplayerAPI"))
                {
                    var mp = domain.FirstOrDefault(a => a.GetName().Name == "Multiplayer");
                    if (mp == null) return; // no DVMP installed: pure singleplayer
                    var apiPath = System.IO.Path.Combine(
                        System.IO.Path.GetDirectoryName(mp.Location) ?? "", "MultiplayerAPI.dll");
                    if (!System.IO.File.Exists(apiPath))
                    {
                        Main.LogAlways($"[MpChannel] Multiplayer is loaded but MultiplayerAPI.dll is missing next to it; client sync disabled.");
                        return;
                    }
                    System.Reflection.Assembly.LoadFrom(apiPath);
                    Main.Log("[MpChannel] MultiplayerAPI.dll force-loaded from the Multiplayer mod folder.");
                }
                DleMpTransport.Init();
                TransportArmed = true;
            }
            catch (Exception ex)
            {
                Main.LogAlways($"[MpChannel] transport init failed ({ex.GetType().Name}: {ex.Message}); client sync disabled.");
            }
        }

        /// <summary>True once event subscriptions are in place (independent of a session
        /// being live; TransportUp tracks an actual hosted session).</summary>
        public static bool TransportArmed { get; private set; }

        /// <summary>Client-side: (re)announce DLE to the host. The ClientStarted hello can
        /// fire before the connection settles, so the world-loaded hook calls this again;
        /// the host deduplicates.</summary>
        public static void AnnounceToHost()
        {
            if (!TransportArmed) return;
            try { DleMpTransport.SendHello(); }
            catch (Exception ex) { Main.Log($"[MpChannel] hello failed: {ex.Message}"); }
        }

        // HOST-side notifications. Safe no-ops in singleplayer or when the transport
        // never came up.

        public static void NotifyJobCreated(string jobId, float pay, bool unpaid, string cargo, int plannedCars)
        {
            if (!TransportUp) return;
            try { DleMpTransport.SendJobSync(jobId, "", pay, unpaid, cargo, plannedCars, null); }
            catch (Exception ex) { Main.Log($"[MpChannel] job-created sync failed: {ex.Message}"); }
        }

        public static void NotifyAttach(string jobId, IEnumerable<string> carIds, float pay, bool unpaid, string cargo, int plannedCars)
        {
            if (!TransportUp) return;
            try { DleMpTransport.SendJobSync(jobId, string.Join(",", carIds), pay, unpaid, cargo, plannedCars, null); }
            catch (Exception ex) { Main.Log($"[MpChannel] attach sync failed: {ex.Message}"); }
        }

        /// <summary>Host: the assignment lock flipped; every DLE client mirrors it so
        /// their local office papers follow the host's (#73).</summary>
        public static void NotifyLockChanged(bool enabled)
        {
            if (!TransportUp) return;
            try { DleMpTransport.SendLock(enabled); }
            catch (Exception ex) { Main.Log($"[MpChannel] lock sync failed: {ex.Message}"); }
        }

        /// <summary>Fax the booklet to a DLE-running client by username: their own mod
        /// prints it into their inventory. False when that player runs no DLE (the
        /// caller falls back to the world-print path).</summary>
        public static bool NotifyFax(string playerName, string jobId)
        {
            if (!TransportUp) return false;
            try { return DleMpTransport.SendFax(playerName, jobId); }
            catch (Exception ex)
            {
                Main.Log($"[MpChannel] fax send failed: {ex.Message}");
                return false;
            }
        }

        // CLIENT side apply, called by the transport on the main thread (dv-mp processes
        // packets in its poll loop).

        private static bool _firstSyncLogged;

        internal static void ApplyJobSync(string jobId, string carIdsCsv, float pay, bool unpaid, bool printBooklet,
            string cargo, int plannedCars)
        {
            if (Main.IsHostOrSingleplayer()) return; // host loopback: its own state is authoritative

            if (!_firstSyncLogged)
            {
                _firstSyncLogged = true;
                Main.LogAlways("[MpChannel] receiving DLE job sync from the host; the channel is live.");
            }
            ClientJobPay[jobId] = pay;
            if (unpaid) ClientUnpaid.Add(jobId); else ClientUnpaid.Remove(jobId);
            if (!string.IsNullOrEmpty(cargo)) ClientJobCargo[jobId] = cargo;
            if (plannedCars > 0) ClientJobCars[jobId] = plannedCars;

            if (!string.IsNullOrEmpty(carIdsCsv))
            {
                if (!TryApplyAttach(jobId, carIdsCsv))
                    _pendingAttaches[jobId] = carIdsCsv; // job not delivered by DVMP yet
                else
                    _pendingAttaches.Remove(jobId);
            }

            // Older stragglers may become appliable once any later packet arrives.
            if (_pendingAttaches.Count > 0)
                foreach (var kv in _pendingAttaches.ToList())
                    if (TryApplyAttach(kv.Key, kv.Value))
                        _pendingAttaches.Remove(kv.Key);

            if (printBooklet && !PrintFaxedBooklet(jobId))
                _pendingFaxes.Add(jobId); // job not delivered by DVMP yet; retry below
            if (_pendingFaxes.Count > 0)
                foreach (var pending in _pendingFaxes.ToList())
                    if (PrintFaxedBooklet(pending))
                        _pendingFaxes.Remove(pending);
        }

        private static readonly HashSet<string> _pendingFaxes =
            new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Fresh client session, fresh client state: job IDs are route-based and a NEW
        /// world can legitimately mint the same IDs, so meta (and the lock flag) left
        /// over from a previous session would let the paper sweep destroy the wrong
        /// world's booklets. Called at client session start and stop.
        /// </summary>
        internal static void ResetClientState()
        {
            ClientJobPay.Clear();
            ClientUnpaid.Clear();
            ClientJobCargo.Clear();
            ClientJobCars.Clear();
            _pendingAttaches.Clear();
            _pendingFaxes.Clear();
            ClientLockOn = false;
        }

        internal static void ApplyLockState(bool enabled)
        {
            if (Main.IsHostOrSingleplayer()) return; // host loopback: its own sweep runs
            bool changed = ClientLockOn != enabled;
            ClientLockOn = enabled;
            if (changed)
                Main.LogAlways($"[MpChannel] host assignment lock is {(enabled ? "ON" : "off")}; local papers follow.");
            try { SweepClientPapers(); }
            catch (Exception ex) { Main.Log($"[MpChannel] client paper sweep failed: {ex.Message}"); }
        }

        /// <summary>
        /// Client-side stale-paper sweep (#73), the mirror of the host director's
        /// DespawnManagedOverviews: the game respawns office overviews locally on every
        /// peer, so a host-side destroy never reaches this client's copies. Swept here:
        /// DLE papers while the lock is on, unpaid-move papers always, and papers whose
        /// job is no longer Available (someone took or dispatch deleted it). Beyond the
        /// UX, this removes the trigger for dv-mp's unguarded job-validate NRE: paper
        /// the host no longer tracks can't be inserted into a validator here.
        /// Only jobs this client KNOWS are DLE's (synced over the channel) are touched.
        /// </summary>
        internal static void SweepClientPapers()
        {
            if (Main.IsHostOrSingleplayer()) return;
            if (StationController.allStations == null) return;
            int removed = 0;
            foreach (var sc in StationController.allStations)
            {
                var overviews = sc?.spawnedJobOverviews;
                if (overviews == null) continue;
                for (int i = overviews.Count - 1; i >= 0; i--)
                {
                    var ov = overviews[i];
                    // Unity's overloaded == catches destroyed components; prune AND remove
                    // here (DestroyJobOverview does not remove itself from this list) or the
                    // next pass throws MissingReferenceException and aborts the sweep.
                    if (ov == null)
                    {
                        overviews.RemoveAt(i);
                        continue;
                    }
                    var id = ov.job?.ID;
                    if (id == null || !ClientJobPay.ContainsKey(id)) continue;
                    bool unpaid = ClientUnpaid.Contains(id);
                    bool stale = ov.job.State != DV.ThingTypes.JobState.Available;
                    if (!ClientLockOn && !unpaid && !stale) continue;
                    overviews.RemoveAt(i);
                    ov.DestroyJobOverview();
                    removed++;
                }
            }
            if (removed > 0)
                Main.LogAlways($"[MpChannel] swept {removed} stale Company Haul paper(s) from local offices.");
        }

        private static Job FindClientJob(string jobId)
        {
            // allJobs first: it holds every live job regardless of state. The original
            // lookup (jobToJobCars + availableJobs) missed TAKEN carless jobs entirely,
            // which is exactly what a faxed job usually is: taken via the board, no cars
            // yet. That was the live-test "fax arrived but the job is not in this world"
            // failure.
            var jm = SingletonBehaviour<JobsManager>.Instance;
            if (jm?.allJobs != null)
                foreach (var j in jm.allJobs)
                    if (j != null && j.ID == jobId) return j;
            if (jm != null)
                foreach (var j in jm.jobToJobCars.Keys)
                    if (j != null && j.ID == jobId) return j;
            if (StationController.allStations != null)
                foreach (var sc in StationController.allStations)
                {
                    var avail = sc?.logicStation?.availableJobs;
                    if (avail == null) continue;
                    foreach (var j in avail)
                        if (j != null && j.ID == jobId) return j;
                }
            return null;
        }

        private static bool TryApplyAttach(string jobId, string carIdsCsv)
        {
            var job = FindClientJob(jobId);
            if (job == null) return false;

            var wanted = new HashSet<string>(carIdsCsv.Split(','), StringComparer.Ordinal);
            var cars = new List<Car>();
            foreach (var kv in TrainCarRegistry.Instance.logicCarToTrainCar)
                if (kv.Key != null && wanted.Contains(kv.Key.ID))
                    cars.Add(kv.Key);
            if (cars.Count == 0) return false;

            foreach (var t in job.tasks)
            {
                if (!(t is WarehouseTask wt)) continue;
                wt.cars.Clear();
                wt.cars.AddRange(cars);
                Traverse.Create(wt).Field(nameof(WarehouseTask.cargoAmount))
                    .SetValue(cars.Sum(c => c.capacity));
            }
            var jm = SingletonBehaviour<JobsManager>.Instance;
            if (jm != null) jm.jobToJobCars[job] = new HashSet<Car>(cars);
            foreach (var c in cars)
            {
                try { c.TrainCar().UpdateJobIdOnCarPlates(job.ID); } catch { }
            }
            Main.LogAlways($"[MpChannel] {jobId}: {cars.Count} attached car(s) synced from the host; plates updated.");
            return true;
        }

        /// <summary>Print a faxed booklet locally; false when the job has not been
        /// delivered by DVMP yet (the caller queues a retry on the next packet).</summary>
        private static bool PrintFaxedBooklet(string jobId)
        {
            var job = FindClientJob(jobId);
            if (job == null)
            {
                Main.LogAlways($"[MpChannel] fax for {jobId} arrived before the job itself; will retry on the next sync.");
                return false;
            }
            var target = PlayerManager.PlayerTransform;
            if (target == null) return false;
            var pos = target.position + target.forward * 0.6f + Vector3.up * 1.1f;
            var rot = Quaternion.LookRotation(target.forward);
            GameObject booklet = null;
            try
            {
                booklet = BookletCreator_Job.Create(job, pos, rot,
                    WorldMover.OriginShiftParent, addToWorldStorage: false)?.gameObject;
            }
            catch (Exception ex)
            {
                // The print itself failed (not a missing job): report done, no retry loop.
                Main.LogAlways($"[MpChannel] fax print failed: {ex.GetType().Name}: {ex.Message}");
                return true;
            }
            if (booklet == null) return true;
            try
            {
                var inv = SingletonBehaviour<DV.InventorySystem.Inventory>.Instance;
                if (inv != null && inv.CanAddItem(booklet) && inv.AddItemToInventory(booklet, false) >= 0)
                {
                    Main.LogAlways($"[MpChannel] {jobId} faxed into the inventory.");
                    return true;
                }
            }
            catch (Exception ex) { Main.Log($"[MpChannel] fax inventory stash failed: {ex.Message}"); }
            try { SingletonBehaviour<StorageController>.Instance?.AddItemToWorldStorageAfterOneFrame(booklet); } catch { }
            Main.LogAlways($"[MpChannel] {jobId} faxed; printed in front of you (inventory was full).");
            return true;
        }

        /// <summary>Host: everything a newly hello'd client needs about live jobs.</summary>
        internal static List<(string jobId, string carIds, float pay, bool unpaid, string cargo, int plannedCars)> SnapshotLiveJobs()
        {
            var rows = new List<(string, string, float, bool, string, int)>();
            foreach (var kv in StaticDirectHaulJobDefinition.jobDefinitions)
            {
                var def = kv.Value;
                if (def?.LiveJob == null) continue;
                var carIds = def.carsToTransport != null && def.carsToTransport.Count > 0
                    ? string.Join(",", def.carsToTransport.Select(c => c.ID))
                    : "";
                rows.Add((kv.Key, carIds, def.deliveryPayment, def.unpaidMove,
                    def.transportedCargo.ToString(), def.plannedCarCount));
            }
            return rows;
        }
    }

    // Everything below touches MultiplayerAPI types INSIDE METHOD BODIES ONLY (fields
    // hold object), so the CLR resolves MultiplayerAPI.dll on first call, never on type
    // load. Do not add MPAPI types to any field or method signature here.

    internal static class DleMpTransport
    {
        private static object _server;                       // IServer while hosting
        private static object _client;                       // IClient while connected
        private static readonly List<object> _dleClients = new List<object>(); // IPlayer

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Init()
        {
            MPAPI.MultiplayerAPI.ServerStarted += OnServerStarted;
            MPAPI.MultiplayerAPI.ServerStopped += () => { _server = null; _dleClients.Clear(); DleMpChannel.TransportUp = false; };
            MPAPI.MultiplayerAPI.ClientStarted += OnClientStarted;
            MPAPI.MultiplayerAPI.ClientStopped += () => { _client = null; DleMpChannel.ResetClientState(); };
            // A session may already be live (mod loaded into a running game).
            if (MPAPI.MultiplayerAPI.Server != null) OnServerStarted(MPAPI.MultiplayerAPI.Server);
            if (MPAPI.MultiplayerAPI.Client != null) OnClientStarted(MPAPI.MultiplayerAPI.Client);
            Main.LogAlways("[MpChannel] DVMP packet channel armed.");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void OnServerStarted(MPAPI.Interfaces.IServer server)
        {
            _server = server;
            DleMpChannel.TransportUp = true;
            server.RegisterPacket<DleHelloPacket>((packet, sender) =>
            {
                if (!_dleClients.Contains(sender)) _dleClients.Add(sender);
                var rows = DleMpChannel.SnapshotLiveJobs();
                Main.LogAlways($"[MpChannel] {sender.Username} runs DLE; syncing {rows.Count} live job(s) to them.");
                foreach (var row in rows)
                    SendTo(sender, row.jobId, row.carIds, row.pay, row.unpaid, row.cargo, row.plannedCars, false);
                // The lock state rides along so a client joining mid-session sweeps
                // immediately instead of waiting for the next toggle.
                SendLockTo(sender, AssignmentStore.Instance.LockEnabled);
            });
            server.OnPlayerDisconnected += player => _dleClients.Remove(player);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void OnClientStarted(MPAPI.Interfaces.IClient client)
        {
            _client = client;
            DleMpChannel.ResetClientState();
            client.RegisterPacket<DleJobSyncPacket>(packet =>
                DleMpChannel.ApplyJobSync(packet.JobId ?? "", packet.CarIdsCsv ?? "",
                    packet.Pay, packet.Unpaid, packet.PrintBooklet,
                    packet.Cargo ?? "", packet.PlannedCars));
            client.RegisterPacket<DleLockPacket>(packet =>
                DleMpChannel.ApplyLockState(packet.Enabled));
            Main.LogAlways("[MpChannel] client session started; DLE sync handler registered.");
            // Say hello so the host knows to sync us. A non-DLE host logs one parse
            // warning and drops it; nothing breaks. Re-sent at world load in case this
            // fires before the connection settles.
            SendHello();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void SendHello()
        {
            if (_client == null) return;
            var client = (MPAPI.Interfaces.IClient)_client;
            client.SendPacketToServer(new DleHelloPacket { Version = 1 });
            Main.LogAlways("[MpChannel] hello sent to the host.");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void SendTo(MPAPI.Interfaces.IPlayer player, string jobId, string carIds, float pay, bool unpaid, string cargo, int plannedCars, bool print)
        {
            ((MPAPI.Interfaces.IServer)_server).SendPacketToPlayer(new DleJobSyncPacket
            {
                JobId = jobId,
                CarIdsCsv = carIds ?? "",
                Cargo = cargo ?? "",
                PlannedCars = plannedCars,
                Pay = pay,
                Unpaid = unpaid,
                PrintBooklet = print,
            }, player);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void SendJobSync(string jobId, string carIds, float pay, bool unpaid, string cargo, int plannedCars, string onlyPlayer)
        {
            if (_server == null) return;
            foreach (var obj in _dleClients)
            {
                var player = (MPAPI.Interfaces.IPlayer)obj;
                if (onlyPlayer != null && !string.Equals(player.Username, onlyPlayer, StringComparison.OrdinalIgnoreCase))
                    continue;
                SendTo(player, jobId, carIds, pay, unpaid, cargo, plannedCars, false);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void SendLockTo(MPAPI.Interfaces.IPlayer player, bool enabled)
        {
            ((MPAPI.Interfaces.IServer)_server).SendPacketToPlayer(
                new DleLockPacket { Enabled = enabled }, player);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void SendLock(bool enabled)
        {
            if (_server == null) return;
            foreach (var obj in _dleClients)
                SendLockTo((MPAPI.Interfaces.IPlayer)obj, enabled);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool SendFax(string playerName, string jobId)
        {
            if (_server == null) return false;
            foreach (var obj in _dleClients)
            {
                var player = (MPAPI.Interfaces.IPlayer)obj;
                if (!string.Equals(player.Username, playerName, StringComparison.OrdinalIgnoreCase)) continue;
                StaticDirectHaulJobDefinition.jobDefinitions.TryGetValue(jobId, out var def);
                SendTo(player, jobId, "",
                    def?.deliveryPayment ?? 0f, def?.unpaidMove ?? false,
                    def?.transportedCargo.ToString() ?? "", def?.plannedCarCount ?? 0, true);
                return true;
            }
            return false;
        }
    }

    /// <summary>Client-to-server: "this client runs DLE, sync me." Auto-serialized.</summary>
    public class DleHelloPacket : MPAPI.Interfaces.Packets.IPacket
    {
        public byte Version { get; set; }
    }

    /// <summary>Server-to-client job sync: cargo/count/pay meta always; attached car ids
    /// when cars exist; PrintBooklet makes the client print the paper (fax). The cargo
    /// and planned count ride in OUR packet on purpose: leaning on dv-mp's task sync for
    /// them left client booklets reading "0 loads of ." Auto-serialized.</summary>
    public class DleJobSyncPacket : MPAPI.Interfaces.Packets.IPacket
    {
        public string JobId { get; set; }
        public string CarIdsCsv { get; set; }
        public string Cargo { get; set; }
        public int PlannedCars { get; set; }
        public float Pay { get; set; }
        public bool Unpaid { get; set; }
        public bool PrintBooklet { get; set; }
    }

    /// <summary>Server-to-client: the host's assignment lock state. Sent at hello and on
    /// every toggle; drives the client-side paper sweep. A 0.42.x client receiving this
    /// unknown packet logs one dv-mp parse warning and drops it; nothing breaks (papers
    /// just don't sweep there until the client updates). Auto-serialized.</summary>
    public class DleLockPacket : MPAPI.Interfaces.Packets.IPacket
    {
        public bool Enabled { get; set; }
    }

    /// <summary>
    /// Client-side sweep host: the game respawns office overviews locally every few
    /// seconds while a player is near a station, so the sweep must repeat exactly like
    /// the host director's does. Pure Unity/game types: safe without DVMP (it just never
    /// gets started outside a client session).
    /// </summary>
    public class DleClientPaperSweeper : MonoBehaviour
    {
        private static GameObject _host;

        public static void StartOnClient()
        {
            if (_host != null) Destroy(_host);
            _host = new GameObject("DLE_ClientPaperSweeper");
            DontDestroyOnLoad(_host);
            _host.AddComponent<DleClientPaperSweeper>();
        }

        private System.Collections.IEnumerator Start()
        {
            var wait = new WaitForSeconds(3f);
            while (true)
            {
                yield return wait;
                try { DleMpChannel.SweepClientPapers(); }
                catch (Exception ex) { Main.Log($"[MpChannel] client paper sweep failed: {ex.Message}"); }
            }
        }
    }
}
