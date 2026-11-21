using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DDEN.MPfix
{
    internal readonly struct AuthorityPeer
    {
        internal readonly object Connection;
        internal readonly bool Running, Server;
        internal AuthorityPeer(object connection, bool running, bool server)
        { Connection = connection; Running = running && connection != null; Server = server; }
        internal bool Same(AuthorityPeer other) => Running == other.Running && Server == other.Server && ReferenceEquals(Connection, other.Connection);
    }

    /// <summary>Event driven: join/opt-in requests, host dirty notifications and bounded transport retries. No steady-state polling.</summary>
    internal sealed class HostConfigSync
    {
        private readonly AuthorityOverlay _overlay;
        private readonly Func<bool> _opt;
        private readonly Func<AuthorityPeer> _peer;
        private readonly Func<double> _clock;
        private readonly Func<JObject, bool> _notify;
        private readonly Func<JObject, Action<JToken>, bool> _request;
        private readonly Action<string> _log;
        private AuthorityPeer _current;
        private bool _initialized, _lastOpt, _dirty, _send, _pull, _pending, _again, _stopped;
        private double _dirtyAt, _sendAt, _pullAt, _timeout, _lastRequest = -100;
        private int _serial, _tries;
        private string _session, _fingerprint;
        private long _revision;
        private AuthoritySnapshot _host, _received;
        internal const string NoticeChannel = "ddn.mp.config.notice.v1";
        internal const string RequestChannel = "ddn.mp.config.get.v1";
        internal bool Locked => _overlay.Locked && _opt() && CurrentClient();
        private bool CurrentClient() { var p = _peer(); return p.Running && !p.Server && p.Same(_current); }
        internal string Notice
        {
            get
            {
                if (_stopped) return "";
                var p = _peer();
                if (!p.Running || !_opt()) return "";
                if (p.Server) return "（主机权威模式）主机配置可编辑";
                if (Locked) return "（主机权威模式）正在采用主机配置";
                return _received != null && !_received.Enabled ? "主机未启用权威配置" : "等待主机配置";
            }
        }
        internal HostConfigSync(AuthorityOverlay overlay, Func<bool> opt, Func<AuthorityPeer> peer, Func<double> clock,
            Func<JObject, bool> notify, Func<JObject, Action<JToken>, bool> request, Action<string> log)
        { _overlay = overlay; _opt = opt; _peer = peer; _clock = clock; _notify = notify; _request = request; _log = log; }

        internal void Tick()
        {
            if (_stopped) return;
            double now = _clock(); var peer = _peer(); bool opt = _opt();
            if (!_initialized || !_current.Same(peer))
            {
                _initialized = true; _current = peer; _lastOpt = opt;
                Reset();
                if (peer.Running && peer.Server) { _session = Guid.NewGuid().ToString("N"); _dirty = true; _dirtyAt = now; }
                else if (peer.Running && opt) SchedulePull(now);
            }
            else if (opt != _lastOpt)
            {
                _lastOpt = opt; _serial++; _pending = _pull = _again = false;
                _overlay.Release(); _received = null;
                if (peer.Running && peer.Server) { _dirty = true; _dirtyAt = now; }
                else if (peer.Running && opt) SchedulePull(now);
            }
            if (!peer.Running) return;
            if (peer.Server)
            {
                if (_dirty && now >= _dirtyAt) BuildHost();
                if (_send && now >= _sendAt)
                { _send = false; if (!_notify(_host.ToJson(false))) { _send = true; _sendAt = now + 1; } }
                return;
            }
            if (!opt) return;
            if (_pending && now >= _timeout)
            { _pending = false; _serial++; _pull = _tries > 0; _pullAt = now + 1; }
            if (!_pending && _pull && now >= _pullAt && _tries > 0) Pull(now);
        }
        private void Reset()
        {
            _serial++; _pending = _pull = _again = _dirty = _send = false; _tries = 0;
            _host = _received = null; _fingerprint = _session = null; _revision = 0; _lastRequest = -100;
            _overlay.Release();
        }
        internal void Stop() { Reset(); _initialized = false; _stopped = true; }
        internal void LocalChanged()
        {
            if (_stopped) return;
            Tick();
            if (_current.Running && _current.Server) { _dirty = true; _dirtyAt = _clock() + 0.25; }
        }
        private void BuildHost()
        {
            var next = _overlay.Capture(_session, _revision + 1, _opt());
            // Revision itself is not a setting change.
            var normalized = next.ToJson(true); normalized.Remove("revision");
            string fingerprint = normalized.ToString(Formatting.None);
            _dirty = false;
            if (_host != null && fingerprint == _fingerprint) return;
            _host = next; _revision = next.Revision; _fingerprint = fingerprint; _send = true; _sendAt = _clock();
        }
        internal JToken HandleRequest(JToken token)
        {
            if (_stopped) return null;
            Tick();
            if (!_current.Running || !_current.Server || !HasProtocol(token) ||
                token["nonce"]?.Type != JTokenType.String || !Guid.TryParseExact(token.Value<string>("nonce"), "N", out _)) return null;
            if (_dirty || _host == null) BuildHost();
            var response = _host.ToJson(true); response["nonce"] = token.Value<string>("nonce"); return response;
        }
        internal void OnNotice(JToken token)
        {
            if (_stopped) return;
            Tick();
            if (!CurrentClient() || !_opt() || !HasProtocol(token)) return;
            // Broadcasts can be relayed by peers. They only request a refresh, never supply authority or values.
            if (_pending) { _again = true; return; }
            SchedulePull(Math.Max(_clock(), _lastRequest + 1));
        }
        private void SchedulePull(double at) { _pull = true; _tries = 3; _pullAt = at; }
        private static bool HasProtocol(JToken token)
        {
            try { return token is JObject && token["protocol"]?.Type == JTokenType.Integer && token.Value<int>("protocol") == AuthoritySnapshot.Protocol; }
            catch { return false; }
        }
        private void Pull(double now)
        {
            _pull = false; _pending = true; _timeout = now + 3; _lastRequest = now; _tries--;
            int serial = ++_serial; var peer = _current; string nonce = Guid.NewGuid().ToString("N");
            bool sent = _request(new JObject { ["protocol"] = AuthoritySnapshot.Protocol, ["nonce"] = nonce }, response =>
            {
                if (serial != _serial || !_opt() || !peer.Same(_peer()) || !CurrentClient()) return;
                _pending = false;
                AuthoritySnapshot snapshot = null;
                bool valid = response is JObject o && o["nonce"]?.Type == JTokenType.String && o.Value<string>("nonce") == nonce && AuthoritySnapshot.TryRead(o, out snapshot);
                if (valid)
                {
                    if (_overlay.Apply(snapshot)) { _received = snapshot; _log?.Invoke(snapshot.Enabled ? "已临时采用主机配置（主机权威模式）。" : "主机已关闭权威配置，恢复本机设置。"); }
                    else if (!_overlay.Locked && snapshot.Enabled) _log?.Invoke("主机配置未能应用，请核对两端模组修订。");
                    _tries = 0;
                }
                if (_again) { _again = false; SchedulePull(Math.Max(_clock(), _lastRequest + 1)); }
                else if (!valid && _tries > 0) { _pull = true; _pullAt = _clock() + 1; }
            });
            if (!sent) { _pending = false; _pull = _tries > 0; _pullAt = now + 1; }
        }
    }
}
