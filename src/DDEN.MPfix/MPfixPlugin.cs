using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using DDEN.Shared;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DDEN.MPfix
{
    /// <summary>
    /// DDEN.MPfix —— 把“多人相关的修复”收在一个插件里：
    ///  ① 无芯片模式（unchipped）主机裁决 → 客机同步（原来的 MPUnchippedSyncFix）；
    ///  ② 联机合成模组物品“无法生成物品”修复（Utils.Create 走 CustomInstantiate + 主机侧网络登记）；
    ///  ③ 物品网络身份补登记（水晶层装备在主机上闪烁的根因）。
    /// 界面与配置统一交给 DDExcNet-CUmodM 显示（模块 id = mpfix）。
    /// </summary>
    [BepInPlugin(Guid, "DDEN MP Fix", "1.1.0")]
    [BepInDependency("net.cucorelib", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("CasualtiesMP", BepInDependency.DependencyFlags.SoftDependency)]
    public class MPfixPlugin : BaseUnityPlugin, IDdenModule
    {
        public const string Guid = "com.dden.mpfix";
        public const string Title = "多人修复";

        internal static MPfixPlugin Instance;
        internal static ManualLogSource LogSource;
        internal static LogChannel Log;

        // ===== 无芯片同步 =====
        internal static ConfigEntry<bool> CfgChipEnabled;
        internal static ConfigEntry<bool> CfgForceHost;
        internal static ConfigEntry<bool> CfgOverrideIndividualRule;
        internal static ConfigEntry<float> CfgWatchdogSeconds;
        internal static ConfigEntry<bool> CfgAnnounce;

        // ===== 联机物品/合成 =====
        internal static ConfigEntry<bool> CfgFixModItemCraftSpawn;
        internal static ConfigEntry<bool> CfgRegisterSpawnedItems;

        // ===== 穿戴兜底 =====
        internal static ConfigEntry<bool> CfgWearFix;
        internal static ConfigEntry<float> CfgWearGrace;
        internal static ConfigEntry<int> CfgWearFixMax;
        internal static ConfigEntry<bool> CfgWearRebuild;

        // ===== 物品物理保险 =====
        internal static ConfigEntry<bool> CfgPhysicsGuard;
        internal static ConfigEntry<bool> CfgSnapWornItems;
        internal static ConfigEntry<int> CfgSnapWornMax;

        // ===== 联机修正（商人商品表 / 工具耐久 / 挖块） =====
        internal static ConfigEntry<bool> CfgTraderSnapshot;
        internal static ConfigEntry<bool> CfgToolDurability;
        internal static ConfigEntry<bool> CfgBlockBreakSync;
        internal static ConfigEntry<bool> CfgCleanWearableVisuals;

        // ===== 娱乐 =====
        internal static ConfigEntry<bool> CfgDoublePurchase;

        // ===== 其它 =====
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

        private KeyCode _standaloneKey = KeyCode.F10;
        private bool _standaloneVisible;
        private Rect _standaloneRect;
        private Vector2 _standaloneScroll;

        // ===== IDdenModule =====
        public string Id => "mpfix";
        public string DisplayName => Title;
        public string Version => Info.Metadata.Version.ToString();
        public bool Ready => Mp.CoreAvailable;
        public string Summary
        {
            get
            {
                if (!Mp.CoreAvailable) return "CUCoreLib 缺失";
                if (!Mp.IsRunning) return "未联机";
                return Mp.IsServer ? "主机 · 下发中" : "客机 · 跟随中";
            }
        }
        public IEnumerable<ConfigEntryBase> ConfigEntries => Config.Entries();

        private void Awake()
        {
            Instance = this;
            LogSource = base.Logger;

            CfgChipEnabled = Config.Bind("无芯片同步", "Enabled", true,
                "总开关。关闭后本模块完全不介入无芯片状态。");
            CfgForceHost = Config.Bind("无芯片同步", "ForceHostUnchipped", true,
                "主机裁决优先：主机开无芯片 → 客机强制无芯片；主机有芯片 → 客机也有芯片。\n" +
                "关掉后只把主机的值当成客机初始值，之后客机可以自己改。");
            CfgOverrideIndividualRule = Config.Bind("无芯片同步", "OverrideIndividualRule", true,
                "把 Together 的 UnchippedIsIndividual 置为 false（全员一致），\n" +
                "使名牌 / 附近语音 / 状态栏等 Together 自己的判定也跟随主机。");
            CfgWatchdogSeconds = Config.Bind("无芯片同步", "WatchdogSeconds", 30f,
                new ConfigDescription("客机纠偏轮询间隔（秒）。芯片状态通常一次同步就够，默认 30 秒；0 = 关闭轮询。",
                    new AcceptableValueRange<float>(0f, 600f)));
            CfgAnnounce = Config.Bind("无芯片同步", "AnnounceToOtherPlayers", false,
                "客机被纠偏时在控制台打印一行说明（排查用）。");

            CfgFixModItemCraftSpawn = Config.Bind("联机合成", "FixModItemCraftSpawn", true,
                "修复联机合成模组物品时报“无法合成：无法生成物品”。\n" +
                "原因：Together 主机侧用 Utils.Create → Resources.Load(id) 生成成品，而模组物品不在 Resources 里。\n" +
                "开启后用 CUCoreLib 的 CustomInstantiate 兜底（掉落/世界生成同路径一并受益）。");
            CfgRegisterSpawnedItems = Config.Bind("联机合成", "RegisterSpawnedItemsOnHost", true,
                "兜底生成出来的物品，主机侧立刻补登记进 NetObjectRegistry。\n" +
                "不登记的话客机穿上后主机会拒绝穿戴同步（Server declines itemwearing），\n" +
                "表现为主机看到装备“掉下去又回到身上”（水晶层装备尤其明显）。");

            CfgStandaloneKey = Config.Bind("UI", "StandaloneKey", new KeyboardShortcut(KeyCode.F10),
                "管理器缺席时的兜底面板快捷键（管理器在跑时请用管理器）。");
            CfgWearFix = Config.Bind("穿戴兜底", "Enabled", true,
                "客机穿上物品但主机没认下来时（主机眼里它还在地上/掉落），\n" +
                "1.5s 后请主机按 id 找到那件物品并正确穿到该玩家身上 —— 不重建、不复制、不掉落。");
            CfgWearGrace = Config.Bind("穿戴兜底", "GraceSeconds", 1.5f,
                new ConfigDescription("穿上后等多久再去问主机（秒）。太快可能撞上还没同步完。",
                    new AcceptableValueRange<float>(0.3f, 10f)));
            CfgWearFixMax = Config.Bind("穿戴兜底", "MaxPerSession", 20,
                new ConfigDescription("每局最多兜底几次；超过就停手。",
                    new AcceptableValueRange<int>(1, 200)));
            CfgWearRebuild = Config.Bind("穿戴兜底", "RebuildIfHostLacksItem", false,
                "仅在主机确实找不到这件物品时才用：取消穿戴 → 掉地 → 请求主机重建。\n" +
                "默认关闭（只做“认出并穿上”）；开着会有复制风险，仅作最后手段。");
            CfgSnapWornItems = Config.Bind("物品物理", "SnapWornItems", true,
                "物品还穿在身上/握在手里，却浮在角色上方（局部坐标错位）时，把它贴合回原位。\n" +
                "每 2 秒扫一次附近角色，只改 localPosition/localRotation，不动缩放。");
            CfgSnapWornMax = Config.Bind("物品物理", "SnapMaxPerSession", 100,
                new ConfigDescription("每局最多修正几次；超过就停手。",
                    new AcceptableValueRange<int>(1, 1000)));
            CfgPhysicsGuard = Config.Bind("物品物理", "GuardDropPhysics", true,
                "只在“丢出/卸下”这些真正的动作后做保险：只处理自由物品（不在手/身/包里），\n" +
                "只补开被关掉的碰撞体与没打开的物理，**不再自动移动物品**（避免位移残留导致错位）。\n" +
                "用于修联机里“丢东西穿墙往下掉”。");
            CfgTraderSnapshot = Config.Bind("联机修正", "TraderSnapshotOnOpen", true,
                "客机每次打开商人界面时，向主机索要一次完整商品表（走 Together 自己的 TraderSync.SendTraderState）。\n" +
                "用于修“看到的商品和主机不一致 → 付了钱却拿不到东西”。");
            CfgToolDurability = Config.Bind("联机修正", "SyncToolDurability", false,
                "（实验）客机使用工具后，把本地扣掉的耐久报给主机，让主机那份一起扣。\n" +
                "默认关闭：只有确认存在“耐久不同步”问题时才需要打开。");
            CfgBlockBreakSync = Config.Bind("联机修正", "SyncBlockBreak", true,
                "客机本地挖碎方块时，额外通知主机把同一格按剩余血量打碎。\n" +
                "用于修“客机挖掉方块后又重新长出来”（主机没认这次破坏 → chunk 同步把原来那块推回来）。");
            CfgCleanWearableVisuals = Config.Bind("联机修正", "CleanStaleWearableVisuals", true,
                "清理“人身上还挂着一件装备贴图、其实没穿戴”的残留视觉层（WearableExtension）。\n" +
                "只删纯视觉子物体，不碰任何真实物品。");
            CfgDoublePurchase = Config.Bind("娱乐", "道生一 一生二", false,
                "道生一，一生二：主机在商人购买时额外奉送一份（同样的物品，直接进买家背包）。\n" +
                "纯娱乐开关，默认关闭。只在主机生效，客机购买也会多一件。");
            CfgVerbose = Config.Bind("Debug", "Verbose", false, "输出调试日志。");

            Log = DDEN.Shared.Log.For(LogSource, "MPfix", CfgVerbose.Value);

            Harmony harmony = new Harmony(Guid);
            TryPatch(harmony, typeof(WorldGeneration_SetUnchipped_Patch), "无芯片变更广播");
            TryPatch(harmony, typeof(Utils_Create_ModItemPatch), "联机合成/物品生成兜底");
            TryPatch(harmony, typeof(WearGhostFix.Body_WearWearable_RecordPatch), "穿戴兜底（幽灵装备）");
            TryPatch(harmony, typeof(ItemPhysicsGuard.Body_DropItem_GuardPatch), "物品掉落物理保险（丢出）");
            TryPatch(harmony, typeof(ItemPhysicsGuard.Body_DropWearable_GuardPatch), "物品掉落物理保险（卸下）");
            ItemPhysicsGuard.InstallHeldQuietRepair(harmony);
            TryPatch(harmony, typeof(WornItemSnap.Body_WearWearable_SnapPatch), "穿戴贴合修正（防浮空错位）");
            TryPatch(harmony, typeof(TraderSnapshotFix.ToggleTradeMenu_Postfix), "打开商人时索要商品表");
            TryPatch(harmony, typeof(DurabilityFix.UseItemInHand_Patch), "工具耐久同步（手持使用）");
            TryPatch(harmony, typeof(DurabilityFix.UseItem_Patch), "工具耐久同步（UseItem）");
            DurabilityFix.InstallHostWatcher(harmony);
            TryPatch(harmony, typeof(BlockBreakFix.DamageBlock_Postfix), "客机挖块后通知主机打碎");
            TryPatch(harmony, typeof(WearableVisualFix.Body_DropWearable_ClearPatch), "卸下时清理穿戴贴图");

            MigrateOldWatchdog();

            RegisterChannels();
            WearGhostFix.RegisterHostHandlers();
            TraderSnapshotFix.RegisterHost();
            DurabilityFix.RegisterHost();
            BlockBreakFix.RegisterHost();
            DoublePurchase.Install(harmony);
            DdenHub.Register(this);
            _standaloneKey = Compat.ResolveHotkey(CfgStandaloneKey, Guid, !DdenHub.ManagerLoaded);

            Log.Info("已加载：无芯片同步 + 联机物品/合成修复。");
            LogMpCompatReport();
            if (UnchippedSelfTest.Requested) StartCoroutine(SelfTestDelayed());
        }

        /// <summary>
        /// 启动时打印“本插件到底还需不需要”的校对结论。
        /// 依据：对 Together 5.5.3 / 5.5.4 两份反编译做的逐点核对（见 docs/README.md「与 Together 版本校对」）。
        /// </summary>
        private static void LogMpCompatReport()
        {
            string ver = "未知";
            try
            {
                Version v = CUCoreLib.Networking.MultiplayerApi.KrokMpVersion;
                if (v != null) ver = v.ToString();
            }
            catch { /* ignored */ }

            Log.Info($"[MPCOMPAT] Together/CasualtiesMP = {ver}");

            bool verified = ver == "5.5.3" || ver == "5.5.4" || ver.StartsWith("5.5.");
            if (!verified)
            {
                Log.Warn("[MPCOMPAT] 未校对过这个 Together 版本：三项修复仍然生效，但请留意日志里是否出现 declines/craft spawn 警告。");
                return;
            }

            Log.Info("[MPCOMPAT] ① 无芯片：ApplyRunSettings 仍只写 runSettings，不写 world.unchippedMode；" +
                     "RunUnchippedLock 结构未变 → 同步仍需启用");
            Log.Info("[MPCOMPAT] ② 联机合成：RecipeResult_SpawnResult 仍用 Utils.Create → Resources.Load；" +
                     "只有 glassshards/水晶生成的跳过补丁 → 模组物品仍需要兜底");
            Log.Info("[MPCOMPAT] ③ 穿戴同步：ServerReceiver__ItemWearing 仍以 TryGetItem(登记表) 为门控；" +
                     "未登记的物品仍会被 decline → 网络登记仍需启用");
        }

        private void TryPatch(Harmony harmony, Type patchType, string what)
        {
            try
            {
                harmony.PatchAll(patchType);
                Log.Info("已启用补丁：" + what);
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "补丁失败：" + what);
            }
        }

        /// <summary>老配置里 WatchdogSeconds 还是旧默认 0.5 时，迁移成 30 秒（芯片状态通常一次同步就够）。</summary>
        private static void MigrateOldWatchdog()
        {
            try
            {
                if (Math.Abs(CfgWatchdogSeconds.Value - 0.5f) > 0.001f) return;
                CfgWatchdogSeconds.Value = 30f;
                Log.Info("已把无芯片纠偏间隔从旧默认 0.5s 迁移为 30s（想更灵敏可在配置里调小）。");
            }
            catch (Exception ex)
            {
                Log.Debug("迁移 WatchdogSeconds 失败：" + ex.Message);
            }
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

            GUI.Label(new Rect(area.x, y, area.width, 18f), "无芯片同步", DdenUI.Sub);
            y += 20f;
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
                GUI.Label(new Rect(area.x, y, 190f, 18f), k, DdenUI.Sub);
                GUI.Label(new Rect(area.x + 196f, y, area.width - 200f, 18f), v, DdenUI.Row);
                y += 19f;
            }

            y += 6f;
            GUI.Label(new Rect(area.x, y, area.width, 18f), "物品 / 合成修复", DdenUI.Sub);
            y += 20f;
            GUI.Label(new Rect(area.x, y, area.width, 18f),
                $"合成兜底：{(CfgFixModItemCraftSpawn.Value ? "开" : "关")}     " +
                $"物品网络登记：{(CfgRegisterSpawnedItems.Value ? "开" : "关")}", DdenUI.Row);
            y += 22f;

            if (DdenUI.Button(new Rect(area.x, y, 160f, 24f), "按主机裁决纠正一次", true)) ForceApply();
            if (DdenUI.Button(new Rect(area.x + 168f, y, 150f, 24f), "重新向主机请求")) RequestNow();
            GUI.enabled = Mp.IsRunning && Mp.IsServer;
            if (DdenUI.Button(new Rect(area.x + 326f, y, 160f, 24f), "主机侧切换开/关")) ToggleHostState();
            GUI.enabled = true;

            y += 32f;
            GUI.Label(new Rect(area.x, y, area.width, 44f),
                "无芯片：客机本地 world.unchippedMode 会被钉在主机裁决上（快照/推送/请求三路 + 0.5s watchdog）。\n" +
                "合成/物品：模组物品不在 Unity Resources，联机时由 CUCoreLib 的 CustomInstantiate 生成并补登记网络身份。",
                DdenUI.Wrap);
        }

        public bool DrawSettings(Rect area) => false;

        // ===== 无芯片同步（原逻辑）=====

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
                if (!CfgChipEnabled.Value) return null;
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
            if (!CfgChipEnabled.Value) return;

            bool changed = !_hasHostValue || _lastHostValue != value;
            _hasHostValue = true;
            _lastHostValue = value;

            if (CfgForceHost.Value) ApplyLocal(value, source);
            else if (changed) ApplyLocal(value, source + ":seed-only");
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
                    DdenHub.SetPanelVisible("mpfix", _standaloneVisible);
                }

                WearGhostFix.Tick();
                WornItemSnap.Tick();
                WearableVisualFix.Tick();

                if (!CfgChipEnabled.Value) return;
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
                var rect = new Rect(120f, 120f, 520f, 420f);
                _standaloneRect = rect;
                GUI.Window(0x4D504658, rect, _ =>
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

        private void OnDestroy()
        {
            try { new Harmony(Guid).UnpatchSelf(); }
            catch { /* ignored */ }
        }
    }

    /// <summary>主机侧：任何人调用 WorldGeneration.SetUnchipped 都会被看到（控制台 / LayerModifier / 规则菜单）。</summary>
    [HarmonyPatch(typeof(WorldGeneration), "SetUnchipped")]
    [HarmonyAfter(new[] { "CasualtiesMP", "net.cucorelib" })]
    internal static class WorldGeneration_SetUnchipped_Patch
    {
        private static void Postfix(bool unchipped)
        {
            MPfixPlugin plugin = MPfixPlugin.Instance;
            if (plugin == null || !MPfixPlugin.CfgChipEnabled.Value) return;
            if (!Mp.IsRunning || !Mp.IsServer) return;

            plugin.RequestImmediatePush(unchipped);
        }
    }
}
