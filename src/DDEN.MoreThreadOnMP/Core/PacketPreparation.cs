using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;

namespace DDEN.MoreThreadOnMP.Core
{
    internal sealed class PreparedGzip
    {
        internal byte[] Compressed, Expanded;
        internal bool Matches(byte[] bytes)
        {
            if (bytes == null || bytes.Length != Compressed.Length) return false;
            for (int i = 0; i < bytes.Length; i++) if (bytes[i] != Compressed[i]) return false;
            return true;
        }
    }
    internal static class PacketPreparation
    {
        internal const int MaxExpanded = 2 * 1024 * 1024;
        // Recognize the native ushort-length gzip envelope. Never change any bytes in the packet.
        // A miss, malformed gzip, large expansion, or different layout simply uses MP's own decoder.
        internal static PreparedGzip Prepare(byte[] packet)
        {
            if (packet == null) return null;
            for (int start = 3; start < Math.Min(packet.Length - 18, 35); start++)
            {
                if (packet[start] != 0x1f || packet[start + 1] != 0x8b || packet[start + 2] != 8) continue;
                int count = packet[start - 2] | (packet[start - 1] << 8);
                if (count < 18 || count > packet.Length - start) continue;
                try
                {
                    using (var source = new MemoryStream(packet, start, count, false))
                    using (var gzip = new GZipStream(source, CompressionMode.Decompress))
                    using (var target = new MemoryStream())
                    {
                        var scratch = new byte[4096]; int read;
                        while ((read = gzip.Read(scratch, 0, scratch.Length)) != 0)
                        {
                            if (target.Length + read > MaxExpanded) return null;
                            target.Write(scratch, 0, read);
                        }
                        var compressed = new byte[count]; Buffer.BlockCopy(packet, start, compressed, 0, count);
                        return new PreparedGzip { Compressed = compressed, Expanded = target.ToArray() };
                    }
                }
                catch (InvalidDataException) { return null; }
                catch (IOException) { return null; }
            }
            return null;
        }
    }
    internal sealed class PreparationThread : IDisposable
    {
        private readonly object _gate = new object();
        private readonly ReceiveThread _source;
        private readonly Thread _thread;
        private readonly Dictionary<WirePeer, Queue<WirePacket>> _ready = new Dictionary<WirePeer, Queue<WirePacket>>();
        private WirePeer[] _peers = Array.Empty<WirePeer>();
        private int _cursor, _count;
        private long _bytes, _prepared;
        private bool _discard;
        private volatile bool _stopped;
        private Exception _fault;
        internal bool Stopped => _stopped;
        internal int Count { get { lock (_gate) return _count; } }
        internal long Bytes { get { lock (_gate) return _bytes; } }
        internal long Prepared => Interlocked.Read(ref _prepared);
        internal Exception Fault { get { lock (_gate) return _fault; } }
        internal PreparationThread(ReceiveThread source)
        {
            _source = source;
            _thread = new Thread(Run) { IsBackground = true, Name = "DDEN MP packet preparation" }; _thread.Start();
        }
        private static int Size(WirePacket packet) => packet.Data.Length + (packet.Prepared?.Expanded.Length ?? 0) + (packet.Prepared?.Compressed.Length ?? 0);
        internal void SetPeers(WirePeer[] peers)
        {
            lock (_gate)
            {
                var live = new HashSet<WirePeer>(peers); var removed = new List<WirePeer>();
                foreach (var pair in _ready) if (!live.Contains(pair.Key))
                {
                    foreach (var packet in pair.Value) { _bytes -= Size(packet); _count--; }
                    removed.Add(pair.Key);
                }
                foreach (var peer in removed) _ready.Remove(peer);
                foreach (var peer in peers) if (!_ready.ContainsKey(peer)) _ready[peer] = new Queue<WirePacket>();
                _peers = (WirePeer[])peers.Clone(); Monitor.PulseAll(_gate);
            }
        }
        internal bool TryTake(WirePeer peer, out WirePacket packet)
        {
            lock (_gate)
            {
                if (!_ready.TryGetValue(peer, out var queue) || queue.Count == 0) { packet = null; return false; }
                packet = queue.Dequeue(); _bytes -= Size(packet); _count--; Monitor.PulseAll(_gate); return true;
            }
        }
        private void Run()
        {
            int roleId = ThreadRoles.Register("prepare"); int idlePeers = 0;
            try
            {
                while (true)
                {
                    WirePeer peer;
                    lock (_gate)
                    {
                        if (_discard) return;
                        if (_count >= 128 || _bytes >= 8 * 1024 * 1024) { Monitor.Wait(_gate, 2); continue; }
                        if (_source.Stopped && _source.Count == 0) return;
                        if (_peers.Length == 0) { Monitor.Wait(_gate, 2); continue; }
                        peer = _peers[_cursor++ % _peers.Length]; if (_cursor > 1000000) _cursor = 0;
                    }
                    if (!_source.TryTake(peer, out var packet))
                    {
                        lock (_gate) { if (++idlePeers >= Math.Max(1, _peers.Length)) { Monitor.Wait(_gate, 1); idlePeers = 0; } }
                        continue;
                    }
                    idlePeers = 0;
                    if (Fault == null)
                    {
                        try { packet.Prepared = PacketPreparation.Prepare(packet.Data); }
                        catch (Exception error) { lock (_gate) _fault = error; } // Publish raw data; native decoding remains available.
                    }
                    if (packet.Prepared != null) Interlocked.Increment(ref _prepared);
                    lock (_gate)
                    {
                        while (!_discard && _ready.ContainsKey(peer) && _count > 0 && _bytes + Size(packet) > 8 * 1024 * 1024) Monitor.Wait(_gate, 2);
                        if (_discard) return;
                        if (_ready.TryGetValue(peer, out var queue)) { queue.Enqueue(packet); _bytes += Size(packet); _count++; }
                    }
                }
            }
            catch (Exception error) { lock (_gate) _fault = error; }
            finally { _stopped = true; ThreadRoles.Remove(roleId); }
        }
        public void Dispose()
        {
            lock (_gate) { _discard = true; _ready.Clear(); _peers = Array.Empty<WirePeer>(); _bytes = 0; _count = 0; Monitor.PulseAll(_gate); }
            if (Thread.CurrentThread != _thread) _thread.Join();
        }
    }
}
