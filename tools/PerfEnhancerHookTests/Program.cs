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
            Check(first.Patched == TimingTargets.All.Length && first.Active, "all independent static/instance mock methods were actually detoured");
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
            hooks.Stop(2);
            var patchInfo = Harmony.GetPatchInfo(original);
            Check(patchInfo.Prefixes.Any(patch => patch.owner == "fixture.other")
                && !patchInfo.Prefixes.Concat(patchInfo.Finalizers).Any(patch => patch.owner == "com.dden.perfdeep"), "disable removes owned hooks while retaining other mods");
            Together.Trace.Calls.Clear(); Together.ChunkSleep.Tick();
            Check(Together.Trace.Calls.Count == 1 && !hooks.Active, "original behavior continues after unpatch");
            hooks.Start(3); Together.ChunkSleep.Tick(); hooks.Ledger.CompleteFrame(4);
            Check(hooks.Capture(4, true).Rows[0].Calls == 1, "re-enabling starts a clean session without duplicate hooks");
        }
        finally { hooks.Stop(5); other.Unpatch(original, HarmonyPatchType.All, "fixture.other"); }
        Console.WriteLine("PASS " + _checks + " runtime Harmony checks on independent mock methods; no Unity/game execution");
    }
}
