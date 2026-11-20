using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using DDEN.Shared;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DDEN.MPUnchippedSyncFix
{
    [BepInPlugin(Guid, "DDEN MP Unchipped Sync Fix", "1.0.0")]
    [BepInDependency("net.cucorelib", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("CasualtiesMP", BepInDependency.DependencyFlags.SoftDependency)]
    public class UnchippedSyncPlugin : BaseUnityPlugin, IDdenModule
    {
        public const string Guid = "com.dden.mpunchippedsync";
        public const string DisplayNameText = "MP 无芯片同步";

        internal static UnchippedSyncPlugin Instance;
        internal static ManualLogSource LogSource;
        internal static LogChannel Log;

        internal static ConfigEntry<bool> CfgEnabled;
        internal static ConfigEntry<bool> CfgForceHost;
        internal static ConfigEntry<bool> CfgOverrideIndividualRule;
        internal static ConfigEntry<float> CfgWatchdogSeconds;
        internal static ConfigEntry<bool> CfgAnnounce;
        internal static ConfigEntry<KeyboardShortcut> CfgStandaloneKey;
        internal static ConfigEntry<bool> CfgVerbose;

        private const float PushCooldown = 0.35f;

        private bool _lastHostValue;
        private bool _hasHostValue;
        private bool _lastPushed;
        private float _lastPushAt = -999f;
        private float _nextWatchdog;
        private bool _wasClient;
        private bool _requested;
        private float _nextRequestAt;
        private bool _forcePushNextTick;
        private int _appliedCount;
        private string _lastReason = "-";
        private float _lastAppliedAt;

        private KeyCode _standaloneKey = KeyCode.F10;
        private bool _standaloneVisible;

        // ===== IDdenModule =====
        public string Id => "mpunchipped";
        public string DisplayName => DisplayNameText;
        public string Version => Info.Metadata.Version.ToString();
        public bool Ready => Mp.CoreAvailable;
        public string Summary => Ready
            ? (Mp.IsRunning ? (Mp.IsServer ? "主机 · 正在下发" : "客机 · 正在跟随") : "未联机")
            : "CUCoreLib 缺失";
        public IEnumerable<ConfigEntryBase> ConfigEntries => Config.Entries();

        private void Awake()
        {
            Instance = this;
            LogSource = base.Logger;

            CfgEnabled = Config.Bind("General", "Enabled", true,
                "总开关。关闭后本组件完全不介入，行为与未安装时一致。");
            CfgForceHost = Config.Bind("General", "ForceHostUnchipped", true,
                "主机裁决优先：主机开无芯片，客机强制无芯片；主机关，客机也强制有芯片。\n" +
                "关掉后只把主机的值同步成“客机的初始值”，客机本地仍可自行改变。");
            CfgOverrideIndividualRule = Config.Bind("General", "OverrideIndividualRule", true,
                "把 Together 的 UnchippedIsIndividual 规则改成 false（全员一致），\n" +
                "使名牌 / 附近语音 / 状态栏等 Together 自己的判定也跟随主机。");
            CfgWatchdogSeconds = Config.Bind("General", "WatchdogSeconds", 0.5f,
                new ConfigDescription("客机纠偏轮询间隔（秒）。0 = 关闭轮询。", new AcceptableValueRange<float>(0f, 10f)));
            CfgAnnounce = Config.Bind("General", "AnnounceToHostChat", false,
                "客机纠偏时在控制台打印一行说明（排查用）。");
            CfgStandaloneKey = Config.Bind("UI", "StandaloneKey", new KeyboardShortcut(KeyCode.F10),
                "管理器缺席时的兜底面板快捷键（管理器在跑时用管理器）。");
            CfgVerbose = Config.Bind("Debug", "Verbose", false, "输出调试日志。");

            Log = DDEN.Shared.Log.For(LogSource, "MPUnchipped", CfgVerbose.Value);

            try
            {
                new Harmony(Guid).PatchAll(typeof(WorldGeneration_SetUnchipped_Patch));
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "Harmony 打补丁失败（主机侧变更广播会退化为主机状态轮询）");
            }

            RegisterChannels();
            DdenHub.Register(this);
            _standaloneKey = Compat.ResolveHotkey(CfgStandaloneKey, Guid, !DdenHub.ManagerLoaded);

            Log.Info("已加载：主机无芯片裁决 → 客机同步。");
            if (UnchippedSelfTest.Requested) StartCoroutine(SelfTestDelayed());
        }

        private System.Collections.IEnumerator SelfTestDelayed()
        {
            yield return new WaitForSecondsRealtime(2f);
            UnchippedSelfTest.Run(Log);
        }

        // ===== 界面 =====

        public void DrawPage(Rect area)
        {
            float y = area.y;

            GUI.Label(new Rect(area.x, y, area.width, 18f), "状态", DdenUI.Sub);
            y += 22f;

            (string k, string v)[] rows =
            {
                ("CUCoreLib 联机桥", Mp.CoreAvailable ? "可用" : "缺失"),
                ("联机状态", !Mp.IsRunning ? "未联机" : (Mp.IsServer ? "主机（下发）" : "客机（跟随）")),
                ("主机裁决", _hasHostValue ? (_lastHostValue ? "无芯片（开）" : "有芯片（关）") : "未知"),
                ("本地世界", Game.InWorld ? (WorldGeneration.world.unchippedMode ? "无芯片（开）" : "有芯片（关）") : "未进世界"),
                ("已纠正次数", _appliedCount.ToString()),
                ("最近原因", _lastReason)
            };

            foreach ((string k, string v) in rows)
            {
                GUI.Label(new Rect(area.x, y, 180f, 18f), k, DdenUI.Sub);
                GUI.Label(new Rect(area.x + 186f, y, area.width - 190f, 18f), v, DdenUI.Row);
                y += 20f;
            }

            y += 8f;
            GUI.Label(new Rect(area.x, y, area.width, 18f), "操作", DdenUI.Sub);
            y += 22f;

            if (DdenUI.Button(new Rect(area.x, y, 150f, 26f), "按主机裁决纠正一次", true)) ForceApply();
            if (DdenUI.Button(new Rect(area.x + 158f, y, 150f, 26f), "重新向主机请求")) RequestNow();
            GUI.enabled = Mp.IsRunning && Mp.IsServer;
            if (DdenUI.Button(new Rect(area.x + 316f, y, 170f, 26f), "主机侧切换开/关")) ToggleHostState();
            GUI.enabled = true;
            y += 34f;

            GUI.Label(new Rect(area.x, y, area.width, 60f),
                "说明：客机侧的 world.unchippedMode 是本地字段，游戏里所有“有没有芯片”的判定都读它。\n" +
                "本模块在快照/推送/请求三条链路上把它钉死在主机裁决上，并用 watchdog 兜住其它模组的改写。",
                DdenUI.Wrap);

            if (!Ready)
            {
                GUI.Label(new Rect(area.x, y + 64f, area.width, 40f),
                    "<color=#f0a>CUCoreLib 未就绪：同步功能暂停。装好 net.cucorelib 后重开游戏即可。</color>", DdenUI.Wrap);
            }
        }

        public bool DrawSettings(Rect area)
        {
            return false; // 交给管理器的通用配置渲染器
        }

        public void ForceApply()
        {
            if (!_hasHostValue)
            {
                RequestNow();
                Game.Alert("[DDEN] 还不知道主机状态，已向主机请求。");
                return;
            }
            ApplyLocal(_lastHostValue, "manual");
            Game.Alert($"[DDEN] 已按主机裁决纠正：{(_lastHostValue ? "无芯片" : "有芯片")}");
        }

        public void RequestNow()
        {
            Mp.RequestServer(UnchippedProtocol.RequestChannel, new JObject { ["ask"] = "unchipped" }, token =>
            {
                if (token == null) return;
                JToken v = token[UnchippedProtocol.StateField];
                if (v == null) return;
                OnHostState(v.Value<bool>(), "request(manual)");
            });
        }

        public void ToggleHostState()
        {
            if (!Mp.IsRunning || !Mp.IsServer) return;
            WorldGeneration world = WorldGeneration.world;
            if (world == null) return;
            bool next = !world.unchippedMode;
            world.SetUnchipped(next);
            WriteRunSetting(next);
            RequestImmediatePush(next);
            Game.Alert($"[DDEN] 主机无芯片：{(next ? "开" : "关")}");
        }

        // ===== 联机协议 =====

        private void RegisterChannels()
        {
            if (!Mp.CoreAvailable)
            {
                Log.Warn("未检测到 CUCoreLib 联机桥，同步功能要等 CUCoreLib 就绪后由 watchdog 自行启用。");
                return;
            }

            Mp.RegisterSyncModule(UnchippedProtocol.ModuleKey, CaptureState, ApplyState);
            Mp.RegisterClientHandler(UnchippedProtocol.PushChannel, token =>
            {
                if (token == null) return;
                JToken v = token.Type == JTokenType.Object ? token[UnchippedProtocol.StateField] : token;
                if (v == null) return;
                OnHostState(v.Value<bool>(), "push");
            });
            Mp.RegisterServerHandler(UnchippedProtocol.RequestChannel, _ =>
            {
                if (!CfgEnabled.Value) return null;
                bool value = Mp.IsServer ? Mp.HostUnchipped() : _lastHostValue;
                if (Mp.IsServer) OnHostValueSeen(value);
                return JObject.FromObject(new
                {
                    unchipped = value,
                    forced = CfgForceHost.Value,
                    host = Mp.PeerName() ?? "host",
                    ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                });
            });
        }

        private JObject CaptureState()
        {
            bool value = Mp.HostUnchipped();
            OnHostValueSeen(value);
            Log.Debug($"capture unchipped={value}");
            return new JObject
            {
                [UnchippedProtocol.StateField] = value,
                ["forced"] = CfgForceHost.Value,
                ["ts"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
        }

        private void ApplyState(JObject payload)
        {
            if (payload == null) return;
            JToken v = payload[UnchippedProtocol.StateField];
            if (v == null) return;
            OnHostState(v.Value<bool>(), "snapshot");
        }

        private void OnHostValueSeen(bool value)
        {
            _hasHostValue = true;
            _lastHostValue = value;
        }

        private void OnHostState(bool value, string source)
        {
            if (!CfgEnabled.Value) return;

            bool changed = !_hasHostValue || _lastHostValue != value;
            _hasHostValue = true;
            _lastHostValue = value;

            if (CfgForceHost.Value) ApplyLocal(value, source);
            else if (changed) ApplyLocal(value, source + ":seed-only");

            Log.Debug($"host unchipped={value} via {source} (changed={changed})");
        }

        private void ApplyLocal(bool value, string reason)
        {
            if (CfgOverrideIndividualRule.Value) Mp.UnchippedIsIndividual = false;

            WorldGeneration world = WorldGeneration.world;
            if (world != null)
            {
                if (world.unchippedMode != value)
                {
                    try
                    {
                        world.SetUnchipped(value);
                        _appliedCount++;
                        _lastReason = reason;
                        _lastAppliedAt = Time.unscaledTime;
                        Log.Debug($"本地 world.SetUnchipped({value}) 生效（{reason}，累计 {_appliedCount} 次）");
                        if (CfgAnnounce.Value && !Mp.IsServer)
                        {
                            Game.Console($"<color=grey>[DDEN] 无芯片状态已按主机同步：{(value ? "开" : "关")}</color>");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Exception(ex, "world.SetUnchipped 失败");
                    }
                }
            }
            else
            {
                WriteRunSetting(value);
                _lastReason = reason + "（主菜单，写入 runSettings）";
            }

            MirrorTogetherLock(value);
            SyncLocalPlayer(value);
        }

        private static void WriteRunSetting(bool value)
        {
            try
            {
                var dict = WorldGeneration.runSettings;
                if (dict != null) dict["unchipped"] = value;

                PreRunScript prs = PreRunScript.instance;
                if (prs != null && prs.runSettings != null) prs.runSettings["unchipped"] = value;
            }
            catch (Exception ex)
            {
                Log.Debug("写 runSettings 失败：" + ex.Message);
            }
        }

        private static void MirrorTogetherLock(bool value)
        {
            if (!Mp.IsRunning) return;
            Type lockType = Reflect.Type("Together.WorldMP+RunUnchippedLock");
            if (lockType == null) return;
            try
            {
                Reflect.SetStatic(lockType, "ApplyingSynced", true);
                Reflect.SetStatic(lockType, "Locked", true);
                Reflect.SetStatic(lockType, "GlobalUnchipped", value);
            }
            catch (Exception ex)
            {
                Log.Debug("镜像 RunUnchippedLock 失败：" + ex.Message);
            }
            finally
            {
                Reflect.SetStatic(lockType, "ApplyingSynced", false);
            }
        }

        private static void SyncLocalPlayer(bool value)
        {
            if (!Mp.IsRunning) return;
            try
            {
                Type scavType = Reflect.Type("Together.ScavPlayer");
                if (scavType == null) return;
                object local = Reflect.GetStatic(scavType, "LocalPlayer");
                if (local == null) return;
                object current = Reflect.GetInstance(local, "unchipped");
                if (current is bool b && b == value) return;
                Reflect.InvokeInstance(local, "SetUnchipped", value);
            }
            catch (Exception ex)
            {
                Log.Debug("同步本地玩家芯片状态失败：" + ex.Message);
            }
        }

        internal void RequestImmediatePush(bool value)
        {
            _hasHostValue = true;
            _lastHostValue = value;
            _forcePushNextTick = true;
        }

        private void Update()
        {
            try
            {
                if (!DdenHub.ManagerLoaded && Input.GetKeyDown(_standaloneKey))
                {
                    _standaloneVisible = !_standaloneVisible;
                }

                if (!DdenHub.ManagerLoaded)
                {
                    if (_standaloneVisible) DdenBlocker.Show(0.12f);
                    else DdenBlocker.Hide();
                }

                if (!CfgEnabled.Value) return;
                if (!Mp.CoreAvailable) return;

                bool running = Mp.IsRunning;
                if (running && Mp.IsServer) { TickHost(); return; }
                if (running && Mp.IsClient) { TickClient(); return; }

                _wasClient = false;
                _requested = false;
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "Update 异常");
            }
        }

        private void TickHost()
        {
            _wasClient = false;

            if (CfgOverrideIndividualRule.Value && Mp.UnchippedIsIndividual)
            {
                Mp.UnchippedIsIndividual = false;
                Log.Info("已把 Together 规则 UnchippedIsIndividual 置为 false（全员跟随主机裁决）。");
            }

            bool value = Mp.HostUnchipped();
            OnHostValueSeen(value);

            float now = Time.unscaledTime;
            bool changed = value != _lastPushed;
            if ((changed && now - _lastPushAt >= PushCooldown) || _forcePushNextTick)
            {
                _forcePushNextTick = false;
                _lastPushed = value;
                _lastPushAt = now;
                Mp.Broadcast(UnchippedProtocol.PushChannel, new JObject
                {
                    [UnchippedProtocol.StateField] = value,
                    ["forced"] = CfgForceHost.Value
                });
                Log.Info($"主机无芯片状态 → 广播 {(value ? "开" : "关")}");
            }
        }

        private void TickClient()
        {
            float now = Time.unscaledTime;

            if (!_requested || !_wasClient || now >= _nextRequestAt)
            {
                _wasClient = true;
                _requested = true;
                _nextRequestAt = now + 20f;
                RequestNow();
            }

            if (CfgForceHost.Value && _hasHostValue && CfgWatchdogSeconds.Value > 0f && now >= _nextWatchdog)
            {
                _nextWatchdog = now + CfgWatchdogSeconds.Value;

                WorldGeneration world = WorldGeneration.world;
                if (world != null && world.unchippedMode != _lastHostValue)
                {
                    Log.Warn($"检测到本地无芯片状态被改动（{world.unchippedMode} != 主机 {_lastHostValue}），按主机裁决纠正。");
                    ApplyLocal(_lastHostValue, "watchdog");
                }
            }
        }

        private void OnGUI()
        {
            if (DdenHub.ManagerLoaded || !_standaloneVisible) return;
            try
            {
                var rect = new Rect(120f, 120f, 460f, 380f);
                _standaloneRect = rect;
                GUI.Window(0x4D5055, rect, _ =>
                {
                    DdenUI.DrawStandalone(new Rect(0, 0, rect.width, rect.height), this, ref _standaloneScroll, false);
                    GUI.DragWindow(new Rect(0, 0, rect.width, 30f));
                }, GUIContent.none, GUIStyle.none);
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "OnGUI");
            }
        }

        private Vector2 _standaloneScroll;
        private Rect _standaloneRect;

        private void OnDestroy()
        {
            try { new Harmony(Guid).UnpatchSelf(); }
            catch { /* ignored */ }
        }
    }

    /// <summary>主机侧：任何人调用 WorldGeneration.SetUnchipped 都会被看到（含控制台 unchipped 指令、LayerModifier）。</summary>
    [HarmonyPatch(typeof(WorldGeneration), "SetUnchipped")]
    [HarmonyAfter(new[] { "CasualtiesMP", "net.cucorelib" })]
    internal static class WorldGeneration_SetUnchipped_Patch
    {
        private static void Postfix(bool unchipped)
        {
            UnchippedSyncPlugin plugin = UnchippedSyncPlugin.Instance;
            if (plugin == null || !UnchippedSyncPlugin.CfgEnabled.Value) return;
            if (!Mp.IsRunning || !Mp.IsServer) return;

            plugin.RequestImmediatePush(unchipped);
        }
    }
}
