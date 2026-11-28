using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DDEN.NetPerfTracker.Enhanced;
using HarmonyLib;

namespace DDEN.Shared
{
    // The only shared operation needed by DeepHooks. Keep this harness free of all Unity/game DLLs.
    public static class Reflect
    { public static Assembly FindAssembly(string name) => AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name); }
}
internal static class Program
{
    private static int _checks, _otherCalls;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); _checks++; }
    private static bool OtherPrefix() { _otherCalls++; return !Together.Trace.Skip; }
    private static void Main()
    {
        try { Run(); }
        catch (Exception error)
        {
            for (var current = error; current != null; current = current.InnerException)
                Console.WriteLine(current.GetType().FullName + ": " + current.Message);
            Environment.ExitCode = 1;
        }
    }
    private static void Run()
    {
        // Force the independent mock assembly to load; no game process, Unity runtime, world or save is used.
        _ = typeof(Together.ChunkSleep).Assembly;
        _ = typeof(LiteEntitySystem.EntityManager).Assembly; _ = typeof(Item).Assembly; _ = typeof(CpuOptimizationAdv.CullWorld).Assembly;
        var original = typeof(Together.ChunkSleep).GetMethod("Tick");
        var other = new Harmony("fixture.other");
        other.Patch(original, prefix: new HarmonyMethod(typeof(Program).GetMethod(nameof(OtherPrefix), BindingFlags.Static | BindingFlags.NonPublic)));
        var hooks = new DeepHooks();
        try
        {
            KrokV5Optimization.OptimizerMain.Tick();
            string expected = string.Join(",", Together.Trace.Calls); Together.Trace.Calls.Clear();
            hooks.Start(0);
            var first = hooks.Capture(0, true);
            bool hasBuildings = typeof(Together.NetObjectRegistry).Assembly.GetType("Together.LayerBuildingStream") != null;
            Check(first.Patched == TimingTargets.All.Length - (hasBuildings ? 0 : 1) && first.Active, "all available independent static/instance mock methods were actually detoured");
            if (!hasBuildings)
                Check(!first.Rows[52].Available && first.Rows[52].PerFrameMs == -1 && first.Status == "partial", "older assembly missing building registration remains unavailable without blocking other hooks");
            Check(first.Counts.Length == 8 && first.Counts.All(count => count.Value == 0), "bounded count readers inspect all eight mock collections without traversing entries");
            var evidence = Newtonsoft.Json.Linq.JObject.Parse(DeepPatchEvidence.Capture());
            var recorded = evidence["Targets"].Single(row => (string)row["Name"] == "Together.ChunkSleep.Tick");
            Check((int)evidence["Schema"] == 1 && recorded["Prefixes"].Any(row => (string)row["Owner"] == "fixture.other")
                && recorded["Prefixes"].Any(row => (string)row["Owner"] == "com.dden.perfdeep"), "export evidence attributes diagnostic and other-owner prefixes separately");
            Check(!string.IsNullOrEmpty((string)recorded["Mvid"]) && evidence["Scope"].ToString().Contains("not proof"), "patch metadata carries build identity and an explicit export-time scope");
            KrokV5Optimization.OptimizerMain.Tick();
            Check(string.Join(",", Together.Trace.Calls) == expected, "nested original calls retain order and execute exactly once");
            Together.Trace.Calls.Clear();
            Together.ChunkSleep.Tick();
            Check(Together.Trace.Calls.Count == 1 && _otherCalls == 1, "original method and another mod prefix execute once");
            Together.Trace.Throw = true;
            Exception observed = null;
            try { Together.ChunkSleep.Tick(); } catch (Exception error) { observed = error; }
            Together.Trace.Throw = false;
            Check(ReferenceEquals(observed, Together.Trace.Failure), "original exception identity propagates through the void finalizer");
            hooks.Ledger.CompleteFrame(1);
            var captured = hooks.Capture(1, true);
            Check(captured.Rows[0].Calls == 2 && captured.Rows[0].Errors == 1, "real patched normal/exception calls reach the timing ledger");
            Check(captured.Rows[11].Calls == 1 && captured.Rows[11].Inclusive && captured.Rows[5].Calls == 1, "nested totals and child timings remain distinct");
            Task.Run(() => Together.ChunkSleep.Tick()).GetAwaiter().GetResult();
            Check(hooks.Capture(1, true).OffThread == 1, "non-main-thread invocations are safely excluded from main-thread timing");
            Together.Trace.Calls.Clear(); Together.Trace.Skip = true;
            Together.ChunkSleep.Tick(); Together.Trace.Skip = false;
            Check(Together.Trace.Calls.Count == 0, "another mod's original-method skip decision is preserved");
            new Together.TransportSteamworks().Update();
            new LiteEntitySystem.ClientEntityManager().Update();
            new Together.SyncInfo().Update(); new Together.SyncInfo().Update();
            new Item().Update(); new Body().Update(); CpuOptimizationAdv.CullWorld.Tick();
            hooks.Ledger.CompleteFrame(1.5); var expanded = hooks.Capture(1.5, true);
            Check(expanded.Rows.Single(row => row.Name == "Together.SyncInfo.Update").Calls == 2, "instance calls aggregate across objects");
            Check(expanded.Rows.Single(row => row.Name == "LiteEntitySystem.EntityManager.Update").Calls == 1
                && expanded.Rows.Single(row => row.Name == "LiteEntitySystem.ClientEntityManager.Update").Calls == 1, "base and override methods have independent nested slots");
            Check(expanded.Rows.Single(row => row.Name == "Item.HandleDecay").Calls == 1, "high-frequency object child retains original execution");
            RegistryHooks(hooks, hasBuildings);
            hooks.Stop(2);
            var patchInfo = Harmony.GetPatchInfo(original);
            Check(patchInfo.Prefixes.Any(patch => patch.owner == "fixture.other")
                && !patchInfo.Prefixes.Concat(patchInfo.Finalizers).Any(patch => patch.owner == "com.dden.perfdeep"), "disable removes owned hooks while retaining other mods");
            foreach (var spec in TimingTargets.All.Skip(50))
            {
                var method = typeof(Together.NetObjectRegistry).Assembly.GetType(spec.Type)?.GetMethod(spec.Method,
                    BindingFlags.Public | BindingFlags.NonPublic | (spec.Instance ? BindingFlags.Instance : BindingFlags.Static), null, spec.Parameters, null);
                var patches = method == null ? null : Harmony.GetPatchInfo(method);
                Check(patches == null || !patches.Prefixes.Concat(patches.Finalizers).Any(patch => patch.owner == "com.dden.perfdeep"), "registry diagnostic hooks are fully removed: " + spec.Name);
            }
            Together.Trace.Calls.Clear(); Together.ChunkSleep.Tick();
            Check(Together.Trace.Calls.Count == 1 && !hooks.Active, "original behavior continues after unpatch");
            hooks.Start(3); Together.ChunkSleep.Tick(); hooks.Ledger.CompleteFrame(4);
            Check(hooks.Capture(4, true).Rows[0].Calls == 1, "re-enabling starts a clean session without duplicate hooks");
        }
        finally { hooks.Stop(5); other.Unpatch(original, HarmonyPatchType.All, "fixture.other"); }
        FrameLoopGraph();
        Console.WriteLine("PASS " + _checks + " runtime Harmony checks on independent mock methods; no Unity/game execution");
    }
    private static void RegistryHooks(DeepHooks hooks, bool hasBuildings)
    {
        hooks.Ledger.BeginRenderedFrame();
        Together.Trace.Calls.Clear();
        new Together.NetObjectRegistry().Update();
        Check(string.Join(",", Together.Trace.Calls) == "registry:false,registry:true" + (hasBuildings ? ",building" : ""), "bool arguments, nested order and original call counts are preserved");
        Together.Trace.Throw = true; Exception observed = null;
        try { Together.NetObjectRegistry._ObjectSyncUpdateLoopUniversal(false); } catch (Exception error) { observed = error; }
        finally { Together.Trace.Throw = false; }
        Check(ReferenceEquals(observed, Together.Trace.Failure), "bool-argument original exception identity is preserved");
        Together.NetObjectRegistry._ObjectSyncUpdateLoopUniversal(1);
        hooks.Ledger.CompleteRenderedFrame(new FrameMeta { Frame = 123, T = 1.75, LoopMs = 40 });
        var snapshot = hooks.Capture(1.75, true);
        Check(snapshot.Rows[50].Calls == 1 && snapshot.Rows[51].Calls == 3 && snapshot.Rows[51].Errors == 1
            && snapshot.Rows.Skip(50).All(row => row.Inclusive), "only the exact bool overload is timed and all appended totals are inclusive");
        Check(hasBuildings ? snapshot.Rows[52].Calls == 1 : !snapshot.Rows[52].Available && snapshot.Rows[52].TotalMs == -1,
            "building registration records one child call or remains unavailable on an older assembly");
        var frame = hooks.Ledger.CaptureFrames("active").Recent.Single();
        Check(frame.Frame == 123 && frame.Calls.Length == 53 && frame.Calls[50] == 1 && frame.Calls[51] == 3
            && (hasBuildings ? frame.Calls[52] == 1 && frame.MethodMs[52] >= 0 : frame.MethodMs[52] == -1),
            "all appended slots reach the same complete-frame export with missing methods kept unknown");
        var evidence = Newtonsoft.Json.Linq.JObject.Parse(DeepPatchEvidence.Capture());
        foreach (var spec in TimingTargets.All.Skip(50))
        {
            var row = evidence["Targets"].Single(target => (string)target["Name"] == spec.Name);
            if (spec.Method == "HostRegister" && !hasBuildings)
                Check(!(bool)row["Available"], "missing older-version target stays unavailable in patch evidence");
            else
                Check((bool)row["Available"] && row["Prefixes"].Any(patch => (string)patch["Owner"] == "com.dden.perfdeep")
                    && row["Finalizers"].Any(patch => (string)patch["Owner"] == "com.dden.perfdeep"), "patch evidence includes the exact instrumented registry target: " + spec.Name);
        }
        var overload = typeof(Together.NetObjectRegistry).GetMethod("_ObjectSyncUpdateLoopUniversal", new[] { typeof(int) });
        var info = Harmony.GetPatchInfo(overload);
        Check(info == null || !info.Prefixes.Concat(info.Finalizers).Any(patch => patch.owner == "com.dden.perfdeep"), "unlisted parameter signature receives no diagnostic patches");
    }
    private sealed class ThirdPartyNode { }
    private static void RunLoop(UnityEngine.LowLevel.PlayerLoopSystem node)
    { node.updateDelegate?.Invoke(); foreach (var child in node.subSystemList ?? Array.Empty<UnityEngine.LowLevel.PlayerLoopSystem>()) RunLoop(child); }
    private static void FrameLoopGraph()
    {
        var ledger = new TimingLedger(System.Diagnostics.Stopwatch.Frequency); ledger.SetAvailable(0, true, ""); ledger.Start(0);
        int originals = 0;
        UnityEngine.LowLevel.PlayerLoop.Current = new UnityEngine.LowLevel.PlayerLoopSystem {
            subSystemList = new[] {
                new UnityEngine.LowLevel.PlayerLoopSystem { type = typeof(UnityEngine.PlayerLoop.FixedUpdate), subSystemList = new[] {
                    new UnityEngine.LowLevel.PlayerLoopSystem { type = typeof(UnityEngine.PlayerLoop.FixedUpdate.ScriptRunBehaviourFixedUpdate), updateDelegate = () => originals++ },
                    new UnityEngine.LowLevel.PlayerLoopSystem { type = typeof(UnityEngine.PlayerLoop.FixedUpdate.Physics2DFixedUpdate), updateDelegate = () => originals++ } } },
                new UnityEngine.LowLevel.PlayerLoopSystem { type = typeof(UnityEngine.PlayerLoop.Update), subSystemList = new[] {
                    new UnityEngine.LowLevel.PlayerLoopSystem { type = typeof(UnityEngine.PlayerLoop.Update.ScriptRunBehaviourUpdate), updateDelegate = () => {
                        originals++; long start = System.Diagnostics.Stopwatch.GetTimestamp(); ledger.End(ledger.Begin(0, start), start + 100, false); } } } },
                new UnityEngine.LowLevel.PlayerLoopSystem { type = typeof(UnityEngine.PlayerLoop.PreLateUpdate), subSystemList = new[] {
                    new UnityEngine.LowLevel.PlayerLoopSystem { type = typeof(UnityEngine.PlayerLoop.PreLateUpdate.ScriptRunBehaviourLateUpdate), updateDelegate = () => originals++ } } } } };
        using (var loop = new RenderedFrameLoop(ledger))
        {
            loop.Install(); Check(loop.BoundaryPresent(), "owned frame bookends enclose the current loop");
            UnityEngine.Time.frameCount = 42; UnityEngine.Time.realtimeSinceStartupAsDouble = 3;
            RunLoop(UnityEngine.LowLevel.PlayerLoop.Current);
            var frame = ledger.CaptureFrames("active").Recent.Single();
            Check(originals == 4 && frame.Frame == 42 && frame.FixedSteps == 1 && frame.Calls[0] == 1, "all original nodes execute once and are committed into the same complete frame");
            Check(frame.ScriptFixedMs >= 0 && frame.PhysicsMs >= 0 && frame.ScriptLateMs >= 0 && !frame.Warmup, "stage timings and warmup state come from owned graph execution");
            var current = UnityEngine.LowLevel.PlayerLoop.Current;
            current.subSystemList = current.subSystemList.Concat(new[] { new UnityEngine.LowLevel.PlayerLoopSystem { type = typeof(ThirdPartyNode), updateDelegate = () => originals++ } }).ToArray();
            UnityEngine.LowLevel.PlayerLoop.Current = current;
            Check(!loop.BoundaryPresent(), "foreign work appended outside the end marker is detected instead of silently claiming complete coverage");
        }
        var restored = UnityEngine.LowLevel.PlayerLoop.Current;
        Check(restored.subSystemList.Length == 4 && restored.subSystemList.Last().type == typeof(ThirdPartyNode)
            && restored.subSystemList[0].subSystemList.Length == 2, "cleanup removes only owned markers while preserving later third-party loop changes");
        originals = 0; RunLoop(restored); Check(originals == 5, "all original and later third-party delegates still execute after cleanup");
    }
}
