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
    ///  ① 无芯片模式（unchipped）主机裁决 → 客机同步；
    ///  ② 联机合成模组物品“无法生成物品”修复（Utils.Create 走 CUCoreLib 两条正路 + 主机侧网络登记）；
    ///  ③ 穿戴兜底（客机穿上、主机没认下来 → 主机按 id 认出并穿上，再推状态给客机）；
    ///  ④ 物品物理保险（丢出/卸下后只补开碰撞体，绝不移动物品）；
    ///  ⑤ 挖块收敛（主机放宽伤害比例 + 客机已碎上报，主机校验后收敛）；
    ///  ⑥ 商人商品表补发 / 穿戴贴图残留 / 孤儿物品；
    ///  ⑦ 娱乐：道生一 一生二（商人购买多给一份，只在主机生效）。
    /// 界面与配置统一交给 DDExcNet-CUmodM 显示（模块 id = mpfix）。
    /// </summary>
    [BepInPlugin(Guid, "DDEN MP Fix", "1.2.0")]
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

        // ===== 联机修正（商人商品表 / 挖块） =====
        internal static ConfigEntry<bool> CfgTraderSnapshot;
        internal static ConfigEntry<bool> CfgBlockBreakSync;
        internal static ConfigEntry<float> CfgBlockDamageRatio;
        internal static ConfigEntry<float> CfgBlockBreakMinFraction;
        internal static ConfigEntry<bool> CfgCleanWearableVisuals;
        internal static ConfigEntry<bool> CfgOrphanItemFix;
        internal static ConfigEntry<int> CfgOrphanFixMax;
        internal static ConfigEntry<float> CfgSnapInterval;
        internal static ConfigEntry<float> CfgVisualInterval;
        internal static ConfigEntry<bool> CfgFreezeGhostItems;
        internal static ConfigEntry<int> CfgFreezeGhostMax;

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
                if (!Mp.CoreAvailable) return "缺少联机组件";
                if (!Mp.IsRunning) return "未联机";
                return Mp.IsServer ? "主机 · 同步中" : "客机 · 跟随中";
            }
        }
        public IEnumerable<ConfigEntryBase> ConfigEntries => Config.Entries();

        private void Awake()
        {
            Instance = this;
            LogSource = base.Logger;

            CfgChipEnabled = Config.Bind("无芯片同步", "Enabled", true,
                "总开关。关掉后这个模块不再管“有没有芯片”这件事。");
            CfgForceHost = Config.Bind("无芯片同步", "ForceHostUnchipped", true,
                "跟随主机：主机开了无芯片你就自动无芯片，主机关了你也恢复有芯片。\n" +
                "关掉后只在进局时跟一次，之后可以自己改。");
            CfgOverrideIndividualRule = Config.Bind("无芯片同步", "OverrideIndividualRule", true,
                "全员一致：名牌、附近语音、状态栏这些显示也跟着主机的设置走。\n" +
                "关掉可能出现“世界里是无芯片，但名牌还按有芯片显示”。");
            CfgWatchdogSeconds = Config.Bind("无芯片同步", "WatchdogSeconds", 30f,
                new ConfigDescription("自动核对的间隔（秒）。平时同步一次就够，默认 30 秒；0 = 不自动核对。",
                    new AcceptableValueRange<float>(0f, 600f)));
            CfgAnnounce = Config.Bind("无芯片同步", "AnnounceToOtherPlayers", false,
                "被自动纠正时在控制台提示一行（排查问题时用）。");

            CfgFixModItemCraftSpawn = Config.Bind("联机合成", "FixModItemCraftSpawn", true,
                "修“联机时合成模组加的物品，提示无法生成物品”。\n" +
                "开启后，模组加的东西在主机上也能正常做出来；地上的掉落物、世界生成也一起受益。");
            CfgRegisterSpawnedItems = Config.Bind("联机合成", "RegisterSpawnedItemsOnHost", true,
                "做出来的物品立刻在主机登记，联机才认它是“一件存在的物品”。\n" +
                "不登记的话，你穿上之后主机不认，装备会表现为“掉下去又回到身上”。");

            CfgStandaloneKey = Config.Bind("UI", "StandaloneKey", new KeyboardShortcut(KeyCode.F10),
                "没有管理器时，用这个键打开修复模块自己的小面板。");
            CfgWearFix = Config.Bind("穿戴兜底", "Enabled", true,
                "你在客机上穿好了，主机那边却没认（它眼里装备还在地上或正在掉）时，\n" +
                "自动请主机把装备正确穿到这名玩家身上。不会复制、不会掉落、不会重建物品。");
            CfgWearGrace = Config.Bind("穿戴兜底", "GraceSeconds", 1.5f,
                new ConfigDescription("穿好之后等多久再去问主机（秒）。太快可能还没同步完。",
                    new AcceptableValueRange<float>(0.3f, 10f)));
            CfgWearFixMax = Config.Bind("穿戴兜底", "MaxPerSession", 20,
                new ConfigDescription("一局最多自动处理几次，超过就停手。",
                    new AcceptableValueRange<int>(1, 200)));
            CfgWearRebuild = Config.Bind("穿戴兜底", "RebuildIfHostLacksItem", false,
                "只有主机真的找不到这件装备时才用：先脱下来放地上，再请主机造一件新的。\n" +
                "默认关闭（会有多出一件的风险），只当最后手段。");
            CfgSnapWornItems = Config.Bind("物品物理", "SnapWornItems", true,
                "装备或手持物品明明在身上，却浮在角色头顶附近时，把它贴回原位。\n" +
                "每 2 秒检查一次附近角色，只改位置，不改大小和角度。");
            CfgSnapWornMax = Config.Bind("物品物理", "SnapMaxPerSession", 100,
                new ConfigDescription("一局最多修正几次，超过就停手。",
                    new AcceptableValueRange<int>(1, 1000)));
            CfgPhysicsGuard = Config.Bind("物品物理", "GuardDropPhysics", true,
                "丢东西后补一道保险：让落地物品恢复正常碰撞，别穿墙掉到地图外面。\n" +
                "只处理真正丢在地上的物品，不会挪动它们。");
            CfgTraderSnapshot = Config.Bind("联机修正", "TraderSnapshotOnOpen", true,
                "每次打开商人界面，向主机要一份最新的商品表。\n" +
                "修“看到的商品和主机不一样，结果付了钱却拿不到东西”。");
            CfgBlockBreakSync = Config.Bind("联机修正", "SyncBlockBreak", true,
                "修“客机把方块挖掉了，过一会儿它又长回来”，以及挖的时候方块血量忽上忽下。\n" +
                "会把你这一镐的伤害和主机对齐，两边看到的结果一致。");
            CfgBlockDamageRatio = Config.Bind("联机修正", "BlockDamageRatio", 6f,
                new ConfigDescription("主机愿意接受你报的挖块伤害，最多是它自己算的几倍（原版是 3 倍）。\n" +
                                      "用了模组工具、或把世界设置的伤害倍率调高时，3 倍经常不够，表现为“怎么挖都挖不动”；\n" +
                                      "填 3 就是完全按原版。",
                    new AcceptableValueRange<float>(1f, 20f)));
            CfgBlockBreakMinFraction = Config.Bind("联机修正", "BlockBreakMinFraction", 0.5f,
                new ConfigDescription("判定门槛：主机记录里这块方块至少已经掉了这个比例的血，才会接受“已经挖穿”的结论。\n" +
                                      "调低更灵敏（更容易修好），调高更保守（更难靠一句上报敲穿硬墙）。",
                    new AcceptableValueRange<float>(0f, 0.95f)));
            CfgCleanWearableVisuals = Config.Bind("联机修正", "CleanStaleWearableVisuals", true,
                "修“人身上还挂着一件装备的贴图，其实并没有穿”。\n" +
                "只清理那层贴图，不碰真实物品。");
            CfgOrphanItemFix = Config.Bind("联机修正", "FixOrphanItems", true,
                "修“有件物品一直贴在角色身上：能打开看内容，但既不算穿着也不在背包里”。\n" +
                "判定持续一小会儿后：能放回背包就放回，放不进就从身上取下放到地上（物品不会消失）。");
            CfgOrphanFixMax = Config.Bind("联机修正", "MaxOrphanFixPerSession", 50,
                new ConfigDescription("一局最多处理几件，超过就停手。",
                    new AcceptableValueRange<int>(1, 500)));
            CfgFreezeGhostItems = Config.Bind("联机修正", "FreezeGhostItems", true,
                "修“一台客机掉线后，另一台客机帧数暴跌”。\n" +
                "掉线玩家的物品有时会留在你这边变成「没人认领、还在一直下坠」的东西，\n" +
                "它们会一直消耗物理计算，把人拖到十几帧。开启后会把这些物品**原地停下**（不移动、不删除）。");
            CfgFreezeGhostMax = Config.Bind("联机修正", "FreezeGhostItemsMaxPerSession", 400,
                new ConfigDescription("一局最多停下几件，超过就停手。",
                    new AcceptableValueRange<int>(0, 4000)));
            CfgSnapInterval = Config.Bind("性能", "ItemSnapScanSeconds", 2f,
                new ConfigDescription("物品贴合巡检的间隔（秒）。机器吃不消就调大。",
                    new AcceptableValueRange<float>(1f, 30f)));
            CfgVisualInterval = Config.Bind("性能", "WearableVisualScanSeconds", 2f,
                new ConfigDescription("装备贴图残留巡检的间隔（秒）。",
                    new AcceptableValueRange<float>(1f, 30f)));
            CfgDoublePurchase = Config.Bind("娱乐", "道生一 一生二", false,
                "道生一，一生二：在商人那里买东西时额外多送一份，直接放进背包。\n" +
                "纯娱乐开关，默认关闭。只需要主机打开，客机买东西同样会多一件。");
            CfgVerbose = Config.Bind("Debug", "Verbose", false, "输出更详细的日志（排查问题用）。");

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
            if (CfgBlockBreakSync.Value)
            {
                TryPatch(harmony, typeof(BlockDamageSyncFix.DamageBlock_Patch), "客机挖块伤害收敛（上报）");
                BlockDamageRelax.Install(harmony);
            }
            else
            {
                Log.Info("挖块收敛与比例放宽未启用（联机修正/SyncBlockBreak = false）。");
            }
            TryPatch(harmony, typeof(WearableVisualFix.Body_DropWearable_ClearPatch), "卸下时清理穿戴贴图");

            MigrateOldWatchdog();

            RegisterChannels();
            WearGhostFix.RegisterHostHandlers();
            TraderSnapshotFix.RegisterHost();
            BlockDamageSyncFix.RegisterHost();
            DoublePurchase.Install(harmony);
            // 三个周期巡检错峰起步：不再同一帧一起扫（主机上那一下就是可见的卡顿）
            WornItemSnap.PrimeScan(0.4f);
            WearableVisualFix.PrimeScan(1.0f);
            OrphanItemFix.PrimeScan(1.6f);
            DdenHub.Register(this);
            _standaloneKey = Compat.ResolveHotkey(CfgStandaloneKey, Guid, !DdenHub.ManagerLoaded);

            Log.Info("已加载：无芯片同步 + 联机物品/合成 + 穿戴兜底 + 物品物理 + 挖块收敛 + 娱乐开关。");
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
                ("联机组件", Mp.CoreAvailable ? "已就绪" : "未安装"),
                ("联机状态", !Mp.IsRunning ? "未联机" : (Mp.IsServer ? "主机（我把设置同步给别人）" : "客机（跟随主机）")),
                ("主机的设置", _hasHostValue ? (_lastHostValue ? "无芯片" : "有芯片") : "还没拿到"),
                ("本机世界", Game.InWorld ? (WorldGeneration.world.unchippedMode ? "无芯片" : "有芯片") : "还没进世界"),
                ("已自动纠正", _appliedCount + " 次"),
                ("最近一次原因", ReasonText(_lastReason))
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
                $"合成修复：{(CfgFixModItemCraftSpawn.Value ? "开" : "关")}     " +
                $"物品登记：{(CfgRegisterSpawnedItems.Value ? "开" : "关")}", DdenUI.Row);
            y += 22f;

            if (DdenUI.Button(new Rect(area.x, y, 150f, 24f), "立即按主机同步", true)) ForceApply();
            if (DdenUI.Button(new Rect(area.x + 158f, y, 130f, 24f), "再问一次主机")) RequestNow();
            GUI.enabled = Mp.IsRunning && Mp.IsServer;
            if (DdenUI.Button(new Rect(area.x + 296f, y, 140f, 24f), "在主机上切换")) ToggleHostState();
            GUI.enabled = true;

            y += 32f;
            GUI.Label(new Rect(area.x, y, area.width, 60f),
                "无芯片：联机时以主机的设置为准，本机自动跟着改（默认每 30 秒核对一次）。\n" +
                "合成 / 物品：模组加的东西在联机里也能正常做出来、捡起来、穿上去。\n" +
                $"挖块：两边的方块血量与破坏结果保持一致（信任上限 {CfgBlockDamageRatio.Value:0.#} 倍、判定门槛 {CfgBlockBreakMinFraction.Value:P0}）。",
                DdenUI.Wrap);
        }

        /// <summary>把内部原因码翻成人话（这些串只用来记原因，不该原样显示给玩家）。</summary>
        private static string ReasonText(string reason)
        {
            if (string.IsNullOrEmpty(reason) || reason == "-") return "-";
            if (reason.IndexOf("runSettings", StringComparison.Ordinal) >= 0) return "还没进世界（设置已记下，进局生效）";
            if (reason.StartsWith("request", StringComparison.Ordinal)) return "向主机查询";
            if (reason.StartsWith("push", StringComparison.Ordinal)) return "主机主动同步";
            if (reason.StartsWith("snapshot", StringComparison.Ordinal)) return "进局时同步";
            if (reason.StartsWith("watchdog", StringComparison.Ordinal)) return "定时核对";
            if (reason.StartsWith("manual", StringComparison.Ordinal)) return "手动纠正";
            if (reason.StartsWith("seed-only", StringComparison.Ordinal)) return "只跟主机初始值";
            return "已同步";
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
                OrphanItemFix.Tick();
                StaleItemFreezer.Tick();

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
