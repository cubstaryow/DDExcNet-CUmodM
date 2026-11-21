using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CageFix
{
    internal static class CageNetwork
    {
        private const string Get = "cagefix.state.get.v1", Notice = "cagefix.state.notice.v1";
        private static Type _api, _trackerType;
        private static PropertyInfo _clientManager, _serverManager;
        private static FieldInfo _sync, _id, _version, _go;
        private static MethodInfo _request, _broadcast;
        private static object _connection;
        private static WorldGeneration _world;
        private static int _layer, _generation;
        private static string _nonce;
        private static float _nextRequest, _deadline, _noticeAt, _lastNotice = -10;
        private static bool _hostDirty, _applying;
        private static readonly Func<JToken, JToken> ReadHandler = Read;
        private static readonly Action<JToken> NoticeHandler = OnNotice;
        private static bool Enabled => Plugin.Active && Plugin.Network.Value && Plugin.NetworkReady;
        private static bool Flag(string name) => _api?.GetProperty(name, BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is bool value && value;
        private static bool Server => Flag("IsServer");
        private static bool Client => Flag("IsClient") && !Server;
        internal static Type FindType(string assembly, string simpleName)
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == assembly);
            if (asm == null) return null;
            try { return asm.GetTypes().FirstOrDefault(t => t.Name == simpleName); }
            catch (ReflectionTypeLoadException ex) { return ex.Types.FirstOrDefault(t => t?.Name == simpleName); }
        }
        internal static void Install()
        {
            _api = AccessTools.TypeByName("CUCoreLib.Networking.MultiplayerApi");
            var bridge = AccessTools.TypeByName("CUCoreLib.Networking.MultiplayerBridge");
            var net = AccessTools.TypeByName("Together.Net");
            var info = AccessTools.TypeByName("Together.SyncInfo");
            _trackerType = AccessTools.TypeByName("Together.SyncInfoGameObjectTracker");
            _clientManager = AccessTools.Property(net, "ClientEntityManager");
            _serverManager = AccessTools.Property(net, "ServerEntityManager");
            _sync = AccessTools.Field(_trackerType, "syncInfo");
            _id = AccessTools.Field(info, "Id"); _version = AccessTools.Field(info, "Version"); _go = AccessTools.Field(info, "go");
            _request = AccessTools.Method(bridge, "RequestServer", new[] { typeof(string), typeof(object), typeof(Action<JToken>), typeof(bool) });
            _broadcast = AccessTools.Method(bridge, "Broadcast", new[] { typeof(string), typeof(object), typeof(bool), typeof(bool) });
            var registerServer = AccessTools.Method(bridge, "RegisterServerHandler", new[] { typeof(string), typeof(Func<JToken, JToken>) });
            var registerClient = AccessTools.Method(bridge, "RegisterClientHandler", new[] { typeof(string), typeof(Action<JToken>) });
            if (_api == null || _trackerType == null || _clientManager == null || _serverManager == null || _sync == null
                || _id == null || _version == null || _go == null || _request == null || _broadcast == null || registerClient == null || registerServer == null)
                throw new MissingMemberException("需要兼容的 Together 与 CUCoreLib 联机接口");
            registerServer.Invoke(null, new object[] { Get, ReadHandler });
            registerClient.Invoke(null, new object[] { Notice, NoticeHandler });
        }
        private static CageKey? Identity(CageState state)
        {
            if (state == null || state.Item == null) return null;
            if (state.Tracker == null) state.Tracker = state.Item.GetComponent(_trackerType);
            if (state.Tracker == null) return null;
            object binding = _sync.GetValue(state.Tracker);
            if (binding == null || (_go.GetValue(binding) as GameObject) != state.Item.gameObject) return null;
            ushort id = Convert.ToUInt16(_id.GetValue(binding));
            return id == 0 ? null : new CageKey(id, Convert.ToByte(_version.GetValue(binding)));
        }
        internal static void Changed(CageState state)
        {
            if (!Enabled || _applying) return;
            state.NeedsSync = true; state.Attempts = 0;
            if (Server) { if (!_hostDirty) _noticeAt = Time.unscaledTime + .15f; _hostDirty = true; }
            else if (Client) _nonce = null; // An old in-flight answer cannot undo a later local use.
        }
        private static void OnNotice(JToken ignored)
        {
            try
            {
                if (!Enabled || !Client || Time.unscaledTime - _lastNotice < .5f) return;
                _lastNotice = Time.unscaledTime;
                // Broadcasts can be forwarded by clients. They only trigger a host query, never apply values.
                foreach (var state in CageRuntime.States) if (state != null) { state.NeedsSync = true; state.Attempts = 0; }
            }
            catch (Exception ex) { Plugin.Debug("状态通知已忽略：" + ex.Message); }
        }
        private static JToken Read(JToken request)
        {
            try
            {
                if (!Server) return new JObject();
                var lookup = new Dictionary<CageKey, CageState>();
                foreach (var state in CageRuntime.States)
                {
                    var key = Identity(state);
                    if (key.HasValue) lookup[key.Value] = state;
                }
                return CageWire.Reply(request, Enabled, key => lookup.TryGetValue(key, out var state) ? (bool?)CageRuntime.IsOn(state.Item) : null);
            }
            catch (Exception ex) { Plugin.Debug("主机状态查询失败：" + ex.Message); return new JObject(); }
        }
        internal static void Tick()
        {
            if (!Enabled || !Flag("IsRunning")) { if (_connection != null || _nonce != null) Reset(); return; }
            var world = WorldGeneration.world;
            object connection = (Server ? _serverManager : _clientManager).GetValue(null);
            int layer = world != null ? world.biomeDepth : int.MinValue;
            if (!ReferenceEquals(connection, _connection) || _world != world || _layer != layer)
            { Reset(); _connection = connection; _world = world; _layer = layer; }
            if (connection == null || world == null || !world.worldExists || world.generatingWorld) return;
            float now = Time.unscaledTime;
            if (Server)
            {
                if (_hostDirty && now >= _noticeAt)
                {
                    _hostDirty = false;
                    _broadcast.Invoke(null, new object[] { Notice, new JObject { ["version"] = 1 }, false, true });
                }
                return;
            }
            if (!Client) return;
            foreach (var state in CageRuntime.States)
            {
                var key = Identity(state);
                if (!Nullable.Equals(key, state.Key)) { state.Key = key; state.NeedsSync = true; state.Attempts = 0; }
            }
            if (_nonce != null && now < _deadline || now < _nextRequest) return;
            _nonce = null;
            var selected = CageRuntime.States.Where(s => s != null && s.Key.HasValue && s.NeedsSync && s.Attempts < 3).Take(CageWire.Batch).ToArray();
            if (selected.Length == 0) return;
            var expected = new HashSet<CageKey>(selected.Select(s => s.Key.Value));
            var leases = selected.ToDictionary(s => s, s => s.Key.Value);
            string nonce = _nonce = System.Guid.NewGuid().ToString("N");
            int generation = _generation;
            _deadline = now + 3; _nextRequest = now + .5f;
            foreach (var state in selected) state.Attempts++;
            bool sent = _request.Invoke(null, new object[] { Get, CageWire.Request(nonce, expected), (Action<JToken>)(response =>
            {
                try
                {
                    if (!Enabled || !Client || generation != _generation || nonce != _nonce
                        || !ReferenceEquals(_clientManager.GetValue(null), _connection) || _world != WorldGeneration.world
                        || _world == null || !_world.worldExists || _world.generatingWorld || _world.biomeDepth != _layer) return;
                    _nonce = null;
                    if (!CageWire.Response(response, nonce, expected, out var values, out bool enabled)) return;
                    _applying = true;
                    try
                    {
                        foreach (var state in selected)
                        {
                            if (state == null || !Nullable.Equals(Identity(state), (CageKey?)leases[state]) || !Nullable.Equals(state.Key, (CageKey?)leases[state])) continue;
                            if (!enabled) { state.NeedsSync = false; continue; }
                            if (values.TryGetValue(state.Key.Value, out bool on)) { CageRuntime.Set(state, on); state.NeedsSync = false; }
                        }
                    }
                    finally { _applying = false; }
                }
                catch (Exception ex) { Plugin.Debug("主机状态回复已忽略：" + ex.Message); }
            }), true }) is bool ok && ok;
            if (!sent) _nonce = null;
        }
        internal static void Reset()
        {
            _generation++; _connection = null; _world = null; _nonce = null; _nextRequest = 0; _hostDirty = false;
            foreach (var state in CageRuntime.States) if (state != null) { state.NeedsSync = true; state.Attempts = 0; state.Key = null; }
            if (Enabled && Server) { _hostDirty = true; _noticeAt = Time.unscaledTime + .15f; }
        }
    }
}
