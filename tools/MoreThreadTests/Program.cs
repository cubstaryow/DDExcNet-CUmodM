using System.Collections.Concurrent;
using System.IO.Compression;
using DDEN.MoreThreadOnMP.Core;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class Program
{
    private static int _checks;
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); _checks++; }
    private static void Until(Func<bool> condition, string message)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition()) { if (watch.ElapsedMilliseconds > 6000) throw new Exception("Timeout: " + message); Thread.Sleep(1); }
        _checks++;
    }
    private static void Main(string[] args)
    {
        Pacing(); Preparation(); ReceiveAndSend(); Pipeline(); Affinity(); Heatmap();
        var root = args.Length > 0 ? args[0] : @"L:\SteamLibrary\steamapps\common\Casualties Unknown";
        BinaryContract(root);
        Console.WriteLine("PASS " + _checks + " offline checks; no game process, saves or native Steam calls");
    }
    private static void Pacing()
    {
        double now = 0; var pacer = new ReceivePacer(() => now);
        Check(pacer.Limit(255) == 255, "disabled pacer preserves native count");
        pacer.Begin(4, 32); Check(pacer.Limit(255) == 16, "initial budget predicts bounded batch");
        now = 50; pacer.Message(50);
        Check(pacer.Limit(255) == 1, "later peers still get one after expensive first peer");
        pacer.End(); Check(pacer.SlowHandlers == 1 && pacer.PeakPollMs == 50, "single slow handler remains measurable, never preempted");
        pacer.Begin(4, 32); Check(pacer.Limit(255) == 1, "next batch adapts to actual prior handler cost"); pacer.End();
        Check(pacer.Limit(255) == 255, "native count restored outside poll");
        pacer.Begin(4, 32); pacer.Cancel(); Check(!pacer.InPoll, "session reset clears pacing scope");
    }
    private static byte[] Gzip(byte[] raw)
    {
        using var output = new MemoryStream();
        using (var zip = new GZipStream(output, CompressionLevel.Optimal, true)) zip.Write(raw);
        return output.ToArray();
    }
    private static byte[] Envelope(byte[] compressed, int start = 3)
    {
        byte[] packet = new byte[start + compressed.Length]; packet[0] = 88;
        packet[start - 2] = (byte)compressed.Length; packet[start - 1] = (byte)(compressed.Length >> 8);
        compressed.CopyTo(packet, start); return packet;
    }
    private static void Preparation()
    {
        var random = new Random(8521);
        for (int length = 0; length < 30000; length += 137)
        {
            var bytes = new byte[length]; random.NextBytes(bytes); var compressed = Gzip(bytes);
            if (compressed.Length == 0) { Check(PacketPreparation.Prepare(Envelope(compressed)) == null, "empty envelope uses native decoder"); continue; }
            for (int start = 3; start < 35; start += 7)
            {
                var packet = Envelope(compressed, start); var before = (byte[])packet.Clone(); var prepared = PacketPreparation.Prepare(packet);
                Check(prepared != null && prepared.Expanded.SequenceEqual(bytes), "predecode identical: length=" + length + " offset=" + start + " gzip=" + compressed.Length);
                Check(prepared.Matches(compressed) && packet.SequenceEqual(before), "raw packet unchanged; reuse checks full input bytes");
                if (compressed.Length > 0) { var altered = (byte[])compressed.Clone(); altered[^1] ^= 1; Check(!prepared.Matches(altered), "a near match cannot reuse prepared result"); }
            }
        }
        Check(PacketPreparation.Prepare(new byte[] { 1, 2, 3 }) == null, "uncompressed packet uses native decoder");
        Check(PacketPreparation.Prepare(Envelope(new byte[] { 31, 139, 8, 0, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 })) == null, "malformed compressed packet retained for native error handling");
        Check(PacketPreparation.Prepare(Envelope(Gzip(new byte[PacketPreparation.MaxExpanded + 1]))) == null, "expansion capped; native path handles larger legitimate envelopes");
    }
    private sealed class FakeWire : IRawWire
    {
        internal readonly ConcurrentDictionary<WirePeer, ConcurrentQueue<byte[]>> Input = new();
        internal readonly ConcurrentQueue<(WirePeer Peer, byte[] Data, int Flags)> Output = new();
        internal readonly ConcurrentQueue<int> ThreadIds = new();
        internal bool FailReceive;
        internal int CongestedSends;
        internal ManualResetEventSlim SendGate;
        public byte[] Receive(WirePeer peer)
        {
            ThreadIds.Enqueue(Environment.CurrentManagedThreadId);
            if (FailReceive) throw new IOException("fixture receive failure");
            return Input.TryGetValue(peer, out var queue) && queue.TryDequeue(out var data) ? data : null;
        }
        public int Send(WirePeer peer, byte[] data, int flags)
        {
            ThreadIds.Enqueue(Environment.CurrentManagedThreadId); SendGate?.Wait();
            if (Interlocked.Decrement(ref CongestedSends) >= 0) return 25;
            Output.Enqueue((peer, (byte[])data.Clone(), flags)); return 1;
        }
        public void ReleaseThreadMemory() { }
    }
    private static void ReceiveAndSend()
    {
        int main = Environment.CurrentManagedThreadId;
        var peer = new WirePeer(123, false, 1); var wire = new FakeWire();
        wire.Input[peer] = new ConcurrentQueue<byte[]>(Enumerable.Range(0, 24).Select(i => new byte[] { (byte)i, 2, 3, 4, 5, 6, 7, 8 }));
        using (var receiver = new ReceiveThread(wire, 4, 32))
        {
            receiver.SetPeers(new[] { peer }); Until(() => receiver.Count == 4, "receive watermark reached");
            Thread.Sleep(20); Check(receiver.Count == 4 && receiver.Bytes == 32, "backpressure retains bounded queue without dropping reliable messages");
            for (int i = 0; i < 24; i++)
            {
                WirePacket packet = null; Until(() => receiver.TryTake(peer, out packet), "receive packet");
                Check(packet.Data[0] == i, "FIFO order survives full queue pressure");
            }
            receiver.RequestStop(); Until(() => receiver.Stopped, "nonblocking receive retirement");
        }
        Check(wire.ThreadIds.All(id => id != main), "native receive never runs on main");
        wire = new FakeWire { SendGate = new ManualResetEventSlim(false) };
        using (var sender = new SendThread(wire, 4, 32))
        {
            sender.SetPeers(new[] { peer });
            for (int i = 0; i < 4; i++) Check(sender.Enqueue(peer, new byte[8] { (byte)i, 0, 0, 0, 0, 0, 0, 0 }, i == 2 ? 8 : 1) == SendAdmission.Accepted, "owned send admitted");
            Check(sender.Enqueue(peer, new byte[1], 8) == SendAdmission.Full, "in-flight send included in hard watermark");
            Check(wire.Output.IsEmpty, "caller does not wait for native send"); wire.SendGate.Set();
            for (int i = 0; i < 4; i++)
            {
                SendCompletion completion = null; Until(() => sender.TryTake(out completion), "send completion");
                Check(completion.Code == 1 && completion.Bytes == 8, "native send result retained");
            }
            Check(wire.Output.Select(packet => (int)packet.Data[0]).SequenceEqual(Enumerable.Range(0, 4)), "send FIFO retained");
            Check(wire.Output.ElementAt(2).Flags == 8, "reliable delivery flags unmodified");
            sender.RequestStop(); Until(() => sender.Stopped, "sender stops only after older packets finish");
            Check(sender.Enqueue(peer, new byte[1], 8) == SendAdmission.Stopped, "native fallback allowed only after send worker quiesces");
        }
        Check(wire.ThreadIds.All(id => id != main), "native sends use independent thread");
        wire = new FakeWire { CongestedSends = 3 };
        using (var sender = new SendThread(wire))
        {
            sender.SetPeers(new[] { peer });
            sender.Enqueue(peer, new byte[] { 1 }, 8); sender.Enqueue(peer, new byte[] { 2 }, 8);
            Until(() => wire.Output.Count == 2, "reliable native congestion retried on sender thread");
            Check(wire.Output.Select(packet => packet.Data[0]).SequenceEqual(new byte[] { 1, 2 }), "retries cannot let later reliable data overtake earlier data");
        }
        var failureWire = new FakeWire { FailReceive = true };
        using var failed = new ReceiveThread(failureWire); failed.SetPeers(new[] { peer });
        Until(() => failed.Stopped, "receive failure retires safely"); Check(failed.Fault is IOException, "receive failure is visible, not silently ignored");
    }
    private static void Pipeline()
    {
        var peer = new WirePeer(67, false, 11); var replacement = new WirePeer(67, false, 12); var wire = new FakeWire();
        wire.Input[peer] = new ConcurrentQueue<byte[]>(Enumerable.Range(0, 80).Select(i => Envelope(Gzip(new byte[] { (byte)i }))));
        using var receiver = new ReceiveThread(wire, 8, 4096); using var prepare = new PreparationThread(receiver);
        receiver.SetPeers(new[] { peer }); prepare.SetPeers(new[] { peer });
        for (int i = 0; i < 80; i++)
        {
            WirePacket packet = null; Until(() => prepare.TryTake(peer, out packet), "prepared output");
            Check(packet.Prepared != null && packet.Prepared.Expanded[0] == i, "third-thread preprocessing keeps per-peer order");
        }
        receiver.SetPeers(new[] { replacement }); prepare.SetPeers(new[] { replacement });
        Check(!prepare.TryTake(peer, out _) && !receiver.TryTake(peer, out _), "old connection lease cannot leak data into reused handle");
        receiver.RequestStop(); Until(() => receiver.Stopped && prepare.Stopped, "both pipeline stages retire without blocking main");
        Check(prepare.Prepared == 80, "actual preparation work counted");
    }
    private static void Affinity()
    {
        var cpus = new[] { new CpuDescription(0, 0, 1, 0), new CpuDescription(0, 1, 1, 0), new CpuDescription(0, 2, 0, 0), new CpuDescription(0, 3, 0, 0) };
        Check(CpuSelection.PerformanceMask(cpus, 0, 15, out bool hybrid) == 3 && hybrid, "higher Windows efficiency class selects P cores");
        Check(CpuSelection.PerformanceMask(cpus, 0, 6, out _) == 2, "respect process affinity restrictions");
        Check(CpuSelection.PerformanceMask(cpus, 0, 12, out _) == 0, "never fall back to efficiency cores");
        Check(CpuSelection.PerformanceMask(new[] { cpus[2], cpus[3] }, 0, 15, out hybrid) == 0 && !hybrid, "uniform/ambiguous topology never guessed");
        var affinity = new WindowsAffinity();
        try { affinity.Apply(); Console.WriteLine("Own test-thread affinity: " + affinity.State + " " + affinity.Detail); Check(affinity.State != "off", "actual OS topology queried without touching game"); }
        finally { affinity.Restore(); }
        Check(!affinity.Locked, "own test affinity restored");
        int ownId = (int)WindowsAffinity.GetCurrentThreadId();
        var before = WindowsThreadTimes.Read().Single(row => row.Id == ownId);
        var busy = System.Diagnostics.Stopwatch.StartNew();
        while (busy.ElapsedMilliseconds < 80) Thread.SpinWait(1000);
        var after = WindowsThreadTimes.Read().Single(row => row.Id == ownId);
        Check(before.Available && after.Available && after.CpuMs > before.CpuMs, "native CPU counter measures real own-thread work instead of Mono's constant-zero stub");
        using var sampler = new ThreadSampler(500);
        Check(sampler.IntervalMilliseconds == 500, "0.5-second sample option");
        sampler.SetInterval(1000); Check(sampler.IntervalMilliseconds == 1000, "1-second sample option");
        sampler.SetInterval(3000); Check(sampler.IntervalMilliseconds == 3000, "3-second sample option");
        sampler.SetInterval(500);
        Until(() => sampler.Snapshot.Rows.Any(row => row.Available) || sampler.Snapshot.Error != null, "real process-thread sampler with two actual snapshots");
        Check(sampler.Snapshot.Error == null, "test-process CPU time sampling available");
        Check(sampler.Snapshot.Rows.Select(row => row.Id).Distinct().Count() == sampler.Snapshot.Rows.Length, "one heat square per real OS thread");
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
            new ThreadLoad { Id = 10, Role = "game", Available = true, RecentLoad = .01 }
        };
        var snapshot = new ThreadSnapshot { Rows = rows };
        Check(ThreadHeatmap.Select(snapshot, false).Select(row => row.Id).SequenceEqual(new[] { 1, 2, 3, 4, 10 }), "keep main and network workers; hide idle engine and monitor threads; 1% cutoff inclusive");
        Check(ThreadHeatmap.Select(snapshot, true).Length == rows.Length, "show-all includes every sampled thread without truncation");
        Check(ThreadHeatmap.Select(snapshot, false).Select(row => row.Id).SequenceEqual(ThreadHeatmap.Select(new ThreadSnapshot { Rows = rows.Reverse().ToArray() }, false).Select(row => row.Id)), "stable role/ID ordering does not jump with load or enumeration order");
        var history = new ThreadActivityWindow();
        var thread = new ThreadLoad { Id = 55, Started = 10, Role = "game", Available = true, Load = .3 };
        history.Update(thread, 0); Check(thread.RecentLoad == .3, "recent active thread included");
        thread.Load = 0; history.Update(thread, 3000); Check(thread.RecentLoad == .3, "brief idle sample does not make cells flicker");
        history.Update(thread, 6001); Check(thread.RecentLoad == 0, "thread hides after recent activity expires");
        thread.Load = .5; history.Update(thread, 7000); thread.Started = 20; thread.Load = 0; history.Update(thread, 8000);
        Check(thread.RecentLoad == 0, "reused OS thread ID cannot inherit earlier activity");
        thread.Load = .8; history.Update(thread, 9000); history.Retain(Array.Empty<int>()); thread.Load = 0; history.Update(thread, 10000);
        Check(thread.RecentLoad == 0, "terminated thread history removed");
        Check(ThreadHeatmap.SampleInterval(3000) == 3000 && ThreadHeatmap.SampleInterval(2000) == 3000, "sampling defaults to 3 seconds with only supported choices");
        foreach (int count in new[] { 0, 1, 13, 116, 512, 4096 })
        foreach (float width in new[] { 240f, 480f, 960f, 1260f })
        foreach (float height in new[] { 80f, 180f })
        {
            var grid = new ThreadGrid(count, width, height);
            Check(count == 0 ? grid.Height == 0 : grid.Columns * grid.Rows >= count && grid.Cell > 0
                && grid.Columns * grid.Cell + (grid.Columns - 1) * grid.Gap <= width + .02f && grid.Height <= height + .02f,
                "all squares fit without horizontal scroll or clipping across widths/counts: " + count + "/" + width);
        }
    }
    private static void BinaryContract(string game)
    {
        using var mp = ModuleDefinition.ReadModule(Path.Combine(game, "BepInEx/plugins/Together/CasualtiesMP.dll"));
        Check(mp.Mvid.ToString() == "b3b58466-cc0d-42b6-910c-d744dd135db2", "installed MP matches exact tested experimental adapter binary");
        var transport = mp.Types.Single(type => type.FullName == "Together.TransportSteamworks");
        var send = transport.Methods.Single(method => method.Name == "SendThroughSteamSocket" && method.Parameters[0].ParameterType.FullName == "Steamworks.HSteamNetConnection&");
        var calls = send.Body.Instructions.Where(code => code.OpCode == OpCodes.Call && code.Operand is MethodReference method && method.Name == "SendMessageToConnection").ToArray();
        Check(calls.Length == 2 && calls.All(code => ((MethodReference)code.Operand).Parameters.Count == 5), "both actual send call sites have the exact wrapper shape");
        var receive = transport.Methods.Single(method => method.Name == "_K_OnReceiveNetworkingMessage");
        Check(receive.Parameters.Count == 2 && receive.Parameters[1].ParameterType.FullName == "System.Byte[]", "native handler takes fully owned packet bytes");
        Check(transport.Fields.Any(field => field.Name == "connectionMapping") && transport.NestedTypes.Single(type => type.Name == "SteamConnectionData").Fields.Any(field => field.Name == "connection"), "connection leases can be tracked without worker access to game objects");
        Check(mp.Types.Single(type => type.FullName == "Together.FindTheBugInator").Methods.Any(method => method.Name == "NoteSteamIn" && method.Parameters.Count == 1), "native traffic accounting hook exists");
        Check(mp.Types.Single(type => type.FullName == "CasualtiesTogetherUtils.Util").Methods.Any(method => method.Name == "Decompress" && method.Parameters.Count == 1 && method.Parameters[0].ParameterType.FullName == "System.Byte[]"), "native decompressor can reuse exact prepared bytes");
        var unzipReader = mp.Types.Single(type => type.FullName == "Together.MyLiteNetLibExtensions").Methods.Single(method => method.Name == "DecompressReader");
        Check(unzipReader.Body.Instructions.Count(code => code.Operand is MethodReference method && method.Name == "Decompress" && method.DeclaringType.FullName == "CasualtiesTogetherUtils.Util") == 1,
            "native reader has exactly one prepared-decode call site, preserving existing pooled decoder on misses");
        using var steam = ModuleDefinition.ReadModule(Path.Combine(game, "CasualtiesUnknown_Data/Managed/com.rlabrecque.steamworks.net.dll"));
        var message = steam.Types.Single(type => type.Name == "SteamNetworkingMessage_t");
        Check(message.Methods.Any(method => method.Name == "Release" && method.IsStatic && method.Parameters.Count == 1), "native messages have static release hook");
        Check(steam.Types.Single(type => type.FullName == "Steamworks.GameServer").Methods.Any(method => method.Name == "Shutdown"), "correct game-server shutdown barrier exists");
    }
}
