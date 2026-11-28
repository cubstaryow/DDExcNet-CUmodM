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
    [BepInPlugin(Guid, "DDEN MoreThreadOnMP", "0.1.0")]
    [BepInDependency("CasualtiesMP", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class MoreThreadPlugin : BaseUnityPlugin, IDdenModule, IDdenModuleVersion, IDdenModuleGate, IDdenModuleActivity, IDdenModuleHealth
    {
        public const string Guid = "com.dden.morethreadonmp";
        private static readonly DdenTextCatalog Text = new DdenTextCatalog(typeof(MoreThreadPlugin).Assembly, "DDEN.MoreThreadOnMP.Resources.localization.en.json");
        private ConfigEntry<bool> _enabled, _receive, _send, _prepare, _pin;
        private ConfigEntry<float> _budget, _sampleInterval;
        private ConfigEntry<int> _cap;
        private readonly TransportHooks _hooks = new TransportHooks();
        private readonly WindowsAffinity _affinity = new WindowsAffinity();
        private MpBindings _bindings;
        private MpThreadRuntime _runtime;
        private ThreadSampler _sampler;
        private LogChannel _log;
        private bool _bindFailed, _wasEnabled;
        private string _failure;
        private double _nextAffinity;
        private int _mainId;
        private bool _showAllThreads, _cachedShowAll;
        private ThreadSnapshot _heatSnapshot;
        private ThreadLoad[] _heatRows = Array.Empty<ThreadLoad>();
        private static readonly float[] SampleChoices = { 0.5f, 1f, 3f };
        private GUIStyle _heatBadge;
        public string Id => "morethreadonmp";
        public string DisplayName => L("实验性联机线程优化");
        public string Version => "0.1.0-alpha";
        public string MinimumManagerVersion => "2.0.0";
        public string TestedManagerVersion => "2.1.0";
        public string MinimumSharedVersion => "2.0.1";
        public string TestedSharedVersion => "2.0.1";
        public int RequiredSharedApiVersion => DdenApi.Version;
        public bool Ready => Mp.TogetherReady && !_bindFailed;
        public bool ModuleEnabled => _enabled?.Value == true;
        public string MasterKey => "实验线程/Enabled";
        public bool IsRunning => _runtime != null && (_runtime.Receiving || _runtime.Sending || _runtime.Preparing || _affinity.Locked);
        public DdenHealth Health => !string.IsNullOrEmpty(_failure) ? DdenHealth.Severe
            : ModuleEnabled && (_runtime?.ReceiveProblem != null || _runtime?.SendProblem != null || _runtime?.PrepareProblem != null
                || (_pin.Value && _affinity.State != "off" && _affinity.State != "locked")) ? DdenHealth.Warn : DdenHealth.Ok;
        public string HealthNote => Health == DdenHealth.Ok ? "" : (_failure ?? Summary);
        public string Summary => _failure != null ? L("适配失败，保留原MP行为") : !ModuleEnabled ? (IsRunning ? L("正在交接回原MP") : L("实验功能关闭"))
            : !Mp.IsRunning ? L("等待联机") : Health == DdenHealth.Warn ? L("部分功能降级，请查看卡片") : L("线程优化运行中");
        public IEnumerable<ConfigEntryBase> ConfigEntries => Config.Entries();
        private static string L(string text) => Text.Text(text);
        private void Awake()
        {
            _log = DDEN.Shared.Log.For(Logger, "MoreThreadOnMP");
            _mainId = ThreadRoles.Register("main");
            DdenLocalization.Register(Config, Text);
            _enabled = Bind("Enabled", false, "启用实验性联机线程优化。默认关闭。");
            _receive = Bind("ReceiveThread", true, "独立线程收包、复制数据并释放Steam消息。");
            _send = Bind("SendThread", true, "独立线程发包，保持原投递标志和连接内顺序。");
            _prepare = Bind("PrepareThread", true, "后台预解压可识别的gzip数据；仅逐字节匹配时复用结果。");
            _pin = Bind("MainThreadPerformanceCores", true, "识别混合架构性能核并约束游戏主线程，关闭时恢复。");
            _budget = Config.Bind("实验线程", "DispatchBudgetMs", 4f, new ConfigDescription("主线程每轮消息处理的软预算（毫秒），无法中断单个处理器。", new AcceptableValueRange<float>(0.5f, 20f)));
            _cap = Config.Bind("实验线程", "MessagesPerPeer", 32, new ConfigDescription("每轮每个连接最多处理的消息数。", new AcceptableValueRange<int>(1, 255)));
            _sampleInterval = Config.Bind("实验线程", "ThreadSampleSeconds", 3f, new ConfigDescription("线程负载采样间隔：0.5、1或3秒，默认3秒；仅影响负载显示。", new AcceptableValueList<float>(0.5f, 1f, 3f)));
            DdenDocs.AddStructured("实验线程", "ThreadSampleSeconds", "调整线程负载显示的采样间隔。", "按需要平衡显示更新速度与采样开销。",
                "只调整负载显示，不改变网络收发、游戏更新和消息处理频率。", false, DdenPerformanceImpact.Low);
            DdenHub.Register(this); _sampler = new ThreadSampler((int)(_sampleInterval.Value * 1000));
        }
        private ConfigEntry<bool> Bind(string key, bool value, string description)
        {
            DdenDocs.AddStructured("实验线程", key, description, "缓解联机主线程的数据处理和突发消息开销。", description,
                true, DdenPerformanceImpact.Medium, performanceNote: "实验功能；需要相同场景对照验证，可能增加消息延迟。");
            return Config.Bind("实验线程", key, value, description);
        }
        private void Update()
        {
            try
            {
                if (_sampler != null && _sampler.IntervalMilliseconds != (int)(_sampleInterval.Value * 1000)) _sampler.SetInterval((int)(_sampleInterval.Value * 1000));
                if (ModuleEnabled && !_wasEnabled) { _bindFailed = false; _failure = null; }
                _wasEnabled = ModuleEnabled;
                if (ModuleEnabled && Mp.TogetherReady && !_bindFailed && !_hooks.Active)
                {
                    try
                    {
                        _bindings = new MpBindings(); _runtime = new MpThreadRuntime(_bindings, _log);
                        TransportHooks.Runtime = _runtime; _hooks.Install(_bindings);
                    }
                    catch (Exception error)
                    { _bindFailed = true; _failure = error.Message; _log.Exception(error, "Transport adapter disabled"); StopRuntime(); }
                }
                if (_runtime != null)
                {
                    _runtime.Configure(ModuleEnabled && Mp.TogetherReady, _receive.Value, _send.Value, _prepare.Value, _budget.Value, _cap.Value);
                    _runtime.Tick();
                    if (!ModuleEnabled && !_runtime.Receiving && !_runtime.Sending && !_runtime.Preparing && _runtime.Incoming == 0) StopRuntime();
                }
                if (ModuleEnabled && Ready && Mp.IsRunning && _pin.Value)
                {
                    if (Time.realtimeSinceStartupAsDouble >= _nextAffinity)
                    { _nextAffinity = Time.realtimeSinceStartupAsDouble + 1; _affinity.Apply(); }
                }
                else _affinity.Restore();
            }
            catch (Exception error) { _failure = error.Message; _bindFailed = true; _log.Exception(error, "Thread runtime stopped"); StopRuntime(); _affinity.Restore(); }
        }
        private void StopRuntime()
        {
            _runtime?.Dispose(); _runtime = null; TransportHooks.Runtime = null;
            _hooks.Uninstall();
        }
        public void DrawPage(Rect area)
        {
            GUI.Label(new Rect(area.x, area.y, area.width - 330, 27), DisplayName + " · " + Version, DdenUI.Title);
            var intervals = SampleChoices;
            for (int i = 0; i < intervals.Length; i++)
            {
                var choice = new Rect(area.xMax - 321 + i * 45, area.y, 42, 27);
                if (DdenUI.Button(choice, intervals[i].ToString("0.#") + "s", _sampleInterval.Value == intervals[i])) _sampleInterval.Value = intervals[i];
                DdenUI.Hover(choice, L("线程负载采样间隔；仅影响显示，不改变收发包和游戏更新频率。"));
            }
            if (DdenUI.Button(new Rect(area.xMax - 178, area.y, 178, 27), ModuleEnabled ? L("总开关：已启用") : L("总开关：已关闭"), ModuleEnabled)) _enabled.Value = !ModuleEnabled;
            GUI.Label(new Rect(area.x, area.y + 31, area.width, 23), Summary + (_failure == null ? "" : " · " + _failure), DdenUI.Wrap);
            float threadHeight = DrawThreads(new Rect(area.x, area.y + 62, area.width, Mathf.Clamp(area.height * .28f, 110, 190)));
            float gap = 12, width = (area.width - gap) / 2, top = area.y + 62 + threadHeight + 12;
            float height = Mathf.Clamp((area.yMax - top - gap) / 2, 100, 210);
            bool live = ModuleEnabled && Ready && Mp.IsRunning;
            Card(new Rect(area.x, top, width, height), L("独立收包线程"), _receive, _runtime?.Receiving == true, _runtime?.ReceiveProblem,
                string.Format(L("待处理 {0} 包 · {1:0.00} MiB\n接收／复制峰值 {2:0.00} ms\n主线程处理峰值 {3:0.00} ms"), _runtime?.Incoming ?? 0,
                    (_runtime?.IncomingBytes ?? 0) / 1048576.0, _runtime?.PeakReceiveMs ?? 0, _runtime?.Pacer.PeakPollMs ?? 0), live);
            Card(new Rect(area.x + width + gap, top, width, height), L("独立发包线程"), _send, _runtime?.Sending == true, _runtime?.SendProblem,
                string.Format(L("待发送 {0} 包 · {1:0.00} MiB\n实际发送成功 {2} · 异步失败 {3}\n队列满 {4}"), _runtime?.Outgoing ?? 0,
                    (_runtime?.OutgoingBytes ?? 0) / 1048576.0, _runtime?.SendsCompleted ?? 0, _runtime?.SendErrors ?? 0, _runtime?.SendFull ?? 0), live);
            Card(new Rect(area.x, top + height + gap, width, height), L("客机数据预处理"), _prepare, _runtime?.Preparing == true, _runtime?.PrepareProblem,
                string.Format(L("后台预解压 {0} · 主线程命中 {1}\n最长排队 {2:0.0} ms\n角色、背包和世界状态由原MP主线程处理。"), _runtime?.PreparedCount ?? 0,
                    _runtime?.DecoderHits ?? 0, _runtime?.PeakQueueAgeMs ?? 0), live && _receive.Value);
            bool affinityProblem = live && _pin.Value && _affinity.State != "locked";
            Card(new Rect(area.x + width + gap, top + height + gap, width, height), L("主线程性能核"), _pin, _affinity.Locked,
                affinityProblem ? (string.IsNullOrEmpty(_affinity.Detail) ? CpuState(_affinity.State) : _affinity.Detail) : null, L("状态：") + CpuState(_affinity.State) + "\n" + _affinity.Detail + "\n"
                    + L("只约束游戏主线程；每秒核验，关闭后恢复原掩码。"), live);
        }
        private void Card(Rect rect, string title, ConfigEntry<bool> entry, bool active, string problem, string details, bool eligible)
        {
            DdenUI.Fill(rect, DdenUI.Panel);
            bool draining = active && (!ModuleEnabled || !entry.Value);
            Color stripe = !string.IsNullOrEmpty(_failure) && ModuleEnabled ? DdenUI.Bad : !string.IsNullOrEmpty(problem) || draining ? DdenUI.Warn : active ? DdenUI.Good : DdenUI.TextDim;
            DdenUI.Fill(new Rect(rect.x, rect.y, 5, rect.height), stripe);
            GUI.Label(new Rect(rect.x + 16, rect.y + 10, rect.width - 115, 24), title, DdenUI.Title);
            if (DdenUI.Button(new Rect(rect.xMax - 89, rect.y + 10, 76, 24), entry.Value ? L("已启用") : L("已关闭"), entry.Value)) entry.Value = !entry.Value;
            string state = !string.IsNullOrEmpty(_failure) && ModuleEnabled ? L("适配失败") : !string.IsNullOrEmpty(problem) ? L("降级")
                : draining ? L("交接中") : active ? L("运行中") : !ModuleEnabled ? L("总开关已关闭") : !entry.Value ? L("已关闭") : eligible ? L("等待数据") : L("等待联机或依赖");
            GUI.Label(new Rect(rect.x + 16, rect.y + 40, rect.width - 30, 21), state, DdenUI.Row);
            GUI.Label(new Rect(rect.x + 16, rect.y + 67, rect.width - 30, rect.height - 71), details + (string.IsNullOrEmpty(problem) ? "" : "\n" + problem), DdenUI.Wrap);
        }
        private float DrawThreads(Rect rect)
        {
            var snapshot = _sampler?.Snapshot ?? new ThreadSnapshot();
            if (DdenUI.Button(new Rect(rect.xMax - 104, rect.y, 104, 23), _showAllThreads ? L("仅显示重点") : L("显示全部"), _showAllThreads)) _showAllThreads = !_showAllThreads;
            if (!ReferenceEquals(_heatSnapshot, snapshot) || _cachedShowAll != _showAllThreads)
            {
                _heatRows = ThreadHeatmap.Select(snapshot, _showAllThreads); _heatSnapshot = snapshot; _cachedShowAll = _showAllThreads;
            }
            GUI.Label(new Rect(rect.x, rect.y, rect.width - 116, 23), snapshot.Rows.Length == 0 ? L(snapshot.Error == null ? "正在采样线程负载…" : "线程负载采样不可用")
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
            string explanation = L("每格代表线程；默认显示主线程、收发与预处理，以及近期CPU达到1%的其他线程。");
            GUI.Label(legend, DdenUI.ElideToWidth(hovered ?? explanation, legend.width, DdenUI.Sub), DdenUI.Sub);
            if (hovered == null) DdenUI.Hover(legend, explanation);
            return grid.Height + 57;
        }
        private static string Badge(string role) => role == "main" ? L("主") : role == "receive" ? L("收") : role == "send" ? L("发") : L("预");
        public bool DrawSettings(Rect area) => false;
        private static string CpuState(string state)
        {
            switch (state)
            {
                case "locked": return L("已核验：仅性能核");
                case "uniform": return L("系统未区分核类型，未修改亲和性");
                case "unsupported": return L("系统不支持此接口");
                case "unavailable": return L("没有允许使用的性能核");
                case "failed": return L("约束或核验失败");
                case "conflict": return L("处理器组发生冲突");
                default: return L("未启用");
            }
        }
        private static string Role(string role)
        {
            switch (role)
            {
                case "main": return L("游戏主线程");
                case "receive": return L("收包线程");
                case "send": return L("发包线程");
                case "prepare": return L("预处理线程");
                case "monitor": return L("负载采样线程");
                default: return L("游戏线程");
            }
        }
        private void OnDisable() { StopRuntime(); _affinity.Restore(); }
        private void OnDestroy()
        {
            StopRuntime(); _affinity.Restore(); _sampler?.Dispose(); _sampler = null;
            ThreadRoles.Remove(_mainId); DdenLocalization.Unregister(Config);
        }
    }
}
