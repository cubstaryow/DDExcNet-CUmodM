using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
        int revision = alerts.Revision, count = alerts.Events.Count;
        DDEN.Shared.DdenLocalization.Language = DDEN.Shared.DdenLanguage.English;
        Check(PerfIndicators.Report(next, 60, 45).Contains("[LOW FPS]"), "English diagnostic markers");
        Check(PerfAlerts.Format(alerts.Events[159]).Contains("State change"), "stored history is localized at display time");
        Check(alerts.Events.Count == count && alerts.Revision == revision && alerts.Events[159].Describe() == invariant, "language changes preserve anomaly history, revisions and log evidence");
        Check(PerfIndicators.Cost("GPU", -1, 10).Contains("unavailable"), "missing timing remains unavailable in English");
        DDEN.Shared.DdenLocalization.Language = DDEN.Shared.DdenLanguage.Chinese;
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
        ProbeChecks();
        Console.WriteLine("PASS " + _checks + " checks");
    }

    private sealed class DangerousText
    {
        public override string ToString() => throw new Exception("must not call arbitrary ToString");
    }
    private static void ProbeChecks()
    {
        var inbox = new ProbeInbox();
        inbox.Add("LES", true, false, "disabled", 0);
        Check(inbox.Totals.Errors == 0 && inbox.Messages().Length == 0, "disabled probe never retains errors");
        inbox.Enable(true);
        inbox.Add("DDEN NetPerfTracker", true, false, "observer error", 0);
        inbox.Add("Other", true, false, "unrelated error", 0);
        inbox.Add("Together", false, false, "ordinary multiplayer chat", 0);
        Check(inbox.Messages().Length == 0, "unrelated logs and normal chat do not feed the probe");
        inbox.Add("LES", true, false, "first failure\n at entity.Update", 0.1f);
        for (int i = 1; i < 10000; i++) inbox.Add("LES", true, false, "repeated failure", 0.1f);
        var counters = inbox.Drain(out var totalSnapshot);
        Check(totalSnapshot.Errors == counters.Errors, "window and cumulative counts share an atomic snapshot");
        Check(counters.Errors == 10000 && counters.DetailSkipped == 9992 && inbox.Messages().Length == 8, "error storm counts every message while retaining at most eight details per second");
        Check(inbox.Drain().Errors == 0 && inbox.Totals.Errors == 10000, "window drain preserves cumulative totals");
        inbox.Add("LES", true, false, "next second", 1.1f);
        for (int i = 0; i < 100; i++) inbox.Add("LES", true, false, "delayed callback", i % 2 == 0 ? 0.9f : 1.2f);
        Check(inbox.Drain().DetailSkipped == 93, "out-of-order callback timestamps cannot reopen detail budgets");
        long beforePause = inbox.Totals.Errors;
        inbox.Enable(false); inbox.Add("LES", true, false, "paused", 1);
        Check(inbox.Totals.Errors == beforePause, "stopping the listener stops capture without erasing history");
        inbox.Enable(true);
        for (int i = 1; i <= 120; i++) inbox.Add("Together", true, false, "later failure " + i, i);
        var retained = inbox.Messages();
        Check(retained.Length == 97 && retained[0].Text.StartsWith("first failure"), "bounded rolling samples retain the first original error after an extended storm");
        inbox.Clear();
        inbox.Add("LES", true, false, new string('x', 9000), 121);
        Check(inbox.Messages()[0].Text.Length < 2100 && inbox.Messages()[0].Text.EndsWith("[truncated]"), "large stack storage is bounded and explicitly marked");
        inbox.Add("LES", true, false, new DangerousText(), 122);
        Check(inbox.Totals.Errors == 2, "log sampling does not invoke arbitrary object ToString methods");
        inbox.Add("Unity Log", true, false, "LiteEntitySystem.Internal.InternalEntity failure", 123);
        inbox.Add("LES", false, false, "P:1 Request baseline 30 because timeout", 124);
        inbox.Add("Together", false, true, "warning", 125);
        Check(inbox.Totals.Errors == 3 && inbox.Totals.BaselineLogs == 1 && inbox.Totals.Warnings == 1, "external LES stack messages, observed baseline logs and warnings remain separate");
        inbox.Add("LES", true, false, "invalid time", float.NaN);
        Check(inbox.Totals.Errors == 3, "invalid timestamps cannot pollute counters");
        inbox.Clear();
        var tasks = new Task[4];
        for (int worker = 0; worker < tasks.Length; worker++)
            tasks[worker] = Task.Run(() => { for (int i = 0; i < 2500; i++) inbox.Add("LES", true, false, "concurrent", 1); });
        Task.WaitAll(tasks);
        Check(inbox.Totals.Errors == 10000 && inbox.Messages().Length == 8, "concurrent log callbacks preserve exact totals and bounded storage");
        inbox.Enable(false); inbox.Clear();
        Check(inbox.Totals.Errors == 0 && inbox.Messages().Length == 0, "explicit clear releases evidence and counters");

        var tail = new FrameTail();
        for (int i = 0; i < 1000; i++) tail.Add(i * 0.01f, i % 100 == 0 ? 100 : 10);
        Check(Math.Abs(tail.LowOnePercent(9.999f, out int frames) - 10) < 0.001f && frames == 1000, "slowest one percent reflects repeated 100ms hitches rather than average FPS");
        Check(tail.LowOnePercent(30, out frames) == -1 && frames == 0, "expired frame history never becomes a current low-FPS value");
        tail.Clear();
        for (int i = 0; i < 119; i++) tail.Add(1, 16);
        Check(tail.LowOnePercent(1, out frames) == -1 && frames == 119, "short warm-up windows remain unavailable");
        tail.Add(1, 16);
        Check(Math.Abs(tail.LowOnePercent(1, out frames) - 62.5f) < 0.001f, "stable frames retain stable low-tail estimate");
        tail.Clear(); tail.Add(1, float.NaN); tail.Add(float.PositiveInfinity, 10); tail.Add(1, -10);
        Check(tail.LowOnePercent(1, out frames) == -1 && frames == 0, "invalid frame readings are rejected");

        var alerts = new PerfAlerts();
        for (int i = 0; i < 20; i++) alerts.Frame(i * 0.016f, 16, 0.1f, null);
        alerts.Frame(1, 100, 0.1f, null);
        int retainedCount = alerts.Events.Count, revision = alerts.Revision;
        alerts.Enabled = false;
        alerts.Frame(2, 500, 20, null);
        alerts.Sample(new PerfSample { T = 2, Fps = 1, GC2 = 4 }, 0, 45);
        Check(alerts.Events.Count == retainedCount && alerts.Revision == revision, "disabled fluctuation alerts preserve history without producing new events");
        alerts.Enabled = true; alerts.Clear();
        var stages = new float[] { -1, -1, -1, -1, -1, 1, -1 };
        for (int i = 0; i < 20; i++) alerts.Frame(i * 0.016f, 16, 0.1f, stages);
        stages[5] = 50; alerts.Frame(1, 16, 0.1f, stages);
        Check(alerts.Events.Count == 0, "deliberate target-FPS waiting is not turned into a fluctuation warning");

        var sample = new PerfSample { LongFrames = 3, Probe = new TogetherSnapshot { Enabled = true, Logs = new ProbeLogCounts { Errors = 5 },
            Tick = -1, TickDelta = -1, Queue = -1, ChangedEntities = -1, QueuedChunks = -1, LesPlayers = -1, PendingBaseline = -1,
            AckAgeSeconds = -1, MainCpuPct = -1, LowOnePercent = -1, JobWaitMs = -1, JobWaitPeakMs = -1, GcMs = -1, GcPeakMs = -1 } };
        string report = ProbeReport.Format(sample);
        Check(report.Contains("仅表示区间相关") && report.Contains("不可用"), "feedback reports correlation and unknown values without claiming causation");
        Check(ProbeReport.Message(new ProbeMessage { Text = "<size=999>error</size>", Source = "<size=99>LES" }).IndexOf('<') < 0, "raw stacks and logger names cannot inject IMGUI rich-text markup");
        DDEN.Shared.DdenLocalization.Language = DDEN.Shared.DdenLanguage.English;
        Check(ProbeReport.Format(sample).Contains("correlation only"), "multiplayer feedback is localized without changing evidence");
        DDEN.Shared.DdenLocalization.Language = DDEN.Shared.DdenLanguage.Chinese;

        string root = Path.GetFullPath(".");
        using var assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly(Path.Combine(root, "src/DDEN.NetPerfTracker/bin/Release/DDEN.NetPerfTracker.dll"));
        Check(assembly.Name.Version == new Version(2, 1, 0, 0), "probe feature has an independent 2.1.0 component version");
        Check(!assembly.MainModule.AssemblyReferences.Any(reference => reference.Name == "CasualtiesMP" || reference.Name == "LiteEntitySystem" || reference.Name == "LiteNetLib"), "optional probe never hard-references Together or its network libraries");
        foreach (var type in AllTypes(assembly.MainModule.Types))
        foreach (var method in type.Methods)
        {
            Check(!method.CustomAttributes.Any(attribute => attribute.AttributeType.Namespace == "HarmonyLib"), "performance module remains free of Harmony patches");
            if (!method.HasBody) continue;
            Check(!method.Body.Instructions.Any(instruction => instruction.Operand is Mono.Cecil.MethodReference call
                && call.DeclaringType.FullName == "DDEN.Shared.Mp" && (call.Name.Contains("Send") || call.Name == "Broadcast" || call.Name == "RequestServer")), "probe never sends network traffic");
        }
        string game = Environment.GetEnvironmentVariable("CUMOD_GAME_DIR") ?? @"L:\SteamLibrary\steamapps\common\Casualties Unknown";
        string deps = Path.Combine(game, "BepInEx/plugins/Together");
        using var les = Mono.Cecil.AssemblyDefinition.ReadAssembly(Path.Combine(deps, "LiteEntitySystem.dll"));
        using var together = Mono.Cecil.AssemblyDefinition.ReadAssembly(Path.Combine(deps, "CasualtiesMP.dll"));
        var net = together.MainModule.Types.Single(type => type.FullName == "Together.Net");
        Check(net.Properties.Any(property => property.Name == "ServerEntityManager" && property.GetMethod.IsStatic), "installed Together exposes the observed server manager");
        Check(together.MainModule.Types.Single(type => type.FullName == "Together.WorldChunkSync").Fields.Any(field => field.Name == "server_chunksync_queue" && field.IsStatic), "installed Together has the observed chunk queue");
        var manager = les.MainModule.Types.Single(type => type.FullName == "LiteEntitySystem.ServerEntityManager");
        Check(new[] { "_pendingClientRequests", "_changedEntities", "_netPlayers" }.All(name => manager.Fields.Any(field => field.Name == name)), "installed LES has all three bounded scalar observations");
        Check(les.MainModule.Types.Single(type => type.FullName == "LiteEntitySystem.EntityManager").Properties.Any(property => property.Name == "Tick" && property.PropertyType.MetadataType == Mono.Cecil.MetadataType.UInt16), "installed LES tick is a wrapping ushort");
        var sparse = les.MainModule.Types.Single(type => type.FullName == "LiteEntitySystem.Collections.SparseMap`1");
        Check(sparse.Methods.Single(method => method.Name == "GetByIndex").ReturnType.IsByReference, "installed SparseMap requires avoiding ordinary invocation of its ref return");
        Check(sparse.Fields.Any(field => field.Name == "_dense" && field.FieldType.IsArray)
            && sparse.NestedTypes.Single(type => type.Name == "SparseEntry").Fields.Any(field => field.Name == "Value"), "read-only peer storage bindings match installed SparseMap metadata");
        var player = les.MainModule.Types.Single(type => type.FullName == "LiteEntitySystem.NetPlayer");
        Check(player.Fields.Any(field => field.Name == "State") && player.Fields.Any(field => field.Name == "ServerTickChangedTime" && field.FieldType.FullName == "System.DateTime"), "installed peer state and acknowledgement timestamp bindings exist");
    }
    private static System.Collections.Generic.IEnumerable<Mono.Cecil.TypeDefinition> AllTypes(System.Collections.Generic.IEnumerable<Mono.Cecil.TypeDefinition> types)
    {
        foreach (var type in types)
        { yield return type; foreach (var nested in AllTypes(type.NestedTypes)) yield return nested; }
    }
}
