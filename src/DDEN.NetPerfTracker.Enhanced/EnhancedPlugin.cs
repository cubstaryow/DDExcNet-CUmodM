using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using DDEN.Shared;
using Newtonsoft.Json;
using UnityEngine;
using static DDEN.NetPerfTracker.Enhanced.ModuleText;

namespace DDEN.NetPerfTracker.Enhanced
{
    [BepInPlugin(Guid, "DDEN NetPerfTracker Enhanced", "0.2.1")]
    [BepInDependency("com.dden.perf", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("CasualtiesMP", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class EnhancedPlugin : BaseUnityPlugin, IDdenModule, IDdenModuleVersion, IDdenModuleGate, IDdenModuleActivity, IDdenModuleHealth
    {
        public const string Guid = "com.dden.perfdeep";
        internal static EnhancedPlugin Instance;
        internal readonly DeepHooks Hooks = new DeepHooks();
        internal ConfigEntry<bool> EnabledEntry;
        private Func<bool> _captureGate;
        private bool _lastWanted;
        private LogChannel _log;
        private float _nextResolve;
        public string Id => "perfdeep";
        public string DisplayName => L("性能跟踪器增强");
        public string Version => "0.2.1-alpha";
        public string MinimumManagerVersion => "2.0.0";
        public string TestedManagerVersion => "2.1.0";
        public string MinimumSharedVersion => "2.0.1";
        public string TestedSharedVersion => "2.0.1";
        public int RequiredSharedApiVersion => DdenApi.Version;
        public bool Ready => _captureGate != null && Mp.TogetherReady;
        public bool ModuleEnabled => EnabledEntry != null && EnabledEntry.Value;
        public bool IsRunning => Hooks.Active;
        public string MasterKey => "诊断/启用深层计时";
        public string Summary => Hooks.Status == "unpatch_failed" ? L("计时未能停止，请重启游戏")
            : Hooks.Status == "unavailable" ? L("计时方法不可用") : Hooks.Status == "partial" ? L("部分方法可用")
            : Hooks.Active ? L("正在细分计时") : ModuleEnabled ? L("等待性能采样与Together探针") : L("深层计时已关闭");
        public DdenHealth Health => Hooks.Status == "unpatch_failed" || Hooks.Status == "unavailable" || Hooks.Status == "partial" ? DdenHealth.Warn : DdenHealth.Ok;
        public string HealthNote => Health == DdenHealth.Warn ? Summary : "";
        public IEnumerable<ConfigEntryBase> ConfigEntries => Config.Entries();
        private void Awake()
        {
            Instance = this;
            _log = DDEN.Shared.Log.For(Logger, "PerfEnhanced");
            ConfigMigration.Retire(Config, "UI/StandaloneKey");
            DdenLocalization.Register(Config, ModuleText.Catalog);
            EnabledEntry = Config.Bind("诊断", "启用深层计时", false,
                "细分联机与物体更新耗时；需要启用性能采样和Together探针。默认关闭。");
            DdenDocs.AddStructured("诊断", "启用深层计时", "细分Together调用组耗时。", "定位主机人数相关开销与客机退场后的卡顿。",
                "仅增加计时；保留原方法及异常。暂停采样、关闭Together探针或此开关时撤下本模块的钩子。", true,
                DdenPerformanceImpact.Medium, performanceNote: "默认关闭；开启后有额外开销，切换后等待几秒再比较");
            DdenHub.Register(this);
        }
        private void Update()
        {
            if (_captureGate == null && Time.realtimeSinceStartup >= _nextResolve)
            {
                _nextResolve = Time.realtimeSinceStartup + 1;
                var getter = Reflect.Type("DDEN.NetPerfTracker.NetPerfTrackerPlugin")?.GetProperty("CanDeepCapture")?.GetGetMethod();
                var capacity = Reflect.Type("DDEN.NetPerfTracker.NetPerfTrackerPlugin")?.GetProperty("DeepCaptureCapacity")?.GetValue(null);
                if (getter != null && capacity is int maxRows && maxRows >= TimingTargets.All.Length) try { _captureGate = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), getter); } catch { }
            }
            bool wanted = ModuleEnabled && Ready && _captureGate();
            if (wanted != _lastWanted)
            {
                _lastWanted = wanted;
                try
                {
                    if (wanted) Hooks.Start(Time.realtimeSinceStartupAsDouble);
                    else Hooks.Stop(Time.realtimeSinceStartupAsDouble);
                }
                catch (Exception error) { Hooks.Stop(Time.realtimeSinceStartupAsDouble); _log.Warn("Deep timing unavailable: " + error.GetType().Name); }
            }
        }
        private void LateUpdate() { if (Hooks.Active) Hooks.Ledger.CompleteFrame(Time.realtimeSinceStartupAsDouble); }
        public void DrawPage(Rect area)
        {
            GUI.Label(new Rect(area.x, area.y, area.width, 26), DisplayName + " · " + Version, DdenUI.Title);
            if (DdenUI.Button(new Rect(area.x, area.y + 32, 245, 25), ModuleEnabled ? L("深层计时：开") : L("深层计时：关"), ModuleEnabled))
                EnabledEntry.Value = !EnabledEntry.Value;
            GUI.Label(new Rect(area.x, area.y + 65, area.width, 190), L("先启用性能采样和Together探针，再开启深层计时。数据在“告警与联机反馈”页查看。\n切换后等待几秒，再比较相同场景。各项耗时可能互相包含，请勿相加。")
                + "\n" + Summary, DdenUI.Wrap);
        }
        public bool DrawSettings(Rect area) => false;

        internal void StopCapture() { _lastWanted = false; Hooks.Stop(Time.realtimeSinceStartupAsDouble); }
        private void OnDisable() { StopCapture(); DdenHub.SetPanelVisible(Id, false); }
        private void OnDestroy() { Hooks.Stop(Time.realtimeSinceStartupAsDouble); DdenLocalization.Unregister(Config); Instance = null; }
    }
    // Reflection-only bridge: absence or a schema mismatch never prevents the base tracker from loading.
    public static class DeepProbeBridge
    {
        public static int ApiVersion => 1;
        public static bool Present => EnhancedPlugin.Instance != null;
        public static bool Enabled => EnhancedPlugin.Instance?.ModuleEnabled == true;
        public static void SetEnabled(bool value) { if (Present) EnhancedPlugin.Instance.EnabledEntry.Value = value; }
        public static void Stop() { if (Present) EnhancedPlugin.Instance.StopCapture(); }
        public static void Clear() { if (Present) EnhancedPlugin.Instance.Hooks.Ledger.Clear(Time.realtimeSinceStartupAsDouble); }
        public static string CaptureEvidence() => Present ? DeepPatchEvidence.Capture() : null;
        public static string CaptureWindow() => Present ? JsonConvert.SerializeObject(
            EnhancedPlugin.Instance.Hooks.Capture(Time.realtimeSinceStartupAsDouble, Enabled),
            new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None, MaxDepth = 8 }) : null;
    }
}
