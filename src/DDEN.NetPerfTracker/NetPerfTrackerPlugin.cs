using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using DDEN.Shared;
using UnityEngine;

namespace DDEN.NetPerfTracker
{
    /// <summary>Read-only, zero patches. Wall-frame timing, optional exact 2D marker and budgeted object censuses.</summary>
    [BepInPlugin(Guid, "DDEN NetPerfTracker", "1.7.1")]
    [BepInDependency("net.cucorelib", BepInDependency.DependencyFlags.SoftDependency)]
    public class NetPerfTrackerPlugin : BaseUnityPlugin, IDdenModule, IDdenModuleGate
    {
        public const string Guid = "com.dden.perf";
        public const string Title = "性能分析";
        internal static NetPerfTrackerPlugin Instance;
        internal static ManualLogSource LogSource;
        internal static LogChannel Log;
        internal static ConfigEntry<bool> CfgEnabled, CfgWriteLog, CfgVerbose;
        internal static ConfigEntry<float> CfgSampleSeconds, CfgHeavySeconds, CfgSlowFps, CfgWindowSeconds, CfgAutoStopMinutes;
        internal static ConfigEntry<int> CfgMaxSamples;
        private ConfigEntry<bool> _detailed, _inspectCollision, _stageTiming;
        private ConfigEntry<float> _budgetMs, _referenceFps, _spikeRatio, _spikeMs, _spikeCooldown;
        private ConfigEntry<int> _spikeCount;
        private readonly SampleRing<PerfSample> _buf = new SampleRing<PerfSample>(2400);
        private readonly ItemCensus _items = new ItemCensus();
        private readonly SceneCensus _scene = new SceneCensus();
        private readonly PhysicsTiming _physics = new PhysicsTiming();
        private readonly FrameStages _stages = new FrameStages();
        private readonly PerfAlerts _alerts = new PerfAlerts();
        private readonly PerfChart[] _charts = new PerfChart[4];
        private readonly Dictionary<string, FieldInfo> _dictionaryFields = new Dictionary<string, FieldInfo>();
        private readonly float[][] _series = new float[12][];
        private float[] _times = Array.Empty<float>();
        private byte[] _events = Array.Empty<byte>();
        private int _cacheVersion = -1, _version, _cacheCount;
        private float _startedAt, _nextLight, _nextObjects, _nextScene, _eventUntil, _nextPump;
        private bool _pageOpened, _paused, _stopped, _wasRecording, _alternate;
        private float _lastNow, _windowSeconds, _worstMs, _physicsSum, _physicsPeak, _selfSum, _selfPeak, _expectedHzSum;
        private int _frames, _fixedSteps, _physicsFrames, _longFrames;
        private float _selfThisFrame, _lastPumpMs;
        private int _lastPlayers = -1, _worldId, _depth = int.MinValue;
        private bool _lastRunning;
        private bool _stageTestActive, _showEvents;
        private int _lastAlertRevision;
        private Vector2 _alertScroll;
        private readonly string[] _eventRows = new string[160];
        private readonly float[] _eventHeights = new float[160];
        private float _eventContentHeight;
        private int _eventViewCount;
        private byte _event;
        private int _gc0, _gc1, _gc2;
        private Process _process;
        private double _lastCpu = -1;
        private float _lastCpuWall, _nextText;
        private string _top = "等待采样", _detail = "", _schedule = "", _file;
        private string _summary = "等待采样";
        private string _indicators = "", _recent = "", _stageButton = "阶段计时：关", _eventsButton = "突变记录：0";
        private float _indicatorHeight, _recentHeight;
        private float _textWidth, _topHeight = 20, _detailHeight = 57, _scheduleHeight = 34;
        private readonly GUIContent _measureText = new GUIContent();
        private static readonly string[] Titles = { "帧耗时 ms", "2D模拟 / 跟踪器 ms", "物品状态（区间清点）", "CPU / GPU / 呈现等待 ms" };
        private bool Recording => CfgEnabled != null && CfgEnabled.Value && !_paused && !_stopped;
        private static double ElapsedMs(long start) => (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;

        public string Id => "perf";
        public string DisplayName => Title;
        public string Version => Info.Metadata.Version.ToString();
        public bool Ready => true;
        public bool ModuleEnabled => CfgEnabled != null && CfgEnabled.Value;
        public string MasterKey => "总开关/启用采样（Enabled）";
        public string Summary => _stopped ? "已停止" : _paused ? "已暂停" : _summary;
        public IEnumerable<ConfigEntryBase> ConfigEntries => Config.Entries();

        private void Awake()
        {
            try
            {
                Instance = this; LogSource = Logger;
                CfgEnabled = Config.Bind("总开关", "启用采样（Enabled）", true, "是否记录新的性能数据。");
                CfgSampleSeconds = Config.Bind("采样", "SampleSeconds", 1f, new ConfigDescription("多久保存一个性能记录（秒）。", new AcceptableValueRange<float>(0.2f, 10f)));
                CfgHeavySeconds = Config.Bind("采样", "HeavySeconds", 15f, new ConfigDescription("两次物品清点的间隔（秒）；玩家数变化时会临时加快。", new AcceptableValueRange<float>(1f, 60f)));
                CfgSlowFps = Config.Bind("采样", "SlowFpsWarn", 45f, new ConfigDescription("低于这个帧数只标记，不判断原因。", new AcceptableValueRange<float>(5f, 144f)));
                CfgWindowSeconds = Config.Bind("显示", "WindowSeconds", 180f, new ConfigDescription("曲线显示最近多少秒。", new AcceptableValueRange<float>(10f, 1800f)));
                CfgMaxSamples = Config.Bind("显示", "MaxSamples", 2400, new ConfigDescription("内存中最多保留多少条记录。", new AcceptableValueRange<int>(60, 20000)));
                CfgWriteLog = Config.Bind("日志", "WriteLog", false, "兼容旧设置；仍只在点“导出日志”时写文件。");
                CfgAutoStopMinutes = Config.Bind("日志", "AutoStopMinutes", 0f, new ConfigDescription("多少分钟后停止记录；0 表示持续记录。", new AcceptableValueRange<float>(0f, 600f)));
                CfgVerbose = Config.Bind("Debug", "Verbose", false, "额外输出采样明细，可能增加日志开销。");
                _detailed = Config.Bind("详细统计", "场景物理对象", false, "统计场景中的清醒、休眠刚体和碰撞体；开销更高，分帧进行。");
                _inspectCollision = Config.Bind("详细统计", "物品碰撞检查", false, "检查清醒自由物品是否有实体碰撞；分帧进行，默认关闭。");
                _budgetMs = Config.Bind("采样", "每帧清点预算毫秒", 0.4f, new ConfigDescription("分帧清点达到预算后暂停；单次引擎调用无法中途打断。", new AcceptableValueRange<float>(0.1f, 2f)));
                _stageTiming = Config.Bind("诊断", "CPU与GPU阶段计时", false, "按需读取主线程、渲染线程、GPU与等待计时；有额外采样成本，默认关闭。缺失不会填零。");
                _referenceFps = Config.Bind("诊断", "参考帧率", 60f, new ConfigDescription("用于计算可疑开销参考预算，不改变游戏限帧。", new AcceptableValueRange<float>(5, 240)));
                _spikeRatio = Config.Bind("诊断", "突变倍数", 2f, new ConfigDescription("同时超过倍数和最小变化量才记录突变。", new AcceptableValueRange<float>(1.25f, 10)));
                _spikeMs = Config.Bind("诊断", "耗时突变最小毫秒", 8f, new ConfigDescription("耗时相对平滑基线的最小增量；自身CPU使用不高于2ms的增量。", new AcceptableValueRange<float>(1, 100)));
                _spikeCount = Config.Bind("诊断", "数量突变最小增量", 50, new ConfigDescription("物品、刚体、网络条目变化的最小绝对数量。", new AcceptableValueRange<int>(1, 10000)));
                _spikeCooldown = Config.Bind("诊断", "重复事件合并秒数", 3f, new ConfigDescription("同一指标在此间隔内重复异常时合并记录。", new AcceptableValueRange<float>(0.5f, 60)));
                Log = DDEN.Shared.Log.For(Logger, "Perf", CfgVerbose.Value);
                Config.SettingChanged += (_, __) => OnSettingsChanged();
                RegisterDocs(); DdenHub.Register(this);
                _buf.SetCapacity(CfgMaxSamples.Value);
                _charts[0] = new PerfChart(new float[2][], new[] { DdenUI.Good, DdenUI.Warn }, new[] { "平均", "最长" });
                _charts[1] = new PerfChart(new float[3][], new[] { DdenUI.Accent, DdenUI.Warn, DdenUI.Bad }, new[] { "2D平均", "跟踪均", "跟踪峰" });
                _charts[2] = new PerfChart(new float[3][], new[] { DdenUI.Good, DdenUI.Accent, DdenUI.Warn }, new[] { "物品", "清醒自由", "向下运动" });
                _charts[3] = new PerfChart(new float[4][], new[] { DdenUI.Accent, DdenUI.Good, DdenUI.Warn, DdenUI.TextDim }, new[] { "主线程", "渲染", "GPU", "等待" });
                ConfigureAlerts();
                Log.Info("性能分析已加载：物理调度与耗时分开；对象分帧清点，详细场景统计默认关闭（无补丁）。");
            }
            catch (Exception ex) { Logger.LogError(ex); }
        }
        private void RegisterDocs()
        {
            DdenDocs.Add("总开关", "启用采样（Enabled）", "是否记录新的性能数据。", "需要停止观察者自身的采样工作。", "关闭后停止计时和对象清点，保留已有记录。", "否", "关闭后不再采样");
            DdenDocs.Add("详细统计", "场景物理对象", "分帧统计场景中的刚体和碰撞体。", "需要区分清醒、休眠和仅启用模拟的物体。", "遍历场景树，按每帧预算暂停，不在一帧搜索全场。", "否", "高于轻量跟踪；默认关闭，统计结果有时间范围");
            DdenDocs.Add("详细统计", "物品碰撞检查", "检查清醒自由物品是否有实体碰撞。", "需要进一步判断遗留副本的状态。", "只检查正在模拟的清醒自由物品；无碰撞或向下运动不直接判为故障。", "否", "额外开销；默认关闭");
            DdenDocs.Add("诊断", "CPU与GPU阶段计时", "分开观察CPU、GPU和等待计时。", "避免把等待或低帧直接归因于物理。", "只连接准确的引擎计时器；结果可能延迟数帧，缺失明确显示。", "否", "有额外成本，默认关闭；不提供具体函数调用栈");
        }
        private void ConfigureAlerts()
        { _alerts.Ratio = _spikeRatio.Value; _alerts.MinMs = _spikeMs.Value; _alerts.CountDelta = _spikeCount.Value; _alerts.Cooldown = _spikeCooldown.Value; }
        private void Start()
        {
            try
            {
                _startedAt = _lastNow = Time.realtimeSinceStartup;
                _nextLight = _startedAt + CfgSampleSeconds.Value;
                _nextObjects = _nextScene = _startedAt;
                ResetWindow(_startedAt);
                if (File.Exists(Path.Combine(Paths.ConfigPath, "cumod.selftest")) || Array.IndexOf(Environment.GetCommandLineArgs(), "-cumodtest") >= 0)
                    StartCoroutine(SelfTest());
            }
            catch (Exception ex) { Log.Exception(ex, "初始化性能跟踪"); }
        }
        private void OnSettingsChanged()
        {
            try
            {
                Log.Verbose = CfgVerbose.Value;
                _buf.SetCapacity(CfgMaxSamples.Value); _version++;
                _items.Cancel(); _scene.Reset(); _nextObjects = _nextScene = Time.realtimeSinceStartup;
                if (!CfgEnabled.Value) _physics.Dispose();
                ConfigureAlerts(); _alerts.ResetBaselines();
                if (!Recording || !_stageTiming.Value) _stages.Dispose();
                else if (_pageOpened) _stages.Start();
                _nextText = 0;
            }
            catch (Exception ex) { Log.Exception(ex, "修改采样设置"); }
        }
        private void ResetWindow(float now)
        {
            _lastNow = now; _frames = _fixedSteps = _physicsFrames = _longFrames = 0;
            _windowSeconds = _worstMs = _physicsSum = _physicsPeak = _selfSum = _selfPeak = _expectedHzSum = 0;
            _gc0 = GC.CollectionCount(0); _gc1 = GC.CollectionCount(1); _gc2 = GC.CollectionCount(2);
            _stages.ResetWindow();
        }
        private void FixedUpdate()
        {
            try { if (Recording) _fixedSteps++; }
            catch (Exception ex) { Log.Exception(ex, "记录物理调度"); }
        }
        private void Update()
        {
            long start = Stopwatch.GetTimestamp();
            try
            {
                float now = Time.realtimeSinceStartup;
                bool recording = Recording;
                if (recording != _wasRecording)
                {
                    _wasRecording = recording; ResetWindow(now); _nextLight = now + CfgSampleSeconds.Value;
                    if (recording) { _event |= 32; _lastCpu = -1; Cpu(now); }
                    _alerts.ResetBaselines();
                    if (!recording) { _items.Cancel(); _scene.Cancel(); _physics.Dispose(); _stages.Dispose(); }
                    else if (_pageOpened) { _physics.Start(); if (_stageTiming.Value) _stages.Start(); }
                }
                float self = _selfThisFrame; _selfThisFrame = 0;
                if (!recording) return;
                if (CfgAutoStopMinutes.Value > 0 && now - _startedAt >= CfgAutoStopMinutes.Value * 60f) { _stopped = true; return; }
                float elapsed = Math.Max(0, now - _lastNow); _lastNow = now;
                if (elapsed > 0)
                {
                    _frames++; _windowSeconds += elapsed; _worstMs = Math.Max(_worstMs, elapsed * 1000f);
                    if (elapsed > 0.05f) _longFrames++;
                    _selfSum += self; _selfPeak = Math.Max(_selfPeak, self);
                    _expectedHzSum += PerfMath.ExpectedHz(Time.timeScale, Time.fixedDeltaTime) * elapsed;
                    float physics = _physics.ReadMs();
                    if (physics >= 0) { _physicsSum += physics; _physicsPeak = Math.Max(_physicsPeak, physics); _physicsFrames++; }
                    _stages.ReadFrame(Application.isFocused);
                    _alerts.Frame(now - _startedAt, elapsed * 1000, self, _stages.Started ? _stages.Current : null, physics);
                }
                if (_pageOpened) PumpCensuses(now);
                if (now >= _nextLight && _windowSeconds > 0)
                { _nextLight = now + Math.Max(0.2f, CfgSampleSeconds.Value); LightSample(now); }
            }
            catch (Exception ex) { Log.Exception(ex, "性能采样"); _items.Cancel(); _scene.Cancel(); }
            finally { _selfThisFrame += (float)ElapsedMs(start); }
        }
        private void PumpCensuses(float now)
        {
            if (now < _nextPump) return;
            bool itemDue = !_items.Running && now >= _nextObjects;
            bool sceneDue = _detailed.Value && !_scene.Running && now >= _nextScene;
            if (!_items.Running && !_scene.Running && !itemDue && !sceneDue) return;
            long start = Stopwatch.GetTimestamp();
            if (itemDue)
            { _items.Begin(now, _inspectCollision.Value); _nextObjects = now + (now < _eventUntil ? 1f : CfgHeavySeconds.Value); }
            if (sceneDue)
            { _scene.Begin(now, gameObject.scene); _nextScene = now + CfgHeavySeconds.Value; }
            long budget = (long)(_budgetMs.Value * Stopwatch.Frequency / 1000.0);
            _alternate = !_alternate;
            if (_alternate) { _items.Step(start, budget, now); _scene.Step(start, budget, now); }
            else { _scene.Step(start, budget, now); _items.Step(start, budget, now); }
            _lastPumpMs = (float)ElapsedMs(start);
            // Even snapshot copy/root discovery is measured. Slow native operations back off instead of repeating every frame.
            if (_lastPumpMs > Math.Max(2f, _budgetMs.Value * 4f)) _nextPump = now + 0.1f;
        }
        private int DictionaryCount(string typeName, string fieldName)
        {
            string key = typeName + "." + fieldName;
            if (!_dictionaryFields.TryGetValue(key, out var field))
            {
                Type type = Reflect.Type(typeName);
                if (type == null) return -1; // Do not permanently cache a plugin that is not loaded yet.
                field = type.GetField(fieldName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                _dictionaryFields[key] = field;
            }
            return (field?.GetValue(null) as IDictionary)?.Count ?? -1;
        }
        private float Cpu(float now)
        {
            try
            {
                if (_process == null) _process = Process.GetCurrentProcess();
                double value = _process.TotalProcessorTime.TotalSeconds;
                float percent = _lastCpu >= 0 && now > _lastCpuWall ? (float)((value - _lastCpu) / (now - _lastCpuWall) / Math.Max(1, Environment.ProcessorCount) * 100) : -1;
                _lastCpu = value; _lastCpuWall = now; return percent;
            }
            catch { return -1; }
        }
        private void LightSample(float now)
        {
            int players = DictionaryCount("Together.ScavPlayer", "ClientIdToPlayerDict");
            if (_lastPlayers >= 0 && players >= 0 && players != _lastPlayers)
            { _event |= 1; _nextObjects = now; _eventUntil = now + 10f; }
            _lastPlayers = players;
            bool running = Mp.IsRunning;
            if (running != _lastRunning) { _lastRunning = running; _event |= 8; _items.Reset(); _scene.Reset(); _alerts.ResetBaselines(); }
            var world = WorldGeneration.world;
            int id = world != null ? world.GetInstanceID() : 0, depth = world != null ? world.biomeDepth : int.MinValue;
            if (id != _worldId || depth != _depth)
            {
                _worldId = id; _depth = depth; _event |= 2; _items.Reset(); _scene.Reset();
                _nextObjects = _nextScene = now;
                _physics.Dispose(); if (_pageOpened) _physics.Start();
                _stages.Dispose(); if (_pageOpened && (_stageTiming.Value || _stageTestActive)) _stages.Start();
                _alerts.ResetBaselines();
            }
            if (_frames / _windowSeconds < CfgSlowFps.Value) _event |= 4;
            var sample = new PerfSample
            {
                T = now - _startedAt, Fps = _frames / _windowSeconds, MeanMs = _windowSeconds * 1000f / _frames,
                WorstMs = _worstMs, LongFrames = _longFrames, Cpu = Cpu(now),
                PhysicsMs = _physicsFrames > 0 ? _physicsSum / _physicsFrames : -1,
                PhysicsPeakMs = _physicsFrames > 0 ? _physicsPeak : -1,
                SelfMs = _selfSum / _frames, SelfPeakMs = _selfPeak,
                StepsPerFrame = PerfMath.PerFrame(_fixedSteps, _frames), StepsPerSecond = PerfMath.PerSecond(_fixedSteps, _windowSeconds),
                ExpectedStepsPerSecond = _expectedHzSum / _windowSeconds,
                Scale = Time.timeScale, FixedDt = Time.fixedDeltaTime, Target = Application.targetFrameRate, VSync = QualitySettings.vSyncCount,
                GC0 = GC.CollectionCount(0) - _gc0, GC1 = GC.CollectionCount(1) - _gc1, GC2 = GC.CollectionCount(2) - _gc2,
                HeapMb = GC.GetTotalMemory(false) / (1024f * 1024f), Players = players,
                WindowSeconds = _windowSeconds, Frames = _frames, FixedSteps = _fixedSteps, PhysicsFrames = _physicsFrames,
                CensusBudgetMs = _budgetMs.Value, LastCensusSliceMs = _lastPumpMs,
                SampleInterval = CfgSampleSeconds.Value, CensusInterval = CfgHeavySeconds.Value,
                ReferenceFps = _referenceFps.Value, WarningFps = CfgSlowFps.Value, SpikeRatio = _spikeRatio.Value, SpikeMinMs = _spikeMs.Value, SpikeCount = _spikeCount.Value, SpikeCooldown = _spikeCooldown.Value,
                Detailed = _detailed.Value, Collision = _inspectCollision.Value,
                Focused = Application.isFocused,
                Sync = DictionaryCount("Together.NetObjectRegistry", "SyncRegistry"), Event = _event,
                Objects = _items.Result, Scene = _scene.Result, Stages = _stages.Snapshot()
            };
            _alerts.Sample(sample, _startedAt, CfgSlowFps.Value);
            if (_lastAlertRevision != _alerts.Revision) { sample.Event |= 128; _lastAlertRevision = _alerts.Revision; }
            _event = 0; _buf.Add(sample); _version++; _nextText = 0;
            _summary = sample.Fps.ToString("0") + " fps · " + _buf.Count + " 点";
            if (CfgVerbose.Value) Log.Debug(Describe(sample));
            ResetWindow(now);
        }

        public void DrawPage(Rect area)
        {
            long started = Stopwatch.GetTimestamp();
            try
            {
                if (!_pageOpened)
                {
                    _pageOpened = true;
                    if (Recording) _physics.Start();
                    if (Recording && _stageTiming.Value) _stages.Start();
                    _nextObjects = Time.realtimeSinceStartup;
                }
                float now = Time.realtimeSinceStartup;
                if (now >= _nextText || _textWidth != area.width)
                {
                    _nextText = now + 0.5f; _textWidth = area.width; RefreshText(now);
                    _measureText.text = _top; _topHeight = DdenUI.Wrap.CalcHeight(_measureText, area.width);
                    _measureText.text = _detail; _detailHeight = DdenUI.Wrap.CalcHeight(_measureText, area.width);
                    _measureText.text = _schedule; _scheduleHeight = DdenUI.Wrap.CalcHeight(_measureText, area.width);
                    _measureText.text = _indicators; _indicatorHeight = DdenUI.Wrap.CalcHeight(_measureText, area.width - 8);
                    _measureText.text = _recent; _recentHeight = DdenUI.Wrap.CalcHeight(_measureText, area.width);
                    if (_showEvents) RefreshEventRows(area.width - 20);
                }
                float y = area.y;
                GUI.Label(new Rect(area.x, y, area.width, 20), "性能分析 · 耗时与物品状态", DdenUI.Title); y += 23;
                GUI.Label(new Rect(area.x, y, area.width, _topHeight), _top, DdenUI.Wrap); y += _topHeight + 2;
                GUI.Label(new Rect(area.x, y, area.width, 18), "物理步数是调度读数，不能判断物理耗时；“不可用”不代表耗时为零。", DdenUI.Sub); y += 21;
                DdenUI.Fill(new Rect(area.x, y, area.width, _indicatorHeight + 8), new Color(0.04f, 0.06f, 0.08f, 0.9f));
                GUI.Label(new Rect(area.x + 4, y + 4, area.width - 8, _indicatorHeight), _indicators, DdenUI.Wrap); y += _indicatorHeight + 12;
                if (_showEvents)
                {
                    float listHeight = Math.Max(70, area.yMax - y - 92);
                    _alertScroll = DdenUI.BeginScrollView(new Rect(area.x, y, area.width, listHeight), _alertScroll, new Rect(0, 0, area.width - 20, _eventContentHeight));
                    try
                    {
                        float rowY = 0;
                        for (int i = 0; i < _eventViewCount; i++)
                        {
                            if (rowY + _eventHeights[i] >= _alertScroll.y && rowY <= _alertScroll.y + listHeight)
                                GUI.Label(new Rect(0, rowY, area.width - 20, _eventHeights[i]), _eventRows[i], DdenUI.Wrap);
                            rowY += _eventHeights[i] + 9;
                        }
                    }
                    finally { DdenUI.EndScrollView(); }
                    y += listHeight + 4; DrawButtons(area, ref y); return;
                }
                if (_buf.Count > 1)
                {
                    BuildCache();
                    float end = _buf[_buf.Count - 1].T, begin = Math.Max(0, end - CfgWindowSeconds.Value);
                    float height = Mathf.Clamp((area.yMax - y - _detailHeight - _scheduleHeight - _recentHeight - 102) / 2f - 4, 48, 120);
                    float half = (area.width - 8) / 2;
                    for (int i = 0; i < 4; i++)
                    {
                        _charts[i].Draw(new Rect(area.x + (i % 2) * (half + 8), y + (i / 2) * (height + 4), half, height), Titles[i], _times, _events, _cacheCount, _version, begin, end);
                    }
                    y += 2 * (height + 4);
                }
                else { GUI.Label(new Rect(area.x, y, area.width, 30), "等待新的记录…", DdenUI.Sub); y += 33; }
                float footerY = Math.Max(y, area.yMax - 88);
                DrawText(area, _detail, _detailHeight, footerY, ref y);
                DrawText(area, _schedule, _scheduleHeight, footerY, ref y);
                DrawText(area, _recent, _recentHeight, footerY, ref y);
                y = footerY;
                DrawButtons(area, ref y);
            }
            catch (Exception ex) { Log.Exception(ex, "绘制性能页面"); }
            finally { _selfThisFrame += (float)ElapsedMs(started); }
        }
        private static void DrawText(Rect area, string text, float height, float limit, ref float y)
        {
            float visible = Math.Min(height, Math.Max(0, limit - y - 2));
            if (visible > 0) GUI.Label(new Rect(area.x, y, area.width, visible), text, DdenUI.Wrap);
            y += visible + 2;
        }
        private void RefreshText(float now)
        {
            if (_buf.Count == 0) { _top = "等待采样"; _detail = _schedule = _indicators = _recent = ""; return; }
            var s = _buf[_buf.Count - 1];
            _top = $"记录于{s.T:0.0}s（距今{Math.Max(0, now - _startedAt - s.T):0.0}s）· {s.Fps:0} fps · 平均{s.MeanMs:0.0}ms / 最长{s.WorstMs:0.0}ms · 2D {Milliseconds(s.PhysicsMs)} · 自身{s.SelfMs:0.00}ms / 峰{s.SelfPeakMs:0.00}ms";
            var o = s.Objects; var scene = s.Scene;
            _indicators = PerfIndicators.Report(s, _referenceFps.Value, CfgSlowFps.Value);
            _recent = _alerts.Recent(2);
            _stageButton = _stageTiming.Value ? "阶段计时：开" : "阶段计时：关";
            _eventsButton = (_showEvents ? "返回曲线" : "突变记录：" + _alerts.Events.Count);
            string objectText = o.Valid ? $"物品 {o.Total}（启用 {o.Active}）· 自由动态 {o.Simulated}（清醒 {o.Awake} / 休眠 {o.Sleeping}）· 向下运动 {o.Downward} · 无实体碰撞 {(o.CollisionChecked ? o.NoSolid.ToString() : "未检查")}" : "物品清点：等待完成";
            string age = o.Valid ? $"物品数据距今 {Math.Max(0, now - o.Ended):0.0}s，清点跨度 {o.Ended - o.Started:0.00}s" : "物品状态尚无完整记录";
            _detail = objectText + "\n" + $"玩家 {s.Players} · 网络条目 {s.Sync} · CPU {Value(s.Cpu)}%（全核心平均）· GC {s.GC0}/{s.GC1}/{s.GC2} · 托管堆 {s.HeapMb:0.0}MB\n" + age;
            if (scene.Valid) _detail += $"；场景清醒 {scene.Awake} / 休眠 {scene.Sleeping}，碰撞体 {scene.Colliders}";
            _schedule = $"物理调度：{s.StepsPerSecond:0.0}步/秒 / 设定 {s.ExpectedStepsPerSecond:0.0}，{s.StepsPerFrame:0.00}步/帧（非耗时）" +
                $"；每帧清点预算 {_budgetMs.Value:0.0}ms，最近分片 {_lastPumpMs:0.00}ms" +
                (_items.Running ? $"；物品清点中 {_items.Progress} 件" : "") + (_scene.Running ? $"；场景清点中 {_scene.Progress} 个对象" : "") +
                (scene.Valid ? $"；场景数据距今 {Math.Max(0, now - scene.Ended):0.0}s" : "");
        }
        private void RefreshEventRows(float width)
        {
            _eventContentHeight = 0;
            _eventViewCount = _alerts.Events.Count;
            for (int i = 0; i < _eventViewCount; i++)
            {
                _eventRows[i] = PerfAlerts.Format(_alerts.Events[_alerts.Events.Count - 1 - i]);
                _measureText.text = _eventRows[i]; _eventHeights[i] = DdenUI.Wrap.CalcHeight(_measureText, width);
                _eventContentHeight += _eventHeights[i] + 9;
            }
        }
        private void DrawButtons(Rect area, ref float y)
        {
            if (DdenUI.Button(new Rect(area.x, y, 65, 23), _stopped ? "清空重启" : _paused ? "继续" : "暂停", true))
            {
                if (_stopped) { _buf.Clear(); _alerts.Clear(); _version++; _startedAt = Time.realtimeSinceStartup; _nextObjects = _nextScene = _startedAt; _items.Reset(); _scene.Reset(); _stopped = _paused = false; }
                else _paused = !_paused;
                _nextText = 0;
            }
            if (DdenUI.Button(new Rect(area.x + 70, y, 65, 23), "清空")) { _buf.Clear(); _alerts.Clear(); _version++; _nextText = 0; _summary = "等待采样"; }
            if (DdenUI.Button(new Rect(area.x + 140, y, 100, 23), "标记并清点")) { _event |= 16; _nextObjects = _nextScene = Time.realtimeSinceStartup; }
            if (DdenUI.Button(new Rect(area.x + 245, y, 110, 23), _detailed.Value ? "详细统计：开" : "详细统计：关")) _detailed.Value = !_detailed.Value;
            if (DdenUI.Button(new Rect(area.x + 360, y, 90, 23), "导出日志")) ExportLog();
            if (DdenUI.Button(new Rect(area.x + 455, y, 90, 23), "打开目录"))
            {
                string dir = Path.Combine(Paths.ConfigPath, "DDEN"); Directory.CreateDirectory(dir);
                Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
            }
            y += 27;
            if (DdenUI.Button(new Rect(area.x, y, 110, 23), _stageButton)) _stageTiming.Value = !_stageTiming.Value;
            if (DdenUI.Button(new Rect(area.x + 115, y, 125, 23), _eventsButton)) { _showEvents = !_showEvents; _nextText = 0; }
            y += 27;
            GUI.Label(new Rect(area.x, y, area.width, 31), "默认轻量记录；详细统计与碰撞检查可选。只在点“导出日志”时写文件。向下运动或无碰撞本身不证明物品异常。", DdenUI.Wrap);
        }
        public bool DrawSettings(Rect area) => false;
        private static string Value(float value) => value < 0 ? "不可用" : value.ToString("0.##", CultureInfo.InvariantCulture);
        private static string Milliseconds(float value) => value < 0 ? "不可用" : value.ToString("0.##", CultureInfo.InvariantCulture) + "ms";
        private void BuildCache()
        {
            if (_cacheVersion == _version) return;
            _cacheVersion = _version; _cacheCount = _buf.Count;
            if (_times.Length < _cacheCount)
            {
                int capacity = Math.Max(128, Math.Max(_cacheCount, _times.Length * 2));
                _times = new float[capacity]; _events = new byte[capacity];
                for (int i = 0; i < _series.Length; i++) _series[i] = new float[capacity];
                _charts[0].SetSeries(0, _series[0]); _charts[0].SetSeries(1, _series[1]);
                for (int i = 0; i < 3; i++) { _charts[1].SetSeries(i, _series[2 + i]); _charts[2].SetSeries(i, _series[5 + i]); }
                for (int i = 0; i < 4; i++) _charts[3].SetSeries(i, _series[8 + i]);
            }
            for (int i = 0; i < _cacheCount; i++)
            {
                var s = _buf[i]; _times[i] = s.T; _events[i] = s.Event;
                _series[0][i] = s.MeanMs; _series[1][i] = s.WorstMs; _series[2][i] = s.PhysicsMs;
                _series[3][i] = s.SelfMs; _series[4][i] = s.SelfPeakMs;
                _series[5][i] = s.Objects.Valid ? s.Objects.Total : -1;
                _series[6][i] = s.Objects.Valid ? s.Objects.Awake : -1;
                _series[7][i] = s.Objects.Valid ? s.Objects.Downward : -1;
                _series[8][i] = s.Stages.Enabled ? s.Stages.Main.Mean : -1;
                _series[9][i] = s.Stages.Enabled ? s.Stages.Render.Mean : -1;
                _series[10][i] = s.Stages.Enabled ? s.Stages.Gpu.Mean : -1;
                _series[11][i] = s.Stages.Enabled ? s.Stages.Present.Mean : -1;
            }
        }
        private static string Describe(PerfSample s)
        {
            var c = CultureInfo.InvariantCulture;
            var b = new StringBuilder(600);
            b.Append("t=").Append(s.T.ToString("0.000", c)).Append(" fps=").Append(s.Fps.ToString("0.0", c));
            b.Append(" frameAvgMs=").Append(s.MeanMs.ToString("0.000", c)).Append(" frameMaxMs=").Append(s.WorstMs.ToString("0.000", c));
            b.Append(" longFrames50ms=").Append(s.LongFrames).Append(" physics2dAvgMs=").Append(s.PhysicsMs.ToString("0.000", c)).Append(" physics2dMaxMs=").Append(s.PhysicsPeakMs.ToString("0.000", c));
            b.Append(" windowSeconds=").Append(s.WindowSeconds.ToString("0.000", c)).Append(" frames=").Append(s.Frames).Append(" fixedSteps=").Append(s.FixedSteps).Append(" physicsFrames=").Append(s.PhysicsFrames);
            b.Append(" observerAvgMs=").Append(s.SelfMs.ToString("0.000", c)).Append(" observerMaxMs=").Append(s.SelfPeakMs.ToString("0.000", c));
            b.Append(" stepsPerFrame=").Append(s.StepsPerFrame.ToString("0.000", c)).Append(" stepsPerSecond=").Append(s.StepsPerSecond.ToString("0.00", c)).Append(" expectedStepHz=").Append(s.ExpectedStepsPerSecond.ToString("0.00", c));
            b.Append(" timeScale=").Append(s.Scale.ToString("0.00", c)).Append(" fixedDt=").Append(s.FixedDt.ToString("0.0000", c)).Append(" targetFps=").Append(s.Target).Append(" vsync=").Append(s.VSync);
            b.Append(" cpuAllCoresPct=").Append(s.Cpu.ToString("0.00", c)).Append(" gc0=").Append(s.GC0).Append(" gc1=").Append(s.GC1).Append(" gc2=").Append(s.GC2).Append(" heapMb=").Append(s.HeapMb.ToString("0.00", c));
            b.Append(" players=").Append(s.Players).Append(" sync=").Append(s.Sync).Append(" event=").Append(s.Event);
            b.Append(" censusBudgetMs=").Append(s.CensusBudgetMs.ToString("0.000", c)).Append(" lastCensusSliceMs=").Append(s.LastCensusSliceMs.ToString("0.000", c));
            b.Append(" sampleInterval=").Append(s.SampleInterval.ToString("0.000", c)).Append(" censusInterval=").Append(s.CensusInterval.ToString("0.000", c));
            b.Append(" referenceFps=").Append(s.ReferenceFps.ToString("0.0", c)).Append(" warningFps=").Append(s.WarningFps.ToString("0.0", c));
            b.Append(" spikeRatio=").Append(s.SpikeRatio.ToString("0.###", c)).Append(" spikeMinMs=").Append(s.SpikeMinMs.ToString("0.###", c)).Append(" spikeCount=").Append(s.SpikeCount).Append(" spikeMergeSeconds=").Append(s.SpikeCooldown.ToString("0.###", c));
            b.Append(" detailed=").Append(s.Detailed ? 1 : 0).Append(" collisionCheck=").Append(s.Collision ? 1 : 0);
            b.Append(" focused=").Append(s.Focused ? 1 : 0);
            var o = s.Objects;
            b.Append(" objectsValid=").Append(o.Valid ? 1 : 0).Append(" objectsStart=").Append((o.Started).ToString("0.000", c)).Append(" objectsEnd=").Append(o.Ended.ToString("0.000", c));
            b.Append(" items=").Append(o.Valid ? o.Total : -1).Append(" activeItems=").Append(o.Valid ? o.Active : -1).Append(" free=").Append(o.Valid ? o.Free : -1);
            b.Append(" freeDynamic=").Append(o.Valid ? o.Simulated : -1).Append(" freeAwake=").Append(o.Valid ? o.Awake : -1).Append(" freeSleeping=").Append(o.Valid ? o.Sleeping : -1).Append(" downward=").Append(o.Valid ? o.Downward : -1);
            b.Append(" awakeNoSolid=").Append(o.Valid && o.CollisionChecked ? o.NoSolid : -1).Append(" objectWorkMs=").Append(o.WorkMs.ToString("0.000", c)).Append(" objectMaxSliceMs=").Append(o.MaxSliceMs.ToString("0.000", c));
            var scene = s.Scene;
            b.Append(" sceneValid=").Append(scene.Valid ? 1 : 0).Append(" sceneStart=").Append(scene.Started.ToString("0.000", c)).Append(" sceneEnd=").Append(scene.Ended.ToString("0.000", c));
            b.Append(" sceneBodies=").Append(scene.Valid ? scene.Rigid : -1).Append(" sceneDynamic=").Append(scene.Valid ? scene.Dynamic : -1).Append(" sceneAwake=").Append(scene.Valid ? scene.Awake : -1).Append(" sceneSleeping=").Append(scene.Valid ? scene.Sleeping : -1);
            b.Append(" sceneKinematic=").Append(scene.Valid ? scene.Kinematic : -1).Append(" enabledColliders=").Append(scene.Valid ? scene.Colliders : -1).Append(" sceneWorkMs=").Append(scene.WorkMs.ToString("0.000", c)).Append(" sceneMaxSliceMs=").Append(scene.MaxSliceMs.ToString("0.000", c));
            b.Append(" stageEnabled=").Append(s.Stages.Enabled ? 1 : 0);
            b.Append(" stageWarming=").Append(s.Stages.Warming ? 1 : 0);
            AppendTiming(b, "cpuTotal", s.Stages.Total); AppendTiming(b, "cpuMain", s.Stages.Main); AppendTiming(b, "cpuRender", s.Stages.Render);
            AppendTiming(b, "gpu", s.Stages.Gpu); AppendTiming(b, "presentWait", s.Stages.Present); AppendTiming(b, "targetWait", s.Stages.Limit); AppendTiming(b, "playerLoop", s.Stages.Loop);
            return b.ToString();
        }
        private static void AppendTiming(StringBuilder b, string name, TimingValue v)
        {
            b.Append(' ').Append(name).Append("AvgMs=").Append((v.Frames > 0 ? v.Mean : -1).ToString("0.000", CultureInfo.InvariantCulture));
            b.Append(' ').Append(name).Append("MaxMs=").Append((v.Frames > 0 ? v.Peak : -1).ToString("0.000", CultureInfo.InvariantCulture));
            b.Append(' ').Append(name).Append("ValidReads=").Append(v.Frames);
        }
        internal int ExportLog()
        {
            if (_buf.Count == 0) return 0;
            try
            {
                string dir = Path.Combine(Paths.ConfigPath, "DDEN"); Directory.CreateDirectory(dir);
                _file = Path.Combine(dir, "perf-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + ".log");
                using (var writer = new StreamWriter(_file, false, new UTF8Encoding(false), 65536))
                {
                    writer.WriteLine("# DDEN perf schema=3 exportedUtc=" + DateTime.UtcNow.ToString("O") + " engineTimeAtStart=" + _startedAt.ToString("0.000", CultureInfo.InvariantCulture));
                    writer.WriteLine("# version=" + Version + " physicsSource=" + _physics.Source + "; -1=unavailable, never zero-filled; timing unit=ms per rendered frame; steps are scheduling only");
                    writer.WriteLine("# object/scene timestamps use engine realtime seconds; counts are interval censuses, not simultaneous snapshots; all settings and costs belong to the recorded point");
                    writer.WriteLine("# free=no parent; freeDynamic=active,simulated,dynamic; awake excludes sleeping; downward=awake free velocityY<-2; noSolid excludes trigger colliders");
                    writer.WriteLine("# event bits:1=players,2=world,4=lowFPS,8=connection,16=manual,32=resume,64=selftest,128=alerts; observer CPU covers sampling+page code, not GPU/engine recorder work");
                    writer.WriteLine("# stage times are independent, not additive; native counters may lag several frames; event t=observation/completed-census time, baselineT=previous observation; repeated events include lastT/peak/count");
                    writer.WriteLine("# stage counters: CPU Total Frame Time, CPU Main Thread Frame Time, CPU Render Thread Frame Time, GPU Frame Time, Gfx.WaitForPresentOnGfxThread, WaitForTargetFPS, PlayerLoop; times in ms; ValidReads counts successful reads, not unique native frames");
                    PerfLog.WriteTimeline(writer, _buf, _alerts.Events, Describe);
                }
                Log.Info("已导出 " + _buf.Count + " 条记录：" + _file); return _buf.Count;
            }
            catch (Exception ex) { Log.Exception(ex, "导出性能记录"); return 0; }
        }
        private System.Collections.IEnumerator SelfTest()
        {
            yield return new WaitForSecondsRealtime(10f);
            bool classified = false;
            try
            {
                _event |= 64;
                TimingProbe.Run();
                classified = CensusProbe.Run();
                _pageOpened = true; _physics.Start();
                _stageTestActive = true; _stages.Start();
                _nextObjects = Time.realtimeSinceStartup;
                _scene.Begin(Time.realtimeSinceStartup, gameObject.scene);
            }
            catch (Exception ex) { _stageTestActive = false; _stages.Dispose(); _scene.Reset(); Log.Exception(ex, "[PERFTEST] FAIL 初始化/分类"); yield break; }
            float deadline = Time.realtimeSinceStartup + 15f;
            while ((_scene.Running || !_items.Result.Valid || _stages.Reads < 60) && Time.realtimeSinceStartup < deadline) yield return null;
            try
            {
                bool ok = classified && _items.Result.Valid && _scene.Result.Valid && _buf.Count > 0;
                Log.Info("[PERFTEST] " + (ok ? "PASS" : "FAIL") + " objects=" + _items.Result.Total + " sceneAwake=" + _scene.Result.Awake + " sceneSleeping=" + _scene.Result.Sleeping + " physicsSource=" + _physics.Source + " maxObjectSliceMs=" + _items.Result.MaxSliceMs.ToString("0.000"));
                var timing = _stages.Snapshot();
                Log.Info("[TIMINGCAP] sampledReads=" + _stages.Reads + " main=" + timing.Main.Mean + "/" + timing.Main.Frames + " render=" + timing.Render.Mean + "/" + timing.Render.Frames + " gpu=" + timing.Gpu.Mean + "/" + timing.Gpu.Frames + " present=" + timing.Present.Mean + "/" + timing.Present.Frames + " limit=" + timing.Limit.Mean + "/" + timing.Limit.Frames);
            }
            catch (Exception ex) { Log.Exception(ex, "[PERFTEST] FAIL 完成"); }
            finally { _scene.Reset(); _stageTestActive = false; if (!_stageTiming.Value) _stages.Dispose(); } // Does not alter saved settings.
        }
        private void OnDestroy()
        {
            try { _physics.Dispose(); _stages.Dispose(); _items.Cancel(); _scene.Cancel(); foreach (var chart in _charts) chart?.Dispose(); _process?.Dispose(); }
            catch (Exception ex) { Log.Debug("结束跟踪失败：" + ex.Message); }
        }
    }
}
