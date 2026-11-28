using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using DDEN.MPreducer.Core;
using DDEN.MPreducer.Integration;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Together;

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

internal static class CheckpointAdditionalFixture
{
    private const BindingFlags Static = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private static Type Resolve(string name) => typeof(SaveSystem).Assembly.GetType(name, false);
    private static string Personal => Path.Combine(CheckpointFixtureState.Root, PlayerCheckpoint.Player.Id, "save.sv");
    private static string Shared => Path.Combine(CheckpointFixtureState.Root, "save.sv");
    private static void UnknownPrefix() { }
    private static IEnumerable<CodeInstruction> UnknownTranspiler(IEnumerable<CodeInstruction> code) => code;
    private static void Seed()
    {
        Net.Running = Net.Server = true;
        SavesystemPatch.PreserveSave = SavesystemPatch.InDebugMap = false;
        WorldGeneration.world = new WorldGeneration();
        PlayerCheckpoint.Player = new ScavPlayer();
        PlayerCheckpoint.Elapsed = 0; PlayerCheckpoint.Delta = 31f;
        SaveSystem.Marker = "fresh"; SaveSystem.Destination = Path.GetDirectoryName(Personal);
        SaveSystem.MutablePayload = new JObject { ["value"] = 1 };
        CheckpointFixtureState.Seed(Personal, new JObject { ["marker"] = "old-player" });
        CheckpointFixtureState.Seed(Shared, new JObject { ["marker"] = "old-shared" });
    }

