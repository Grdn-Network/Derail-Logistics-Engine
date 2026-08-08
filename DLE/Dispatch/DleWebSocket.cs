using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace DLE.Dispatch
{
    /// <summary>
    /// A minimal server-side WebSocket (RFC 6455), ported from the RemoteDispatch fork's
    /// RawWebSocket (owner: "steal the websocket we built in our RD version"). RD had to
    /// fish the connection stream out of Mono's broken HttpListener by reflection; DLE's
    /// server is its own TcpListener, so the stream is simply ours and only the framing
    /// travels. The board subscribes here and the host PUSHES the rails payloads when
    /// they change, which replaces five second polling with sub second updates and no
    /// wasted identical responses.
    ///
    /// Threading: each client owns a receive thread (close handshake, ping/pong) and all
    /// writes go through a per-client lock. Broadcasts run on the thread pool; the game's
    /// main thread only serializes payloads and hands off bytes, never touches a socket.
    /// </summary>
    internal static class WsHub
    {
        private const string WsGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        private sealed class WsClient
        {
            public TcpClient Tcp;
            public Stream Stream;
            public readonly object WriteLock = new object();
            public volatile bool Dead;
        }

        private static readonly List<WsClient> _clients = new List<WsClient>();

        public static bool HasClients { get { lock (_clients) return _clients.Count > 0; } }
        public static int Count { get { lock (_clients) return _clients.Count; } }

        /// <summary>Raised (on a worker thread) when a client joins, so the next push
        /// tick can send a full snapshot rather than waiting for something to change.</summary>
        public static volatile bool SnapshotWanted;

        /// <summary>
        /// Complete the upgrade and adopt the socket. Returns false when the request is
        /// not a well-formed upgrade, in which case the caller still owns the socket.
        /// </summary>
        public static bool Attach(TcpClient tcp, Stream stream, string secWebSocketKey)
        {
            if (string.IsNullOrEmpty(secWebSocketKey)) return false;
            string accept;
            using (var sha1 = SHA1.Create())
                accept = Convert.ToBase64String(sha1.ComputeHash(Encoding.ASCII.GetBytes(secWebSocketKey + WsGuid)));
            var handshake =
                "HTTP/1.1 101 Switching Protocols\r\n" +
                "Upgrade: websocket\r\n" +
                "Connection: Upgrade\r\n" +
                "Sec-WebSocket-Accept: " + accept + "\r\n\r\n";
            var bytes = Encoding.ASCII.GetBytes(handshake);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();

            // A live feed never times out on reads; writes stay bounded so one stuck
            // client cannot wedge a broadcast for long.
            tcp.ReceiveTimeout = 0;
            tcp.SendTimeout = 8000;
            try { tcp.NoDelay = true; } catch { }

            var c = new WsClient { Tcp = tcp, Stream = stream };
            lock (_clients) _clients.Add(c);
            SnapshotWanted = true;
            Main.LogAlways($"[Ws] board subscribed ({Count} live).");

            new Thread(() => ReceiveLoop(c)) { IsBackground = true, Name = "DLE-Ws-Recv" }.Start();
            new Thread(() => KeepAliveLoop(c)) { IsBackground = true, Name = "DLE-Ws-Ping" }.Start();
            return true;
        }

        /// <summary>Send one text frame to every live client. Never call on the main
        /// thread: writes block. The push tick queues this onto the pool.</summary>
        public static void BroadcastText(string json)
        {
            var payload = Encoding.UTF8.GetBytes(json);
            WsClient[] snap;
            lock (_clients) snap = _clients.ToArray();
            foreach (var c in snap)
            {
                if (c.Dead) continue;
                try { SendFrame(c, 0x1, payload); }
                catch { Drop(c); }
            }
        }

        public static void CloseAll()
        {
            WsClient[] snap;
            lock (_clients) { snap = _clients.ToArray(); _clients.Clear(); }
            foreach (var c in snap)
            {
                try { SendFrame(c, 0x8, Array.Empty<byte>()); } catch { }
                try { c.Stream.Dispose(); } catch { }
                try { c.Tcp.Close(); } catch { }
            }
        }

        private static void Drop(WsClient c)
        {
            bool removed;
            lock (_clients) removed = _clients.Remove(c);
            c.Dead = true;
            try { c.Stream.Dispose(); } catch { }
            try { c.Tcp.Close(); } catch { }
            if (removed) Main.Log($"[Ws] board unsubscribed ({Count} live).");
        }

        // Reads incoming frames so the close handshake works and pings are answered.
        // Client-to-server data frames are ignored: actions still travel over HTTP.
        private static void ReceiveLoop(WsClient c)
        {
            try
            {
                while (!c.Dead)
                {
                    var frame = ReadFrame(c);
                    if (frame == null) break;
                    int opcode = frame.Value.opcode;
                    if (opcode == 0x8) break;
                    if (opcode == 0x9) SendFrame(c, 0xA, frame.Value.payload);
                }
            }
            catch { }
            finally { Drop(c); }
        }

        private static void KeepAliveLoop(WsClient c)
        {
            try
            {
                while (!c.Dead)
                {
                    Thread.Sleep(30000);
                    if (c.Dead) break;
                    SendFrame(c, 0x9, Array.Empty<byte>());
                }
            }
            catch { Drop(c); }
        }

        // Server-to-client frames are never masked.
        private static void SendFrame(WsClient c, int opcode, byte[] payload)
        {
            var header = new byte[10];
            int n = 0;
            header[n++] = (byte)(0x80 | (opcode & 0x0F));
            int len = payload.Length;
            if (len < 126) header[n++] = (byte)len;
            else if (len <= 0xFFFF) { header[n++] = 126; header[n++] = (byte)(len >> 8); header[n++] = (byte)len; }
            else { header[n++] = 127; for (int i = 7; i >= 0; i--) header[n++] = (byte)((long)len >> (8 * i)); }
            lock (c.WriteLock)
            {
                c.Stream.Write(header, 0, n);
                if (len > 0) c.Stream.Write(payload, 0, len);
                c.Stream.Flush();
            }
        }

        private static (int opcode, byte[] payload)? ReadFrame(WsClient c)
        {
            var h = ReadExactly(c, 2);
            if (h == null) return null;
            int opcode = h[0] & 0x0F;
            bool masked = (h[1] & 0x80) != 0;
            long len = h[1] & 0x7F;
            if (len == 126)
            {
                var e = ReadExactly(c, 2);
                if (e == null) return null;
                len = (e[0] << 8) | e[1];
            }
            else if (len == 127)
            {
                var e = ReadExactly(c, 8);
                if (e == null) return null;
                len = 0;
                for (int i = 0; i < 8; i++) len = (len << 8) | e[i];
            }
            if (len > 64 * 1024) return null;   // the board never sends anything big
            byte[] mask = null;
            if (masked)
            {
                mask = ReadExactly(c, 4);
                if (mask == null) return null;
            }
            var payload = len > 0 ? ReadExactly(c, (int)len) : Array.Empty<byte>();
            if (payload == null) return null;
            if (mask != null)
                for (int i = 0; i < payload.Length; i++) payload[i] ^= mask[i % 4];
            return (opcode, payload);
        }

        private static byte[] ReadExactly(WsClient c, int count)
        {
            var buf = new byte[count];
            int read = 0;
            while (read < count)
            {
                int r = c.Stream.Read(buf, read, count - read);
                if (r <= 0) return null;
                read += r;
            }
            return buf;
        }
    }
}
