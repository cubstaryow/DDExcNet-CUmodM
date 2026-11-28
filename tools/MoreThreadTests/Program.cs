using System.Diagnostics;
using System.Runtime.InteropServices;
using DDEN.MPreducer.Core;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static partial class Program
{
    private static int _checks;
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); _checks++; }
    private static void Until(Func<bool> condition, string message)
    {
        var watch = Stopwatch.StartNew();
        while (!condition()) { if (watch.ElapsedMilliseconds > 6000) throw new Exception("Timeout: " + message); Thread.Sleep(1); }
        _checks++;
    }
    private static void Main(string[] args)
    {
        Heatmap(); Observation(); CheckpointWriterTests.Run(Check);
        string game = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("CUMOD_GAME_DIR")
            ?? @"L:\SteamLibrary\steamapps\common\Casualties Unknown";
        BinaryContract(FindRepository());
        RegressionContract(game);
        CheckpointBinaryContract.Verify(game, Check);
        Console.WriteLine("PASS " + _checks + " offline checks; owned temporary-file saving, test-process thread sampling and read-only game metadata; no game execution or affinity changes");
    }
    private static string FindRepository()
    {
        for (var directory = new DirectoryInfo(Environment.CurrentDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src/DDEN.MPreducer/DDEN.MPreducer.csproj"))) return directory.FullName;
        throw new DirectoryNotFoundException("Run from within the CUmod repository.");
    }
    private static void Observation()
    {
        Check(ThreadObservationClock.Now >= 0, "sampling clock uses monotonic milliseconds");
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        { Console.WriteLine("Native thread observation skipped on a non-Windows host."); return; }
        int ownId = ThreadRoles.Register("main");
        try
        {
            Check(ownId != 0 && ThreadRoles.Names[ownId] == "main", "observer identifies the current test thread without changing scheduling");
            var before = WindowsThreadTimes.Read().Single(row => row.Id == ownId);
            var busy = Stopwatch.StartNew();
            while (busy.ElapsedMilliseconds < 80) Thread.SpinWait(1000);
            var after = WindowsThreadTimes.Read().Single(row => row.Id == ownId);
            Check(before.Available && after.Available && before.Created == after.Created && after.CpuMs > before.CpuMs,
                "read-only native CPU counters measure actual test-thread work");
            int monitorId = 0;
            using (var sampler = new ThreadSampler(500))
            {
                Check(sampler.IntervalMilliseconds == 500, "0.5-second sampling option");
                sampler.SetInterval(1000); Check(sampler.IntervalMilliseconds == 1000, "1-second sampling option");
                sampler.SetInterval(3000); Check(sampler.IntervalMilliseconds == 3000, "3-second sampling option");
                sampler.SetInterval(2000); Check(sampler.IntervalMilliseconds == 3000, "unsupported intervals use the bounded default");
                sampler.SetInterval(500);
                Until(() => sampler.Snapshot.Rows.Any(row => row.Available) || sampler.Snapshot.Error != null,
                    "two real observations produce CPU deltas");
                var snapshot = sampler.Snapshot;
                Check(snapshot.Error == null && snapshot.Rows.Length > 0, "test-process observation succeeds");
                Check(snapshot.Rows.Select(row => row.Id).Distinct().Count() == snapshot.Rows.Length, "one cell per OS thread identity");
                Check(snapshot.Rows.All(row => !row.Available || row.Load >= 0 && row.Load <= 1), "observed CPU fractions remain within one logical core");
                Check(snapshot.Rows.Any(row => row.Id == ownId && row.Role == "main"), "registered main thread is present in the observation");
                monitorId = snapshot.Rows.Single(row => row.Role == "monitor").Id;
            }
            Check(monitorId != 0 && !ThreadRoles.Names.ContainsKey(monitorId), "disposing observation joins its sampler and removes its role");
        }
        finally { ThreadRoles.Remove(ownId); }
        Check(!ThreadRoles.Names.ContainsKey(ownId), "main observation role is removed without touching OS thread settings");
    }
    private static void Heatmap()
    {
        var rows = new[]
        {
            new ThreadLoad { Id = 40, Role = "game", Available = true, Load = 0, RecentLoad = 0 },
            new ThreadLoad { Id = 4, Role = "prepare" }, new ThreadLoad { Id = 3, Role = "send" },
            new ThreadLoad { Id = 2, Role = "receive" }, new ThreadLoad { Id = 1, Role = "main" },
            new ThreadLoad { Id = 8, Role = "monitor", Available = true, RecentLoad = .001 },
            new ThreadLoad { Id = 9, Role = "game", Available = true, RecentLoad = .009 },
            new ThreadLoad { Id = 10, Role = "game", Available = true, RecentLoad = .01 },
            new ThreadLoad { Id = 11, Role = "monitor", Available = true, RecentLoad = .02 }
        };
        var snapshot = new ThreadSnapshot { Rows = rows };
        Check(ThreadHeatmap.Select(snapshot, false).Select(row => row.Id).SequenceEqual(new[] { 1, 10, 11 }),
            "only main is always visible; all other roles, including obsolete worker labels, use the inclusive 1% activity cutoff");
        Check(ThreadHeatmap.IsCore("main") && !ThreadHeatmap.IsCore("send") && !ThreadHeatmap.IsCore("receive")
            && !ThreadHeatmap.IsCore("prepare") && !ThreadHeatmap.IsCore("monitor"), "only the game main thread receives the core badge");
        Check(ThreadHeatmap.Select(snapshot, true).Length == rows.Length, "show-all includes every observed thread");
        Check(ThreadHeatmap.Select(snapshot, false).Select(row => row.Id).SequenceEqual(ThreadHeatmap.Select(new ThreadSnapshot { Rows = rows.Reverse().ToArray() }, false).Select(row => row.Id)),
            "main-first and ID ordering stays stable across enumeration changes");
        var history = new ThreadActivityWindow();
        var thread = new ThreadLoad { Id = 55, Started = 10, Role = "game", Available = true, Load = .3 };
        history.Update(thread, 0); Check(thread.RecentLoad == .3, "recent active thread is visible");
        thread.Load = 0; history.Update(thread, 3000); Check(thread.RecentLoad == .3, "brief idle periods do not flicker");
        history.Update(thread, 6001); Check(thread.RecentLoad == 0, "old activity expires after six seconds");
        thread.Load = .5; history.Update(thread, 7000); thread.Started = 20; thread.Load = 0; history.Update(thread, 8000);
        Check(thread.RecentLoad == 0, "reused OS thread IDs do not inherit old activity");
        thread.Load = .8; history.Update(thread, 9000); history.Retain(Array.Empty<int>()); thread.Load = 0; history.Update(thread, 10000);
        Check(thread.RecentLoad == 0, "terminated threads lose their history");
        thread.Available = false; thread.Load = 1; history.Update(thread, 11000);
        Check(thread.RecentLoad == 0, "an unavailable observation cannot fabricate busy history");
        Check(ThreadHeatmap.SampleInterval(3000) == 3000 && ThreadHeatmap.SampleInterval(2000) == 3000, "only supported observation intervals are accepted");
        foreach (int count in new[] { 0, 1, 13, 116, 512, 4096 })
        foreach (float width in new[] { 240f, 480f, 960f, 1260f })
        foreach (float height in new[] { 80f, 180f })
        {
            var grid = new ThreadGrid(count, width, height);
            Check(count == 0 ? grid.Height == 0 : grid.Columns * grid.Rows >= count && grid.Cell > 0
                && grid.Columns * grid.Cell + (grid.Columns - 1) * grid.Gap <= width + .02f && grid.Height <= height + .02f,
                "thread cells fit available width and height: " + count + "/" + width);
        }
    }
    private static IEnumerable<TypeDefinition> ArtifactTypes(IEnumerable<TypeDefinition> types)
    { foreach (var type in types) { yield return type; foreach (var child in ArtifactTypes(type.NestedTypes)) yield return child; } }
    private static void BinaryContract(string repository)
    {
        string file = Path.Combine(repository, "src/DDEN.MPreducer/bin/Release/DDEN.MPreducer.dll");
        using var assembly = AssemblyDefinition.ReadAssembly(file);
        var types = ArtifactTypes(assembly.MainModule.Types).ToArray();
        var methods = types.SelectMany(type => type.Methods).ToArray();
        var instructions = methods.Where(method => method.HasBody).SelectMany(method => method.Body.Instructions).ToArray();
        var calls = instructions.Select(instruction => instruction.Operand as MethodReference).Where(method => method != null).ToArray();
        var strings = instructions.Select(instruction => instruction.Operand as string).Where(value => value != null).ToArray();
        string[] retired = { "WirePeer", "WirePacket", "ReceiveThread", "SendThread", "PreparationThread", "PacketPreparation",
            "PreparedPacket", "ReceivePacer", "ChunkHashCache", "WindowsAffinity",
            "CpuSelection", "CpuDescription", "MpBindings", "MpThreadRuntime", "TransportHooks", "PreparationHooks",
            "ChunkHashJobs", "SimulationHooks", "WireClock" };
        foreach (string name in retired)
            Check(!types.Any(type => type.Name == name), "retired implementation is absent from the artifact: " + name);
        Check(!assembly.MainModule.AssemblyReferences.Any(reference => reference.Name.Contains("steamworks", StringComparison.OrdinalIgnoreCase)
            || reference.Name == "Unity.Collections" || reference.Name == "Unity.Jobs"), "artifact has no Steam transport or Job buffer dependencies");
        Check(!calls.Any(method => method.DeclaringType.FullName.StartsWith("Steamworks.", StringComparison.Ordinal)
            || method.Name is "SendMessageToConnection" or "ReceiveMessagesOnConnection" or "SendMessageToUser" or "ReceiveMessagesOnChannel"),
            "artifact never calls Steam send or receive APIs");
        Check(!strings.Any(value => value is "SendThroughSteamSocket" or "_Update_PollMessages" or "_K_OnReceiveNetworkingMessage"
            or "Together.TransportSteamworks" or "HashRegion"),
            "artifact contains no reflection target for retired transport or hash hooks");
        Check(!types.SelectMany(type => type.Fields).Any(field => field.FieldType.FullName.Contains("ConcurrentQueue", StringComparison.Ordinal)
            || field.FieldType.FullName.Contains("BlockingCollection", StringComparison.Ordinal)
            || field.FieldType.FullName.Contains("Queue\u00601<System.Byte[]>", StringComparison.Ordinal)),
            "artifact contains no packet queue; only observation history may use ordinary queues");
        var savingTypes = new HashSet<string> { "DDEN.MPreducer.Core.CheckpointFileStamp", "DDEN.MPreducer.Core.CheckpointWriter",
            "DDEN.MPreducer.Core.PreparedCheckpoint", "DDEN.MPreducer.Integration.CheckpointCapture", "DDEN.MPreducer.Integration.CheckpointHooks" };
        Check(methods.Where(method => method.HasBody && method.Body.Instructions.Any(instruction => instruction.Operand is MethodReference call
            && (call.DeclaringType.FullName == "System.IO.File" && call.Name is "WriteAllBytes" or "WriteAllText" or "Copy" or "Move" or "Replace" or "Delete" or "Create"
                || call.DeclaringType.FullName is "System.IO.FileStream" or "System.IO.StreamWriter" or "System.IO.Compression.GZipStream")))
            .All(method => savingTypes.Any(type => method.DeclaringType.FullName == type || method.DeclaringType.FullName.StartsWith(type + "/", StringComparison.Ordinal))),
            "file IO and gzip are confined to the restored checkpoint implementation and its compiler-generated helpers");
        Check(!calls.Any(method => method.DeclaringType.FullName == "System.Threading.ThreadPool"
            || method.DeclaringType.FullName == "System.Threading.Tasks.Task" && method.Name == "Run"
            || method.DeclaringType.FullName.StartsWith("System.Threading.Tasks.TaskFactory", StringComparison.Ordinal) && method.Name == "StartNew"),
            "validation creates no task or thread-pool work pipeline");
        var threadConstructors = methods.Where(method => method.HasBody && method.Body.Instructions.Any(instruction =>
            instruction.OpCode == OpCodes.Newobj && instruction.Operand is MethodReference constructor && constructor.DeclaringType.FullName == "System.Threading.Thread")).ToArray();
        Check(threadConstructors.Length == 2 && threadConstructors.All(method => method.DeclaringType.FullName
            is "DDEN.MPreducer.Core.ThreadSampler" or "DDEN.MPreducer.Core.CheckpointWriter"),
            "only the optional observer and checkpoint writer create threads");
        var native = methods.Where(method => method.IsPInvokeImpl).ToArray();
        var allowedNative = new HashSet<string> { "GetCurrentThreadId", "CreateToolhelp32Snapshot", "Thread32First", "Thread32Next",
            "GetCurrentProcessId", "OpenThread", "GetThreadTimes", "CloseHandle", "GetThreadDescription", "LocalFree" };
        Check(native.Length > 0 && native.All(method => allowedNative.Contains(method.PInvokeInfo.EntryPoint)
            && method.PInvokeInfo.Module.Name.Equals("kernel32.dll", StringComparison.OrdinalIgnoreCase)
            && method.DeclaringType.FullName is "DDEN.MPreducer.Core.WindowsThreadTimes" or "DDEN.MPreducer.Core.ThreadObservationClock"),
            "all native entry points are limited to read-only thread observation and handle cleanup");
        Check(!calls.Any(method => method.Name.Contains("Affinity", StringComparison.Ordinal) || method.Name.StartsWith("SetThread", StringComparison.Ordinal))
            && !strings.Any(value => value.Contains("SetThreadAffinity", StringComparison.Ordinal) || value.Contains("SetThreadGroupAffinity", StringComparison.Ordinal)),
            "artifact has no thread affinity setter or dynamic setter binding");
        var plugin = types.Single(type => type.FullName == "DDEN.MPreducer.MPreducerPlugin");
        var awake = plugin.Methods.Single(method => method.Name == "Awake");
        Check(ConfigDefault(awake, "实验线程", "Enabled", 0), "master validation switch defaults off");
        Check(ConfigDefault(awake, "回归验证", "HostSimulationRadius", 4), "simulation radius defaults to original nine-by-nine behavior");
        Check(ConfigDefault(awake, "回归验证", "SuppressDiagnosticScans", 0), "diagnostic suppression defaults off");
        Check(ConfigDefault(awake, "线程观察", "Enabled", 0), "observer defaults off for an unchanged measurement baseline");
        Check(ConfigDefault(awake, "实验线程", "CheckpointSaveWorker", 0), "background checkpoint defaults off independently");
        Check(ConfigDefault(awake, "检查点保存", "ExtendIntervalEnabled", 0), "checkpoint frequency control defaults off independently");
        Check(ConfigDefault(awake, "检查点保存", "IntervalSeconds", 60), "requested interval defaults to sixty seconds");
        Check(assembly.Name.Name == "DDEN.MPreducer", "renamed assembly produces the requested DLL name");
        Check(assembly.MainModule.Resources.Any(resource => resource.Name == "DDEN.MPreducer.Resources.localization.en.json"),
            "assembly rename retains the resource name used by the localization catalog");
        Check(assembly.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == "System.Reflection.AssemblyDescriptionAttribute"
            && Equals(attribute.ConstructorArguments[0].Value, "让超速的亿堆检测器减速")), "assembly preserves the user-provided description");
        Check(plugin.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == "BepInEx.BepInPlugin"
            && Equals(attribute.ConstructorArguments[0].Value, "com.dden.morethreadonmp")), "rename preserves the existing plugin configuration identity");
    }
    private static bool ConfigDefault(MethodDefinition awake, string section, string key, int expected)
    {
        var keyLoad = awake.Body.Instructions.Single(instruction => instruction.OpCode == OpCodes.Ldstr && Equals(instruction.Operand, key)
            && instruction.Previous?.OpCode == OpCodes.Ldstr && Equals(instruction.Previous.Operand, section)
            && (instruction.Next?.OpCode == OpCodes.Ldc_I4_0 || instruction.Next?.OpCode == OpCodes.Ldc_I4_4
                || instruction.Next?.OpCode == OpCodes.Ldc_I4 || instruction.Next?.OpCode == OpCodes.Ldc_I4_S));
        int value = keyLoad.Next.OpCode == OpCodes.Ldc_I4_0 ? 0 : keyLoad.Next.OpCode == OpCodes.Ldc_I4_4 ? 4 : Convert.ToInt32(keyLoad.Next.Operand);
        return value == expected;
    }
}