    internal static void Run(Action<bool, string> check)
    {
        Seed();
        using (var entered = new ManualResetEventSlim())
        using (var release = new ManualResetEventSlim())
        using (var hooks = new CheckpointHooks(new CheckpointBindings(false, Resolve), new CheckpointWriter(text =>
        {
            entered.Set();
            if (!release.Wait(5000)) throw new TimeoutException("Snapshot fixture release");
            return CheckpointWriter.Compress(text);
        })))
        {
            hooks.Install(); hooks.Configure(true);
            try
            {
                PlayerCheckpoint.Tick();
                check(entered.Wait(5000), "capture reaches background worker after main-thread serialization");
                SaveSystem.MutablePayload["value"] = 999;
                PlayerCheckpoint.Player.Species = "mutated-after-capture";
                WorldGeneration.world.biomeDepth = 999;
            }
            finally { release.Set(); }
            hooks.Drain();
            JObject saved = CheckpointFixtureState.Read(Personal);
            check((int)saved["payload"]["value"] == 1 && (int)saved["biome"] == 7 && (string)saved["ggSpecies"] == "fixture-species",
                "mutating source JSON, player and world after enqueue cannot alter the immutable worker snapshot");
            int accessCount = CheckpointFixtureState.UnityAccessThreads.Count;
            foreach (Action action in new Action[] { hooks.Install, () => hooks.Configure(false), hooks.Pump, hooks.Drain, hooks.Dispose })
                check(OnWorker(action) is InvalidOperationException, "checkpoint lifecycle rejects non-main-thread calls");
            check(CheckpointFixtureState.UnityAccessThreads.Count == accessCount, "rejected lifecycle calls never access game/provider state");
        }

        var saveMethod = typeof(SaveSystem).GetMethod("SaveGame");
        var tickMethod = typeof(PlayerCheckpoint).GetMethod("Tick");
        var timerType = typeof(DDEN.NetPerfTracker.Enhanced.DeepHooks);
        var perf = new Harmony("com.dden.perfdeep");
        perf.Patch(saveMethod, prefix: new HarmonyMethod(timerType.GetMethod("Before", Static)), finalizer: new HarmonyMethod(timerType.GetMethod("After", Static)));
        perf.Patch(tickMethod, prefix: new HarmonyMethod(timerType.GetMethod("Before", Static)), finalizer: new HarmonyMethod(timerType.GetMethod("After", Static)));
        try
        {
            Seed();
            using var hooks = new CheckpointHooks(new CheckpointBindings(false, Resolve));
            hooks.Install(); hooks.Configure(true);
            using var interval = new CheckpointIntervalHooks(false, Resolve);
            interval.Install(); interval.Configure(true, 60);
            PlayerCheckpoint.Delta = 30; PlayerCheckpoint.Elapsed = 0;
            PlayerCheckpoint.Tick();
            check(hooks.Captured == 0 && interval.Active, "interval and background hooks coexist with verified perfdeep timer signatures");
            PlayerCheckpoint.Tick(); hooks.Drain();
            check(hooks.Captured == 1 && hooks.Completed == 1 && hooks.Problem == null
                && DDEN.NetPerfTracker.Enhanced.DeepHooks.BeforeCalls > 0 && DDEN.NetPerfTracker.Enhanced.DeepHooks.AfterCalls > 0,
                "second 30-second tick captures once at 60 seconds without bypassing pure timing callbacks");
            interval.Configure(false, 60);
            PlayerCheckpoint.Tick(); hooks.Drain();
            check(hooks.Captured == 2 && interval.EffectiveSeconds == 30, "disabling interval restores native threshold while background capture stays independent");
            interval.Configure(true, 60);
            hooks.Dispose();
            int writes = PlayerCheckpoint.WriteCalls;
            PlayerCheckpoint.Elapsed = 0;
            PlayerCheckpoint.Tick();
            check(PlayerCheckpoint.WriteCalls == writes && interval.Active, "disposing background hooks leaves the independent 60-second interval active");
            PlayerCheckpoint.Tick();
            check(PlayerCheckpoint.WriteCalls == writes + 1, "interval-only mode saves synchronously when its threshold is reached");
            using var restarted = new CheckpointHooks(new CheckpointBindings(false, Resolve));
            restarted.Install(); restarted.Configure(true);
            PlayerCheckpoint.Elapsed = 0; PlayerCheckpoint.Tick(); PlayerCheckpoint.Tick(); restarted.Drain();
            check(restarted.Captured == 1 && interval.Active && interval.Problem == null,
                "background hooks can be reinstalled while the independent interval remains active");
            var external = new Harmony("fixture.save-only-unknown");
            external.Patch(saveMethod, prefix: new HarmonyMethod(typeof(CheckpointAdditionalFixture), nameof(UnknownPrefix)));
            try
            {
                PlayerCheckpoint.Elapsed = 60; PlayerCheckpoint.Delta = 0; PlayerCheckpoint.Tick();
                check(restarted.Problem != null && interval.Active && interval.EffectiveSeconds == 60,
                    "background compatibility rejection does not disable a still-compatible interval hook");
            }
            finally { external.Unpatch(saveMethod, HarmonyPatchType.All, external.Id); }
        }
        finally { perf.Unpatch(saveMethod, HarmonyPatchType.All, perf.Id); perf.Unpatch(tickMethod, HarmonyPatchType.All, perf.Id); }

        // Trust a patch's exact method and target, never just a familiar owner string.
        foreach (string owner in new[] { CheckpointHooks.Owner, "com.dden.perfdeep" })
        {
            var impostor = new Harmony(owner);
            impostor.Patch(saveMethod, prefix: new HarmonyMethod(typeof(CheckpointAdditionalFixture), nameof(UnknownPrefix)));
            try
            {
                Seed(); using var hooks = new CheckpointHooks(new CheckpointBindings(false, Resolve));
                hooks.Install(); hooks.Configure(true); PlayerCheckpoint.Tick(); hooks.Drain();
                check(hooks.Captured == 0 && hooks.Problem != null && (string)CheckpointFixtureState.Read(Personal)["marker"] == "fresh",
                    "spoofed trusted patch owner retains synchronous native save: " + owner);
                hooks.Dispose();
                check(Harmony.GetPatchInfo(saveMethod).Prefixes.Any(patch => patch.PatchMethod == typeof(CheckpointAdditionalFixture).GetMethod(nameof(UnknownPrefix), Static)),
                    "disposing owned hooks preserves an unrelated patch even when it uses the same owner string");
            }
            finally { impostor.Unpatch(saveMethod, typeof(CheckpointAdditionalFixture).GetMethod(nameof(UnknownPrefix), Static)); }
        }

        foreach (bool dispose in new[] { false, true })
        {
            Seed();
            using var hooks = new CheckpointHooks(new CheckpointBindings(false, Resolve));
            hooks.Install(); hooks.Configure(true);
            int snapshots = CheckpointFixtureState.GameSaveCalls, providers = CUCoreLib.Saving.SaveCoordinator.ProviderCalls;
            SaveSystem.DuringSave = () => { if (dispose) hooks.Dispose(); else hooks.Configure(false); };
            try { PlayerCheckpoint.Tick(); }
            finally { SaveSystem.DuringSave = null; }
            JObject saved = CheckpointFixtureState.Read(Personal);
            check(!hooks.Busy && (string)saved["marker"] == "fresh" && saved["CUCoreLib"] != null && saved["ggSpecies"] != null,
                "closing during a save callback finishes the full pending document before returning: " + (dispose ? "dispose" : "disable"));
            check(CheckpointFixtureState.GameSaveCalls == snapshots + 1 && CUCoreLib.Saving.SaveCoordinator.ProviderCalls == providers + 1,
                "closing during capture does not invoke game/provider snapshot callbacks twice");
        }

        Seed();
        using (var hooks = new CheckpointHooks(new CheckpointBindings(false, Resolve)))
        {
            hooks.Install(); hooks.Configure(true);
            SaveSystem.Marker = "direct-load-current"; PlayerCheckpoint.Tick();
            SaveSystem.TryLoadGame();
            check(!hooks.Busy && SaveSystem.LoadedMarker == "direct-load-current",
                "direct native TryLoadGame drains the pending checkpoint before reading disk");
            SaveSystem.Marker = "has-save-current"; PlayerCheckpoint.Tick();
            bool exists = SaveSystem.HasSave();
            check(exists && !hooks.Busy && (string)CheckpointFixtureState.Read(Personal)["marker"] == "has-save-current",
                "native HasSave drains pending writes before testing current save state");
        }

        Interval(check);

        Seed();
        var injected = new CheckpointWriter();
        bool constructorRejected = false;
        try { new CheckpointHooks(writer: injected); }
        catch (NotSupportedException) { constructorRejected = true; }
        check(constructorRejected && !injected.TryStart(new CheckpointWriteRequest(1, 1,
            new CheckpointWriteTarget(Personal, "rejected", CheckpointFileStamp.Capture(Personal)))),
            "failed verified binding disposes an injected writer without starting work");

        Seed();
        using (var hooks = new CheckpointHooks(new CheckpointBindings(false, Resolve)))
        {
            hooks.Install(); hooks.Configure(true);
            using var invalidInterval = new CheckpointIntervalHooks(false, name => name == "Together.PlayerCheckpoint" ? typeof(BadIntervalCheckpoint) : Resolve(name));
            bool rejected = false;
            try { invalidInterval.Install(); } catch (Exception) { rejected = true; }
            PlayerCheckpoint.Tick(); hooks.Drain();
            check(rejected && hooks.Captured == 1 && hooks.Completed == 1 && hooks.Problem == null,
                "an interval IL-shape rejection leaves the independently verified background save functional");
        }
    }

