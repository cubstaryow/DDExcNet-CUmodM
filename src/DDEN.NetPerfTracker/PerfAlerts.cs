using static DDEN.NetPerfTracker.ModuleText;
using System;
using System.Globalization;
using System.Text;

namespace DDEN.NetPerfTracker
{
    internal enum AlertLevel { Info, Watch, Problem }
    internal sealed class PerfEvent
    {
        internal float T, LastT, Before, After, Peak, BaselineT;
        internal float RuleRatio, RuleMinDelta, MergeSeconds;
        internal int Repeats;
        internal string Key, Metric, Reason;
        internal AlertLevel Level;
        internal string Describe()
        {
            var c = CultureInfo.InvariantCulture;
            return "t=" + T.ToString("0.000", c) + " event=" + Key + " level=" + Level +
                " before=" + Before.ToString("0.###", c) + " after=" + After.ToString("0.###", c) +
                " peak=" + Peak.ToString("0.###", c) + " baselineT=" + BaselineT.ToString("0.000", c) +
                " ruleRatio=" + RuleRatio.ToString("0.###", c) + " ruleMinDelta=" + RuleMinDelta.ToString("0.###", c) + " mergeSeconds=" + MergeSeconds.ToString("0.###", c) +
                " lastT=" + LastT.ToString("0.000", c) + " repeats=" + Repeats + " note=" + Metric + "：" + Reason;
        }
    }
    internal sealed class PerfAlerts
    {
        private static readonly string[] FrameKeys = { "frameSpike", "observerSpike", "cpuTotalSpike", "cpuMainSpike", "cpuRenderSpike", "gpuSpike", "presentWaitSpike", "targetWaitSpike", "loopSpike", "physics2dSpike" };
        private static readonly string[] FrameLabels = { "帧间隔ms", "跟踪器自身CPU ms", "CPU整帧（含等待）ms", "CPU主线程ms", "CPU渲染线程ms", "GPU ms", "呈现等待ms", "限帧等待ms", "主循环（含等待）ms", "物理2D ms" };
        private readonly float[] _baseline = new float[10];
        private readonly int[] _observations = new int[10];
        private readonly PerfEvent[] _last = new PerfEvent[24];
        private ObjectCounts _objects;
        private SceneCounts _scene;
        private PerfSample _previous;
        private bool _hasPrevious;
        internal readonly SampleRing<PerfEvent> Events = new SampleRing<PerfEvent>(160);
        internal int Revision { get; private set; }
        internal float Ratio = 2, MinMs = 8, CountDelta = 50, Cooldown = 3;
        internal bool Enabled = true;
        internal void Record(int slot, float t, string key, string metric, float before, float after, string reason, AlertLevel level, float baselineT = -1)
        {
            var e = _last[slot];
            if (e != null && t >= e.LastT && t - e.LastT < Cooldown)
            { e.LastT = t; e.After = after; e.Peak = key == "lowFPS" ? Math.Min(e.Peak, after) : Math.Max(e.Peak, after); e.Repeats++; if (level > e.Level) e.Level = level; }
            else
            {
                e = new PerfEvent { T = t, LastT = t, Before = before, After = after, Peak = after,
                    BaselineT = baselineT, Key = key, Metric = metric, Reason = reason, Level = level, Repeats = 1,
                    RuleRatio = slot <= 8 || slot == 21 || (slot >= 10 && slot <= 16) ? Ratio : -1,
                    RuleMinDelta = slot <= 8 || slot == 21 ? (slot == 1 ? Math.Min(2, MinMs) : MinMs) : slot >= 10 && slot <= 16 ? CountDelta : -1,
                    MergeSeconds = Cooldown };
                Events.Add(e); _last[slot] = e;
            }
            Revision++;
        }
        internal void Frame(float t, float ms, float observer, float[] stages, float physics = -1)
        {
            if (!Enabled) return;
            for (int i = 0; i < 10; i++)
            {
                float value = i == 0 ? ms : i == 1 ? observer : i == 9 ? physics : stages == null ? -1 : stages[i - 2];
                if (value < 0 || float.IsNaN(value) || float.IsInfinity(value)) continue;
                float baseline = _baseline[i], delta = i == 1 ? Math.Min(2, MinMs) : MinMs;
                // A longer deliberate frame-cap wait is a state, not a performance fluctuation warning.
                if (i != 7 && _observations[i] >= 12 && value >= baseline * Ratio && value - baseline >= delta)
                    Record(i == 9 ? 21 : i, t, FrameKeys[i], FrameLabels[i], baseline, value,
                        i < 2 ? "相对近期平稳基线上升，同时超过倍数与最小毫秒变化阈值。" : "阶段读数突增；计时可能延迟若干帧，等待也不等于计算瓶颈。",
                        i == 0 && value >= 50 ? AlertLevel.Problem : AlertLevel.Watch);
                _baseline[i] = _observations[i] == 0 ? value : baseline + (value - baseline) * 0.03f;
                _observations[i]++;
            }
        }
        private void Count(int slot, float t, string key, string label, int before, int after, float baselineT, bool suspect)
        {
            float delta = Math.Abs(after - before), small = Math.Max(1, Math.Min(before, after));
            if (before < 0 || after < 0 || delta < CountDelta || delta / small < Ratio - 1) return;
            Record(slot, t, key, label, before, after,
                "两次完整统计之间发生较大变化；是区间变化，不能确定生成/唤醒瞬间，也不能单凭数量认定掉帧原因。",
                suspect && after > before ? AlertLevel.Watch : AlertLevel.Info, baselineT);
        }
        internal void Sample(PerfSample s, float engineStart, float slowFps)
        {
            if (!Enabled) return;
            if (s.Fps < slowFps)
                Record(9, s.T, "lowFPS", "低于帧率警戒", slowFps, s.Fps, "事实读数低于警戒；主动限帧或垂直同步也可能造成此结果。", AlertLevel.Problem);
            if (s.Objects.Valid && (!_objects.Valid || s.Objects.Ended != _objects.Ended))
            {
                if (_objects.Valid)
                {
                    float t = s.Objects.Ended - engineStart, prior = _objects.Ended - engineStart;
                    Count(10, t, "items", "注册物品数", _objects.Total, s.Objects.Total, prior, false);
                    Count(11, t, "activeItems", "启用物品数", _objects.Active, s.Objects.Active, prior, false);
                    Count(12, t, "awakeItems", "清醒无父级动态物品数", _objects.Awake, s.Objects.Awake, prior, true);
                    Count(13, t, "sleepingItems", "休眠无父级动态物品数", _objects.Sleeping, s.Objects.Sleeping, prior, false);
                    Count(14, t, "downwardItems", "向下运动物品数", _objects.Downward, s.Objects.Downward, prior, true);
                }
                _objects = s.Objects;
            }
            if (s.Scene.Valid && (!_scene.Valid || s.Scene.Ended != _scene.Ended))
            {
                if (_scene.Valid) Count(15, s.Scene.Ended - engineStart, "sceneAwake", "场景清醒动态刚体数", _scene.Awake, s.Scene.Awake, _scene.Ended - engineStart, true);
                _scene = s.Scene;
            }
            if (_hasPrevious)
            {
                Count(16, s.T, "syncEntries", "网络条目数", _previous.Sync, s.Sync, _previous.T, false);
                if (_previous.Players >= 0 && s.Players >= 0 && s.Players != _previous.Players)
                    Record(17, s.T, "players", "玩家字典条目变化", _previous.Players, s.Players, "检测到人数变化，已触发物品补清点；检测有采样间隔，不等同精确断线时刻。", AlertLevel.Info, _previous.T);
                if (Math.Abs(s.HeapMb - _previous.HeapMb) >= 64 && Math.Max(s.HeapMb, _previous.HeapMb) >= Math.Max(1, Math.Min(s.HeapMb, _previous.HeapMb)) * 1.25f)
                    Record(18, s.T, "heap", "托管堆MB", _previous.HeapMb, s.HeapMb, "托管内存突变；可能与资源分配或回收有关，不等于GC耗时。", AlertLevel.Info, _previous.T);
                if (s.StepsPerFrame >= Math.Max(1, _previous.StepsPerFrame * Ratio) && s.StepsPerFrame - _previous.StepsPerFrame >= 1)
                    Record(19, s.T, "scheduling", "物理步/帧", _previous.StepsPerFrame, s.StepsPerFrame, "补步增多；渲染帧率下降本身就会使该比值升高，不表示物理耗时增加。", AlertLevel.Info, _previous.T);
            }
            if (s.GC2 > 0) Record(20, s.T, "gc2", "完整GC次数", 0, s.GC2, "本采样区间发生完整GC；没有GC计时，不能断言它造成了长帧。", AlertLevel.Info, s.T - s.WindowSeconds);
            _previous = s; _hasPrevious = true;
        }
        internal void ResetBaselines()
        { Array.Clear(_baseline, 0, 10); Array.Clear(_observations, 0, 10); _objects = default; _scene = default; _hasPrevious = false; Array.Clear(_last, 0, _last.Length); }
        internal void Clear() { ResetBaselines(); Events.Clear(); Revision++; }
        internal string Recent(int maximum)
        {
            var b = new StringBuilder();
            for (int i = Events.Count - 1; i >= Math.Max(0, Events.Count - maximum); i--)
                b.Append(Format(Events[i])).Append('\n');
            return b.Length == 0 ? L("尚无突变记录；缺少计时数据不代表已排除对应瓶颈。") : b.ToString().TrimEnd();
        }
        internal static string Format(PerfEvent e)
        {
            var b = new StringBuilder();
            string color = e.Level == AlertLevel.Problem ? "#f06b6b" : e.Level == AlertLevel.Watch ? "#ffa050" : "#80bfff";
            b.Append("<color=").Append(color).Append(">[").Append(e.Level == AlertLevel.Problem ? L("低帧/长帧") : e.Level == AlertLevel.Watch ? L("排查线索") : L("状态变化")).Append("] ");
            b.Append(e.T.ToString("0.0")).Append("s ").Append(L(e.Metric)).Append(' ').Append(e.Before.ToString("0.##")).Append(" → ").Append(e.After.ToString("0.##"));
            if (e.Repeats > 1) b.Append(LF($"（合并{e.Repeats}次，至{e.LastT:0.0}s，{L(e.Key == "lowFPS" ? "最低" : "峰")}{e.Peak:0.##}）"));
            if (e.BaselineT >= 0) b.Append(LF($"（前值记录于{e.BaselineT:0.0}s）"));
            if (e.RuleRatio > 0) b.Append(LF($"（阈值≥{e.RuleRatio:0.##}倍且变化≥{e.RuleMinDelta:0.##}）"));
            return b.Append("</color> ").Append(L(e.Reason)).ToString();
        }
    }
    internal static class PerfIndicators
    {
        internal static string Cost(string name, float value, float limit)
        {
            if (value < 0) return "<color=#9198a0>" + L(name) + L(" [不可用]</color>");
            bool high = value >= limit;
            return "<color=" + (high ? "#ffa050" : "#b8c3ce") + ">" + L(name) + (high ? L(" [偏高] ") : " ") + value.ToString("0.##") + "ms</color>";
        }
        internal static string Report(PerfSample s, float target, float lowFps)
        {
            float budget = 1000 / Math.Max(1, target);
            string status = s.Fps < lowFps ? L("<color=#f06b6b>[低帧] ") + s.Fps.ToString("0.0") + L("fps 低于警戒 ") + lowFps.ToString("0") + "</color>" : L("<color=#7ccf98>[帧率未低于警戒]</color>");
            status += s.WorstMs >= 50 ? L("　<color=#f06b6b>[长帧] 最长 ") + s.WorstMs.ToString("0.0") + "ms</color>" : "";
            status += L("　诊断参考 ") + target.ToString("0") + "fps / " + budget.ToString("0.0") + L("ms（不改变游戏限帧）\n");
            status += L("游戏目标帧率 ") + (s.Target > 0 ? s.Target.ToString() : L("未指定")) + L(" · 垂直同步 ") + s.VSync + "　";
            status += Cost(L("物理2D"), s.PhysicsMs, budget * 0.5f) + "　" + Cost(L("自身峰值"), s.SelfPeakMs, Math.Max(2, budget * 0.15f));
            if (!s.Stages.Enabled) return status + L("　<color=#9198a0>[阶段计时关闭]</color>");
            var t = s.Stages;
            if (t.Warming) return status + L("　<color=#9198a0>[阶段计时预热中]</color>");
            status += "\n" + Cost(L("CPU主线程"), t.Main.Frames > 0 ? t.Main.Mean : -1, budget * 0.8f);
            status += s.Focused ? "　" + Cost(L("渲染线程"), t.Render.Frames > 0 ? t.Render.Mean : -1, budget * 0.8f) + "　" + Cost("GPU", t.Gpu.Frames > 0 ? t.Gpu.Mean : -1, budget * 0.8f) : L("　<color=#9198a0>[窗口失焦，渲染/GPU未评估]</color>");
            status += "\n" + Wait(L("呈现等待"), t.Present, s.Fps < lowFps, Math.Max(2, budget * 0.4f)) + "　" + Wait(L("主动限帧等待"), t.Limit, false, 0) + L("（等待不等于计算；计时有延迟，不相加）");
            return status;
        }
        private static string Wait(string name, TimingValue value, bool lowFps, float threshold)
        {
            if (value.Frames == 0) return "<color=#9198a0>" + name + L(" [不可用]</color>");
            bool watch = lowFps && value.Mean >= threshold;
            return "<color=" + (watch ? "#ffa050" : "#80bfff") + ">" + name + (watch ? L(" [等待偏多·原因未定] ") : L(" [状态] ")) + value.Mean.ToString("0.##") + "ms</color>";
        }
    }
}
