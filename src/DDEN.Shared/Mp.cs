using System;
using Newtonsoft.Json.Linq;

namespace DDEN.Shared
{
    /// <summary>
    /// CUCoreLib 联机桥的薄封装：只在这里碰 CUCoreLib 的公开 API，
    /// 其余代码用 null 安全的方式说话（没装 CUCoreLib / 没联机时静默降级）。
    /// </summary>
    public static class Mp
    {
        public static bool CoreAvailable
        {
            get
            {
                try { return CUCoreLib.Networking.MultiplayerApi.IsAvailable; }
                catch (Exception ex) { Log.Debug("Mp.CoreAvailable: " + ex.Message); return false; }
            }
        }

        public static bool IsRunning
        {
            get
            {
                try { return CUCoreLib.Networking.MultiplayerApi.IsRunning; }
                catch { return false; }
            }
        }

        public static bool IsServer
        {
            get
            {
                try { return CUCoreLib.Networking.MultiplayerApi.IsServer; }
                catch { return false; }
            }
        }

        public static bool IsClient
        {
            get
            {
                try { return CUCoreLib.Networking.MultiplayerApi.IsClient; }
                catch { return false; }
            }
        }

        /// <summary>注册“主机快照里的一个模块”：capture 在主机打包时调用，apply 在客机收到快照时调用。</summary>
        public static bool RegisterSyncModule(string key, Func<JObject> capture, Action<JObject> apply)
        {
            try
            {
                CUCoreLib.Networking.MultiplayerSyncRegistry.RegisterModule(key, capture, apply);
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
                CUCoreLib.Networking.MultiplayerBridge.RegisterClientHandler(channel, handler);
                return true;
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
                CUCoreLib.Networking.MultiplayerBridge.RegisterServerHandler(channel, handler);
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug($"RegisterServerHandler('{channel}') 失败：{ex.Message}");
                return false;
            }
        }

        public static bool Broadcast(string channel, object payload, bool includeHost = false)
        {
            try { return CUCoreLib.Networking.MultiplayerBridge.Broadcast(channel, payload, includeHost); }
            catch (Exception ex) { Log.Debug($"Broadcast('{channel}') 失败：{ex.Message}"); return false; }
        }

        public static bool SendToServer(string channel, object payload)
        {
            try { return CUCoreLib.Networking.MultiplayerBridge.SendToServer(channel, payload); }
            catch (Exception ex) { Log.Debug($"SendToServer('{channel}') 失败：{ex.Message}"); return false; }
        }

        public static bool RequestServer(string channel, object payload, Action<JToken> onResponse)
        {
            try { return CUCoreLib.Networking.MultiplayerBridge.RequestServer(channel, payload, onResponse); }
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
                object rules = Reflect.GetStatic(Reflect.Type("Together.Multiplayer"), "rules");
                if (rules == null) return false;
                object v = Reflect.GetInstance(rules, "UnchippedIsIndividual");
                return v is bool b && b;
            }
            set
            {
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
