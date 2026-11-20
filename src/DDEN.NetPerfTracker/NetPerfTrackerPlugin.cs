using System;
using System.Collections;
using System.Collections.Generic;
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
    /// <summary>
    /// DDEN.Perf —— 只读的**可视化性能分析器**（子 mod，管理器里单独一页）。
    ///
    /// 用途：联机时「一台客机掉线 → 另一台客机帧数暴跌、主机没事、必须全员重进或换层才恢复」这类问题，
    /// 现场看一眼曲线 + 事件标记就知道是掉帧、还是对象数量涨了。
    ///
    /// 设计约束（刻意为之）：
    ///  · **不打任何 Harmony 补丁**：只是一个 MonoBehaviour，不碰游戏逻辑，最坏情况是自己不采样。
    ///  · 采样分两档：轻采样（帧时间/帧率，默认 1 秒一次，开销可忽略）与重采样（数世界对象，默认 5 秒一次，
    ///    每次都把自己的耗时一起记下来 —— 免得"观察者自己就是元凶"）。
    ///  · 图表只画窗口内可见的列数（宽 600px 最多 600 次 Fill），不会因为攒了几千个点而变卡。
    ///  · 同时把数据写进 `BepInEx/config/DDEN/perf-*.log`（一行一个快照，key=value），方便事后 diff / 发给我。
    /// </summary>
    [BepInPlugin(Guid, "DDEN NetPerfTracker", "1.2.0")]
    [BepInDependency("net.cucorelib", BepInDependency.DependencyFlags.SoftDependency)]
    public class NetPerfTrackerPlugin : BaseUnityPlugin, IDdenModule
    {
        public const string Guid = "com.dden.perf";
        public const string Title = "性能分析";

        internal static NetPerfTrackerPlugin Instance;
        internal static ManualLogSource LogSource;
        internal static LogChannel Log;

        // ===== 配置 =====
        internal static ConfigEntry<bool> CfgEnabled;
        internal static ConfigEntry<float> CfgSampleSeconds;
        internal static ConfigEntry<float> CfgHeavySeconds;
        internal static ConfigEntry<float> CfgSlowFps;
        internal static ConfigEntry<float> CfgWindowSeconds;
        internal static ConfigEntry<int> CfgMaxSamples;
        internal static ConfigEntry<bool> CfgWriteLog;
        internal static ConfigEntry<float> CfgAutoStopMinutes;
        internal static ConfigEntry<bool> CfgVerbose;

        // ===== 一个采样点 =====
        private struct Sample
        {
            public float T;        // 相对开始的时间
            public float Fps;      // 本窗口平均
            public float Worst;    // 本窗口最差帧折算 fps
            public float DtMs;     // 最差帧时长（毫秒）
            public float Cpu;      // 整个游戏进程的 CPU 占用（占全部核心的百分比）
            public float Steps;    // 物理步/帧（理想 1.0，>1 说明物理在追帧）
            public int Items, Free, Sim, Bodies, Rigid, Cols, Sync, Dead, Players;
            public byte Event;     // 1=玩家数变化 2=换层 4=掉帧 8=联机状态变化
        }

        private readonly List<Sample> _buf = new List<Sample>(256);

        // 逐帧累加
        private int _frames;
        private float _winTime;
        private float _worstDt;
        private int _fixedSteps;        // 本窗口内 FixedUpdate 次数 → 物理步/帧

        // CPU 采样（整个游戏进程）
        private System.Diagnostics.Process _proc;
        private double _lastCpuSeconds = -1.0;
        private float _lastCpuWall = -1f;
        private float _cpuPercent = -1f;

        // 采样节拍
        private float _startedAt;
        private float _nextLight;
        private float _nextHeavy;
        private bool _stopped;
        private bool _paused;
        private float _slowFor;

        // 最近一次重采样结果（轻采样沿用它）
        private int _items = -1, _free = -1, _sim = -1, _bodies = -1, _sync = -1, _dead = -1, _players = -1;
        private int _rigid = -1, _cols = -1;
        private float _heavyCostMs;
        private float _heavyAt = -99f;
        private bool _colliderScan = true;      // 统计撞体太贵时自动关掉
        private int _heavySlowCount;
        private int _heavyPass;

        // 玩家数 / 世界 / 联机状态，用于判定"事件"
        private int _lastPlayers = -1;
        private int _lastWorldId;
        private int _lastDepth = int.MinValue;
        private bool _lastRunning;
        private byte _pendingEvent;

        // 日志
        private string _file;
        private StreamWriter _writer;
        private int _lines;
        private float _nextLog;

        // 曲线缓存
        private int _bufVersion;
        private int _cacheVersion = -1;
        private float[] _cFps, _cWorst, _cItems, _cFree, _cSim, _cBodies, _cSync, _cPlayers, _cCpu, _cSteps, _cRigid;
        private float[] _cT;

        // ===== IDdenModule =====
        public string Id => "perf";
        public string DisplayName => Title;
        public string Version => Info.Metadata.Version.ToString();
        public bool Ready => true;
        public string Summary
        {
            get
            {
                if (_stopped) return "已停止";
                if (_buf.Count == 0) return "等待采样";
                Sample s = _buf[_buf.Count - 1];
                return string.Format(CultureInfo.InvariantCulture, "{0:0} fps · {1} 点", s.Fps, _buf.Count);
            }
        }
        public IEnumerable<ConfigEntryBase> ConfigEntries => Config.Entries();

        private void Awake()
        {
            Instance = this;
            LogSource = base.Logger;

            CfgEnabled = Config.Bind("采样", "Enabled", true,
                "是否采样。关掉后这一页不更新曲线，也不写日志。");
            CfgSampleSeconds = Config.Bind("采样", "SampleSeconds", 1f,
                new ConfigDescription("多久记一个点（秒）。曲线的时间分辨率就是它。",
                    new AcceptableValueRange<float>(0.2f, 10f)));
            CfgHeavySeconds = Config.Bind("采样", "HeavySeconds", 5f,
                new ConfigDescription("多久统计一次世界里的物品/角色数量（秒）。这个比记帧时间贵，别调太小。",
                    new AcceptableValueRange<float>(1f, 60f)));
            CfgSlowFps = Config.Bind("采样", "SlowFpsWarn", 45f,
                new ConfigDescription("低于这个帧数就在曲线上标一个红点（只做标记，不改任何东西）。",
                    new AcceptableValueRange<float>(5f, 144f)));
            CfgWindowSeconds = Config.Bind("显示", "WindowSeconds", 180f,
                new ConfigDescription("页面上显示最近多少秒的曲线。",
                    new AcceptableValueRange<float>(10f, 1800f)));
            CfgMaxSamples = Config.Bind("显示", "MaxSamples", 2400,
                new ConfigDescription("内存里最多保留多少个采样点（2400 点 × 1 秒 = 40 分钟）。",
                    new AcceptableValueRange<int>(60, 20000)));
            CfgWriteLog = Config.Bind("日志", "WriteLog", true,
                "同时把数据写成文本日志（BepInEx/config/DDEN/perf-*.log），方便事后回看或发给作者。");
            CfgAutoStopMinutes = Config.Bind("日志", "AutoStopMinutes", 0f,
                new ConfigDescription("多少分钟后自动停止采样与写日志（0 = 一直开着）。",
                    new AcceptableValueRange<float>(0f, 600f)));
            CfgVerbose = Config.Bind("Debug", "Verbose", false, "同时在 BepInEx 主日志里输出采样行（排查启动问题时用）。");

            Log = DDEN.Shared.Log.For(LogSource, "Perf", CfgVerbose.Value);

            DdenHub.Register(this);
            Log.Info("性能分析已加载（不装补丁，只在管理器里画曲线 + 写日志）。");
        }

        private void Start()
        {
            _startedAt = Time.realtimeSinceStartup;
            _nextLight = _startedAt + Mathf.Max(0.2f, CfgSampleSeconds.Value);
            _nextHeavy = _startedAt;
            _nextLog = _startedAt;
            _lastWorldId = 0;
            if (CfgWriteLog.Value && CfgEnabled.Value) OpenFile();
        }

        // ===== 采样 =====
        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _frames++;
            _winTime += dt;
            if (dt > _worstDt) _worstDt = dt;

            if (_stopped || !CfgEnabled.Value) return;

            float now = Time.realtimeSinceStartup;

            if (CfgAutoStopMinutes.Value > 0f && now - _startedAt > CfgAutoStopMinutes.Value * 60f)
            {
                _stopped = true;
                Line("stop", "reason=auto-stop");
                Log.Info("已按设置自动停止采样。");
                return;
            }

            if (_winTime > 0.2f && _frames / Mathf.Max(0.0001f, _winTime) < CfgSlowFps.Value) _slowFor += dt;
            else _slowFor = 0f;

            if (now >= _nextHeavy)
            {
                _nextHeavy = now + Mathf.Max(1f, CfgHeavySeconds.Value);
                HeavySample();
            }

            if (_paused) return;
            if (now >= _nextLight)
            {
                _nextLight = now + Mathf.Max(0.2f, CfgSampleSeconds.Value);
                LightSample();
            }
        }

        /// <summary>物理步计数：物理跟不上时 Unity 会在一帧里补跑多次 FixedUpdate，这个数字直接反映物理压力。</summary>
        private void FixedUpdate()
        {
            _fixedSteps++;
        }

        /// <summary>整个游戏进程的 CPU 占用（占全部核心的百分比）。拿不到就返回 -1（曲线断开）。</summary>
        private float SampleCpu()
        {
            try
            {
                if (_proc == null) _proc = System.Diagnostics.Process.GetCurrentProcess();
                _proc.Refresh();
                double cpuSeconds = _proc.TotalProcessorTime.TotalSeconds;
                float wall = Time.realtimeSinceStartup;

                if (_lastCpuSeconds < 0.0 || _lastCpuWall < 0f)
                {
                    _lastCpuSeconds = cpuSeconds;
                    _lastCpuWall = wall;
                    return _cpuPercent;
                }

                float dtWall = wall - _lastCpuWall;
                if (dtWall < 0.05f) return _cpuPercent;

                double dtCpu = cpuSeconds - _lastCpuSeconds;
                _lastCpuSeconds = cpuSeconds;
                _lastCpuWall = wall;

                int cores = Mathf.Max(1, Environment.ProcessorCount);
                _cpuPercent = (float)(dtCpu / dtWall / cores * 100.0);
                return _cpuPercent;
            }
            catch
            {
                _proc = null;
                return -1f;
            }
        }

        private void LightSample()
        {
            // 帧率（本窗口）
            float fps = _winTime > 0f ? _frames / _winTime : 0f;
            float worst = _worstDt > 0f ? 1f / _worstDt : 0f;
            // 物理步/帧：帧数太少时这个比值没意义，用 1 兜底
            float steps = _frames > 0 ? _fixedSteps / (float)_frames : 1f;
            float cpu = SampleCpu();

            // 玩家数 / 联机状态 / 换层：这些便宜，每个轻采样都查
            int players = PlayersCount();
            if (players >= 0)
            {
                if (_lastPlayers >= 0 && players != _lastPlayers) _pendingEvent |= 1;
                _lastPlayers = players;
            }
            bool running = Mp.IsRunning;
            if (running != _lastRunning) { _pendingEvent |= 8; _lastRunning = running; }

            try
            {
                WorldGeneration w = WorldGeneration.world;
                if (w != null)
                {
                    int id = w.GetInstanceID();
                    int depth = Convert.ToInt32(Reflect.GetInstance(w, "biomeDepth") ?? 0);
                    if ((_lastWorldId != 0 && id != _lastWorldId) || (_lastDepth != int.MinValue && depth != _lastDepth))
                    {
                        _pendingEvent |= 2;
                    }
                    _lastWorldId = id;
                    _lastDepth = depth;
                }
            }
            catch { /* ignored */ }

            if (fps > 0f && fps < CfgSlowFps.Value) _pendingEvent |= 4;

            var s = new Sample
            {
                T = Time.realtimeSinceStartup - _startedAt,
                Fps = fps,
                Worst = worst,
                DtMs = _worstDt * 1000f,
                Cpu = cpu,
                Steps = steps,
                Items = _items,
                Free = _free,
                Sim = _sim,
                Bodies = _bodies,
                Rigid = _rigid,
                Cols = _cols,
                Sync = _sync,
                Dead = _dead,
                Players = players >= 0 ? players : _players,
                Event = _pendingEvent
            };
            _pendingEvent = 0;

            _buf.Add(s);
            _bufVersion++;
            int cap = Mathf.Max(60, CfgMaxSamples.Value);
            while (_buf.Count > cap) _buf.RemoveAt(0);

            // 重置窗口
            _frames = 0;
            _winTime = 0f;
            _worstDt = 0f;
            _fixedSteps = 0;

            // 日志：默认 5 秒一条（曲线点更多，但日志不必那么密）
            if (_writer != null && Time.realtimeSinceStartup >= _nextLog)
            {
                _nextLog = Time.realtimeSinceStartup + 5f;
                Line("hb", Describe(s));
            }
            if (CfgVerbose.Value) Log.Debug(Describe(s));
        }

        /// <summary>重采样：数世界对象。每次记下自己的耗时。</summary>
        private void HeavySample()
        {
            float t0 = Time.realtimeSinceStartup;
            try
            {
                Item[] all = UnityEngine.Object.FindObjectsOfType<Item>(true);
                _items = all.Length;
                _free = 0;
                _sim = 0;
                for (int i = 0; i < all.Length; i++)
                {
                    Item it = all[i];
                    if (it == null) continue;
                    Transform t = it.transform;
                    if (t == null || t.parent != null) continue;
                    _free++;
                    Rigidbody2D rb = it.rb;
                    if (rb != null && rb.simulated) _sim++;
                }
            }
            catch { /* ignored */ }

            try { _bodies = UnityEngine.Object.FindObjectsOfType<Body>().Length; }
            catch { /* ignored */ }

            // 物理压力：活跃刚体 + 碰撞体。碰撞体这项最贵：
            // 刚体一多（>150，典型是掉线后残留的一堆自由物品）就不再每轮都数，隔一轮数一次，
            // 免得"观察者自己"成了周期性卡顿的来源。
            try
            {
                Rigidbody2D[] rbs = UnityEngine.Object.FindObjectsOfType<Rigidbody2D>();
                int live = 0;
                for (int i = 0; i < rbs.Length; i++)
                {
                    if (rbs[i] != null && rbs[i].simulated) live++;
                }
                _rigid = live;
            }
            catch { _rigid = -1; }

            _heavyPass++;
            bool wantCols = _colliderScan && (_rigid >= 0 && _rigid <= 150 || (_heavyPass & 1) == 0);
            if (wantCols)
            {
                try { _cols = UnityEngine.Object.FindObjectsOfType<Collider2D>().Length; }
                catch { _cols = -1; }
            }

            try
            {
                IDictionary reg = ReflectDict("Together.NetObjectRegistry", "SyncRegistry");
                if (reg != null)
                {
                    _sync = reg.Count;
                    _dead = 0;
                    foreach (DictionaryEntry e in reg)
                    {
                        var go = e.Key as UnityEngine.Object;
                        if (go == null) { _dead++; continue; }
                        object v = e.Value;
                        if (v == null) { _dead++; continue; }
                        if (IsDestroyedFlag(v)) _dead++;
                    }
                }
            }
            catch { _sync = -1; _dead = -1; }

            _heavyCostMs = (Time.realtimeSinceStartup - t0) * 1000f;
            _heavyAt = Time.realtimeSinceStartup - _startedAt;

            // 自适应降级：统计自己太贵就先砍掉最贵的碰撞体计数（绝不能观察者把机器拖慢）
            if (_heavyCostMs > 5f && _colliderScan)
            {
                if (++_heavySlowCount >= 2)
                {
                    _colliderScan = false;
                    Log.Info($"统计自身耗时 {_heavyCostMs:0.0}ms 偏高，已关闭碰撞体计数（曲线不受影响）。");
                }
            }
            else if (_heavyCostMs <= 5f)
            {
                _heavySlowCount = 0;
            }
        }

        private static bool IsDestroyedFlag(object syncInfo)
        {
            try
            {
                Type t = syncInfo.GetType();
                PropertyInfo p = t.GetProperty("IsDestroyed", BindingFlags.Public | BindingFlags.Instance);
                if (p != null && p.PropertyType == typeof(bool)) return (bool)p.GetValue(syncInfo, null);
                FieldInfo f = t.GetField("IsDestroyed", BindingFlags.Public | BindingFlags.Instance);
                if (f != null && f.FieldType == typeof(bool)) return (bool)f.GetValue(syncInfo);
            }
            catch { /* ignored */ }
            return false;
        }

        private static int PlayersCount()
        {
            IDictionary d = ReflectDict("Together.ScavPlayer", "ClientIdToPlayerDict");
            return d == null ? -1 : d.Count;
        }

        // ===== 日志 =====
        private void OpenFile()
        {
            try
            {
                string dir = Path.Combine(Paths.ConfigPath, "DDEN");
                Directory.CreateDirectory(dir);
                _file = Path.Combine(dir, "perf-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".log");
                _writer = new StreamWriter(new FileStream(_file, FileMode.Create, FileAccess.Write, FileShare.ReadWrite),
                    new UTF8Encoding(false)) { AutoFlush = true };

                _writer.WriteLine("# DDEN 性能分析日志");
                _writer.WriteLine("# 一行一个快照，key=value；E=事件（hb 心跳 / stop 停止 / shutdown 退出）");
                _writer.WriteLine("# 环境：游戏 " + Application.version + " · Unity " + Application.unityVersion
                                 + " · 管理器 " + SafeVersion("com.dden.cumodm")
                                 + " · 修复 " + SafeVersion("com.dden.mpfix")
                                 + " · 联机框架 " + SafeTogetherVersion());
                _writer.WriteLine("# items=物品总数 free=无父级物品 sim=无父级且物理开着(" + "最关键" + ") bodies=角色数"
                                 + " sync=网络对象登记表条目 deadsync=其中已销毁条目 players=玩家数 costMs=本次统计耗时");
            }
            catch (Exception ex)
            {
                Log.Warn("打不开日志文件（曲线照常）: " + ex.Message);
            }
        }

        private string Describe(Sample s)
        {
            var sb = new StringBuilder(240);
            sb.Append("fps=").Append(s.Fps.ToString("0.0", CultureInfo.InvariantCulture));
            sb.Append(" worst=").Append(s.Worst.ToString("0.0", CultureInfo.InvariantCulture));
            sb.Append(" dtms=").Append(s.DtMs.ToString("0.0", CultureInfo.InvariantCulture));
            sb.Append(" cpu=").Append(s.Cpu.ToString("0.0", CultureInfo.InvariantCulture));
            sb.Append(" steps=").Append(s.Steps.ToString("0.00", CultureInfo.InvariantCulture));
            sb.Append(" target=").Append(Application.targetFrameRate);
            sb.Append(" vsync=").Append(QualitySettings.vSyncCount);
            sb.Append(" ts=").Append(Time.timeScale.ToString("0.00", CultureInfo.InvariantCulture));
            sb.Append(" fdt=").Append(Time.fixedDeltaTime.ToString("0.000", CultureInfo.InvariantCulture));
            sb.Append(" items=").Append(s.Items);
            sb.Append(" free=").Append(s.Free);
            sb.Append(" sim=").Append(s.Sim);
            sb.Append(" bodies=").Append(s.Bodies);
            sb.Append(" rigid=").Append(s.Rigid);
            sb.Append(" cols=").Append(s.Cols);
            sb.Append(" sync=").Append(s.Sync);
            sb.Append(" deadsync=").Append(s.Dead);
            sb.Append(" players=").Append(s.Players);
            sb.Append(" costMs=").Append(_heavyCostMs.ToString("0.0", CultureInfo.InvariantCulture));
            if (s.Event != 0) sb.Append(" ev=").Append(EventText(s.Event));
            return sb.ToString();
        }

        private static string EventText(byte e)
        {
            var parts = new List<string>(4);
            if ((e & 1) != 0) parts.Add("players");
            if ((e & 2) != 0) parts.Add("world");
            if ((e & 4) != 0) parts.Add("slow");
            if ((e & 8) != 0) parts.Add("net");
            return string.Join("+", parts.ToArray());
        }

        private void Line(string tag, string body)
        {
            if (_writer == null) return;
            try
            {
                _writer.Write("t=");
                _writer.Write((Time.realtimeSinceStartup - _startedAt).ToString("0.0", CultureInfo.InvariantCulture));
                _writer.Write(" E=");
                _writer.Write(tag);
                _writer.Write(' ');
                _writer.Write(body);
                _writer.WriteLine();
                _lines++;
            }
            catch (Exception ex) { Log.Debug("写日志失败：" + ex.Message); }
        }

        private static string SafeVersion(string guid)
        {
            try
            {
                BepInEx.PluginInfo info;
                if (BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue(guid, out info)) return info.Metadata.Version.ToString();
            }
            catch { /* ignored */ }
            return "-";
        }

        private static string SafeTogetherVersion()
        {
            try
            {
                Version v = CUCoreLib.Networking.MultiplayerApi.KrokMpVersion;
                if (v != null) return v.ToString();
            }
            catch { /* ignored */ }
            return "-";
        }

        // ===== 反射读字典（失败只试一次）=====
        private static readonly Dictionary<string, IDictionary> DictCache = new Dictionary<string, IDictionary>(StringComparer.Ordinal);
        private static readonly HashSet<string> DictFailed = new HashSet<string>(StringComparer.Ordinal);

        private static IDictionary ReflectDict(string typeName, string fieldName)
        {
            string key = typeName + "." + fieldName;
            IDictionary cached;
            if (DictCache.TryGetValue(key, out cached) && cached != null) return cached;
            if (DictFailed.Contains(key)) return null;
            try
            {
                Type t = Reflect.Type(typeName);
                if (t == null) { DictFailed.Add(key); return null; }
                FieldInfo f = t.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (f == null) { DictFailed.Add(key); return null; }
                var dict = f.GetValue(null) as IDictionary;
                if (dict == null) { DictFailed.Add(key); return null; }
                DictCache[key] = dict;
                return dict;
            }
            catch { DictFailed.Add(key); return null; }
        }

        // ===== 界面：曲线页 =====
        public void DrawPage(Rect area)
        {
            float y = area.y;

            Sample last = _buf.Count > 0 ? _buf[_buf.Count - 1] : default(Sample);
            GUI.Label(new Rect(area.x, y, area.width, 20f), "性能分析", DdenUI.Title);
            y += 22f;

            // KPI：只放最该一眼看到的三个数
            string kpi = string.Format(CultureInfo.InvariantCulture,
                "帧数 <color=#8cd97c>{0:0}</color> fps（最差 {1:0}）　CPU <color=#ffcf70>{2:0}%</color>　物理 <color=#89c4ff>{3:0.00}</color> 步/帧",
                last.Fps, last.Worst, last.Cpu, last.Steps);
            GUI.Label(new Rect(area.x, y, area.width, 18f), kpi, DdenUI.Row);
            y += 20f;

            if (_buf.Count < 2)
            {
                DdenUI.Label(new Rect(area.x, y, area.width, 40f),
                    _stopped ? "已停止采样。" : "采样中…进一局游戏、跑几秒就会有曲线。", DdenUI.Wrap);
                DrawButtons(area, ref y);
                return;
            }

            BuildCache();

            float win = Mathf.Max(10f, CfgWindowSeconds.Value);
            float tEnd = _buf[_buf.Count - 1].T;
            float tStart = Mathf.Max(0f, tEnd - win);

            // 4 张图平分剩余高度
            float chartH = Mathf.Clamp((area.height - y - 108f) / 4f, 54f, 130f);
            float chartW = area.width;

            Chart(new Rect(area.x, y, chartW, chartH), "帧数（绿=平均　橙=本窗口最差帧）", tStart, tEnd,
                new[] { _cFps, _cWorst },
                new[] { DdenUI.Good, DdenUI.Warn }, new[] { "平均", "最差" }, 0f, -1f, CfgSlowFps.Value);
            y += chartH + 5f;

            Chart(new Rect(area.x, y, chartW, chartH), "CPU（整个游戏进程 / 全部核心）", tStart, tEnd,
                new[] { _cCpu },
                new[] { DdenUI.Warn }, new[] { "CPU%" }, 0f, 100f, -1f);
            y += chartH + 5f;

            Chart(new Rect(area.x, y, chartW, chartH), "物理压力（蓝=活跃刚体　橙=物理步/帧×100，正常≈83，>100 说明物理在追帧）",
                tStart, tEnd,
                new[] { _cRigid, _cSteps },
                new[] { DdenUI.Accent, DdenUI.Warn }, new[] { "刚体", "步/帧×100" }, 0f, -1f, -1f);
            y += chartH + 5f;

            Chart(new Rect(area.x, y, chartW, chartH), "世界对象（绿=物品　橙=无主物品　蓝=无主且物理开　灰=角色）",
                tStart, tEnd,
                new[] { _cItems, _cFree, _cSim, _cBodies },
                new[] { DdenUI.Good, DdenUI.Warn, DdenUI.Accent, DdenUI.TextDim },
                new[] { "物品", "无主", "无主+物理", "角色" }, 0f, -1f, -1f);
            y += chartH + 6f;

            // 文字区：图上不适合画的细节都放这里
            string detail = string.Format(CultureInfo.InvariantCulture,
                "物品 {0}（无主 {1} · 无主且物理开着 {2}）　角色 {3}　活跃刚体 {4}　碰撞体 {5}\n" +
                "网络对象 {6}（其中已销毁 {7}）　玩家 {8}　targetFrameRate {9}　vSync {10}　timeScale {11}　fixedDt {12}\n" +
                "重采样耗时 {13:0.0}ms（间隔 {14:0}s）· 采样点 {15} 个 · 窗口 {16:0}s · 低于 {17:0} fps 标红点",
                last.Items, last.Free, last.Sim, last.Bodies, last.Rigid, last.Cols,
                last.Sync, last.Dead, last.Players, Application.targetFrameRate, QualitySettings.vSyncCount,
                Time.timeScale.ToString("0.00", CultureInfo.InvariantCulture),
                Time.fixedDeltaTime.ToString("0.000", CultureInfo.InvariantCulture),
                _heavyCostMs, CfgHeavySeconds.Value, _buf.Count, win, CfgSlowFps.Value);
            GUI.Label(new Rect(area.x, y, area.width, 56f), detail, DdenUI.Wrap);
            y += 58f;

            DrawButtons(area, ref y);
        }

        private void DrawButtons(Rect area, ref float y)
        {
            if (DdenUI.Button(new Rect(area.x, y, 90f, 24f), _paused ? "继续" : "暂停", true)) _paused = !_paused;
            if (DdenUI.Button(new Rect(area.x + 96f, y, 90f, 24f), "清空")) { _buf.Clear(); _bufVersion++; }
            if (DdenUI.Button(new Rect(area.x + 192f, y, 110f, 24f), "写一条日志"))
            {
                if (_writer == null && CfgWriteLog.Value) OpenFile();
                Sample s = _buf.Count > 0 ? _buf[_buf.Count - 1] : default(Sample);
                Line("manual", Describe(s));
            }
            if (DdenUI.Button(new Rect(area.x + 308f, y, 120f, 24f), "打开日志目录")) OpenFolder();
            y += 30f;

            GUI.Label(new Rect(area.x, y, area.width, 34f),
                "这一页只读：不打补丁、不改游戏状态。曲线攒在内存里（上限见设置），同时在 BepInEx/config/DDEN/ 写文本日志。",
                DdenUI.Wrap);
        }

        public bool DrawSettings(Rect area) => false;

        /// <summary>
        /// 画一张折线图：网格 + 多条曲线 + 事件竖线 + 红点（低于阈值）。
        /// 只按像素列聚合，所以几千个点也不会把这一页画卡。
        /// </summary>
        private void Chart(Rect r, string title, float tStart, float tEnd,
                           float[][] series, Color[] colors, string[] labels,
                           float min, float max, float warnBelow)
        {
            DdenUI.Fill(r, new Color(0f, 0f, 0f, 0.28f));
            DdenUI.Frame(r, DdenUI.AccentDim);

            GUI.Label(new Rect(r.x + 6f, r.y + 2f, r.width - 12f, 16f), title, DdenUI.Sub);

            Rect inner = new Rect(r.x + 34f, r.y + 18f, r.width - 42f, r.height - 24f);
            if (inner.width < 8f || inner.height < 8f) return;

            // 值域
            float vmax = max;
            if (vmax <= 0f)
            {
                vmax = 1f;
                for (int si = 0; si < series.Length; si++)
                {
                    float[] arr = series[si];
                    if (arr == null) continue;
                    for (int i = 0; i < arr.Length; i++)
                    {
                        if (arr[i] > vmax) vmax = arr[i];
                    }
                }
                vmax *= 1.15f;
            }

            // 网格 + 刻度
            for (int g = 0; g <= 2; g++)
            {
                float gy = inner.y + inner.height * (g / 2f);
                DdenUI.Fill(new Rect(inner.x, gy, inner.width, 1f), new Color(1f, 1f, 1f, 0.07f));
                float val = Mathf.Lerp(vmax, min, g / 2f);
                GUI.Label(new Rect(r.x + 2f, gy - 7f, 30f, 14f), val.ToString("0"), DdenUI.Small);
            }

            int count = _buf.Count;
            float span = Mathf.Max(0.001f, tEnd - tStart);
            int cols = Mathf.Max(2, Mathf.RoundToInt(inner.width));

            // 每列取窗口内的样本（取最近一个）
            int startIdx = 0;
            while (startIdx < count - 1 && _buf[startIdx].T < tStart) startIdx++;

            for (int si = 0; si < series.Length; si++)
            {
                float[] arr = series[si];
                if (arr == null) continue;
                Color c = colors != null && si < colors.Length ? colors[si] : DdenUI.Accent;
                float prevX = -1f, prevY = -1f;
                for (int col = 0; col < cols; col++)
                {
                    int idx = startIdx + Mathf.RoundToInt((count - 1 - startIdx) * (col / (float)(cols - 1)));
                    if (idx < 0 || idx >= count) continue;
                    float v = arr[idx];
                    if (v < 0f) continue;               // 该点没有数据
                    float px = inner.x + inner.width * (col / (float)(cols - 1));
                    float py = inner.y + inner.height * (1f - Mathf.InverseLerp(min, vmax, Mathf.Clamp(v, min, vmax)));
                    // 两点之间连成一段竖线（1px 粗），够细也够省
                    if (prevX >= 0f) DdenUI.Fill(new Rect(px - 1f, Mathf.Min(py, prevY), 2f, Mathf.Abs(py - prevY) + 1f), c);
                    else DdenUI.Fill(new Rect(px, py, 2f, 1f), c);
                    prevX = px; prevY = py;
                }
            }

            // 事件竖线 + 掉帧红点
            for (int i = startIdx; i < count; i++)
            {
                Sample s = _buf[i];
                float px = inner.x + inner.width * Mathf.Clamp01((s.T - tStart) / span);
                if (s.Event != 0)
                {
                    Color ec = (s.Event & 1) != 0 ? DdenUI.Warn : ((s.Event & 2) != 0 ? DdenUI.Accent : DdenUI.Bad);
                    DdenUI.Fill(new Rect(px, inner.y, 1f, inner.height), new Color(ec.r, ec.g, ec.b, 0.55f));
                }
                if (warnBelow > 0f && s.Fps > 0f && s.Fps < warnBelow)
                {
                    float py = inner.y + inner.height * (1f - Mathf.InverseLerp(min, vmax, Mathf.Clamp(s.Fps, min, vmax)));
                    DdenUI.Fill(new Rect(px - 1f, py - 1f, 3f, 3f), DdenUI.Bad);
                }
            }

            // 时间轴说明
            GUI.Label(new Rect(inner.x, r.yMax - 15f, 80f, 14f), "-" + Mathf.RoundToInt(tEnd - tStart) + "s", DdenUI.Small);
            DdenUI.Label(new Rect(inner.x, r.yMax - 15f, inner.width, 14f), "", DdenUI.Small);

            // 图例
            if (labels != null)
            {
                float lx = inner.xMax;
                for (int i = labels.Length - 1; i >= 0; i--)
                {
                    float w = 14f + labels[i].Length * 11f;
                    lx -= w;
                    if (lx < inner.x + 40f) break;
                    Color c = colors != null && i < colors.Length ? colors[i] : DdenUI.Accent;
                    DdenUI.Fill(new Rect(lx, r.y + 6f, 8f, 8f), c);
                    GUI.Label(new Rect(lx + 10f, r.y + 1f, w - 10f, 16f), labels[i], DdenUI.Sub);
                }
            }
        }

        /// <summary>把环形缓冲摊平成数组（只在新增采样点时重建一次）。</summary>
        private void BuildCache()
        {
            if (_cacheVersion == _bufVersion && _cFps != null) return;
            _cacheVersion = _bufVersion;

            int n = _buf.Count;
            _cT = new float[n];
            _cFps = new float[n];
            _cWorst = new float[n];
            _cCpu = new float[n];
            _cSteps = new float[n];
            _cRigid = new float[n];
            _cItems = new float[n];
            _cFree = new float[n];
            _cSim = new float[n];
            _cBodies = new float[n];
            _cSync = new float[n];
            _cPlayers = new float[n];

            for (int i = 0; i < n; i++)
            {
                Sample s = _buf[i];
                _cT[i] = s.T;
                _cFps[i] = s.Fps;
                _cWorst[i] = s.Worst;
                _cCpu[i] = s.Cpu;
                _cSteps[i] = s.Steps * 100f;      // ×100 让它和"刚体数"同量级，否则永远贴在 0 线上看不见
                _cRigid[i] = s.Rigid;
                _cItems[i] = s.Items;
                _cFree[i] = s.Free;
                _cSim[i] = s.Sim;
                _cBodies[i] = s.Bodies;
                _cSync[i] = s.Sync;
                _cPlayers[i] = s.Players;
            }
        }

        private void OpenFolder()
        {
            try
            {
                string dir = Path.Combine(Paths.ConfigPath, "DDEN");
                Directory.CreateDirectory(dir);
                Application.OpenURL("file:///" + dir.Replace('\\', '/'));
            }
            catch (Exception ex) { Log.Debug("打开目录失败：" + ex.Message); }
        }

        private void OnDestroy()
        {
            try
            {
                if (_writer != null)
                {
                    Line("shutdown", "lines=" + _lines);
                    _writer.Flush();
                    _writer.Dispose();
                    _writer = null;
                }
            }
            catch { /* ignored */ }
        }
    }
}
