using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DDEN.NetPerfTracker;

internal static class Program
{
    private static int _checks;
    private static void Check(bool condition, string description)
    { if (!condition) throw new Exception(description); _checks++; }

    private static void Main()
    {
        var ring = new SampleRing<int>(7);
        var expected = new List<int>();
        int capacity = 7;
        for (int value = 0; value < 10000; value++)
        {
            if (value % 113 == 0)
            {
                capacity = value % 31 + 1; ring.SetCapacity(capacity);
                if (expected.Count > capacity) expected.RemoveRange(0, expected.Count - capacity);
            }
            ring.Add(value); expected.Add(value);
            if (expected.Count > capacity) expected.RemoveAt(0);
            Check(ring.Count == expected.Count, "bounded count");
            for (int i = 0; i < expected.Count; i++) Check(ring[i] == expected[i], "resize/wrap preserves chronological newest records");
        }
        ring.Clear(); Check(ring.Count == 0, "clear"); ring.Add(42); Check(ring[0] == 42, "restart after clear");
        bool invalid = false;
        try { _ = ring[1]; } catch (ArgumentOutOfRangeException) { invalid = true; }
        Check(invalid, "unused capacity is not a record");
        ring.SetCapacity(0); ring.Add(43); Check(ring.Count == 1 && ring[0] == 43, "capacity minimum");

        float[] times = { 1, 2.5f, 3, 100, 101.2f, 0, 0 };
        Check(PerfMath.AtOrBefore(times, 5, 0) == -1, "no invented data before first record");
        Check(PerfMath.AtOrBefore(times, 5, 3) == 2, "exact irregular time");
        Check(PerfMath.AtOrBefore(times, 5, 99) == 2, "pause gap uses recorded timestamp");
        Check(PerfMath.AtOrBefore(times, 5, 100) == 3, "resume timestamp");
        Check(PerfMath.AtOrBefore(times, 5, 1000) == 4, "ignore spare capacity");
        Check(PerfMath.AtOrBefore(times, 0, 1000) == -1, "empty history");
        Check(Math.Abs(PerfMath.PerFrame(50, 60) - 0.833333f) < 0.0001f, "50 Hz at 60 fps is 0.83 steps/frame");
        Check(Math.Abs(PerfMath.PerFrame(372, 100) - 3.72f) < 0.0001f, "lower render rate raises scheduling ratio");
        Check(Math.Abs(PerfMath.PerSecond(372, 7.44) - 50) < 0.0001f, "3.72 steps/frame can still be ordinary 50 Hz");
        Check(PerfMath.PerFrame(0, 0) == -1 && PerfMath.PerSecond(0, 0) == -1, "missing denominator stays unknown");
        Check(PerfMath.ExpectedHz(1, 0.02f) == 50, "normal scheduling");
        Check(PerfMath.ExpectedHz(0.5f, 0.02f) == 25, "time scale affects expected wall-clock Hz");
        Check(PerfMath.ExpectedHz(0, 0.02f) == 0, "paused physics");
        Check(PerfMath.ExpectedHz(1, 0) == -1, "invalid fixed step stays unknown");

        var samples = new SampleRing<PerfSample>(2);
        var point = new PerfSample { T = 1, Target = 60, Scale = 1, PhysicsMs = -1,
            Objects = new ObjectCounts { Valid = true, Total = 100, Started = 0.5f, Ended = 0.9f, WorkMs = 2, MaxSliceMs = 0.4f } };
        samples.Add(point);
        point.Target = 144; point.Scale = 0; point.Objects.Total = 200; point.Objects.WorkMs = 20; point.PhysicsMs = 0;
        samples.Add(point);
        Check(samples[0].Target == 60 && samples[0].Scale == 1, "historical settings are not replaced by current values");
        Check(samples[0].Objects.Total == 100 && samples[0].Objects.WorkMs == 2, "historical census costs are immutable snapshots");
        Check(samples[0].Objects.Ended == 0.9f, "census age is not refreshed by repeating the result");
        Check(samples[0].PhysicsMs == -1 && samples[1].PhysicsMs == 0, "unavailable is distinct from measured zero");
        var alerts = new PerfAlerts();
        for (int i = 0; i < 20; i++) alerts.Frame(i * 0.016f, 16, 0.1f, null);
        Check(alerts.Events.Count == 0, "stable frames produce no events");
        alerts.Frame(0.34f, 80, 0.1f, null);
        Check(alerts.Events.Count == 1 && alerts.Events[0].Key == "frameSpike" && alerts.Events[0].Level == AlertLevel.Problem, "isolated 80ms spike is captured before the next one-second sample");
        alerts.Frame(0.36f, 100, 0.1f, null);
        Check(alerts.Events.Count == 1 && alerts.Events[0].Repeats == 2 && alerts.Events[0].Peak == 100 && alerts.Events[0].T == 0.34f && alerts.Events[0].LastT == 0.36f, "coalescing preserves first/last time, peak, repeat count");
        Check(alerts.Events[0].Before > 15 && alerts.Events[0].Before < 17, "event retains original pre-spike baseline");
        alerts.ResetBaselines(); alerts.Frame(10, 500, 20, null);
        Check(alerts.Events.Count == 1, "world/resume warm-up does not compare with old baseline");
        alerts.Clear();
        var first = new PerfSample { T = 1, Fps = 60, Sync = 100, Players = 3, HeapMb = 100, Objects = new ObjectCounts { Valid = true, Awake = 10, Total = 100, Active = 100, Ended = 1001 }, Stages = new StageTimings { Enabled = false } };
        alerts.Sample(first, 1000, 45);
        Check(alerts.Events.Count == 0, "first census establishes a baseline without declaring a jump");
        var next = first; next.T = 2; next.Objects.Ended = 1001.5f; next.Objects.Awake = 60;
        alerts.Sample(next, 1000, 45);
        Check(alerts.Events.Count == 1 && alerts.Events[0].Key == "awakeItems" && alerts.Events[0].T == 1.5f && alerts.Events[0].BaselineT == 1, "census change uses census time and previous census time, not sample/export time");
        alerts.Sample(next, 1000, 45);
        Check(alerts.Events.Count == 1 && alerts.Events[0].Repeats == 1, "reusing an aged census does not re-report the change");
        Check(alerts.Events[0].Reason.Contains("不能确定") && alerts.Events[0].Level == AlertLevel.Watch, "count correlation is not a causal claim");
        alerts.Clear(); first.Objects.Awake = 1; alerts.Sample(first, 1000, 45); next = first; next.T = 2; next.Objects.Ended = 1002; next.Objects.Awake = 2; alerts.Sample(next, 1000, 45);
        Check(alerts.Events.Count == 0, "one-to-two count doubling is suppressed by minimum absolute delta");
        var absent = TimingValue.Unknown;
        next.Focused = true;
        next.Stages = new StageTimings { Enabled = true, Total = absent, Main = absent, Render = absent, Gpu = absent, Present = absent, Limit = absent, Loop = absent };
        string report = PerfIndicators.Report(next, 60, 45);
        Check(report.Contains("GPU [不可用]") && !report.Contains("GPU [偏高]"), "missing GPU data never becomes a zero or high-load diagnosis");
        next.Stages.Gpu = new TimingValue { Mean = 25, Peak = 30, Frames = 50 };
        report = PerfIndicators.Report(next, 60, 45);
        Check(report.Contains("GPU [偏高]") && report.Contains("不相加"), "costly GPU is highlighted with non-additive warning");
        next.Focused = false;
        Check(PerfIndicators.Report(next, 60, 45).Contains("失焦") && !PerfIndicators.Report(next, 60, 45).Contains("GPU [偏高]"), "background GPU readings cannot diagnose a current bottleneck");
        next.Focused = true; next.Stages.Warming = true;
        Check(PerfIndicators.Report(next, 60, 45).Contains("预热"), "warm-up is distinct from unavailable");
        next.Stages.Warming = false;
        next.Fps = 30; next.WorstMs = 100;
        report = PerfIndicators.Report(next, 60, 45);
        Check(report.Contains("[低帧]") && report.Contains("[长帧]"), "visible text markers accompany color");
        for (int i = 0; i < 500; i++) alerts.Record(23, 10 + i * 4, "test", "test", 1, 2, "bounded", AlertLevel.Info);
        Check(alerts.Events.Count == 160, "anomaly history is bounded");
        string invariant = alerts.Events[159].Describe();
        Check(invariant.Contains("baselineT=") && invariant.Contains("lastT=") && invariant.Contains("before=") && invariant.Contains("after="), "export contains temporal and before/after evidence");
        alerts.Clear();
        for (int i = 0; i < 20; i++) alerts.Frame(i * 0.016f, 16, 0.1f, null, 0.5f);
        alerts.Frame(0.4f, 16, 0.1f, null, 12);
        Check(alerts.Events.Count == 1 && alerts.Events[0].Key == "physics2dSpike", "real physics timing spikes use their own counter, not scheduling");
        Check(alerts.Events[0].RuleRatio == 2 && alerts.Events[0].RuleMinDelta == 8, "event retains thresholds used at occurrence");
        alerts.Clear();
        alerts.Record(9, 1, "lowFPS", "fps", 45, 30, "limit possible", AlertLevel.Problem);
        alerts.Record(9, 2, "lowFPS", "fps", 45, 10, "limit possible", AlertLevel.Problem);
        Check(alerts.Events[0].Peak == 10 && PerfAlerts.Format(alerts.Events[0]).Contains("最低"), "coalesced low-FPS event preserves worst rather than highest FPS");
        var timeline = new SampleRing<PerfSample>(5); timeline.Add(new PerfSample { T = 1 }); timeline.Add(new PerfSample { T = 3 });
        var unordered = new SampleRing<PerfEvent>(5);
        unordered.Add(new PerfEvent { T = 2, Key = "laterDiscoveredFirst" }); unordered.Add(new PerfEvent { T = 0.5f, Key = "olderCensus" }); unordered.Add(new PerfEvent { T = 4, Key = "afterLastPoint" });
        var writer = new StringWriter(CultureInfo.InvariantCulture);
        PerfLog.WriteTimeline(writer, timeline, unordered, s => "SAMPLE t=" + s.T.ToString("0.000", CultureInfo.InvariantCulture));
        string text = writer.ToString();
        Check(text.IndexOf("EVENT t=0.500", StringComparison.Ordinal) < text.IndexOf("SAMPLE t=1.000", StringComparison.Ordinal), "late census event exports at its census time");
        Check(text.IndexOf("SAMPLE t=1.000", StringComparison.Ordinal) < text.IndexOf("EVENT t=2.000", StringComparison.Ordinal) && text.IndexOf("EVENT t=2.000", StringComparison.Ordinal) < text.IndexOf("SAMPLE t=3.000", StringComparison.Ordinal), "event records merge with corresponding sample time");
        Check(text.IndexOf("SAMPLE t=3.000", StringComparison.Ordinal) < text.IndexOf("EVENT t=4.000", StringComparison.Ordinal), "sub-second event after last sample is not lost");
        Console.WriteLine("PASS " + _checks + " checks");
    }
}
