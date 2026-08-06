using DLE.Jobs;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace DLE.Dispatch
{
    // This assembly exists ONLY to hold the types that touch MultiplayerAPI. Its packet
    // classes implement an MPAPI interface, so they cannot load when the Multiplayer mod
    // is absent, and UMM enumerates every type in a mod assembly: keeping them in the
    // core killed the whole mod for anyone without Multiplayer (#163). DLE loads this
    // file by reflection only after MultiplayerAPI is confirmed present.
    //
    // The packet type NAMES and NAMESPACE must not change: LiteNetLib keys packets by
    // type full name, so DLE.Dispatch.DleJobSyncPacket here is wire-identical to the one
    // that shipped inside the core assembly.

    // Everything below touches MultiplayerAPI types INSIDE METHOD BODIES ONLY (fields
    // hold object), so the CLR resolves MultiplayerAPI.dll on first call, never on type
    // load. Do not add MPAPI types to any field or method signature here.

    public static class DleMpTransport
    {
        private static object _server;                       // IServer while hosting
        private static object _client;                       // IClient while connected
        private static readonly List<object> _dleClients = new List<object>(); // IPlayer

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Init()
        {
            // Hand the core its send functions; it holds no MPAPI type of its own.
            DleMpChannel.SendHelloFn = SendHello;
            DleMpChannel.SendJobSyncFn = SendJobSync;
            DleMpChannel.SendLockFn = SendLock;
            DleMpChannel.SendFaxFn = SendFax;
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
        public static void SendHello()
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
        public static void SendJobSync(string jobId, string carIds, float pay, bool unpaid, string cargo, int plannedCars, string onlyPlayer)
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
        public static void SendLock(bool enabled)
        {
            if (_server == null) return;
            foreach (var obj in _dleClients)
                SendLockTo((MPAPI.Interfaces.IPlayer)obj, enabled);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool SendFax(string playerName, string jobId)
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

}
