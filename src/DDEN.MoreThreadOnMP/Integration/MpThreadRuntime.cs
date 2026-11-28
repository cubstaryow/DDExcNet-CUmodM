using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using DDEN.MoreThreadOnMP.Core;
using DDEN.Shared;
using UnityEngine;

namespace DDEN.MoreThreadOnMP.Integration
{
    internal sealed class MpThreadRuntime : IDisposable
    {
        private readonly MpBindings _bindings;
        private readonly LogChannel _log;
        private readonly int _mainThread = Thread.CurrentThread.ManagedThreadId;
        private readonly Dictionary<object, MpRoute> _connections = new Dictionary<object, MpRoute>();
        private volatile Dictionary<(uint, bool), WirePeer> _published = new Dictionary<(uint, bool), WirePeer>();
        private List<MpRoute> _routes = new List<MpRoute>();
        private object _transport;
        private int _routeFrame = -1, _roundRobin;
        private long _lease;
        private ReceiveThread _receive;
        private PreparationThread _prepare;
        private SendThread _send;
        private bool _preparedMode, _receiveFailed, _sendFailed, _retryOnEnable;
        private bool _wanted, _wantReceive, _wantSend, _wantPrepare;
        private double _nextErrorLog;
        internal readonly ReceivePacer Pacer = new ReceivePacer(() => WireClock.Now);
        internal PreparedGzip CurrentPrepared { get; private set; }
        internal long DecoderHits, SendErrors, SendFull, SendsCompleted;
        internal double PeakQueueAgeMs;
        internal double BudgetMs = 4;
        internal int MaxPerPeer = 32;
        internal bool Receiving => _receive != null && !_receive.Stopped;
        internal bool Sending => _send != null && !_send.Stopped;
        internal bool Preparing => _prepare != null && !_prepare.Stopped;
        internal int Incoming => (_receive?.Count ?? 0) + (_prepare?.Count ?? 0);
        internal long IncomingBytes => (_receive?.Bytes ?? 0) + (_prepare?.Bytes ?? 0);
        internal int Outgoing => _send?.Count ?? 0;
        internal long OutgoingBytes => _send?.Bytes ?? 0;
        internal long PreparedCount => _prepare?.Prepared ?? 0;
        internal double NativeReceiveMs => _receive?.NativeMs ?? 0;
        internal double PeakReceiveMs => _receive?.PeakReceiveMs ?? 0;
        internal string ReceiveProblem { get; private set; }
        internal string PrepareProblem { get; private set; }
        internal string SendProblem { get; private set; }
        internal MpThreadRuntime(MpBindings bindings, LogChannel log) { _bindings = bindings; _log = log; }
        internal void Configure(bool wanted, bool receive, bool send, bool prepare, double budget, int cap)
        {
            if (wanted && !_retryOnEnable) { _receiveFailed = _sendFailed = false; ReceiveProblem = PrepareProblem = SendProblem = null; }
            _retryOnEnable = wanted;
            _wanted = wanted; _wantReceive = receive; _wantSend = send; _wantPrepare = prepare;
            BudgetMs = budget; MaxPerPeer = cap;
            if (!wanted || !receive || _preparedMode != prepare) _receive?.RequestStop();
            if (!wanted || !send) _send?.RequestStop();
        }
        internal void Tick()
        {
            object live = Mp.IsRunning ? Reflect.GetStatic("Together.Net", "Transport") : null;
            if (!ReferenceEquals(live, _transport)) { ResetSession(); _transport = live; }
            if (live != null && _bindings.TransportType.IsInstanceOfType(live)) Refresh(live);
            if (_receive?.Fault != null)
            {
                ReceiveProblem = _receive.Fault.GetType().Name + ": " + _receive.Fault.Message;
                _receiveFailed = true; _receive.RequestStop(); Report("Receive fallback: " + ReceiveProblem);
            }
            if (_prepare?.Fault != null) { PrepareProblem = _prepare.Fault.GetType().Name + ": " + _prepare.Fault.Message; Report("Decoder fallback: " + PrepareProblem); }
            while (_send != null && _send.TryTake(out var result))
            {
                if (result.Error == null && result.Code == 1) SendsCompleted++;
                else if (result.Error != null || result.Code != 3)
                {
                    SendErrors++; SendProblem = result.Error?.Message ?? ("Steam EResult " + result.Code);
                    _sendFailed = true; _send.RequestStop(); Report("Asynchronous send falling back after queue drain: " + SendProblem);
                }
            }
            if (_send != null && _send.Stopped) { _send.Dispose(); _send = null; }
            RetireReceiver();
        }
        private void RetireReceiver()
        {
            if (_receive == null || !_receive.Stopped || _receive.Count != 0 || (_prepare != null && (!_prepare.Stopped || _prepare.Count != 0))) return;
            _prepare?.Dispose(); _prepare = null; _receive.Dispose(); _receive = null;
        }
        private void Refresh(object transport, bool force = false)
        {
            if (!force && _routeFrame == Time.frameCount) return;
            _routeFrame = Time.frameCount;
            _routes = _bindings.Routes(transport, _connections, ref _lease);
            _connections.Clear();
            var published = new Dictionary<(uint, bool), WirePeer>();
            foreach (var route in _routes) { _connections[route.Connection] = route; published[(route.Peer.Handle, route.Peer.Server)] = route.Peer; }
            _published = published;
            var peers = _routes.Select(route => route.Peer).ToArray();
            _receive?.SetPeers(peers); _prepare?.SetPeers(peers); _send?.SetPeers(peers);
        }
        internal bool Poll(object transport)
        {
            if (Thread.CurrentThread.ManagedThreadId != _mainThread || !Mp.IsRunning) return false;
            if (!ReferenceEquals(transport, _transport)) { ResetSession(); _transport = transport; }
            Refresh(transport, true);
            RetireReceiver();
            if (_receive == null && _wanted && _wantReceive && !_receiveFailed && _routes.Count > 0)
            {
                bool server = _routes[0].Peer.Server;
                _receive = new ReceiveThread(new SteamRawWire(server));
                _preparedMode = _wantPrepare;
                if (_preparedMode) _prepare = new PreparationThread(_receive);
                Refresh(transport, true);
            }
            if (_receive == null) return false;
            var source = _receive; var preparation = _prepare;
            Pacer.Begin(BudgetMs, MaxPerPeer);
            try
            {
                var routes = _routes.ToArray();
                int offset = routes.Length == 0 ? 0 : _roundRobin++ % routes.Length;
                if (_roundRobin > 1000000) _roundRobin = 0;
                for (int n = 0; n < routes.Length; n++)
                {
                    var route = routes[(n + offset) % routes.Length];
                    int cap = Pacer.Limit(MaxPerPeer);
                    for (int i = 0; i < cap; i++)
                    {
                        if (!ReferenceEquals(_transport, transport) || !ReferenceEquals(source, _receive)) return true;
                        // A message may remove its own or another connection. No repeated collection rebuilding.
                        if (!_bindings.IsLive(transport, route)) break;
                        bool ready = preparation != null ? preparation.TryTake(route.Peer, out var packet) : source.TryTake(route.Peer, out packet);
                        if (!ready) break;
                        PeakQueueAgeMs = Math.Max(PeakQueueAgeMs, WireClock.Now - packet.ArrivedMs);
                        _bindings.NoteIncoming(packet.Data.Length);
                        double started = WireClock.Now;
                        try { CurrentPrepared = packet.Prepared; _bindings.Dispatch(transport, route.Connection, packet.Data); }
                        catch (Exception error) { Report("MP receive handler: " + error); }
                        finally { CurrentPrepared = null; Pacer.Message(WireClock.Now - started); }
                        if (WireClock.Now - started >= BudgetMs) break;
                        // Re-evaluate the soft frame budget after every handler, not only per native batch.
                        if (i > 0 && Pacer.Limit(MaxPerPeer) == 1) break;
                    }
                }
                return true;
            }
            finally { Pacer.End(); }
        }
        internal bool QueueSend(uint handle, bool server, IntPtr pointer, uint size, int flags, out int result)
        {
            result = 0;
            if (Thread.CurrentThread.ManagedThreadId == _mainThread && _transport != null)
            {
                Refresh(_transport);
                if (!_published.ContainsKey((handle, server))) Refresh(_transport, true);
                if (_send == null && _wanted && _wantSend && !_sendFailed && _published.ContainsKey((handle, server)))
                { _send = new SendThread(new SteamRawWire(server)); _send.SetPeers(_routes.Select(route => route.Peer).ToArray()); }
            }
            var sender = _send;
            if (sender == null || !_published.TryGetValue((handle, server), out var peer)) return false;
            var owned = new byte[checked((int)size)];
            if (owned.Length > 0) Marshal.Copy(pointer, owned, 0, owned.Length);
            var admission = sender.Enqueue(peer, owned, flags);
            if (admission == SendAdmission.Stopped) return false; // All earlier queued sends have completed.
            if (admission == SendAdmission.Full)
            {
                Interlocked.Increment(ref SendFull);
                SendProblem = "Send queue full (Steam LimitExceeded)";
                _sendFailed = true; sender.RequestStop();
                result = 25; // LimitExceeded: retain MP's normal backpressure/error handling; never overtake queued packets.
            }
            else result = 1;
            return true;
        }
        private void Report(string message)
        { if (WireClock.Now >= _nextErrorLog) { _nextErrorLog = WireClock.Now + 5000; _log.Warn(message); } }
        internal void ResetSession()
        {
            // Discard only when the native transport itself is being reset/disposed, never on a config toggle.
            _prepare?.Dispose(); _prepare = null;
            _receive?.Dispose(); _receive = null;
            _send?.Finish(); _send = null;
            _connections.Clear(); _routes.Clear(); _published = new Dictionary<(uint, bool), WirePeer>();
            _transport = null; _routeFrame = -1; CurrentPrepared = null; Pacer.Cancel();
        }
        public void Dispose() { ResetSession(); }
    }
}
