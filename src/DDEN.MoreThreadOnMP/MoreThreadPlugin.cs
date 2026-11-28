using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using DDEN.MoreThreadOnMP.Core;
using DDEN.MoreThreadOnMP.Integration;
using DDEN.Shared;
using UnityEngine;

namespace DDEN.MoreThreadOnMP
{
    [BepInPlugin(Guid, "DDEN MoreThreadOnMP", "0.2.0")]
    [BepInDependency("CasualtiesMP", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class MoreThreadPlugin : BaseUnityPlugin, IDdenModule, IDdenModuleVersion, IDdenModuleGate, IDdenModuleActivity, IDdenModuleHealth
    {
        public const string Guid = "com.dden.morethreadonmp";
        private static readonly DdenTextCatalog Text = new DdenTextCatalog(typeof(MoreThreadPlugin).Assembly, "DDEN.MoreThreadOnMP.Resources.localization.en.json");
        private ConfigEntry<bool> _enabled, _observe, _suppress;
        private ConfigEntry<int> _radius;
        private ConfigEntry<float> _sampleInterval;
        private RegressionValidationHooks _validation;
        private ThreadSampler _sampler;
        private LogChannel _log;
        private string _failure;
        private (int Radius, int Effective, bool Suppress, bool Observer, bool Host)? _reportedMode;
        private bool _wasEnabled, _showAllThreads, _cachedShowAll;
        private int _mainId;
        private double _nextReport;
        private ThreadSnapshot _heatSnapshot;
        private ThreadLoad[] _heatRows = Array.Empty<ThreadLoad>();
        private static readonly float[] SampleChoices = { 0.5f, 1f, 3f };
        private static readonly int[] RadiusChoices = { 4, 2, 1 };
        private GUIStyle _heatBadge;
        private Vector2 _cardsScroll;
        public string Id => "morethreadonmp";
        public string DisplayName => L("联机性能验证（MTOMP）");
        public string Version => "0.2.0-alpha";
        public string MinimumManagerVersion => "2.0.0";
        public string TestedManagerVersion => "2.1.0";
        public string MinimumSharedVersion => "2.0.1";
        public string TestedSharedVersion => "2.0.1";
        public int RequiredSharedApiVersion => DdenApi.Version;
        public bool Ready => Mp.TogetherReady;
        public bool ModuleEnabled => _enabled?.Value == true;
        public string MasterKey => "实验线程/Enabled";
        public bool IsRunning => ModuleEnabled && (_sampler != null || (_validation != null && Mp.IsRunning && Mp.IsServer
            && ((_radius.Value != 4 && _validation.RadiusProblem == null) || (_suppress.Value && _validation.DiagnosticProblem == null))));
        public DdenHealth Health => !ModuleEnabled ? DdenHealth.Ok : _failure != null || _validation?.Problem != null || _validation?.SampleProblem != null || _sampler?.Snapshot.Error != null ? DdenHealth.Warn : DdenHealth.Ok;
        public string HealthNote => Health == DdenHealth.Ok ? "" : (_failure ?? _validation?.Problem ?? _validation?.SampleProblem ?? _sampler?.Snapshot.Error ?? "");
        public string Summary => !ModuleEnabled ? L("验证关闭") : Health != DdenHealth.Ok ? L("部分验证不可用，保留原逻辑")
            : !Mp.IsRunning || !Mp.IsServer ? L("等待主机开服") : _radius.Value == 4 && !_suppress.Value ? L("原版基线") : L("对照条件已启用");
        public IEnumerable<ConfigEntryBase> ConfigEntries => Config.Entries();
        private static string L(string text) => Text.Text(text);
        private void Awake()
        {
            _log = DDEN.Shared.Log.For(Logger, "MoreThreadOnMP");
            _mainId = ThreadRoles.Register("main");
            DdenLocalization.Register(Config, Text);
            _enabled = Config.Bind("实验线程", "Enabled", false, "启用联机性能验证。默认关闭。");
            _radius = Config.Bind("回归验证", "HostSimulationRadius", 4,
                new ConfigDescription("主机模拟半径：4为原版9×9，2为5×5，1为旧版3×3。默认保留原版。", new AcceptableValueList<int>(4, 2, 1)));
            _suppress = Config.Bind("回归验证", "SuppressDiagnosticScans", false, "跳过MP主机的周期诊断扫描；不停止同步注册。默认关闭。");
            _observe = Config.Bind("线程观察", "Enabled", false, "显示线程CPU负载。默认关闭，进行帧率对照时保持同一设置。");
            _sampleInterval = Config.Bind("实验线程", "ThreadSampleSeconds", 3f,
                new ConfigDescription("线程负载采样间隔：0.5、1或3秒，默认3秒；仅影响负载显示。", new AcceptableValueList<float>(0.5f, 1f, 3f)));
            DdenDocs.AddStructured("实验线程", "Enabled", "启用独立的主机性能对照。", "定位版本更新后的性能回退。",
                "默认保留原版模拟范围与诊断扫描。每次只改变一个条件。", true, DdenPerformanceImpact.Medium);
            DdenDocs.AddStructured("回归验证", "HostSimulationRadius", "比较不同模拟范围的主机负担。", "验证5.5.3之后模拟范围扩大的影响。",
                "仅主机生效，所有玩家位置仍参与计算。缩小范围会减少屏外模拟，需检查远处机关及分散玩家行为；关闭恢复原范围，唤醒由原MP预算处理。", true, DdenPerformanceImpact.High);
            DdenDocs.AddStructured("回归验证", "SuppressDiagnosticScans", "隔离周期诊断扫描造成的开销。", "观察周期长帧是否减少。",
                "暂停周期诊断扫描与其腐坏计数，清空旧诊断历史；物品腐坏、存档、收发与同步注册保持原版。", true, DdenPerformanceImpact.Medium);
            DdenDocs.AddStructured("线程观察", "Enabled", "观察各线程CPU负载。", "辅助比较主线程与其他线程的占用。",
                "只读本进程线程时间，不修改CPU亲和性；线程采样本身有开销。", false, DdenPerformanceImpact.Low);
            DdenDocs.AddStructured("实验线程", "ThreadSampleSeconds", "调整线程负载显示的采样间隔。", "按需要平衡显示更新速度与采样开销。",
                "只调整负载显示，不改变网络收发、游戏更新和消息处理频率。", false, DdenPerformanceImpact.Low);
            DdenHub.Register(this);
        }
        private void Update()
        {
            if (ModuleEnabled && !_wasEnabled) { _failure = null; _reportedMode = null; }
            _wasEnabled = ModuleEnabled;
            if (!ModuleEnabled) { StopValidation(); StopSampler(); return; }
            try
            {
                if (!_observe.Value) StopSampler();
                else if (_sampler == null) _sampler = new ThreadSampler((int)(_sampleInterval.Value * 1000));
                else if (_sampler.IntervalMilliseconds != (int)(_sampleInterval.Value * 1000)) _sampler.SetInterval((int)(_sampleInterval.Value * 1000));
                if (!Ready) { StopValidation(); return; }
                if (_validation == null && _failure == null)
                {
                    _validation = new RegressionValidationHooks(report: message => _log.Warn(message));
                    _validation.Install();
                }
                if (_validation == null) return;
                _validation.Configure(true, _radius.Value, _suppress.Value);
                _validation.Tick();
                var mode = (Radius: _radius.Value, Effective: _validation.EffectiveRadius, Suppress: _suppress.Value,
                    Observer: _observe.Value, Host: Mp.IsRunning && Mp.IsServer);
                bool changed = mode != _reportedMode;
                bool report = mode.Host && Time.realtimeSinceStartupAsDouble >= _nextReport;
                if (changed || report)
                {
                    string description = "radius=" + mode.Radius + "; effective=" + mode.Effective
                        + "; suppressScans=" + mode.Suppress + "; observer=" + mode.Observer + "; host=" + mode.Host;
                    if (changed) { _reportedMode = mode; _log.Info("Regression validation mode: " + description); }
                    if (report)
                    {
                        _nextReport = Time.realtimeSinceStartupAsDouble + 30;
                        _log.Info("Regression validation: " + description + "; simulatedChunks=" + _validation.SimulatedChunks
                            + "; skippedScans=" + _validation.SkippedScans + "; samplePeakMs=" + _validation.PeakSampleMs.ToString("0.00"));
                    }
                }
            }
            catch (Exception error)
            { _failure = error.GetType().Name + ": " + error.Message; _log.Exception(error, "Regression validation disabled"); StopValidation(); }
        }
        private void StopValidation()
        {
            var validation = _validation; _validation = null;
            try { validation?.Dispose(); }
            catch (Exception error) { _failure = error.Message; _log.Exception(error, "Regression validation cleanup failed"); }
            _nextReport = 0;
        }
        private void StopSampler()
        { var sampler = _sampler; _sampler = null; sampler?.Dispose(); }
        public void DrawPage(Rect area)
        {
            GUI.Label(new Rect(area.x, area.y, area.width - 200, 27), DisplayName + " · " + Version, DdenUI.Title);
            if (DdenUI.Button(new Rect(area.xMax - 178, area.y, 178, 27), ModuleEnabled ? L("总开关：已启用") : L("总开关：已关闭"), ModuleEnabled)) _enabled.Value = !ModuleEnabled;
            GUI.Label(new Rect(area.x, area.y + 32, area.width - 140, 23), Summary, DdenUI.Wrap);
            if (DdenUI.Button(new Rect(area.xMax - 130, area.y + 31, 130, 25), L("原版基线")))
            { _radius.Value = 4; _suppress.Value = false; _observe.Value = false; }
            var observe = new Rect(area.x, area.y + 62, 170, 25);
            if (DdenUI.Button(observe, L("线程观察") + ": " + (_observe.Value ? L("已启用") : L("已关闭")), _observe.Value)) _observe.Value = !_observe.Value;
            for (int i = 0; i < SampleChoices.Length; i++)
                if (DdenUI.Button(new Rect(area.x + 181 + i * 48, area.y + 62, 44, 25), SampleChoices[i].ToString("0.#") + "s", _sampleInterval.Value == SampleChoices[i])) _sampleInterval.Value = SampleChoices[i];
            float top = area.y + 98;
            if (_observe.Value) top += DrawThreads(new Rect(area.x, top, area.width, Mathf.Clamp(area.height * .26f, 110, 190))) + 12;
            float height = 190, contentWidth = Mathf.Max(100, area.width - 12);
            _cardsScroll = DdenUI.BeginScrollView(new Rect(area.x, top, area.width, Mathf.Max(40, area.yMax - top)), _cardsScroll, new Rect(0, 0, contentWidth, height * 2 + 12));
            try
            {
                DrawRadiusCard(new Rect(0, 0, contentWidth, height));
                DrawScanCard(new Rect(0, height + 12, contentWidth, height));
            }
            finally { DdenUI.EndScrollView(); }
        }
        private void CardBase(Rect rect, string title, string problem)
        {
            DdenUI.Fill(rect, DdenUI.Panel);
            DdenUI.Fill(new Rect(rect.x, rect.y, 5, rect.height), problem != null ? DdenUI.Warn : DdenUI.TextDim);
            GUI.Label(new Rect(rect.x + 16, rect.y + 10, rect.width - 32, 25), title, DdenUI.Title);
        }
        private void DrawRadiusCard(Rect rect)
        {
            string problem = _failure ?? _validation?.RadiusProblem ?? _validation?.SampleProblem;
            CardBase(rect, L("主机模拟范围对照"), problem);
            float width = (rect.width - 44) / 3;
            for (int i = 0; i < RadiusChoices.Length; i++)
            {
                int radius = RadiusChoices[i];
                string label = radius == 4 ? L("原版9×9") : radius == 2 ? "5×5" : L("旧版3×3");
                if (DdenUI.Button(new Rect(rect.x + 16 + i * (width + 6), rect.y + 43, width, 26), label, _radius.Value == radius)) _radius.Value = radius;
            }
            int effective = _validation?.EffectiveRadius ?? 4;
            int chunks = _validation?.SimulatedChunks ?? -1;
            string details = string.Format(L("实际窗口 {0}×{0} · 模拟表区块 {1}\n采样峰值 {2:0.00} ms"), effective * 2 + 1,
                chunks < 0 ? L("不可用") : chunks.ToString(), _validation?.PeakSampleMs ?? 0)
                + "\n" + L("仅主机生效。缩小范围会减少屏外模拟；每次只改变一个条件。")
                + (problem == null ? "" : "\n" + problem);
            GUI.Label(new Rect(rect.x + 16, rect.y + 79, rect.width - 32, rect.height - 83), details, DdenUI.Wrap);
        }
        private void DrawScanCard(Rect rect)
        {
            string problem = _failure ?? _validation?.DiagnosticProblem;
            CardBase(rect, L("跳过MP周期诊断扫描"), problem);
            if (DdenUI.Button(new Rect(rect.x + 16, rect.y + 43, 155, 26), _suppress.Value ? L("已启用") : L("保留原扫描"), _suppress.Value)) _suppress.Value = !_suppress.Value;
            string details = string.Format(L("已跳过 {0} 次诊断入口"), _validation?.SkippedScans ?? 0) + "\n"
                + L("仅主机生效；保留物体同步与业务注册。不会跳过整个服务器更新。")
                + "\n" + L("先记录原版基线，再单独启用此项；切换开服状态前先导出性能记录。")
                + (problem == null ? "" : "\n" + problem);
            GUI.Label(new Rect(rect.x + 16, rect.y + 79, rect.width - 32, rect.height - 83), details, DdenUI.Wrap);
        }
        private float DrawThreads(Rect rect)
        {
            var snapshot = _sampler?.Snapshot ?? new ThreadSnapshot();
            if (DdenUI.Button(new Rect(rect.xMax - 104, rect.y, 104, 23), _showAllThreads ? L("仅显示重点") : L("显示全部"), _showAllThreads)) _showAllThreads = !_showAllThreads;
            if (!ReferenceEquals(_heatSnapshot, snapshot) || _cachedShowAll != _showAllThreads)
            {
                _heatRows = ThreadHeatmap.Select(snapshot, _showAllThreads); _heatSnapshot = snapshot; _cachedShowAll = _showAllThreads;
            }
            GUI.Label(new Rect(rect.x, rect.y, rect.width - 116, 23), _sampler == null ? L("线程观察已关闭") : snapshot.Rows.Length == 0 ? L(snapshot.Error == null ? "正在采样线程负载…" : "线程负载采样不可用")
                : string.Format(L("显示 {0}/{1} 线程 · 活跃 {2} · 工作线程 {3} · {4:0.#}s采样"), _heatRows.Length, snapshot.Rows.Length, snapshot.Active, snapshot.Workers, _sampleInterval.Value), DdenUI.Row);
            if (snapshot.Error != null) { GUI.Label(new Rect(rect.x, rect.y + 27, rect.width, 40), snapshot.Error, DdenUI.Wrap); return 70; }
            var grid = new ThreadGrid(_heatRows.Length, rect.width, rect.height - 52);
            string hovered = null;
            for (int i = 0; i < _heatRows.Length; i++)
            {
                var row = _heatRows[i];
                var square = new Rect(rect.x + (i % grid.Columns) * (grid.Cell + grid.Gap), rect.y + 29 + (i / grid.Columns) * (grid.Cell + grid.Gap), grid.Cell, grid.Cell);
                Color color = !row.Available ? DdenUI.TextDim : row.Load < 0.5 ? Color.Lerp(DdenUI.Good, Color.yellow, (float)row.Load * 2)
                    : Color.Lerp(Color.yellow, DdenUI.Bad, (float)(row.Load - 0.5) * 2);
                DdenUI.Fill(square, color);
                if (grid.Cell >= 16 && ThreadHeatmap.IsCore(row.Role))
                {
                    if (_heatBadge == null) { _heatBadge = new GUIStyle(DdenUI.Tiny); _heatBadge.normal.textColor = new Color(.08f, .1f, .12f); }
                    GUI.Label(square, Badge(row.Role), _heatBadge);
                }
                if (!square.Contains(Event.current.mousePosition)) continue;
                string name = string.IsNullOrEmpty(row.Name) ? L("系统未提供线程名") : row.Name;
                string load = row.Available ? (row.Load * 100).ToString("0.0") + "%" : L("采样不可用");
                string info = "TID " + row.Id + " · " + Role(row.Role) + " · " + name + "\n"
                    + string.Format(L("当前CPU：{0}\n近6秒采样峰值：{1:0.0}%\n百分比以一个逻辑核满载为100%。"), load, row.RecentLoad * 100);
                DdenUI.Hover(square, info);
                hovered = "TID " + row.Id + " · " + Role(row.Role) + " · " + name + " · CPU " + load;
            }
            var legend = new Rect(rect.x, rect.y + 33 + grid.Height, rect.width, 21);
            string explanation = L("每格代表线程；默认显示主线程和近期CPU达到1%的其他线程。");
            GUI.Label(legend, DdenUI.ElideToWidth(hovered ?? explanation, legend.width, DdenUI.Sub), DdenUI.Sub);
            if (hovered == null) DdenUI.Hover(legend, explanation);
            return grid.Height + 57;
        }
        private static string Badge(string role) => L("主");
        private static string Role(string role) => role == "main" ? L("游戏主线程") : role == "monitor" ? L("负载采样线程") : L("游戏线程");
        public bool DrawSettings(Rect area) => false;
        private void OnDisable() { StopValidation(); StopSampler(); }
        private void OnApplicationQuit() { StopValidation(); StopSampler(); }
        private void OnDestroy()
        { StopValidation(); StopSampler(); ThreadRoles.Remove(_mainId); DdenLocalization.Unregister(Config); }
    }
}
