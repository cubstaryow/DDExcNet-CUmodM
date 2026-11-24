using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace DDEN.Shared
{
    /// <summary>
    /// CUCoreLib 联机桥的薄封装：只在这里碰 CUCoreLib 的公开 API，
    /// 其余代码用 null 安全的方式说话（没装 CUCoreLib / 没联机时静默降级）。
    /// </summary>
    public static class Mp
    {
        private static Func<bool> _initialized, _forceDisabled;
        private static Func<bool> _running, _client, _server, _bridgeReady, _coreAvailable;
        private static readonly Dictionary<string, MethodInfo> Methods = new Dictionary<string, MethodInfo>();
        private static bool? Flag(ref Func<bool> read, string typeName, string name)
        {
            try
            {
                if (read == null)
                {
                    const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                    var type = Reflect.Type(typeName);
                    var getter = type?.GetProperty(name, flags)?.GetGetMethod(true);
                    if (getter != null)
                        read = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), getter);
                    else
                    {
                        var field = type?.GetField(name, flags);
                        if (field?.FieldType != typeof(bool)) return null;
                        read = () => (bool)field.GetValue(null);
                    }
                }
                return read();
            }
            catch { return null; }
        }
        public static bool TogetherReady => Compat.PluginLoaded("CasualtiesMP")
            && Flag(ref _initialized, "Together.Plugin", "SUCCESFULLY_INITIALIZED") == true
            && Flag(ref _forceDisabled, "Together.Plugin", "FORCE_DISABLE_MP_MOD") == false;
        public static bool CoreReady => CoreAvailable && BridgeReady;

        private static object Bridge(string method, Type[] types, params object[] args)
        {
            if (!CoreReady) return null;
            if ((method == "Broadcast" || method == "SendToServer" || method == "RequestServer") && !IsRunning) return null;
            string key = method + "/" + types.Length;
            if (!Methods.TryGetValue(key, out var call))
            {
                var type = Reflect.Type("CUCoreLib.Networking.MultiplayerBridge");
                call = type?.GetMethod(method, BindingFlags.Static | BindingFlags.Public, null, types, null);
                if (call == null) return null;
                Methods[key] = call;
            }
            object result = call.Invoke(null, args);
            return call.ReturnType == typeof(void) ? (object)true : result;
        }
        public static bool BridgeReady => TogetherReady && Compat.PluginLoaded("net.cucorelib") && Flag(ref _bridgeReady, "CUCoreLib.Networking.MultiplayerApi", "BridgeReceiversInstalled") == true;
        public static bool CoreAvailable
        {
            get
            {
                try { return TogetherReady && Compat.PluginLoaded("net.cucorelib") && Flag(ref _coreAvailable, "CUCoreLib.Networking.MultiplayerApi", "IsAvailable") == true; }
                catch (Exception ex) { Log.Debug("Mp.CoreAvailable: " + ex.Message); return false; }
            }
        }

        public static bool IsRunning
        {
            get
            {
                try { return TogetherReady && Flag(ref _running, "Together.Net", "IsRunning") == true; }
                catch { return false; }
            }
        }

        public static bool IsServer
        {
            get
            {
                try { return TogetherReady && Flag(ref _server, "Together.Net", "IsServer") == true; }
                catch { return false; }
            }
        }

        public static bool IsClient
        {
            get
            {
                try { return TogetherReady && Flag(ref _client, "Together.Net", "IsClient") == true; }
                catch { return false; }
            }
        }

        /// <summary>注册“主机快照里的一个模块”：capture 在主机打包时调用，apply 在客机收到快照时调用。</summary>
        public static bool RegisterSyncModule(string key, Func<JObject> capture, Action<JObject> apply)
        {
            if (!CoreReady) return false;
            try
            {
                var type = Reflect.Type("CUCoreLib.Networking.MultiplayerSyncRegistry");
                var method = type?.GetMethod("RegisterModule", new[] { typeof(string), typeof(Func<JObject>), typeof(Action<JObject>) });
                if (method == null) return false;
                method.Invoke(null, new object[] { key, capture, apply });
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"RegisterSyncModule('{key}') 失败：{ex.Message}");
                return false;
            }
        }

        public static bool RegisterClientHandler(string channel, Action<JToken> handler)
        {
            try
            {
                if (Reflect.Type("CUCoreLib.Networking.MultiplayerBridge") == null) return false;
                return Bridge("RegisterClientHandler", new[] { typeof(string), typeof(Action<JToken>) }, channel, handler) is bool ok && ok;
            }
            catch (Exception ex)
            {
                Log.Debug($"RegisterClientHandler('{channel}') 失败：{ex.Message}");
                return false;
            }
        }

        public static bool RegisterServerHandler(string channel, Func<JToken, JToken> handler)
        {
            try
            {
                if (Reflect.Type("CUCoreLib.Networking.MultiplayerBridge") == null) return false;
                return Bridge("RegisterServerHandler", new[] { typeof(string), typeof(Func<JToken, JToken>) }, channel, handler) is bool ok && ok;
            }
            catch (Exception ex)
            {
                Log.Debug($"RegisterServerHandler('{channel}') 失败：{ex.Message}");
                return false;
            }
        }

        public static bool Broadcast(string channel, object payload, bool includeHost = false)
        {
            try { return Bridge("Broadcast", new[] { typeof(string), typeof(object), typeof(bool), typeof(bool) }, channel, payload, includeHost, true) is bool sent && sent; }
            catch (Exception ex) { Log.Debug($"Broadcast('{channel}') 失败：{ex.Message}"); return false; }
        }

        public static bool SendToServer(string channel, object payload)
        {
            try { return Bridge("SendToServer", new[] { typeof(string), typeof(object), typeof(bool) }, channel, payload, true) is bool sent && sent; }
            catch (Exception ex) { Log.Debug($"SendToServer('{channel}') 失败：{ex.Message}"); return false; }
        }

        public static bool RequestServer(string channel, object payload, Action<JToken> onResponse)
        {
            try { return Bridge("RequestServer", new[] { typeof(string), typeof(object), typeof(Action<JToken>), typeof(bool) }, channel, payload, onResponse, true) is bool sent && sent; }
            catch (Exception ex) { Log.Debug($"RequestServer('{channel}') 失败：{ex.Message}"); return false; }
        }

        /// <summary>主机的“无芯片”裁决源：世界中看世界，主菜单看 runSettings。</summary>
        public static bool HostUnchipped()
        {
            WorldGeneration world = WorldGeneration.world;
            if (world != null && world.worldExists)
            {
                return world.unchippedMode;
            }
            return RunSettingUnchipped();
        }

        public static bool RunSettingUnchipped()
        {
            try
            {
                var dict = WorldGeneration.runSettings;
                if (dict != null && dict.TryGetValue("unchipped", out object v) && v is bool b) return b;

                if (!TogetherReady) return false;
                Type mpWorld = Reflect.Type("Together.WorldMP");
                if (mpWorld != null)
                {
                    object settings = Reflect.GetStatic(mpWorld, "runSettings");
                    if (settings is System.Collections.Generic.IDictionary<string, object> d
                        && d.TryGetValue("unchipped", out object v2) && v2 is bool b2)
                    {
                        return b2;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Mp.RunSettingUnchipped: " + ex.Message);
            }
            return false;
        }

        /// <summary>Together 的规则里是否允许每个玩家各自决定芯片状态。</summary>
        public static bool UnchippedIsIndividual
        {
            get
            {
                if (!TogetherReady) return false;
                object rules = Reflect.GetStatic(Reflect.Type("Together.Multiplayer"), "rules");
                if (rules == null) return false;
                object v = Reflect.GetInstance(rules, "UnchippedIsIndividual");
                return v is bool b && b;
            }
            set
            {
                if (!TogetherReady) return;
                Type mp = Reflect.Type("Together.Multiplayer");
                if (mp == null) return;
                Reflect.MutateStructMember(mp.FullName, "rules", boxed =>
                {
                    Reflect.TrySetInstance(boxed, "UnchippedIsIndividual", value);
                });
            }
        }

        public static string PeerName()
        {
            try
            {
                if (!TogetherReady) return null;
                object local = Reflect.GetStatic(Reflect.Type("Together.ScavPlayer"), "LocalPlayer");
                object name = Reflect.GetInstance(local, "playerName");
                return name as string;
            }
            catch
            {
                return null;
            }
        }
    }
}
