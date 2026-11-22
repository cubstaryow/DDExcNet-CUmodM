using System;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;

namespace HeatingCageFix
{
    [BepInPlugin(Guid, "ET-Fix", "1.7.4")]
    [BepInDependency("com.a4kk.enhancedtools", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("net.cucorelib", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("CasualtiesMP", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.dden.heatingcagefix";
        internal static Plugin Instance;
        internal static ConfigEntry<bool> Master, Decay, Heat, Remember, Network, Verbose;
        internal static ConfigEntry<bool> NoBatteryEnabled, ReenableLight;
        internal static ConfigEntry<string> ExtraIds;
        internal static bool Ready, SaveReady, NetworkReady;
        internal static string Problem = "尚未就绪";
        private Harmony _harmony;
        private float _nextTick;
        private bool _enforcing;
        internal static bool Active => Ready && Master.Value;
        internal static void Error(Exception ex, string action) => Instance?.Logger.LogWarning("[ET-Fix] " + action + "：" + ex.Message);
        internal static void Debug(string message) { if (Verbose?.Value == true) Instance?.Logger.LogInfo("[ET-Fix] " + message); }
        internal static void LogInfo(string message) => Instance?.Logger.LogInfo(message);

        private void Awake()
        {
            Instance = this;
            try
            {
                Master = Config.Bind("总开关", "启用修复", true, "启用加热笼修复。需要 Enhanced Tools 和 CUCoreLib。");
                Decay = Config.Bind("加热笼", "关闭时停止腐坏", true, "关闭加热笼后停止自然腐坏，开启后恢复正常消耗，不回填已经损失的耐久。");
                Heat = Config.Bind("加热笼", "关闭时禁止火源合成", true, "关闭的加热笼不再充当合成热源；其他热源和加热笼维修配方照常使用。");
                Remember = Config.Bind("存档", "记住开关状态", true, "随游戏正常存档记录加热笼开关，读档后恢复。");
                Network = Config.Bind("联机", "同步开关状态", true, "向主机确认加热笼最终开关状态，处理晚加入和连续切换。多人建议各端安装。");
                Verbose = Config.Bind("调试", "详细日志", false, "记录状态切换和同步失败，排查问题时开启。");
                NoBatteryEnabled = Config.Bind("EnhancedTools 无电池", "启用无电池修复", true, "增强钛砍刀/钛镐没有电池时也能使用（不改 condition，仍显示 0%）。");
                ReenableLight = Config.Bind("EnhancedTools 无电池", "装电池后自动点灯", true, "装上电池后自动重新点亮工具灯。");
                ExtraIds = Config.Bind("EnhancedTools 无电池", "额外物品 id", "", "额外应用无电池修复的物品 id，逗号分隔。");
                _harmony = new Harmony(Guid);
                if (!Chainloader.PluginInfos.TryGetValue("com.a4kk.enhancedtools", out var et) || et.Instance == null)
                    throw new InvalidOperationException("缺少 Enhanced Tools");
                if (!Chainloader.PluginInfos.ContainsKey("net.cucorelib")) throw new InvalidOperationException("缺少 CUCoreLib");
                CageRuntime.Install(_harmony, et.Instance.GetType().Assembly.GetType("HeatingCageFire"));
                ETBattery.Install(_harmony);
                Ready = true; Problem = null;
                try { CagePatches.InstallPersistence(_harmony); SaveReady = true; }
                catch (Exception ex) { Remember.Value = false; Error(ex, "存档状态恢复不可用"); }
                try { if (Chainloader.PluginInfos.ContainsKey("CasualtiesMP")) { CageNetwork.Install(); NetworkReady = true; } else Network.Value = false; }
                catch (Exception ex) { Network.Value = false; Error(ex, "联机状态确认不可用"); }
                Config.SettingChanged += (_, __) => Changed();
                Changed();
                Logger.LogInfo("[ET-Fix] 已加载：按实例控制腐坏与热源资格，配置可交给 DDEN 管理。");
            }
            catch (Exception ex)
            {
                Ready = false; Problem = ex.Message;
                _harmony?.UnpatchSelf();
                if (Master != null) { EnforceUnavailable(); Config.SettingChanged += (_, __) => Changed(); }
                Error(ex, "修复已停用");
            }
        }
        private void Start()
        {
            try
            {
                if (Ready && Item.allItems != null)
                    foreach (var item in Item.allItems.ToArray()) if (item != null && item.id == CageRules.ItemId) CageRuntime.Track(item);
                DdenBridge.DdenDocs();
            }
            catch (Exception ex) { Error(ex, "初始化已有加热笼"); }
        }
        private void Changed()
        {
            if (_enforcing) return;
            try
            {
                EnforceUnavailable();
                CageRuntime.ApplySettings(); CageNetwork.Reset();
            }
            catch (Exception ex) { Error(ex, "应用配置"); }
        }
        private void EnforceUnavailable()
        {
            _enforcing = true;
            try
            {
                if (!Ready) { Master.Value = false; Decay.Value = false; Heat.Value = false; }
                if (!Ready || !SaveReady) Remember.Value = false;
                if (!Ready || !NetworkReady) Network.Value = false;
            }
            finally { _enforcing = false; }
        }
        private void Update()
        {
            if (UnityEngine.Time.unscaledTime < _nextTick) return;
            _nextTick = UnityEngine.Time.unscaledTime + .25f;
            try { CageRuntime.Tick(); CageNetwork.Tick(); }
            catch (Exception ex) { Error(ex, "更新加热笼状态"); }
        }
        private void OnDestroy()
        {
            try { CageNetwork.Reset(); _harmony?.UnpatchSelf(); CageRuntime.Clear(); }
            catch (Exception ex) { Error(ex, "卸载修复"); }
            Ready = false; Problem = "修复已卸载"; Instance = null;
        }
    }
    // Public static convention only: no assembly or plugin dependency on DDEN.
    public static class DdenBridge
    {
        public static ConfigFile DdenConfig => Plugin.Instance?.Config;
        public static string DdenTitle => "ET-Fix";
        private static string _language = "zh-CN";
        public static void DdenSetLanguage(string language)
        {
            _language = string.Equals(language, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "zh-CN";
        }
        public static string DdenLocalize(string original, string language)
        {
            if (string.IsNullOrEmpty(original)) return original;
            if (!string.Equals(language, "en", StringComparison.OrdinalIgnoreCase)) return original;
            return DdenText.En(original);
        }

        public static string DdenSummary => !Plugin.Ready ? "功能不可用：" + Plugin.Problem : Plugin.Master.Value ? "已启用" : "已关闭";
        public static string DdenUnavailable(ConfigEntryBase entry)
        {
            if (entry == Plugin.Verbose) return null;
            if (!Plugin.Ready) return Plugin.Problem;
            if (entry == Plugin.Remember && !Plugin.SaveReady) return "当前存档接口不可用";
            if (entry == Plugin.Network && !Plugin.NetworkReady) return "当前联机接口不可用";
            if (entry != Plugin.Master && !Plugin.Master.Value) return "修复总开关已关闭";
            return null;
        }
        public static void DdenDocs()
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "DDEN.Shared")?.GetType("DDEN.Shared.DdenDocs");
            var method = type?.GetMethod("Add", new[] { typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(bool) });
            if (method == null || DdenConfig == null) return;
            foreach (var pair in DdenConfig)
            {
                bool noBattery = pair.Value == Plugin.NoBatteryEnabled || pair.Value == Plugin.ReenableLight;
                bool normal = noBattery || pair.Value == Plugin.Master || pair.Value == Plugin.Decay
                    || pair.Value == Plugin.Heat || pair.Value == Plugin.Remember || pair.Value == Plugin.Network;
                method.Invoke(null, new object[] { pair.Key.Section, pair.Key.Key, pair.Value.Description.Description,
                    pair.Value == Plugin.Verbose ? "状态异常时缺少排查信息。" : (noBattery ? "工具没有电池时无法使用。" : "加热笼关闭后仍腐坏或仍被当作热源。"),
                    pair.Value == Plugin.Verbose ? "按需记录状态和同步结果。" : (noBattery ? "装电池后恢复可用并重新点灯。" : "按每只加热笼的实际开关状态处理。"),
                    normal ? "否" : "是 · 尚需游戏内验证",
                    noBattery ? "只影响增强钛工具，开销可忽略。" : "仅处理加热笼；联机按变化确认状态。", false });
            }
        }
    }
    internal static class DdenText
    {
        internal static string En(string zh)
        {
            if (zh == null) return null;
            if (zh.StartsWith("功能不可用：", StringComparison.Ordinal))
                return "Unavailable: " + En(zh.Substring("功能不可用：".Length));
            switch (zh)
            {
                case "总开关": return "Master";
                case "启用修复": return "Enable fix";
                case "启用加热笼修复。需要 Enhanced Tools 和 CUCoreLib。": return "Enable the heating-cage fix. Requires Enhanced Tools and CUCoreLib.";
                case "加热笼": return "Heating cage";
                case "关闭时停止腐坏": return "Stop decay when off";
                case "关闭加热笼后停止自然腐坏，开启后恢复正常消耗，不回填已经损失的耐久。": return "Stops natural decay while the cage is off; normal consumption resumes when on. Lost durability is not restored.";
                case "关闭时禁止火源合成": return "No heat-source crafting when off";
                case "关闭的加热笼不再充当合成热源；其他热源和加热笼维修配方照常使用。": return "An off cage no longer counts as a crafting heat source; other heat sources and cage repair recipes still work.";
                case "存档": return "Save";
                case "记住开关状态": return "Remember switch state";
                case "随游戏正常存档记录加热笼开关，读档后恢复。": return "Records the cage switch in normal saves and restores it on load.";
                case "联机": return "Multiplayer";
                case "同步开关状态": return "Sync switch state";
                case "向主机确认加热笼最终开关状态，处理晚加入和连续切换。多人建议各端安装。": return "Confirms the final cage switch state with the host, handling late join and rapid toggles. Recommended on every client.";
                case "调试": return "Debug";
                case "详细日志": return "Verbose log";
                case "记录状态切换和同步失败，排查问题时开启。": return "Logs state changes and sync failures; enable when troubleshooting.";
                case "EnhancedTools 无电池": return "EnhancedTools no-battery";
                case "启用无电池修复": return "Enable no-battery fix";
                case "增强钛砍刀/钛镐没有电池时也能使用（不改 condition，仍显示 0%）。": return "Enhanced titanium machete/pickaxe work without a battery (condition untouched, still shows 0%).";
                case "装电池后自动点灯": return "Re-light after battery";
                case "装上电池后自动重新点亮工具灯。": return "Re-lights the tool lamp after a battery is installed.";
                case "额外物品 id": return "Extra item ids";
                case "额外应用无电池修复的物品 id，逗号分隔。": return "Extra item ids to apply the no-battery fix to, comma-separated.";
                case "已启用": return "Enabled";
                case "已关闭": return "Disabled";
                case "当前存档接口不可用": return "Save interface unavailable";
                case "当前联机接口不可用": return "Multiplayer interface unavailable";
                case "修复总开关已关闭": return "Master switch is off";
                case "状态异常时缺少排查信息。": return "Missing diagnostics when state is wrong.";
                case "加热笼关闭后仍腐坏或仍被当作热源。": return "Cage still decays or counts as a heat source while off.";
                case "按需记录状态和同步结果。": return "Logs state and sync results on demand.";
                case "按每只加热笼的实际开关状态处理。": return "Handles each cage by its actual switch state.";
                case "工具没有电池时无法使用。": return "Tool cannot be used without a battery.";
                case "装电池后恢复可用并重新点灯。": return "Restores usability and re-lights after a battery is installed.";
                case "只影响增强钛工具，开销可忽略。": return "Only affects enhanced titanium tools; negligible cost.";
                case "仅处理加热笼；联机按变化确认状态。": return "Only touches the heating cage; confirms state changes in multiplayer.";
                case "是 · 尚需游戏内验证": return "Yes · needs in-game verification";
                case "否": return "No";
                default: return zh;
            }
        }
    }

}