    private static void Interval(Action<bool, string> check)
    {
        Seed();
        double clock = 0;
        var tick = typeof(PlayerCheckpoint).GetMethod("Tick");
        using (var interval = new CheckpointIntervalHooks(false, Resolve, clock: () => clock))
        {
            interval.Install();
            check(interval.EffectiveSeconds == 30 && !interval.Active, "interval installation preserves native default until explicitly enabled");
            interval.Configure(true, 60);
            PlayerCheckpoint.Delta = 29; PlayerCheckpoint.Elapsed = 0;
            int writes = PlayerCheckpoint.WriteCalls;
            PlayerCheckpoint.Tick(); PlayerCheckpoint.Tick();
            check(PlayerCheckpoint.WriteCalls == writes && PlayerCheckpoint.Elapsed == 58, "60-second mode preserves accumulated native elapsed time");
            PlayerCheckpoint.Delta = 2; PlayerCheckpoint.Tick();
            check(PlayerCheckpoint.WriteCalls == writes + 1 && PlayerCheckpoint.Elapsed == 0, "selected interval triggers exactly one periodic write and native reset");
            PlayerCheckpoint.Elapsed = 45; PlayerCheckpoint.Delta = 0;
            interval.Configure(false, 60); PlayerCheckpoint.Tick();
            check(PlayerCheckpoint.WriteCalls == writes + 2, "disabling does not erase elapsed time and an overdue native checkpoint runs immediately");
            interval.Configure(true, 300);
            PlayerCheckpoint.Elapsed = 0; PlayerCheckpoint.Delta = 0;
            SaveSystem.Marker = "manual-at-300"; SaveSystem.SaveGame();
            check((string)CheckpointFixtureState.Read(Personal)["marker"] == "manual-at-300", "manual SaveGame remains immediate with extended periodic interval");
            SaveSystem.Marker = "direct-exit-write"; PlayerCheckpoint.WriteOne(PlayerCheckpoint.Player, false);
            check((string)CheckpointFixtureState.Read(Personal)["marker"] == "direct-exit-write", "direct departure/forced WriteOne remains immediate outside Tick");
            PlayerCheckpoint.WriteLayerTransition();
            check((int)CheckpointFixtureState.Read(Personal)["biome"] == 8, "layer-transition save bypasses periodic interval and retains native next-layer biome");
            Net.Server = false;
            check(interval.EffectiveSeconds == 30 && !interval.Active, "clients retain original interval value");
            Net.Server = true; Net.Running = false;
            check(interval.EffectiveSeconds == 30 && !interval.Active, "singleplayer/stopped sessions retain original interval value");
            Net.Running = true;
            interval.Configure(true, 1); check(interval.EffectiveSeconds == 30, "interval lower bound clamps to original 30 seconds");
            interval.Configure(true, 999); check(interval.EffectiveSeconds == 300, "interval upper bound remains bounded");
            check(OnWorker(() => interval.Configure(false, 60)) is InvalidOperationException, "interval lifecycle refuses foreign-thread configuration");
            var unknown = new Harmony(CheckpointIntervalHooks.Owner);
            unknown.Patch(tick, transpiler: new HarmonyMethod(typeof(CheckpointAdditionalFixture), nameof(UnknownTranspiler)));
            try
            {
                clock = 2; interval.Tick();
                check(interval.Problem != null && interval.EffectiveSeconds == 30, "late unknown transpiler with spoofed interval owner restores native threshold");
            }
            finally { unknown.Unpatch(tick, typeof(CheckpointAdditionalFixture).GetMethod(nameof(UnknownTranspiler), Static)); }
        }
        PlayerCheckpoint.Elapsed = 0; PlayerCheckpoint.Delta = 30;
        int after = PlayerCheckpoint.WriteCalls; PlayerCheckpoint.Tick();
        check(PlayerCheckpoint.WriteCalls == after + 1, "disposing interval removes only its patch and restores native 30-second behavior");

        var labels = new CodeInstruction(OpCodes.Ldc_R4, 30f);
        labels.labels.Add(default);
        labels.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
        var shape = new[] { new CodeInstruction(OpCodes.Ldsfld, typeof(PlayerCheckpoint).GetField("_wait", Static)), labels, new CodeInstruction(OpCodes.Bge_Un_S) };
        var rewritten = CheckpointIntervalHooks.IntervalTranspiler(shape).ToArray();
        check(ReferenceEquals(rewritten[1], labels) && labels.opcode == OpCodes.Call && labels.labels.Count == 1 && labels.blocks.Count == 1,
            "interval replacement preserves branch labels and exception blocks on the original instruction");
        bool rejected = false;
        try { CheckpointIntervalHooks.IntervalTranspiler(new[] { new CodeInstruction(OpCodes.Ldc_R4, 30f) }).ToArray(); }
        catch (NotSupportedException) { rejected = true; }
        check(rejected, "unverified interval IL shape fails closed instead of altering another 30-second constant");
    }

    private static Exception OnWorker(Action action)
    {
        Exception error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { error = exception; } });
        thread.Start(); if (!thread.Join(6000)) throw new TimeoutException("Lifecycle guard fixture"); return error;
    }

    private static class BadIntervalCheckpoint
    {
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static void Tick() { }
    }
}
