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
    [BepInPlugin(Guid, "DDEN MP Fix", "1.7.0")]
    [BepInDependency("net.cucorelib", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("CasualtiesMP", BepInDependency.DependencyFlags.SoftDependency)]
    public class MPfixPlugin : BaseUnityPlugin, IDdenModule, IDdenModuleGate, IDdenModuleHealth, IDdenConfigPolicy, IDdenConfigAvailability
    {
        public const string Guid = "com.dden.mpfix";
        public const string Title = "多人修复";

        internal static MPfixPlugin Instance;
        internal static ManualLogSource LogSource;
        internal static LogChannel Log;

        // ===== 无芯片同步 =====
        internal static ConfigEntry<bool> CfgMasterEnabled;
        internal static ConfigEntry<bool> CfgUseHostAuthority;
        internal static AuthoritySetting<bool> CfgChipEnabled;
        internal static AuthoritySetting<bool> CfgForceHost;
        internal static AuthoritySetting<bool> CfgOverrideIndividualRule;
        internal static AuthoritySetting<float> CfgWatchdogSeconds;
        internal static AuthoritySetting<bool> CfgAnnounce;

        // ===== 联机物品/合成 =====
        internal static AuthoritySetting<bool> CfgFixModItemCraftSpawn;

        // ===== 玩家名称（只改显示，不改变连接和存档身份） =====
        internal static AuthoritySetting<bool> CfgFixPlayerNames;

        // ===== 穿戴兜底 =====
        internal static AuthoritySetting<bool> CfgWearFix;
        internal static AuthoritySetting<int> CfgWearFixMax;

        // ===== 物品物理保险 =====
        internal static AuthoritySetting<bool> CfgPhysicsGuard;

        // ===== 联机修正（商人商品表 / 挖块 / 掉线遗留物品） =====
        internal static AuthoritySetting<bool> CfgTraderSnapshot;
        internal static AuthoritySetting<bool> CfgBlockBreakSync;
        internal static AuthoritySetting<float> CfgBlockBreakMinFraction;
        internal static AuthoritySetting<bool> CfgSyncBuildingDamage;
        internal static AuthoritySetting<bool> CfgSyncBodyDamage;
        internal static AuthoritySetting<bool> CfgAggressiveConverge;
        internal static AuthoritySetting<bool> CfgQuietTraderUIModItems;
        internal static AuthoritySetting<bool> CfgRelayGhostItems;

        // ===== 娱乐 =====
        internal static AuthoritySetting<bool> CfgDoublePurchase;

        // ===== 其它 =====
        internal static AuthoritySetting<KeyboardShortcut> CfgStandaloneKey;
        internal static AuthoritySetting<bool> CfgVerbose;
        private AuthorityOverlay _configOverlay;
        private HostConfigSync _authority;
        private FeatureDependencies _dependencies;

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
        private bool _repairRunning;

        private KeyCode _standaloneKey = KeyCode.F10;
        private bool _standaloneVisible;
        private Rect _standaloneRect;
        private Vector2 _standaloneScroll;

        // ===== IDdenModule =====
        public string Id => "mpfix";

        /// <summary>模块真正可以动手吗：总开关打开 **且** 环境版本已核对/被用户显式忽略。</summary>
        internal static bool Active =>
            CfgMasterEnabled != null && CfgMasterEnabled.Value && !DdenGate.Blocked;

        /// <summary>管理器据此把本模块的配置项整体置灰（版本闸门拦住 / 总开关关掉时）。</summary>
        public bool ModuleEnabled => Active;

        /// <summary>主开关行：模块被闸门关掉时它仍然可点（否则再也打不开）。</summary>
        public string MasterKey => "总开关/启用本模块（MasterEnabled）";
        public string ConfigNotice => !string.IsNullOrEmpty(_authority?.Notice) ? _authority.Notice
            : _dependencies?.BlockedCount > 0 ? "部分功能缺少前置，已停用" : "";
        public bool CanEditConfig(ConfigEntryBase entry, bool normallyInteractive)
            => (_dependencies?.Available(entry) ?? true) && (ReferenceEquals(entry, CfgMasterEnabled) || ReferenceEquals(entry, CfgUseHostAuthority)
                || (normallyInteractive && !(_authority?.Locked ?? false)));
        public object ConfigDisplayValue(ConfigEntryBase entry) => entry.SettingType == typeof(bool) && !(_dependencies?.Available(entry) ?? true)
            ? false : _configOverlay?.Display(entry) ?? entry.BoxedValue;
        public string ConfigUnavailableReason(ConfigEntryBase entry) => _dependencies?.Reason(entry);

        private void InitializeAuthority()
        {
            _configOverlay = new AuthorityOverlay(AuthoritySettings.All, ApplyEffectiveSettingChange);
            if (!Compat.PluginLoaded("net.cucorelib") || !Compat.PluginLoaded("CasualtiesMP")) return;
            _authority = new HostConfigSync(_configOverlay, () => CfgUseHostAuthority.Value, AuthorityConnection.Read,
                () => Time.realtimeSinceStartup, token => Mp.Broadcast(HostConfigSync.NoticeChannel, token, false),
                (token, response) => Mp.RequestServer(HostConfigSync.RequestChannel, token, response),
                message => Log.Info("[主机配置] " + message));
            object connection = null;
            var requests = new Dictionary<uint, float>();
            bool server = MpChannels.TryRegisterSender(HostConfigSync.RequestChannel, (sender, token) =>
            {
                var peer = AuthorityConnection.Read();
                if (!peer.Running || !peer.Server) return null;
                if (!ReferenceEquals(connection, peer.Connection)) { connection = peer.Connection; requests.Clear(); }
                float now = Time.realtimeSinceStartup;
                if (requests.TryGetValue(sender, out float previous) && now - previous < 0.25f) return new JObject { ["error"] = "rate" };
                if (requests.Count >= 64) requests.Clear();
                requests[sender] = now;
                return _authority.HandleRequest(token);
            });
            bool client = Mp.RegisterClientHandler(HostConfigSync.NoticeChannel, token => _authority.OnNotice(token));
            if (!server || !client || !AuthorityConnection.Available)
                NotePatchError("主机权威配置", "配置通道或连接标识不可用");
            else ClearPatchError("主机权威配置");
        }

        private static void ApplyEffectiveSettingChange(ConfigEntryBase entry)
        {
            if (ReferenceEquals(entry, CfgStandaloneKey.Local))
            {
                if (Instance != null) Instance._standaloneKey = CfgStandaloneKey.Value.MainKey;
                return; // A temporary host key or restoring it must never rewrite local key preferences.
            }
            ApplySettingChange(entry);
        }

        internal bool CheckAuthorityConfig()
        {
            if (Mp.IsRunning || Game.InWorld) return true;
            var expected = new HashSet<ConfigEntryBase>();
            foreach (var entry in Config.Entries())
                if (!ReferenceEquals(entry, CfgMasterEnabled) && !ReferenceEquals(entry, CfgUseHostAuthority)) expected.Add(entry);
            var test = new AuthorityOverlay(AuthoritySettings.All, null);
            try
            {
                var snapshot = test.Capture(System.Guid.NewGuid().ToString("N"), 1, true);
                if (snapshot.Values.Count != expected.Count) return false;
                foreach (var setting in AuthoritySettings.All) if (!expected.Remove(setting.Entry)) return false;
                if (!AuthoritySnapshot.TryRead(snapshot.ToJson(true), out var decoded) || !test.Apply(decoded)) return false;
                foreach (var setting in AuthoritySettings.All)
                    if (setting.SerializeLocal() != snapshot.Values[setting.Id].Text) return false;
                return CanEditConfig(CfgMasterEnabled, false) && CanEditConfig(CfgUseHostAuthority, false) && AuthorityConnection.Available;
            }
            finally { test.Release(); }
        }

        /// <summary>健康度：版本核对没通过 = 严重（红）；有补丁装失败 = 有错误（黄）；其余正常。</summary>
        public DdenHealth Health
        {
            get
            {
                if (DdenGate.Blocked) return DdenHealth.Severe;
                if (_patchErrors.Count > 0) return DdenHealth.Warn;
                return DdenHealth.Ok;
            }
        }

        public string HealthNote
        {
            get
            {
                if (DdenGate.Blocked) return "版本核对未通过";
                if (_patchErrors.Count > 0)
                {
                    foreach (var kv in _patchErrors) return kv.Key + "（" + kv.Value + "）";
                }
                return null;
            }
        }

        private static bool _autoDisabledByGate;

        /// <summary>“因闸门自动关掉总开关”的持久化标记：进程重启后仍能自动恢复（内存标志会丢）。</summary>
        private static string GateFlagPath =>
            System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "DDEN", "gate-autodisabled.flag");

        private static void SetGateFlag(bool on)
        {
            try
            {
                string path = GateFlagPath;
                string dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                if (on) System.IO.File.WriteAllText(path, DateTime.Now.ToString("s"));
                else if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            }
            catch (Exception ex) { Log.Debug("写闸门标记失败：" + ex.Message); }
        }

        private static bool HasGateFlag()
        {
            try { return System.IO.File.Exists(GateFlagPath); }
            catch { return false; }
        }

        /// <summary>给界面用的一句话状态。</summary>
        internal static string GateText =>
            !Compat.PluginLoaded("CasualtiesMP") ? "未安装多人联机组件，相关功能已停用"
            : DdenGate.Blocked
                ? DdenGate.NoticeText + "（" + DdenGate.Reason + "）"
                : "版本核对通过：" + DdenGate.GameVersion + " / 联机 " + DdenGate.MpVersion;

        /// <summary>把每个开关的完整说明登记给提示框（功能 / 修复的问题 / 解决方案 / 实验性 / 性能影响）。</summary>
        private static void RegisterDocs()
        {
            DdenDocs.Add("总开关", "启用本模块（MasterEnabled）",
                "整个多人修复的总开关，关掉后所有修复都不动手。",
                "想临时排除“是不是 DDEN 引起的”问题时用它一键停手。", "只做开关判断，不改游戏逻辑。");
            DdenDocs.Add("总开关", "采用权威主机配置（UseHostAuthorityConfig）",
                "主机启用后统一下发设置，客机启用后临时采用主机设置。",
                "希望同一局使用一致的修复配置。", "进局和主机配置变化时同步；客机暂时锁定其他设置，退出或取消采用后恢复本机值。",
                "否", "按需发送，不定时重复下发；默认关闭");

            DdenDocs.Add("无芯片同步", "启用（Enabled）",
                "同步主机无芯片规则，修正主机无芯片而客机仍有芯片。",
                "联机时主机开了无芯片，自己这边却没跟着变（或反过来）。",
                "应用主机的实际规则与本地玩家状态；进局、主机变更和主动请求均可同步。", "否", "无");
            DdenDocs.Add("无芯片同步", "强制跟随主机（ForceHostUnchipped）",
                "按已收到的主机状态持续纠正本地无芯片规则。",
                "主机已开无芯片，客机规则仍被本地状态覆盖。",
                "收到主机值时应用，并按核对间隔纠正本地漂移；关闭后仍会接受首次、变化和请求结果。", "否", "无");
            DdenDocs.Add("无芯片同步", "全员显示一致（OverrideIndividualRule）",
                "关闭各自选择模式的规则，让无芯片设置统一跟随主机。",
                "个人无芯片选项使客机与主机的实际规则不同。",
                "将联机组件的个人无芯片规则关闭，再统一世界与本地玩家状态。", "否", "无");
            DdenDocs.Add("无芯片同步", "核对间隔秒（WatchdogSeconds）",
                "本地规则与最近主机状态的核对间隔；0关闭此项纠正。",
                "本地无芯片规则在同步后又发生漂移。",
                "按间隔对照已缓存的主机状态，发现不同则纠正；不是按此间隔发网络请求。", "否", "极低");
            DdenDocs.Add("无芯片同步", "纠正时提示（AnnounceToOtherPlayers）",
                "被自动纠正时在控制台提示一行。",
                "排查时不知道有没有触发纠正。", "只在纠正发生时打印一行说明。", "否", "无");

            DdenDocs.Add("联机合成", "修“无法生成物品”（FixModItemCraftSpawn）",
                "合成结果生成失败时尝试补救。",
                "联机合成模组加的配方时提示“无法生成物品”，东西出不来。",
                "先沿用正常生成结果；只有主机合成未生成结果时才尝试补救。", "否", "仅失败时尝试");
            DdenDocs.Add("玩家名称", "尝试修复名称显示（FixPlayerNames）",
                "尝试恢复中文名字，修正空名字、问号和数字名字。",
                "中文名字被截掉、变成问号或长串数字，主机名字为空。",
                "从玩家资料恢复显示名称，只使用游戏现有字体，缺少的字显示为 ?；只改变本机显示，不改变存档身份。",
                "是 · 需要多人实测", "缓存名称与字体；不会逐帧扫描场景");

            DdenDocs.Add("穿戴兜底", "启用穿戴兜底（Enabled）",
                "客机穿上装备但主机没认时，请主机把装备正确穿给这名玩家。",
                "客机穿上的装备在图里跟着人跑、主机那边却当它在地上。",
                "客机上报一次，主机按这名玩家重新穿一次并把状态推给所有人；不复制、不掉落。",
                "否", "极低（只在出错时触发，默认关闭）");
            DdenDocs.Add("穿戴兜底", "一局最多处理几次（MaxPerSession）",
                "一局最多自动处理几次，超过就停手。",
                "某个物品反复触发兜底导致来回折腾。", "计数到上限后本局不再处理。", "否", "无");

            DdenDocs.Add("物品物理", "丢弃物解除穿墙（GuardDropPhysics）",
                "玩家亲手丢出物品时，补开被关掉的碰撞体。",
                "丢出去的物品穿过地板和墙体一路掉出地图。",
                "只在“丢出”这一下把被关掉的碰撞体打开；不挪位置、不改物理模式，正压在实体里时不动。",
                "否", "无（只在丢东西那一下执行）");

            DdenDocs.Add("联机修正", "打开商人时对齐商品表（TraderSnapshotOnOpen）",
                "每次打开商人界面，向主机要一份最新的商品表。",
                "看到的商品和主机不一样，付了钱却拿不到东西。",
                "打开界面时请主机重发一次完整商品表。", "否", "极低（只在开界面时一次）");
            DdenDocs.Add("联机修正", "修“挖方块”-伤害重算（SyncBlockBreak）",
                "默认补齐主机挖块漏算的世界伤害倍率；激进时还校验碎块报告。",
                "挖方块时血量掉一截又弹回去，或者挖掉的方块过一会儿又长回来。",
                "主机替客机结算时漏了一个世界伤害倍率，这里把它补上再交给联机框架原本的判定。",
                "否", "极低（主机攻击时补算，保留原判定）");
            DdenDocs.Add("联机修正", "修“打箱子/植物/动物”-伤害重算（SyncBuildingDamage）",
                "默认补齐主机近战漏算的世界伤害倍率；激进时校验客机结果。",
                "打储物箱、植物、动物时血量掉一下又弹回去。",
                "主机使用该客机的远端角色状态补齐倍率，保留原结算；激进时仅在校验通过后使用客机报告。", "否", "低（只在攻击时执行）");
            DdenDocs.Add("联机修正", "修“打角色/肢体”-伤害重算（SyncBodyDamage）",
                "默认补齐角色肢体近战的世界伤害倍率；激进时校验客机结果。",
                "客机近战打中角色或肢体类敌人，血量先减少又回升。",
                "主机使用该客机的远端角色状态补齐倍率，保留原结算；激进时仅在校验通过后使用客机报告。",
                "否", "低（只在攻击时执行）");
            DdenDocs.Add("联机修正", "挖掘修复-激进（实验性 · ⚠帧数影响）（AggressiveConverge）",
                "对已开启分支校验客机上报；方块分批补差，单位随原攻击结算。",
                "只补世界伤害倍率后，两边的攻击伤害仍然不一致。",
                "方块挖碎后由主机检查并补差；单位和角色附加报告到原攻击，校验后只结算一次，拒绝则回退基础补算。",
                "是", "大量挖掘仍有开销；已减少消息数量并分散处理，实际影响需联机测试");
            DdenDocs.Add("联机修正", "挖块判定门槛-仅激进时生效（BlockBreakMinFraction）",
                "激进模式下，主机账上这块至少掉这么多才接受“已挖穿”。",
                "激进模式被拿来一镐敲穿硬墙。", "按比例做一次可信度门槛判断。",
                "否（仅激进模式生效）", "无");
            DdenDocs.Add("联机修正", "离场物品广播清理（RelayGhostItems）",
                "玩家离场时，按主机给的清单清掉客机本地的遗留副本。",
                "一名玩家掉线后，另一台客机帧数暴跌、地上的东西还在原地闪。",
                "主机记录离场物品的准确身份，其他客机核对确认后分帧清理；有人持有或内容物未确认时保留。",
                "否", "低（只处理退场清单，大量物品分帧清理）");
            DdenDocs.Add("联机修正", "商人界面不再刷“prefab 为空”（QuietTraderUIModItems）",
                "商人界面自检不再对模组物品报错。",
                "打开商人界面时控制台一直刷“prefab 为空”的错误。",
                "只对“模组物品不在游戏资源里”这种情况静音，真出错照常报警。", "否", "无");

            DdenDocs.Add("娱乐", "道生一 一生二（DoublePurchase）",
                "商人购买必定给两个；其它造物品行为有 20% 概率多一件。",
                "不需要修什么，纯玩法开关。", "购买后补一件给买家；造物时按概率多生成一件。",
                "否（玩法）", "低（多一件物品）");
            DdenDocs.Add("界面", "独立面板热键（StandaloneKey）",
                "没有管理器时，用它打开修复面板。", "只装了本模块没装管理器时没有界面。",
                "按下这个键打开独立小面板。", "否", "无");
            DdenDocs.Add("调试", "详细日志（Verbose）",
                "输出更详细的日志。", "排查问题需要更多现场信息。", "把调试级日志也写进 BepInEx 日志。",
                "否", "低（只在开日志时）");
        }

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
            AuthoritySettings.All.Clear();

            CfgMasterEnabled = Config.Bind("总开关", "启用本模块（MasterEnabled）", true,
                "整个多人修复的总开关；关掉后所有修复都不动手。");
            CfgUseHostAuthority = Config.Bind("总开关", "采用权威主机配置（UseHostAuthorityConfig）", false,
                "主机统一下发设置；客机临时采用，退出联机后恢复本机配置。");

            CfgChipEnabled = Config.Bind("无芯片同步", "启用（Enabled）", true,
                "同步主机无芯片规则，修正主机无芯片而客机仍有芯片。");
            CfgForceHost = Config.Bind("无芯片同步", "强制跟随主机（ForceHostUnchipped）", true,
                "按已收到的主机状态持续纠正本地无芯片规则。");
            CfgOverrideIndividualRule = Config.Bind("无芯片同步", "全员显示一致（OverrideIndividualRule）", true,
                "关闭各自选择模式的规则，让无芯片设置统一跟随主机。");
            CfgWatchdogSeconds = Config.Bind("无芯片同步", "核对间隔秒（WatchdogSeconds）", 30f,
                new ConfigDescription("本地规则与最近主机状态的核对间隔；0关闭此项纠正。",
                    new AcceptableValueRange<float>(0f, 600f)));
            CfgAnnounce = Config.Bind("无芯片同步", "纠正时提示（AnnounceToOtherPlayers）", false,
                "被自动纠正时在控制台提示一行。");

            CfgFixModItemCraftSpawn = Config.Bind("联机合成", "修“无法生成物品”（FixModItemCraftSpawn）", true,
                "合成结果生成失败时尝试补救，正常生成时不额外发放。");
            CfgFixPlayerNames = Config.Bind("玩家名称", "尝试修复名称显示（FixPlayerNames）", false,
                "尝试恢复中文名字，修正空名字、问号和数字名字；只改变本机显示。");

            CfgWearFix = Config.Bind("穿戴兜底", "启用穿戴兜底（Enabled）", false,
                "客机穿上装备但主机没认时，请主机把装备正确穿给这名玩家。");
            CfgWearFixMax = Config.Bind("穿戴兜底", "一局最多处理几次（MaxPerSession）", 20,
                new ConfigDescription("一局最多自动处理几次，超过就停手。",
                    new AcceptableValueRange<int>(1, 200)));
            CfgPhysicsGuard = Config.Bind("物品物理", "丢弃物解除穿墙（GuardDropPhysics）", true,
                "玩家亲手丢出物品时，补开被关掉的碰撞体。");

            CfgTraderSnapshot = Config.Bind("联机修正", "打开商人时对齐商品表（TraderSnapshotOnOpen）", true,
                "每次打开商人界面，向主机要一份最新的商品表。");
            CfgBlockBreakSync = Config.Bind("联机修正", "修“挖方块”-伤害重算（SyncBlockBreak）", true,
                "默认补齐主机挖块漏算的世界伤害倍率；激进时还校验碎块报告。");
            CfgSyncBuildingDamage = Config.Bind("联机修正", "修“打箱子/植物/动物”-伤害重算（SyncBuildingDamage）", true,
                "默认补齐主机近战漏算的世界伤害倍率；激进时校验客机结果。");
            CfgSyncBodyDamage = Config.Bind("联机修正", "修“打角色/肢体”-伤害重算（SyncBodyDamage）", true,
                "默认补齐角色肢体近战的世界伤害倍率；激进时校验客机结果。");
            CfgAggressiveConverge = Config.Bind("联机修正", "挖掘修复-激进（实验性 · ⚠帧数影响）（AggressiveConverge）", false,
                "对已开启分支校验客机上报；方块分批补差，单位随原攻击结算。");
            CfgBlockBreakMinFraction = Config.Bind("联机修正", "挖块判定门槛-仅激进时生效（BlockBreakMinFraction）", 0.5f,
                new ConfigDescription("激进模式下，主机账上这块至少掉这么多才接受“已挖穿”。",
                    new AcceptableValueRange<float>(0f, 0.95f)));
            CfgRelayGhostItems = Config.Bind("联机修正", "离场物品广播清理（RelayGhostItems）", true,
                "玩家离场时，按主机给的清单清掉客机本地的遗留副本。");
            CfgQuietTraderUIModItems = Config.Bind("联机修正", "商人界面不再刷“prefab 为空”（QuietTraderUIModItems）", true,
                "商人界面自检不再对模组物品报错。");
            CfgDoublePurchase = Config.Bind("娱乐", "道生一 一生二（DoublePurchase）", false,
                "商人购买必定给两个；其它造物品行为有 20% 概率多一件。");
            CfgStandaloneKey = Config.Bind("界面", "独立面板热键（StandaloneKey）", new KeyboardShortcut(KeyCode.F10),
                "没有管理器时，用它打开修复面板。");
            CfgVerbose = Config.Bind("调试", "详细日志（Verbose）", false, "输出更详细的日志。");

            Log = DDEN.Shared.Log.For(LogSource, "MPfix", CfgVerbose.Value);
            RegisterDocs();
            _dependencies = new FeatureDependencies(Config.Entries(), Compat.PluginLoaded);
            foreach (var setting in AuthoritySettings.All)
            {
                var captured = setting;
                setting.Available = () => _dependencies.Available(captured.Entry);
            }
            _dependencies.EnforceAll();
            if (_dependencies.BlockedCount > 0) Log.Info("[前置检查] 缺少所需模组的功能已关闭，相应配置不可用。");

            // 环境版本核对（用户口径 2026-11）：对不上且用户没在管理器「总览」打开「忽略核对环境版本」时，
            // 本模块整体不动手（主开关出口 Active 会返回 false），并在主界面挂醒目提示。
            DdenGate.RegisterParticipant(Guid);   // 本模块的修复依赖环境版本核对
            DdenGate.Probe(force: true);
            if (!Compat.PluginLoaded("CasualtiesMP"))
            {
                Log.Info("[前置检查] 未安装多人联机组件，修复与娱乐功能均已停用。");
            }
            else if (DdenGate.Blocked)
            {
                Log.Warn($"[版本核对] {DdenGate.Reason}");
                Log.Warn("[版本核对] 本模块暂不动手。到管理器「总览 → 忽略核对环境版本」打开它才会生效。");
            }
            else
            {
                Log.Info($"[版本核对] 通过：游戏 {DdenGate.GameVersion} · 联机组件 {DdenGate.MpVersion}" +
                         (DdenGate.IgnoreEnv ? "（用户已选择忽略核对）" : ""));
            }

            // 用户口径（2026-11）：改完配置立即生效，不要重启游戏。
            // 绝大多数开关本来就在“每次用到时读配置”，这里只额外处理两件必须在改动瞬间重算的事：
            //   ① 详细日志（Log 通道的 verbose 是缓存值）；② 独立面板热键（缓存在 _standaloneKey）。
            // 另外把“无芯片”那组改动直接触发一次同步，免得等下一个 watchdog 周期。
            try { Config.SettingChanged += (sender, args) => OnSettingChanged(args.ChangedSetting); }
            catch (Exception ex) { Logger.LogWarning("订阅配置变更失败（改配置后需重启才生效）：" + ex.Message); }

            Harmony harmony = new Harmony(Guid);
            bool hasMp = Compat.PluginLoaded("CasualtiesMP"), hasCore = Compat.PluginLoaded("net.cucorelib");
            if (hasMp)
            {
                TryPatch(harmony, typeof(WorldGeneration_SetUnchipped_Patch), "无芯片变更广播");
                TryPatch(harmony, typeof(Utils_Create_ModItemPatch), "联机合成/物品生成兜底");
                if (hasCore && !CraftSpawnScope.Install(harmony)) NotePatchError("联机合成/物品生成兜底", "合成入口不可用");
                if (!PlayerNameRepair.Install(harmony)) NotePatchError("玩家名称", "名称显示入口不可用");
                TryPatch(harmony, typeof(WearGhostFix.Body_WearWearable_RecordPatch), "穿戴兜底（幽灵装备）");
                TryPatch(harmony, typeof(ItemPhysicsGuard.Body_DropItem_GuardPatch), "物品掉落物理保险（丢出）");
                TryPatch(harmony, typeof(ItemPhysicsGuard.Body_DropWearable_GuardPatch), "物品掉落物理保险（卸下）");
                TryPatch(harmony, typeof(TraderSnapshotFix.ToggleTradeMenu_Postfix), "打开商人时索要商品表");

                // Install when prerequisites exist; runtime settings still take effect immediately.
                if (hasCore) TraderUIQuietFix.Install(harmony);
                TryPatch(harmony, typeof(BlockDamageSyncFix.DamageBlock_Patch), "客机挖块伤害收敛（上报）");
                TryPatch(harmony, typeof(BlockDamageSyncFix.WorldReset_Patch), "换层清理挖掘上报");
                BlockDamageRelax.Install(harmony);
                BuildingDamageRelax.Install(harmony);
                UnitDamageRepair.Install(harmony);
                MigrateOldWatchdog();
                if (hasCore)
                {
                    RegisterChannels();
                    WearGhostFix.RegisterHostHandlers();
                    TraderSnapshotFix.RegisterHost();
                    BlockDamageSyncFix.RegisterHost();
                }
                DoublePurchase.Install(harmony);
                if (hasCore) GhostItemRelay.Install(harmony);
            }
            _warmupUntil = Time.realtimeSinceStartup + 6f;      // 巡检耗时统计跳过冷启动
            DdenHub.Register(this);
            _standaloneKey = Compat.ResolveHotkey(CfgStandaloneKey, Guid, !DdenHub.ManagerLoaded);
            InitializeAuthority();

            Log.Info("已加载：无芯片同步 + 联机物品/合成 + 穿戴兜底 + 物品物理 + 挖块收敛 + 掉线遗留物品 + 娱乐开关。");
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
                Version v = Compat.PluginVersion("CasualtiesMP");
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
            Log.Info("[MPCOMPAT] ② 联机合成：优先沿用正常生成结果，仅在主机合成生成失败时兜底；不在前缀重复实例化");
            Log.Info("[MPCOMPAT] ③ 穿戴同步：ServerReceiver__ItemWearing 仍以 TryGetItem(登记表) 为门控；" +
                     "未登记的物品仍会被 decline → 网络登记仍需启用");
        }

        private void TryPatch(Harmony harmony, Type patchType, string what)
        {
            try
            {
                harmony.PatchAll(patchType);
                Log.Info("已启用补丁：" + what);
                ClearPatchError(what);
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "补丁失败：" + what);
                NotePatchError(what, ex.GetType().Name + " · " + ex.Message);
            }
        }

        // ===== 运行一览（用户口径 2026-11：单独页显示本次环境里每项修复的运行状况）=====

        internal enum FixState { Off, Running, Watching, Error }

        private static readonly Dictionary<string, string> _patchErrors =
            new Dictionary<string, string>(StringComparer.Ordinal);

        internal static void NotePatchError(string what, string reason)
        {
            if (string.IsNullOrEmpty(what)) return;
            _patchErrors[what] = reason ?? "未知原因";
        }

        internal static void ClearPatchError(string what)
        {
            if (string.IsNullOrEmpty(what)) return;
            _patchErrors.Remove(what);
        }

        /// <summary>某个补丁是否装成功过（成功 = 没有错误记录且已尝试过）。</summary>
        private static FixState PatchState(string what)
            => _patchErrors.ContainsKey(what) ? FixState.Error : FixState.Running;

        /// <summary>按当前配置 + 联机状态算出一行的状态。</summary>
        private FixState StateOf(bool switchOn, bool needsMp = true, string patch = null)
        {
            if (!Compat.PluginLoaded("CasualtiesMP") || DdenGate.Blocked || (CfgMasterEnabled != null && !CfgMasterEnabled.Value)) return FixState.Off;
            if (!switchOn) return FixState.Off;
            if (patch != null && _patchErrors.ContainsKey(patch)) return FixState.Error;
            if (needsMp && !Mp.IsRunning) return FixState.Watching;
            return FixState.Running;
        }

        private string NoteOf(string patch)
            => patch != null && _patchErrors.TryGetValue(patch, out string r) ? r : null;

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
            bool enabled = GUI.enabled;
            try { DrawStatusPage(area); }
            catch (Exception ex) { Log.Exception(ex, "绘制联机修复页面"); }
            finally { GUI.enabled = enabled; }
        }

        private void DrawStatusPage(Rect area)
        {
            float y = area.y;

            // 环境版本 / 总开关状态条（2026-11 用户口径：版本对不上就在这里说清楚）
            var gate = new Rect(area.x, y, area.width, DdenGate.Blocked ? 42f : 24f);
            DdenUI.Fill(gate, DdenGate.Blocked ? new Color(0.45f, 0.05f, 0.05f, 0.9f)
                                               : new Color(0.10f, 0.18f, 0.10f, 0.7f));
            DdenUI.Frame(gate, DdenGate.Blocked ? DdenUI.Bad : DdenUI.Good);
            DdenUI.Label(new Rect(gate.x + 8f, gate.y + 3f, gate.width - 16f, 18f),
                DdenGate.Blocked ? "<b>" + DdenGate.NoticeText + "</b>"
                                 : "本模块已启用 · " + GateText,
                DdenUI.Row);
            if (DdenGate.Blocked)
            {
                DdenUI.Label(new Rect(gate.x + 8f, gate.y + 22f, gate.width - 16f, 18f),
                    DdenGate.Reason + " 到「总览 → 忽略核对环境版本」打开后生效。", DdenUI.Sub);
            }
            y = gate.yMax + 6f;
            if (!string.IsNullOrEmpty(ConfigNotice))
            { DdenUI.Label(new Rect(area.x, y, area.width, 18f), DdenUI.ElideToWidth(ConfigNotice, area.width, DdenUI.Sub), DdenUI.Sub); y += 22f; }

            // ===== 运行一览 =====
            DdenUI.Label(new Rect(area.x, y, area.width, 16f), "运行一览", DdenUI.Sub);
            y += 18f;

            (string name, FixState state, string note)[] fixes =
            {
                ("无芯片同步", StateOf(CfgChipEnabled.Value), null),
                ("联机生成（模组物品）", StateOf(CfgFixModItemCraftSpawn.Value, true, "联机合成/物品生成兜底"), null),
                ("玩家名称显示", StateOf(CfgFixPlayerNames.Value, true, "玩家名称"), null),
                ("物品网络登记", StateOf(true, false), null),
                ("穿戴兜底", CfgWearFix.Value && Active ? FixState.Watching : FixState.Off, null),
                ("物品物理保险", StateOf(CfgPhysicsGuard.Value, false, "物品掉落物理保险（丢出）"), null),
                ("挖方块伤害重算", StateOf(CfgBlockBreakSync.Value), null),
                ("打单位伤害重算", StateOf(CfgSyncBuildingDamage.Value), null),
                ("打角色/肢体伤害重算", StateOf(CfgSyncBodyDamage.Value), null),
                ("挖掘修复-激进上报", StateOf(CfgAggressiveConverge.Value &&
                    (CfgBlockBreakSync.Value || CfgSyncBuildingDamage.Value || CfgSyncBodyDamage.Value)), null),
                ("商人商品表对齐", StateOf(CfgTraderSnapshot.Value), null),
                ("商人界面静音", StateOf(CfgQuietTraderUIModItems.Value, false), null),
                ("离场物品清理", StateOf(CfgRelayGhostItems.Value), null),
                ("娱乐：一生二", StateOf(CfgDoublePurchase.Value, false), null),
                ("环境版本核对", !Compat.PluginLoaded("CasualtiesMP") ? FixState.Off : DdenGate.Blocked ? FixState.Error : FixState.Running,
                    DdenGate.Blocked ? DdenGate.Reason : null)
            };

            for (int i = 0; i < fixes.Length; i++)
            {
                var cell = new Rect(area.x, y + i * 20f, area.width, 18f);
                var item = fixes[i];

                DdenUI.Fill(cell, new Color(1f, 1f, 1f, i % 2 == 0 ? 0.02f : 0.005f));
                DdenUI.Label(new Rect(cell.x + 6f, cell.y, cell.width - 100f, cell.height),
                    item.name, DdenUI.Sub);

                (Color c, string t) style = item.state switch
                {
                    FixState.Running => (DdenUI.Good, "运行中"),
                    FixState.Watching => (DdenUI.Accent, "监控中"),
                    FixState.Error => (DdenUI.Bad, "错误"),
                    _ => (new Color(0.6f, 0.62f, 0.66f, 1f), "关")
                };
                DdenUI.Chip(new Rect(cell.xMax - 88f, cell.y + 1f, 82f, 16f), style.t, style.c);

                if (item.state == FixState.Error && !string.IsNullOrEmpty(item.note))
                {
                    DdenUI.Hover(cell, "错误：" + item.note);
                }
            }
            y += fixes.Length * 20f + 8f;

            DrawUnchippedStatus(new Rect(area.x, y, area.width, 180f));
            y += 190f;
            DdenUI.Label(new Rect(area.x + 4f, y, 90f, 18f), "巡检开销", DdenUI.Sub);
            DdenUI.Label(new Rect(area.x + 96f, y, area.width - 100f, 18f), TickCostText, DdenUI.Sub);
        }

        private void DrawUnchippedStatus(Rect card)
        {
            bool running = Mp.IsRunning, server = running && Mp.IsServer, inWorld = Game.InWorld;
            bool ready = Mp.CoreAvailable;
            float left = card.x + 12f, width = card.width - 24f, half = (width - 12f) * 0.5f;

            DdenUI.Fill(card, DdenUI.Panel);
            DdenUI.Frame(card, DdenUI.AccentDim);
            DdenUI.Fill(new Rect(card.x, card.y, 2f, card.height), DdenUI.AccentDim);
            DdenUI.Label(new Rect(left, card.y + 9f, width - 158f, 24f), "无芯片同步", DdenUI.Title);
            DdenUI.Chip(new Rect(card.xMax - 160f, card.y + 12f, 76f, 19f),
                ready ? "组件就绪" : "组件缺失", ready ? DdenUI.Good : DdenUI.Bad);
            DdenUI.Chip(new Rect(card.xMax - 76f, card.y + 12f, 64f, 19f),
                !running ? "未联机" : server ? "主机" : "客机", running ? DdenUI.Accent : DdenUI.TextDim);

            string hostValue = !running ? "等待联机" : _hasHostValue ? (_lastHostValue ? "无芯片" : "有芯片") : "等待同步";
            string localValue = inWorld ? (WorldGeneration.world.unchippedMode ? "无芯片" : "有芯片") : "未进入世界";
            DrawStatusValue(new Rect(left, card.y + 43f, half, 50f), "主机设置", hostValue, running && _hasHostValue);
            DrawStatusValue(new Rect(left + half + 12f, card.y + 43f, half, 50f), "本机世界", localValue, inWorld);

            DdenUI.Label(new Rect(left, card.y + 103f, 50f, 19f), "已纠正", DdenUI.Sub);
            DdenUI.Label(new Rect(left + 52f, card.y + 103f, half - 52f, 19f), _appliedCount + " 次", DdenUI.Row);
            DdenUI.Label(new Rect(left + half + 12f, card.y + 103f, 58f, 19f), "最近记录", DdenUI.Sub);
            var reasonRect = new Rect(left + half + 72f, card.y + 103f, half - 60f, 19f);
            string reason = ReasonText(_lastReason);
            string visibleReason = DdenUI.ElideToWidth(reason, reasonRect.width, DdenUI.Sub);
            DdenUI.Label(reasonRect, visibleReason, DdenUI.Sub);
            if (visibleReason != reason) DdenUI.Hover(reasonRect, reason);

            DdenUI.Fill(new Rect(left, card.y + 131f, width, 1f), DdenUI.AccentDim);
            float buttonWidth = (width - 16f) / 3f;
            var apply = new Rect(left, card.y + 141f, buttonWidth, 27f);
            var request = new Rect(apply.xMax + 8f, apply.y, buttonWidth, apply.height);
            var toggle = new Rect(request.xMax + 8f, apply.y, buttonWidth, apply.height);
            bool enabled = GUI.enabled;
            try
            {
                bool canSync = enabled && Active && running;
                GUI.enabled = canSync;
                if (DdenUI.Button(apply, "同步到本机", canSync)) ForceApply();
                if (DdenUI.Button(request, "刷新主机状态")) RequestNow();
                GUI.enabled = canSync && server && WorldGeneration.world != null;
                if (DdenUI.Button(toggle, "切换无芯片")) ToggleHostState();
            }
            finally { GUI.enabled = enabled; }
            if (!running) DdenUI.Hover(new Rect(left, apply.y, width, apply.height), "联机后可使用同步操作。");
            else if (!server) DdenUI.Hover(toggle, "仅主机可切换世界设置。");
        }

        private static void DrawStatusValue(Rect area, string title, string value, bool known)
        {
            DdenUI.Fill(area, DdenUI.PanelAlt);
            DdenUI.Label(new Rect(area.x + 10f, area.y + 5f, area.width - 20f, 15f), title, DdenUI.Sub);
            DdenUI.Label(new Rect(area.x + 10f, area.y + 23f, area.width - 20f, 22f),
                known ? value : "<color=#9ea8b8>" + value + "</color>", DdenUI.Title);
        }

        /// <summary>把内部原因码翻成人话（这些串只用来记原因，不该原样显示给玩家）。</summary>
        private static string ReasonText(string reason)
        {
            if (string.IsNullOrEmpty(reason) || reason == "-") return "暂无";
            if (reason.IndexOf("runSettings", StringComparison.Ordinal) >= 0) return "待进入世界";
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
            if (!Active) return;
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
            if (!Active) return;
            Mp.RequestServer(UnchippedProtocol.RequestChannel, new JObject { ["ask"] = "unchipped" }, token =>
            {
                if (token == null) return;
                JToken v = token[UnchippedProtocol.StateField];
                if (v == null) return;
                OnHostState(v.Value<bool>(), "request(manual)", manual: true);
            });
        }

        public void ToggleHostState()
        {
            if (!Active) return;
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
                if (!Active || !CfgChipEnabled.Value || !Mp.IsRunning || !Mp.IsServer)
                    return new JObject { ["enabled"] = false };
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
            if (!Active || !CfgChipEnabled.Value || !Mp.IsServer) return null;
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

        private void OnHostState(bool value, string source, bool manual = false)
        {
            if (!Active) return;
            // 手动按钮（面板上的「立即按主机同步 / 再问一次主机」）不受「无芯片同步/启用」开关限制
            if (!CfgChipEnabled.Value && !manual) return;

            bool changed = !_hasHostValue || _lastHostValue != value;
            _hasHostValue = true;
            _lastHostValue = value;

            if (manual || CfgForceHost.Value) ApplyLocal(value, source);
            else if (changed) ApplyLocal(value, source + ":seed-only");
        }

        private void ApplyLocal(bool value, string reason)
        {
            if (!Active) return;
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

        /// <summary>配置改动即时生效：重算缓存值，必要时立刻触发一次同步。</summary>
        private static void OnSettingChanged(ConfigEntryBase entry)
        {
            try
            {
                if (entry == null) return;
                var p = Instance;
                if (p?._dependencies?.Enforce(entry) == true) return;
                if (p?._authority != null)
                {
                    p._authority.LocalChanged();
                    // Editing/reloading the local file may update saved preferences while the temporary host value stays effective.
                    if (p._authority.Locked && p._configOverlay.Contains(entry)) return;
                }
                ApplySettingChange(entry);
            }
            catch (Exception ex) { Log.Exception(ex, "更新主机配置状态"); }
        }

        private static void ApplySettingChange(ConfigEntryBase entry)
        {
            try
            {
                if (entry == null) return;
                if (ReferenceEquals(entry, CfgMasterEnabled) || ReferenceEquals(entry, CfgFixPlayerNames.Local)) PlayerNameRepair.Invalidate();
                if (ReferenceEquals(entry, CfgMasterEnabled) || ReferenceEquals(entry, CfgRelayGhostItems.Local))
                    GhostItemRelay.Reset();
                if (ReferenceEquals(entry, CfgMasterEnabled) || ReferenceEquals(entry, CfgWearFix.Local))
                    WearGhostFix.Reset();
                if (ReferenceEquals(entry, CfgMasterEnabled) || ReferenceEquals(entry, CfgTraderSnapshot.Local))
                    TraderSnapshotFix.Reset();

                if (ReferenceEquals(entry, CfgVerbose.Local))
                {
                    Log.Verbose = CfgVerbose.Value;
                    Log.Info($"详细日志已{(CfgVerbose.Value ? "打开" : "关闭")}（立即生效）。");
                    return;
                }

                if (ReferenceEquals(entry, CfgStandaloneKey.Local))
                {
                    MPfixPlugin p = Instance;
                    if (p != null) p._standaloneKey = p._configOverlay?.Locked == true
                        ? CfgStandaloneKey.Value.MainKey : Compat.ResolveHotkey(CfgStandaloneKey, Guid, false);
                    Log.Info($"独立面板热键已改为 {CfgStandaloneKey.Value.MainKey}（立即生效）。");
                    return;
                }

                // 无芯片那组：立刻推一次，不用等 watchdog
                if (entry.Definition != null && entry.Definition.Section == "无芯片同步")
                {
                    MPfixPlugin p = Instance;
                    if (p != null) { p._forcePushNextTick = true; p._nextWatchdog = 0f; }
                    Log.Info($"无芯片设置已更新：{entry.Definition.Key}（立即应用）。");
                    return;
                }

                Log.Debug($"配置已更新：{entry.Definition?.Section}/{entry.Definition?.Key}（立即生效）。");
            }
            catch (Exception ex)
            {
                Log.Debug("应用配置变更失败：" + ex.Message);
            }
        }

        private void Update()
        {
            try
            {
                _authority?.Tick(); // Must run even when the repair master switch or a repair branch is disabled.
                PlayerNameRepair.Tick();
                // 闸门联动（用户口径 2026-11）：版本对不上 → 自动把总开关关掉；核对通过/被忽略 → 自动恢复。
                if (CfgMasterEnabled != null)
                {
                    if (DdenGate.Blocked && CfgMasterEnabled.Value && !_autoDisabledByGate)
                    {
                        _autoDisabledByGate = true;
                        SetGateFlag(true);                 // 持久化：进程重启后也能自动恢复
                        CfgMasterEnabled.Value = false;
                        Log.Warn("[版本核对] 已自动关闭本模块总开关（版本不一致）；到「总览 → 忽略核对环境版本」可恢复。");
                    }
                    else if (!DdenGate.Blocked && CfgMasterEnabled.Value && HasGateFlag())
                    {
                        _autoDisabledByGate = false;       // 陈旧标记：这次环境正常了，清掉
                        SetGateFlag(false);
                    }
                    else if (!DdenGate.Blocked && !CfgMasterEnabled.Value && (HasGateFlag() || _autoDisabledByGate))
                    {
                        _autoDisabledByGate = false;
                        SetGateFlag(false);
                        CfgMasterEnabled.Value = true;
                        Log.Info("[版本核对] 已自动恢复本模块总开关。");
                    }
                }

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

                // 两个周期巡检（穿戴兜底 / 掉线遗留物品）：各自内部有节拍，这里只量"本模块每帧吃了多少"，
                // 每 60 秒汇总一条日志 —— 让"巡检是不是卡顿源"有数据，而不是靠感觉。
                bool repairRunning = Mp.IsRunning;
                if (_repairRunning != repairRunning)
                {
                    _repairRunning = repairRunning;
                    WearGhostFix.Reset();
                    TraderSnapshotFix.Reset();
                    _hasHostValue = false;
                    _wasClient = _requested = false;
                    _forcePushNextTick = true;
                    _nextWatchdog = 0f;
                }
                float t0 = Time.realtimeSinceStartup;
                WearGhostFix.Tick();
                float t1 = Time.realtimeSinceStartup;
                GhostItemRelay.Tick();
                BlockDamageSyncFix.Tick();   // 挖块上报攒批：到点把攒下的碎块一起发
                float t2 = Time.realtimeSinceStartup;

                ReportTickCost((t2 - t0) * 1000f, (t1 - t0) * 1000f, (t2 - t1) * 1000f);

                if (!Active || !CfgChipEnabled.Value) return;
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

        private float _tickSum;
        private float _tickMax;
        private int _tickCount;
        private float _tickLogAt = -99f;
        private float _warmupUntil;

        /// <summary>本模块 Tick 的平均/峰值耗时（毫秒，只统计热身之后的稳态）。状态页与日志都用它。</summary>
        internal static string TickCostText
        {
            get
            {
                MPfixPlugin p = Instance;
                if (p == null || p._tickCount <= 0) return "热身中…";
                return $"{p._tickSum / p._tickCount:0.000}ms / 峰值 {p._tickMax:0.000}ms";
            }
        }

        private void ReportTickCost(float total, float wear, float relayAndMining)
        {
            // 开局 6 秒是冷启动（JIT、资源加载），这段时间的数字没有参考价值，别记进来
            if (Time.realtimeSinceStartup < _warmupUntil) return;

            _tickSum += total;
            _tickCount++;
            if (total > _tickMax) _tickMax = total;

            // 至少攒 180 帧（约 3 秒）再看平均，避免"拿一次冷启动当结论"
            if (_tickCount < 180) return;
            if (Time.unscaledTime - _tickLogAt < 60f) return;
            _tickLogAt = Time.unscaledTime;

            float avg = _tickSum / _tickCount;
            float peak = _tickMax;
            _tickSum = 0f;
            _tickMax = 0f;
            _tickCount = 0;

            // 平均 <0.5ms 且峰值 <2ms 就是噪声级，不刷日志（状态页随时能看）
            if (avg < 0.5f && peak < 2f) return;

            MPfixPlugin.Log.Info($"[巡检] 本模块每帧合计 平均 {avg:0.000}ms / 峰值 {peak:0.000}ms " +
                                 $"（最近一次：穿戴兜底 {wear:0.000} / 遗留清理和挖块 {relayAndMining:0.000}）");
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
            try { _authority?.Stop(); } catch (Exception ex) { Log.Debug("恢复本机配置失败：" + ex.Message); }
            PlayerNameRepair.Stop();
            GhostItemRelay.Reset();
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
            if (!MPfixPlugin.Active) return;
            if (plugin == null || !MPfixPlugin.CfgChipEnabled.Value) return;
            if (!Mp.IsRunning || !Mp.IsServer) return;

            plugin.RequestImmediatePush(unchipped);
        }
    }
}
