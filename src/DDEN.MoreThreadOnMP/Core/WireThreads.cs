using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace DDEN.MoreThreadOnMP.Core
{
    internal readonly struct WirePeer : IEquatable<WirePeer>
    {
        internal readonly uint Handle;
        internal readonly bool Server;
        internal readonly long Lease;
        internal WirePeer(uint handle, bool server, long lease) { Handle = handle; Server = server; Lease = lease; }
        public bool Equals(WirePeer other) => Handle == other.Handle && Server == other.Server && Lease == other.Lease;
        public override bool Equals(object other) => other is WirePeer peer && Equals(peer);
        public override int GetHashCode() => Handle.GetHashCode() ^ Lease.GetHashCode() ^ (Server ? 7919 : 0);
    }
    internal interface IRawWire
    {
        byte[] Receive(WirePeer peer);
        int Send(WirePeer peer, byte[] data, int flags);
        void ReleaseThreadMemory();
    }
    internal sealed class WirePacket
    {
        internal WirePeer Peer;
        internal byte[] Data;
        internal double ArrivedMs;
        internal PreparedGzip Prepared;
        internal byte MessageId => Data.Length == 0 ? (byte)0 : Data[0];
    }
    internal static class WireClock
    { internal static double Now => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency; }

    internal sealed class ReceiveThread : IDisposable
    {
        private readonly object _gate = new object();
        private readonly IRawWire _wire;
        private readonly Thread _thread;
        private readonly Dictionary<WirePeer, Queue<WirePacket>> _queues = new Dictionary<WirePeer, Queue<WirePacket>>();
        private WirePeer[] _peers = Array.Empty<WirePeer>();
        private int _count, _cursor;
        private long _bytes;
        private readonly int _packetLimit;
        private readonly long _byteLimit;
        private bool _stop, _discard;
        private volatile bool _stopped;
        private Exception _fault;
        private long _received;
        private double _nativeMs, _copyPipelinePeakMs;
        internal bool Stopped => _stopped;
        internal int Count { get { lock (_gate) return _count; } }
        internal long Bytes { get { lock (_gate) return _bytes; } }
        internal Exception Fault { get { lock (_gate) return _fault; } }
        internal long Received => Interlocked.Read(ref _received);
        internal double NativeMs { get { lock (_gate) return _nativeMs; } }
        internal double PeakReceiveMs { get { lock (_gate) return _copyPipelinePeakMs; } }
        internal ReceiveThread(IRawWire wire, int packets = 512, long bytes = 8 * 1024 * 1024)
        {
            _wire = wire; _packetLimit = Math.Max(1, packets); _byteLimit = Math.Max(1, bytes);
            _thread = new Thread(Run) { IsBackground = true, Name = "DDEN Steam receive" }; _thread.Start();
        }
        internal void SetPeers(WirePeer[] peers)
        {
            lock (_gate)
            {
                var live = new HashSet<WirePeer>(peers);
                var removed = new List<WirePeer>();
                foreach (var pair in _queues)
                    if (!live.Contains(pair.Key))
                    {
                        foreach (var packet in pair.Value) { _bytes -= packet.Data.Length; _count--; }
                        removed.Add(pair.Key);
                    }
                foreach (var peer in removed) _queues.Remove(peer);
                foreach (var peer in peers) if (!_queues.ContainsKey(peer)) _queues[peer] = new Queue<WirePacket>();
                _peers = (WirePeer[])peers.Clone(); Monitor.PulseAll(_gate);
            }
        }
        internal bool TryTake(WirePeer peer, out WirePacket packet)
        {
            lock (_gate)
            {
                if (!_queues.TryGetValue(peer, out var queue) || queue.Count == 0) { packet = null; return false; }
                packet = queue.Dequeue(); _bytes -= packet.Data.Length; _count--;
                Monitor.PulseAll(_gate); return true;
            }
        }
        private void Run()
        {
            int roleId = ThreadRoles.Register("receive");
            double releaseAt = WireClock.Now + 1000; int idlePeers = 0;
            try
            {
                while (true)
                {
                    WirePeer peer;
                    lock (_gate)
                    {
                        if (_stop) return;
                        if (_peers.Length == 0 || _count >= _packetLimit || _bytes >= _byteLimit)
                        { Monitor.Wait(_gate, 2); continue; }
                        peer = _peers[_cursor++ % _peers.Length];
                        if (_cursor > 1000000) _cursor = 0;
                    }
                    double start = WireClock.Now;
                    byte[] data = _wire.Receive(peer);
                    double elapsed = WireClock.Now - start;
                    lock (_gate) { _nativeMs += elapsed; _copyPipelinePeakMs = Math.Max(_copyPipelinePeakMs, elapsed); }
                    if (data != null)
                    {
                        idlePeers = 0;
                        // One in-flight packet may exceed the byte watermark; no further native receive
                        // happens until space is available. Reliable messages are never dropped on overflow.
                        lock (_gate)
                        {
                            while (!_discard && _queues.ContainsKey(peer) && _count > 0
                                && (_count >= _packetLimit || _bytes + data.Length > _byteLimit)) Monitor.Wait(_gate, 2);
                            if (_discard) return;
                            if (_queues.TryGetValue(peer, out var queue))
                            {
                                queue.Enqueue(new WirePacket { Peer = peer, Data = data, ArrivedMs = WireClock.Now });
                                _count++; _bytes += data.Length; Interlocked.Increment(ref _received);
                            }
                        }
                    }
                    else lock (_gate)
                    {
                        if (++idlePeers >= Math.Max(1, _peers.Length)) { Monitor.Wait(_gate, 1); idlePeers = 0; }
                    }
                    if (WireClock.Now >= releaseAt) { _wire.ReleaseThreadMemory(); releaseAt = WireClock.Now + 1000; }
                }
            }
            catch (Exception error) { lock (_gate) _fault = error; }
            finally
            {
                try { _wire.ReleaseThreadMemory(); } catch (Exception error) { lock (_gate) _fault = error; }
                _stopped = true;
                ThreadRoles.Remove(roleId);
            }
        }
        internal void RequestStop(bool discard = false)
        {
            lock (_gate)
            {
                _stop = true; _discard |= discard;
                if (_discard) { _queues.Clear(); _peers = Array.Empty<WirePeer>(); _bytes = 0; _count = 0; }
                Monitor.PulseAll(_gate);
            }
        }
        public void Dispose()
        {
            RequestStop(true);
            // Lifecycle barrier only: Steam must remain initialized until the last native call returns.
            if (Thread.CurrentThread != _thread) _thread.Join();
        }
    }

    internal enum SendAdmission { Accepted, Full, Stopped }
    internal sealed class SendCompletion
    {
        internal WirePeer Peer;
        internal int Code, Bytes;
        internal Exception Error;
    }
    internal sealed class SendThread : IDisposable
    {
        private sealed class Outgoing { internal WirePeer Peer; internal byte[] Data; internal int Flags; }
        private readonly object _gate = new object();
        private readonly Queue<Outgoing> _queue = new Queue<Outgoing>();
        private readonly Queue<SendCompletion> _results = new Queue<SendCompletion>();
        private readonly IRawWire _wire;
        private readonly Thread _thread;
        private readonly long _limit;
        private readonly int _packetLimit;
        private long _bytes;
        private int _outstanding;
        private bool _stop, _discard, _accept = true;
        private volatile bool _stopped;
        private HashSet<WirePeer> _live = new HashSet<WirePeer>();
        internal bool Stopped => _stopped;
        internal int Count { get { lock (_gate) return _queue.Count; } }
        internal long Bytes { get { lock (_gate) return _bytes; } }
        internal SendThread(IRawWire wire, int packets = 512, long bytes = 8 * 1024 * 1024)
        {
            _wire = wire; _limit = Math.Max(1, bytes); _packetLimit = Math.Max(1, packets);
            _thread = new Thread(Run) { IsBackground = true, Name = "DDEN Steam send" }; _thread.Start();
        }
        internal void SetPeers(WirePeer[] peers) { lock (_gate) _live = new HashSet<WirePeer>(peers); }
        internal SendAdmission Enqueue(WirePeer peer, byte[] ownedData, int flags)
        {
            lock (_gate)
            {
                if (!_accept || _discard) return SendAdmission.Stopped;
                // Include the executing send in the watermark until its completion is consumed.
                if (_outstanding >= _packetLimit || _bytes + ownedData.Length > _limit) return SendAdmission.Full;
                _queue.Enqueue(new Outgoing { Peer = peer, Data = ownedData, Flags = flags });
                _bytes += ownedData.Length; _outstanding++; Monitor.PulseAll(_gate); return SendAdmission.Accepted;
            }
        }
        internal bool TryTake(out SendCompletion result)
        {
            lock (_gate)
            {
                if (_results.Count == 0) { result = null; return false; }
                result = _results.Dequeue(); _bytes -= result.Bytes; _outstanding--; Monitor.PulseAll(_gate); return true;
            }
        }
        private void Run()
        {
            int roleId = ThreadRoles.Register("send"); double releaseAt = WireClock.Now + 1000;
            try
            {
                while (true)
                {
                    Outgoing job;
                    lock (_gate)
                    {
                        while (!_discard && !_stop && _queue.Count == 0) Monitor.Wait(_gate, 1000);
                        if (_discard || (_stop && _queue.Count == 0)) { _accept = false; return; }
                        job = _queue.Dequeue();
                    }
                    var result = new SendCompletion { Peer = job.Peer, Bytes = job.Data.Length };
                    try
                    {
                        bool live; lock (_gate) live = _live.Contains(job.Peer);
                        result.Code = live ? _wire.Send(job.Peer, job.Data, job.Flags) : 3;
                        double deadline = WireClock.Now + 250;
                        while (result.Code == 25 && (job.Flags & 8) != 0 && WireClock.Now < deadline)
                        {
                            lock (_gate) live = !_discard && _live.Contains(job.Peer);
                            if (!live) break;
                            Thread.Sleep(1); // Native LimitExceeded has not accepted this reliable packet.
                            result.Code = _wire.Send(job.Peer, job.Data, job.Flags);
                        }
                    }
                    catch (Exception error) { result.Error = error; }
                    if (WireClock.Now >= releaseAt)
                    {
                        try { _wire.ReleaseThreadMemory(); } catch (Exception error) { result.Error = error; }
                        releaseAt = WireClock.Now + 1000;
                    }
                    lock (_gate) { if (!_discard) _results.Enqueue(result); }
                }
            }
            finally
            {
                try { _wire.ReleaseThreadMemory(); } catch { }
                lock (_gate) { _accept = false; _stopped = true; }
                ThreadRoles.Remove(roleId);
            }
        }
        internal void RequestStop(bool discard = false)
        { lock (_gate) { _stop = true; _discard |= discard; if (_discard) { _accept = false; _queue.Clear(); _results.Clear(); _bytes = 0; } Monitor.PulseAll(_gate); } }
        public void Dispose() { RequestStop(true); if (Thread.CurrentThread != _thread) _thread.Join(); }
        internal void Finish() { RequestStop(); if (Thread.CurrentThread != _thread) _thread.Join(); Dispose(); }
    }
}
