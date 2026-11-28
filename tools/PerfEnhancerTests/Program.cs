using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using DDEN.NetPerfTracker.Enhanced;
using DDEN.Diagnostics;
using Mono.Cecil;
using Newtonsoft.Json;
using System.Text;

internal static class Program
{
    private static int _checks;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); _checks++; }
    private static void Main()
    {
        Check(TimingTargets.All.Length == 53 && TimingTargets.All[44].Name == "Together.SyncInfoGameObjectTracker.FlushFrameSamples"
            && TimingTargets.All[49].Name == "Together.SyncInfoGameObjectTracker.UpdateDistanceChecks"
            && TimingTargets.All[50].Name == "Together.NetObjectRegistry.Update"
            && TimingTargets.All[51].Name == "Together.NetObjectRegistry._ObjectSyncUpdateLoopUniversal"
            && TimingTargets.All[52].Name == "Together.LayerBuildingStream.HostRegister", "registry diagnostics append three slots without moving prior frame evidence indexes");
        Check(TimingTargets.All.Where(spec => spec.Parameters.Length > 0).Single().Name == TimingTargets.All[51].Name
            && TimingTargets.All[51].Parameters.SequenceEqual(new[] { typeof(bool) })
            && TimingTargets.All.Skip(50).All(spec => spec.Inclusive), "only the explicitly allowlisted bool signature is added and all new totals include children");
        var ledger = new TimingLedger(1000);
        ledger.SetAvailable(0, true, ""); ledger.SetAvailable(11, true, "");
        ledger.Start(10);
        var parent = ledger.Begin(11, 100); var child = ledger.Begin(0, 110);
        ledger.End(child, 130, false); ledger.End(parent, 150, false);
        var beforeCommit = ledger.Drain(10.01);
        Check(beforeCommit.Frames == 0 && beforeCommit.Rows[0].PerFrameMs == -1, "unfinished frames are never divided by an earlier frame count");
        ledger.CompleteFrame(10.02);
        var snapshot = ledger.Drain(10.03);
        Check(snapshot.Frames == 1 && snapshot.Rows[0].Calls == 1 && snapshot.Rows[0].TotalMs == 20, "unfinished calls remain intact across a snapshot boundary");
        Check(snapshot.Rows[11].TotalMs == 50 && snapshot.Rows[11].Inclusive, "parent includes children and is explicitly marked overlapping");
        Check(snapshot.Rows[1].PerFrameMs == -1 && !snapshot.Rows[1].Available, "missing methods stay unknown rather than zero");
        Check(ledger.Drain(10.04).Rows[0].PerFrameMs == -1, "empty windows remain unavailable");
        ledger.CompleteFrame(10.05);
        snapshot = ledger.Drain(10.06);
        Check(snapshot.Rows[0].Calls == 0 && snapshot.Rows[0].PerFrameMs == 0 && snapshot.Rows[0].MeanCallMs == -1, "hooked but uncalled differs from unavailable or mean-per-call zero");
        ledger.End(ledger.Begin(0, 200), 210, true);
        ledger.End(ledger.Begin(0, 220), 250, false);
        ledger.CompleteFrame(10.07);
        ledger.CompleteFrame(10.08);
        snapshot = ledger.Drain(10.09);
        Check(snapshot.Rows[0].Calls == 2 && snapshot.Rows[0].Errors == 1, "exception-ending calls are counted without treating them as duplicate calls");
        Check(snapshot.Rows[0].PerFrameMs == 20 && snapshot.Rows[0].MeanCallMs == 20
            && snapshot.Rows[0].PeakCallMs == 30 && snapshot.Rows[0].PeakFrameMs == 40, "fixed-update bursts expose peak accumulated frame cost separately from a single call");
        var stale = ledger.Begin(0, 300); ledger.Stop(11); ledger.Start(12); ledger.End(stale, 500, false);
        ledger.CompleteFrame(12.1); snapshot = ledger.Drain(12.2);
        Check(snapshot.Rows[0].Calls == 0, "in-flight calls from a stopped session never contaminate a resumed window");
        stale = ledger.Begin(0, 600); ledger.Clear(13); ledger.End(stale, 700, false);
        ledger.CompleteFrame(13.1); Check(ledger.Drain(13.2).Rows[0].Calls == 0, "clear invalidates in-flight tokens and releases counters");
        ledger.End(ledger.Begin(0, 900), 800, false); ledger.CompleteFrame(13.3);
        Check(ledger.Drain(13.4).Rows[0].Calls == 0, "invalid clocks cannot produce negative timings");
        ledger.Stop(14); ledger.End(ledger.Begin(0, 1000), 1100, false); ledger.CompleteFrame(15);
        Check(ledger.Drain(16).Frames == 0, "disabled ledger does not gather frames or calls");
        var roundtrip = JsonConvert.DeserializeObject<DeepWindow>(JsonConvert.SerializeObject(snapshot));
        Check(roundtrip.Schema == 1 && roundtrip.Rows.Length == TimingTargets.All.Length && roundtrip.Rows[0].Name == TimingTargets.All[0].Name, "data-only bridge round-trips without a hard module reference");
        FrameTracing();

        string root = Path.GetFullPath(".");
        using var enhanced = AssemblyDefinition.ReadAssembly(Path.Combine(root, "src/DDEN.NetPerfTracker.Enhanced/bin/Release/DDEN.NetPerfTracker.Enhanced.dll"));
        var hooks = enhanced.MainModule.Types.Single(type => type.Name == "DeepHooks");
        var plugin = enhanced.MainModule.Types.Single(type => type.Name == "EnhancedPlugin");
        Check((string)plugin.CustomAttributes.Single(attribute => attribute.AttributeType.Name == "BepInPlugin").ConstructorArguments[2].Value == "0.2.1"
            && plugin.Methods.Single(method => method.Name == "get_Version").Body.Instructions.Any(instruction => instruction.Operand is string value && value == "0.2.1-alpha"), "enhancement keeps a numeric loader version and explicit alpha release metadata");
        var translations = JsonConvert.DeserializeObject<Dictionary<string,string>>(Encoding.UTF8.GetString(enhanced.MainModule.Resources.OfType<EmbeddedResource>()
            .Single(resource => resource.Name.EndsWith("localization.en.json", StringComparison.Ordinal)).GetResourceData()));
        foreach (var type in Types(enhanced.MainModule.Types))
        foreach (var method in type.Methods.Where(method => method.HasBody))
        for (int i = 1; i < method.Body.Instructions.Count; i++)
            if (method.Body.Instructions[i].Operand is MethodReference call && call.DeclaringType.Name == "ModuleText" && call.Name == "L"
                && method.Body.Instructions[i-1].Operand is string key)
                Check(translations.ContainsKey(key), "enhancement English catalog covers: " + key);
        var before = hooks.Methods.Single(method => method.Name == "Before");
        var after = hooks.Methods.Single(method => method.Name == "After");
        Check(before.ReturnType.MetadataType == MetadataType.Void && before.Parameters.All(parameter => parameter.Name == "__state" || !parameter.ParameterType.IsByReference), "prefix cannot skip the original or mutate its arguments");
        Check(after.ReturnType.MetadataType == MetadataType.Void && after.Parameters.All(parameter => !parameter.ParameterType.IsByReference)
            && after.Parameters.Any(parameter => parameter.Name == "__exception"), "void finalizer cannot replace or suppress the original exception");
        Check(!before.Parameters.Concat(after.Parameters).Any(parameter => parameter.Name == "__result" || parameter.Name == "__runOriginal"), "diagnostic callbacks have no result or original-execution control");
        Check(!enhanced.MainModule.AssemblyReferences.Any(reference => reference.Name is "CasualtiesMP" or "LiteEntitySystem" or "LiteNetLib" or "DDEN.NetPerfTracker"), "enhancement remains optional and never hard-references Together or the tracker");
        foreach (var type in Types(enhanced.MainModule.Types))
        foreach (var method in type.Methods.Where(method => method.HasBody))
            Check(!method.Body.Instructions.Any(instruction => instruction.Operand is MethodReference called
                && called.DeclaringType.FullName == "DDEN.Shared.Mp" && (called.Name.Contains("Send") || called.Name == "Broadcast" || called.Name == "RequestServer")), "enhancer does not send network traffic");
        using var baseTracker = AssemblyDefinition.ReadAssembly(Path.Combine(root,"src/DDEN.NetPerfTracker/bin/Release/DDEN.NetPerfTracker.dll"));
        Check(!baseTracker.MainModule.AssemblyReferences.Any(reference => reference.Name == "0Harmony" || reference.Name == "DDEN.NetPerfTracker.Enhanced"), "base tracker remains Harmony-free and enhancement-free at assembly load time");
        var capacity = baseTracker.MainModule.Types.Single(type => type.Name == "NetPerfTrackerPlugin").Methods.Single(method => method.Name == "get_DeepCaptureCapacity");
        Check(capacity.Body.Instructions.Any(instruction => instruction.Operand is sbyte value && value >= TimingTargets.All.Length
            || instruction.Operand is int wide && wide >= TimingTargets.All.Length), "tracker advertises enough capacity for every appended timing slot");
        var stop = hooks.Methods.Single(method => method.Name == "Stop");
        Check(stop.Body.Instructions.Any(instruction => instruction.Operand is MethodReference method && method.Name == "Unpatch")
            && !stop.Body.Instructions.Any(instruction => instruction.Operand is MethodReference method && method.Name == "UnpatchAll"), "cleanup uses owner-specific unpatch, never global removal");
        string game = Environment.GetEnvironmentVariable("CUMOD_GAME_DIR") ?? @"L:\SteamLibrary\steamapps\common\Casualties Unknown";
        using var together = AssemblyDefinition.ReadAssembly(Path.Combine(game, "BepInEx/plugins/Together/CasualtiesMP.dll"));
        foreach (var spec in TimingTargets.All)
        {
            string file = spec.Assembly == "Assembly-CSharp" ? Path.Combine(game,"CasualtiesUnknown_Data/Managed/Assembly-CSharp.dll")
                : spec.Assembly == "CpuOptimizationAdv" ? Path.Combine(game,"BepInEx/plugins/CPU优化-CpuOptimizationAdv.dll")
                : Path.Combine(game,"BepInEx/plugins/Together/" + spec.Assembly + ".dll");
            if (!File.Exists(file) && spec.Assembly == "CpuOptimizationAdv") { Console.WriteLine("Optional CPU optimizer absent; its timing target remains unavailable."); continue; }
            using var targetAssembly = AssemblyDefinition.ReadAssembly(file);
            var method = targetAssembly.MainModule.Types.Single(type => type.FullName == spec.Type).Methods
                .Single(method => Matches(method, spec));
            Check(method.ReturnType.MetadataType == MetadataType.Void && !method.HasGenericParameters, "installed Together method signature matches: " + spec.Name);
        }
        VerifyPublishedRegistryTargets(root);
        Check(TimingTargets.All.Count(spec => spec.Group == "chunks") == 3 && TimingTargets.All.Count(spec => spec.Group == "fixed") == 2
            && TimingTargets.All.Count(spec => spec.Group == "opt" && !spec.Inclusive) == 6, "all requested group children have a diagnostic slot");
        Console.WriteLine("PASS " + _checks + " enhancement checks (ledger, bridge, safety IL, installed targets)");
    }
    private static IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> types)
    { foreach (var type in types) { yield return type; foreach (var child in Types(type.NestedTypes)) yield return child; } }
    private static bool Matches(MethodDefinition method, TargetSpec spec) => method.Name == spec.Method && method.IsStatic != spec.Instance
        && method.Parameters.Select(parameter => parameter.ParameterType.FullName).SequenceEqual(spec.Parameters.Select(parameter => parameter.FullName));
    private static void VerifyPublishedRegistryTargets(string root)
    {
        string directory = Environment.GetEnvironmentVariable("CUMOD_TOGETHER_COMPARE_DIR") ?? Path.Combine(root, "artifacts/together-version-compare");
        foreach (string version in new[] { "5.5.3", "5.5.4", "5.5.5" })
        {
            string path = Path.Combine(directory, version, "CasualtiesMP.dll");
            if (!File.Exists(path)) { Console.WriteLine("Published " + version + " DLL unavailable; version-specific metadata check skipped."); continue; }
            using var assembly = AssemblyDefinition.ReadAssembly(path);
            Check(assembly.Name.Name == "CasualtiesMP" && assembly.Name.Version == new Version(version + ".0"),
                "version metadata check uses a published Together assembly rather than the version-zero mock: " + version);
            foreach (var spec in TimingTargets.All.Skip(50))
            {
                var method = assembly.MainModule.Types.SingleOrDefault(type => type.FullName == spec.Type)?.Methods.SingleOrDefault(candidate => Matches(candidate, spec));
                bool expected = version != "5.5.3" || spec.Method != "HostRegister";
                Check(expected ? method != null && method.ReturnType.MetadataType == MetadataType.Void && method.HasBody && !method.HasGenericParameters : method == null,
                    "published " + version + " registry target availability matches: " + spec.Name);
            }
            Console.WriteLine("Published metadata verified: " + version + " identity=" + assembly.Name.Version + "/" + assembly.MainModule.Mvid);
        }
    }
    private static void FrameTracing()
    {
        var ledger = new TimingLedger(1000); ledger.SetAvailable(0, true, ""); ledger.Start(0);
        for (int frame = 0; frame < 300; frame++)
        {
            ledger.BeginRenderedFrame();
            ledger.End(ledger.Begin(0, frame * 100), frame * 100 + 3, false);
            ledger.CompleteRenderedFrame(new FrameMeta { Frame = frame, T = frame * .01, LoopMs = frame == 100 ? 50 : 8,
                Warmup = frame < 10, FixedSteps = frame % 3, GC0 = frame == 100 ? 1 : 0, GC1 = 0, GC2 = 0, Role = 1,
                ScriptUpdateMs = 3, ScriptFixedMs = 0, ScriptLateMs = 0, PhysicsMs = -1 });
            if (frame % 20 == 0) ledger.Drain(frame * .01);
        }
        var trace = ledger.CaptureFrames("active");
        Check(trace.Recent.Length == 256 && trace.Recent[0].Frame == 44 && trace.Recent[255].Frame == 299, "fixed-capacity recent ring retains ordered completed frames across drains");
        Check(trace.SpikeContext.Select(frame => frame.Frame).SequenceEqual(Enumerable.Range(96, 9)), "long frame retains four frames before and after, even after recent frames roll over");
        var spike = trace.SpikeContext.Single(frame => frame.Frame == 100);
        Check(spike.Calls[0] == 1 && spike.MethodMs[0] == 3 && spike.GC0 == 1 && spike.FixedSteps == 1 && spike.PhysicsMs == -1, "method counters, GC and fixed steps refer to the same numbered frame; missing stage stays unknown");
        Check(trace.MeasuredFrames == 290 && trace.Over16Ms == 1 && trace.Over33Ms == 1 && Math.Abs(trace.P99UpperMs - 8) < .001, "histogram covers every non-warmup frame, not the selected spike subset");
        ledger.Stop(4); ledger.SetAvailable(0, false, "disabled"); trace = ledger.CaptureFrames("disabled");
        Check(trace.Recent.Length == 256 && trace.SpikeContext[4].MethodMs[0] == 3, "pause retains recorded availability and frame history for manual export");
        var copy = JsonConvert.DeserializeObject<DeepFrameTrace>(JsonConvert.SerializeObject(trace));
        Check(copy.Schema == 1 && copy.Recent.Length == 256 && copy.Targets.Length <= 64, "optional trace contract round-trips within bounded schema");
        ledger.Start(5); Check(ledger.CaptureFrames("active").Recent.Length == 0, "new capture clears previous scene/session history");
        for (int frame = 0; frame < 400; frame++)
        { ledger.BeginRenderedFrame(); ledger.CompleteRenderedFrame(new FrameMeta { Frame = frame, T = 5 + frame, LoopMs = 80 }); }
        trace = ledger.CaptureFrames("active");
        Check(trace.SpikeContext.Length == 128 && trace.SpikeContext[0].Frame == 272 && trace.MeasuredFrames == 400, "continuous overload bounds retained context without losing histogram counts");
        ledger.Clear(410); Check(ledger.CaptureFrames("active").MeasuredFrames == 0, "clear resets retained context and percentile histogram");
        ledger.BeginRenderedFrame(); ledger.Clear(411); ledger.CompleteRenderedFrame(new FrameMeta { Frame = 500, T = 412, LoopMs = 100 });
        Check(ledger.CaptureFrames("active").Recent.Length == 0, "clear in the middle of a frame excludes its incomplete timings");
        ledger.BeginRenderedFrame(); ledger.CompleteRenderedFrame(new FrameMeta { Frame = 501, T = 413, LoopMs = 150, ManualExport = true });
        Check(ledger.CaptureFrames("active").MeasuredFrames == 0 && ledger.CaptureFrames("active").Recent.Single().ManualExport,
            "manual export cost is flagged and excluded from the performance histogram");
        for (int i = 0; i < 100; i++) { ledger.BeginRenderedFrame(); ledger.CompleteRenderedFrame(new FrameMeta { Frame = 502 + i, T = i, LoopMs = 8 }); }
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 5000; i++) { ledger.BeginRenderedFrame(); ledger.CompleteRenderedFrame(new FrameMeta { Frame = 602 + i, T = i, LoopMs = 8 }); }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
        Check(allocated == 0, "steady-state frame ring and histogram allocate zero managed bytes in the test runtime");
    }
}
