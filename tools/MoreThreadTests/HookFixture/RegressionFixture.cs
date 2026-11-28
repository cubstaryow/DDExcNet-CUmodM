using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading;
using DDEN.MPreducer.Integration;
using HarmonyLib;

// Independent, tiny managed world. No game/Unity assemblies or real saves are loaded.
namespace DDEN.Shared { internal static class Reflect { internal static Type Type(string name) => typeof(Reflect).Assembly.GetType(name); } }
namespace UnityEngine { public struct Vector2 { public int x, y; public Vector2(int x, int y) { this.x = x; this.y = y; } } }
public sealed class WorldGeneration { public static WorldGeneration world = new WorldGeneration(); public int Size = 31; }
public sealed class Item
{
    public int Id, Decays;
    [MethodImpl(MethodImplOptions.NoInlining)] public void HandleDecay() { Decays++; Together.FindTheBugReports.NoteDecay(this); }
}
namespace Together
{
    public static class Net { public static bool IsRunning { get; set; } = true; public static bool IsServer { get; set; } = true; }
    public static class SharedMain { internal static bool[,] CHUNKS_VISIBLE_TO_PLAYERS = new bool[31, 31]; }
    public static class FindTheBugReports
    {
        private struct BagSample { public int Calls; }
        private static readonly Dictionary<int, BagSample> Bags = new Dictionary<int, BagSample>();
        private static readonly List<Item> BagItems = new List<Item>();
        public static int Calls, Notes;
        public static int BagCount => Bags.Count;
        public static int ItemCount => BagItems.Count;
        public static void Remember(Item item) => BagItems.Add(item);
        public static void ResetHistory() { Bags.Clear(); BagItems.Clear(); Notes = 0; }
        [MethodImpl(MethodImplOptions.NoInlining)] public static void Tick() { Calls++; ScanBags(); }
        [MethodImpl(MethodImplOptions.NoInlining)] private static void ScanBags() { BagItems.Clear(); if (Bags.Count > 400) Bags.Clear(); }
        [MethodImpl(MethodImplOptions.NoInlining)] public static void NoteDecay(Item item)
        { Notes++; Bags.TryGetValue(item.Id, out var sample); sample.Calls++; Bags[item.Id] = sample; }
    }
    public static class LayerBuildingStream { public static int Calls; [MethodImpl(MethodImplOptions.NoInlining)] public static void HostRegister() { Calls++; } }
}
internal static class BrokenReports
{
    private struct BagSample { }
    private static readonly Dictionary<int, BagSample> Bags = null;
    private static readonly List<Item> BagItems = new List<Item>();
    public static int Calls, Notes;
    [MethodImpl(MethodImplOptions.NoInlining)] public static void Tick() { Calls++; if (Bags != null) Bags.Clear(); }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void NoteDecay(Item item) { Notes++; }
}
internal static class WrongReports
{
    private struct BagSample { }
    private static readonly Dictionary<int, BagSample> Bags = new Dictionary<int, BagSample>();
    private static readonly List<object> BagItems = new List<object>();
    public static int Calls;
    [MethodImpl(MethodImplOptions.NoInlining)] public static void Tick() { Calls++; }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void NoteDecay(Item item) { }
}
internal static class MissingReports
{
    private struct BagSample { }
    private static readonly Dictionary<int, BagSample> Bags = new Dictionary<int, BagSample>();
    [MethodImpl(MethodImplOptions.NoInlining)] public static void Tick() { }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void NoteDecay(Item item) { }
}
namespace KrokV5Optimization
{
    public static class FixSimRange
    {
        public static readonly List<UnityEngine.Vector2> Players = new List<UnityEngine.Vector2>();
        public static readonly List<UnityEngine.Vector2> RetainedObjects = new List<UnityEngine.Vector2>();
        public static bool FailRebuild;
        public static int Rebuilds;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Mark(bool[,] grid, WorldGeneration world, UnityEngine.Vector2 center)
        {
            for (int y = center.y - 4; y <= center.y + 4; y++)
                for (int x = center.x - 4; x <= center.x + 4; x++)
                    if (x >= 0 && y >= 0 && x < grid.GetLength(0) && y < grid.GetLength(1)) grid[x, y] = true;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool Rebuild()
        {
            if (FailRebuild) throw new InvalidOperationException("fixture rebuild failure");
            Rebuilds++;
            var grid = Together.SharedMain.CHUNKS_VISIBLE_TO_PLAYERS;
            Array.Clear(grid, 0, grid.Length);
            foreach (var center in Players) Mark(grid, WorldGeneration.world, center);
            foreach (var center in RetainedObjects) Mark(grid, WorldGeneration.world, center);
            return false;
        }
    }
}
namespace DDEN.NetPerfTracker.Enhanced
{
    public struct CallStamp { }
    internal static class DeepHooks
    {
        internal static int BeforeCalls, AfterCalls;
        private static void Before(MethodBase __originalMethod, out CallStamp __state) { __state = default; BeforeCalls++; }
        private static void After(CallStamp __state, Exception __exception) { AfterCalls++; }
    }
}
internal static class WrongTimingState
{
    private static void Before(MethodBase __originalMethod, out int __state) { __state = 0; }
    private static void After(int __state, Exception __exception) { }
}
internal static class RefTimingState
{
    private static void Before(MethodBase __originalMethod, ref DDEN.NetPerfTracker.Enhanced.CallStamp __state) { }
    private static void After(DDEN.NetPerfTracker.Enhanced.CallStamp __state, Exception __exception) { }
}
internal static class RefTimingFinalizer
{
    private static void Before(MethodBase __originalMethod, out DDEN.NetPerfTracker.Enhanced.CallStamp __state) { __state = default; }
    private static void After(ref DDEN.NetPerfTracker.Enhanced.CallStamp __state, Exception __exception) { }
}
internal static class RefTimingException
{
    private static void Before(MethodBase __originalMethod, out DDEN.NetPerfTracker.Enhanced.CallStamp __state) { __state = default; }
    private static void After(DDEN.NetPerfTracker.Enhanced.CallStamp __state, ref Exception __exception) { }
}
internal static class RegressionFixture
{
    private static int _checks;
    private static double _now;
    private static readonly Type Range = typeof(KrokV5Optimization.FixSimRange);
    private static MethodInfo Mark => Range.GetMethod("Mark");
    private static MethodInfo Scan => typeof(Together.FindTheBugReports).GetMethod("Tick");
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); _checks++; }
    private static RegressionValidationHooks New(bool pin = false, Func<string, Type> resolve = null)
        => new RegressionValidationHooks(pin, resolve ?? DDEN.Shared.Reflect.Type, clock: () => _now);
    private static int Count() => Together.SharedMain.CHUNKS_VISIBLE_TO_PLAYERS.Cast<bool>().Count(value => value);
    private static void Rebuild() => KrokV5Optimization.FixSimRange.Rebuild();
    private static void Foreign() { }
    private static bool Block() => false;
    private static void Reset()
    {
        Together.Net.IsRunning = Together.Net.IsServer = true;
        Together.FindTheBugReports.Calls = Together.LayerBuildingStream.Calls = 0;
        Together.FindTheBugReports.ResetHistory();
        KrokV5Optimization.FixSimRange.Players.Clear(); KrokV5Optimization.FixSimRange.RetainedObjects.Clear();
        KrokV5Optimization.FixSimRange.Players.Add(new UnityEngine.Vector2(15, 15));
        KrokV5Optimization.FixSimRange.FailRebuild = false; _now = 0; Rebuild();
    }
    private static void Main(string[] args)
    {
        if (args.Length != 0)
            Console.WriteLine("PASS " + RegressionBinaryContract.Verify(args[0], fixtureTimingHooks: typeof(DDEN.NetPerfTracker.Enhanced.DeepHooks)) + " real DLL metadata/IL regression contract checks");
        Reset();
        using (var hooks = New())
        {
            hooks.Install(); Rebuild();
            Check(Count() == 81 && hooks.EffectiveRadius == 4, "disabled default preserves 9x9");
            hooks.Configure(true, 1, false);
            Check(Count() == 9 && hooks.EffectiveRadius == 1, "radius change synchronously rebuilds 3x3");
            using (var duplicate = New())
            {
                bool refused = false;
                try { duplicate.Install(); } catch (InvalidOperationException) { refused = true; }
                Check(refused, "second adapter cannot take ownership of installed hooks");
            }
            Rebuild(); Check(Count() == 9, "disposing refused second adapter preserves first adapter hooks");
            hooks.Configure(true, 2, false); Check(Count() == 25, "radius 2 keeps 5x5");
            KrokV5Optimization.FixSimRange.Players.Add(new UnityEngine.Vector2(0, 0));
            KrokV5Optimization.FixSimRange.RetainedObjects.Add(new UnityEngine.Vector2(30, 30));
            hooks.Configure(true, 1, false); Check(Count() == 17, "all players, retained objects and boundary clipping remain original");
            Together.FindTheBugReports.Tick(); Check(Together.FindTheBugReports.Calls == 1, "scan default preserved");
            hooks.Configure(true, 1, true); Together.FindTheBugReports.Tick(); Together.LayerBuildingStream.HostRegister();
            Check(Together.FindTheBugReports.Calls == 1 && hooks.SkippedScans == 1 && Together.LayerBuildingStream.Calls == 1,
                "only diagnostics skipped; business registration untouched");
            hooks.Tick(); Check(hooks.SimulatedChunks == 17 && hooks.PeakSampleMs >= 0, "sample reads the original grid");
            Array.Clear(Together.SharedMain.CHUNKS_VISIBLE_TO_PLAYERS, 0, 31 * 31); hooks.Tick();
            Check(hooks.SimulatedChunks == 17, "grid sampling limited to once per second");
            _now = 1.1; hooks.Tick(); Check(hooks.SimulatedChunks == 0, "next grid sample observed without object scan");
            Together.Net.IsServer = false; hooks.Tick(); Together.FindTheBugReports.Tick();
            Check(hooks.EffectiveRadius == 4 && Count() == 131 && Together.FindTheBugReports.Calls == 2, "client always original radius and diagnostics");
            Together.Net.IsServer = true; Together.Net.IsRunning = false; hooks.Tick(); Together.FindTheBugReports.Tick();
            Check(hooks.EffectiveRadius == 4 && Together.FindTheBugReports.Calls == 3, "single player never receives validation changes");
            Together.Net.IsRunning = true; hooks.Tick(); Check(Count() == 17, "host role resumes selected radius");
            WorldGeneration.world = new WorldGeneration();
            Together.SharedMain.CHUNKS_VISIBLE_TO_PLAYERS = new bool[31, 31];
            hooks.Tick(); Check(Count() == 17, "world replacement rebuilds current selection without waiting for sample interval");
            int offThreadCount = 0;
            var thread = new Thread(() => { var grid = new bool[31, 31]; KrokV5Optimization.FixSimRange.Mark(grid, WorldGeneration.world, new UnityEngine.Vector2(15, 15)); offThreadCount = grid.Cast<bool>().Count(x => x); Together.FindTheBugReports.Tick(); });
            thread.Start(); thread.Join(); Check(offThreadCount == 81 && Together.FindTheBugReports.Calls == 4, "off-thread invocations retain originals");
            hooks.Configure(false, 1, true); Check(Count() == 131 && hooks.EffectiveRadius == 4, "disable restores grid immediately without TTL wait");
            hooks.Configure(true, 1, false);
        }
        Check(Count() == 131 && Harmony.GetPatchInfo(Mark)?.Owners.Contains(RegressionValidationHooks.Owner) != true, "dispose restores radius and removes own hook");

        Reset();
        var foreign = new Harmony("fixture.foreign.regression");
        using (var hooks = New())
        {
            hooks.Install(); hooks.Configure(true, 1, true);
            foreign.Patch(Mark, prefix: new HarmonyMethod(typeof(RegressionFixture), nameof(Foreign)));
            _now = 2; hooks.Tick(); Together.FindTheBugReports.Tick();
            Check(hooks.EffectiveRadius == 4 && Count() == 81 && hooks.RadiusProblem != null && hooks.DiagnosticProblem == null
                && hooks.SkippedScans == 1, "late unknown radius patch fails radius only and promptly rebuilds");
            foreign.Unpatch(Mark, HarmonyPatchType.All, foreign.Id);
            _now = 4; hooks.Tick(); Check(hooks.EffectiveRadius == 1 && Count() == 9, "removed conflict permits audited radius again");
            foreign.Patch(Scan, prefix: new HarmonyMethod(typeof(RegressionFixture), nameof(Foreign)));
            _now = 6; hooks.Tick(); Together.FindTheBugReports.Tick();
            Check(hooks.DiagnosticProblem != null && hooks.EffectiveRadius == 1 && Together.FindTheBugReports.Calls == 1, "unknown diagnostic patch preserves original scan independently");
            foreign.UnpatchSelf();
            hooks.Configure(true, 3, false); Check(hooks.EffectiveRadius == 4 && Count() == 81, "invalid radius fails closed");
        }
        Reset();
        var timing = new Harmony("com.dden.perfdeep");
        timing.Patch(Scan, prefix: new HarmonyMethod(typeof(DDEN.NetPerfTracker.Enhanced.DeepHooks), "Before"),
            finalizer: new HarmonyMethod(typeof(DDEN.NetPerfTracker.Enhanced.DeepHooks), "After"));
        using (var hooks = New())
        {
            hooks.Install(); hooks.Configure(true, 2, true); Together.FindTheBugReports.Tick();
            Check(hooks.Problem == null && hooks.DiagnosticSuppressionActive && hooks.SkippedScans == 1 && DDEN.NetPerfTracker.Enhanced.DeepHooks.AfterCalls > 0,
                "exact known pure timing hooks preserved around skipped diagnostic body");
        }
        timing.UnpatchSelf();
        timing.Patch(Scan, prefix: new HarmonyMethod(typeof(RegressionFixture), nameof(Block)));
        using (var hooks = New())
        { hooks.Install(); hooks.Configure(true, 1, true); Check(hooks.DiagnosticProblem != null && !hooks.DiagnosticSuppressionActive && hooks.EffectiveRadius == 1, "timing owner alone cannot authorize business prefix"); }
        timing.UnpatchSelf();
        TimingCompatibility();

        Reset();
        using (var hooks = New(pin: true))
        { hooks.Install(); hooks.Configure(true, 1, true); Check(hooks.EffectiveRadius == 4 && hooks.RadiusProblem != null && hooks.DiagnosticProblem != null, "MVID mismatch refuses both adapters"); }
        using (var hooks = New(resolve: name => name == "KrokV5Optimization.FixSimRange" ? null : DDEN.Shared.Reflect.Type(name)))
        { hooks.Install(); hooks.Configure(true, 1, true); Together.FindTheBugReports.Tick(); Check(hooks.RadiusProblem != null && hooks.SkippedScans == 1, "missing radius binding does not block diagnostic experiment"); }
        using (var hooks = New())
        { hooks.Install(); KrokV5Optimization.FixSimRange.FailRebuild = true; hooks.Configure(true, 1, true); Check(hooks.EffectiveRadius == 4 && hooks.RadiusProblem != null, "original rebuild exception disables radius safely"); KrokV5Optimization.FixSimRange.FailRebuild = false; }
        bool malformedRejected = false;
        try { RegressionValidationHooks.RadiusTranspiler(new[] { new CodeInstruction(OpCodes.Ldc_I4_4), new CodeInstruction(OpCodes.Ret) }, new DynamicMethod("bad", typeof(void), Type.EmptyTypes).GetILGenerator()).ToArray(); }
        catch (NotSupportedException) { malformedRejected = true; }
        Check(malformedRejected, "unexpected Mark IL shape is rejected");
        DiagnosticHistory();
        Console.WriteLine("PASS " + _checks + " real Harmony regression validation fixture checks; no game process or saves");
    }
    private static void TimingCompatibility()
    {
        var actualTiming = typeof(DDEN.NetPerfTracker.Enhanced.DeepHooks);
        foreach (var candidate in new[] { actualTiming, typeof(WrongTimingState), typeof(RefTimingState), typeof(RefTimingFinalizer), typeof(RefTimingException) })
        {
            Reset();
            var patcher = new Harmony(candidate == actualTiming ? "fixture.unknown.timing" : "com.dden.perfdeep");
            try
            {
                patcher.Patch(Scan, prefix: new HarmonyMethod(candidate, "Before"), finalizer: new HarmonyMethod(candidate, "After"));
                using (var hooks = New(resolve: name => name == "DDEN.NetPerfTracker.Enhanced.DeepHooks" ? candidate : DDEN.Shared.Reflect.Type(name)))
                {
                    hooks.Install(); hooks.Configure(true, 1, true); Together.FindTheBugReports.Tick();
                    Check(hooks.DiagnosticProblem != null && !hooks.DiagnosticSuppressionActive && hooks.SkippedScans == 0
                        && Together.FindTheBugReports.Calls == 1 && hooks.EffectiveRadius == 1,
                        "unknown owner or altered timing signature retains the original scan: " + candidate.Name);
                }
            }
            finally { patcher.UnpatchSelf(); }
        }
        Reset();
        var timing = new Harmony("com.dden.perfdeep");
        using (var hooks = New())
        {
            hooks.Install(); hooks.Configure(true, 2, true);
            Check(hooks.DiagnosticSuppressionActive, "suppression reports active only after both hooks and history preparation are ready");
            try
            {
                timing.Patch(Scan, prefix: new HarmonyMethod(actualTiming, "Before"), finalizer: new HarmonyMethod(actualTiming, "After"));
                _now = 2; hooks.Tick(); Together.FindTheBugReports.Tick();
                Check(hooks.DiagnosticProblem == null && hooks.DiagnosticSuppressionActive && hooks.SkippedScans == 1,
                    "enabling the real-shape timing pair during suppression does not disable it");
                timing.UnpatchSelf(); _now = 4; hooks.Tick(); Together.FindTheBugReports.Tick();
                Check(hooks.DiagnosticSuppressionActive && hooks.SkippedScans == 2,
                    "removing timing does not change effective suppression");
                Together.Net.IsServer = false;
                Check(!hooks.DiagnosticSuppressionActive, "effective suppression immediately reports inactive on a client");
                Together.Net.IsServer = true; hooks.Configure(false, 2, true);
                Check(!hooks.DiagnosticSuppressionActive, "disabled validation reports no effective suppression");
            }
            finally { timing.UnpatchSelf(); }
        }
    }
    private static void DiagnosticHistory()
    {
        Reset();
        for (int i = 0; i < 601; i++) Together.FindTheBugReports.NoteDecay(new Item { Id = i });
        Together.FindTheBugReports.Remember(new Item { Id = 999 });
        Check(Together.FindTheBugReports.BagCount == 601 && Together.FindTheBugReports.ItemCount == 1, "fixture reproduces retained diagnostic history");
        using (var hooks = New())
        {
            hooks.Install();
            Check(!hooks.DiagnosticSuppressionActive && Together.FindTheBugReports.BagCount == 601 && Together.FindTheBugReports.ItemCount == 1,
                "reading inactive suppression status never clears diagnostic history");
            hooks.Configure(true, 1, true);
            Check(Together.FindTheBugReports.BagCount == 0 && Together.FindTheBugReports.ItemCount == 0, "entering suppression clears old counters and Item references");
            int businessDecays = 0;
            for (int i = 0; i < 3000; i++)
            {
                var item = new Item { Id = i }; item.HandleDecay(); businessDecays += item.Decays; Together.FindTheBugReports.Tick();
            }
            Check(businessDecays == 3000 && hooks.SkippedDecayNotes == 3000 && Together.FindTheBugReports.BagCount == 0
                && Together.FindTheBugReports.ItemCount == 0, "long suppression stops diagnostic accumulation while all gameplay decay still runs");
            Together.Net.IsServer = false;
            Together.FindTheBugReports.NoteDecay(new Item { Id = 3001 }); Together.FindTheBugReports.Tick();
            Check(Together.FindTheBugReports.BagCount == 1 && Together.FindTheBugReports.Calls == 1, "client restores both diagnostic producer and scans");
            Together.Net.IsServer = true;
            Together.FindTheBugReports.NoteDecay(new Item { Id = 3002 });
            Check(Together.FindTheBugReports.BagCount == 0, "first host diagnostic call clears resumed history before suppressing");
            hooks.Configure(false, 1, true);
            Together.FindTheBugReports.NoteDecay(new Item { Id = 3003 }); Together.FindTheBugReports.Tick();
            Check(Together.FindTheBugReports.BagCount == 1 && Together.FindTheBugReports.Calls == 2, "disable restores both original diagnostic methods");
            hooks.Configure(true, 1, true);
        }
        Together.FindTheBugReports.NoteDecay(new Item { Id = 3004 }); Together.FindTheBugReports.Tick();
        Check(Together.FindTheBugReports.BagCount == 1 && Together.FindTheBugReports.Calls == 3, "dispose removes NoteDecay hook as well as scan hook");

        Reset(); var foreign = new Harmony("fixture.note.foreign");
        var note = typeof(Together.FindTheBugReports).GetMethod("NoteDecay");
        using (var hooks = New())
        {
            hooks.Install(); hooks.Configure(true, 1, true);
            foreign.Patch(note, prefix: new HarmonyMethod(typeof(RegressionFixture), nameof(Foreign)));
            _now = 2; hooks.Tick(); Together.FindTheBugReports.NoteDecay(new Item { Id = 1 }); Together.FindTheBugReports.Tick();
            Check(hooks.DiagnosticProblem != null && !hooks.DiagnosticSuppressionActive && hooks.SkippedScans == 0 && hooks.SkippedDecayNotes == 0
                && Together.FindTheBugReports.BagCount == 1 && Together.FindTheBugReports.Calls == 1 && hooks.EffectiveRadius == 1,
                "late unknown producer patch restores complete diagnostic group independently of radius");
            foreign.UnpatchSelf();
            foreign.Patch(typeof(Together.FindTheBugReports).GetMethod("ScanBags", BindingFlags.Static | BindingFlags.NonPublic),
                prefix: new HarmonyMethod(typeof(RegressionFixture), nameof(Foreign)));
            _now = 4; hooks.Tick(); Check(hooks.DiagnosticProblem != null, "unknown helper patch also refuses bypass of diagnostic helper behavior");
            foreign.UnpatchSelf();
        }
        using (var hooks = New(resolve: name => name == "Together.FindTheBugReports" ? typeof(BrokenReports) : DDEN.Shared.Reflect.Type(name)))
        {
            hooks.Install(); hooks.Configure(true, 1, true); BrokenReports.Tick(); BrokenReports.NoteDecay(new Item());
            Check(hooks.DiagnosticProblem != null && !hooks.DiagnosticSuppressionActive && BrokenReports.Calls == 1 && BrokenReports.Notes == 1 && hooks.EffectiveRadius == 1,
                "cleanup failure restores both original diagnostic methods without blocking radius");
        }
        foreach (var invalid in new[] { typeof(WrongReports), typeof(MissingReports) })
            using (var hooks = New(resolve: name => name == "Together.FindTheBugReports" ? invalid : DDEN.Shared.Reflect.Type(name)))
            {
                hooks.Install(); hooks.Configure(true, 1, true);
                Check(hooks.DiagnosticProblem != null && hooks.EffectiveRadius == 1, "incorrect or missing diagnostic field refuses suppression: " + invalid.Name);
            }
    }
}
