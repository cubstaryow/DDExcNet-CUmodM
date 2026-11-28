using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using DDEN.MoreThreadOnMP.Core;
using DDEN.MoreThreadOnMP.Integration;
using HarmonyLib;
using Steamworks;

// Independent managed fixture. No Unity assemblies, real world, lobby or Steam native calls.
namespace DDEN.Shared
{ public static class Reflect { public static Type Type(string name) => name == "CasualtiesTogetherUtils.Util" ? typeof(FixtureDecoder)
    : name == "Together.MyLiteNetLibExtensions" ? typeof(FixtureReader) : null; } }
namespace DDEN.MoreThreadOnMP.Integration
{
    internal sealed class MpBindings
    {
        internal MethodInfo Poll = typeof(FixtureTransport).GetMethod("Poll");
        internal MethodInfo Send = typeof(FixtureTransport).GetMethod("Send");
        internal MethodInfo Reset = typeof(FixtureTransport).GetMethod("Reset");
    }
    internal sealed class MpThreadRuntime
    {
        internal PreparedGzip CurrentPrepared;
        internal long DecoderHits;
        internal bool HandlePoll;
        internal int ResetCalls, QueueCalls, Code = 1, LastFlags;
        internal bool LastServer;
        internal uint LastConnection;
        internal byte[] LastData;
        internal bool Poll(object transport) => HandlePoll;
        internal void ResetSession() { ResetCalls++; }
        internal bool QueueSend(uint handle, bool server, IntPtr data, uint bytes, int flags, out int result)
        {
            QueueCalls++; LastConnection = handle; LastServer = server; LastFlags = flags;
            LastData = new byte[(int)bytes]; Marshal.Copy(data, LastData, 0, (int)bytes); result = Code; return true;
        }
    }
}
internal static class FixtureDecoder
{
    internal static int Calls;
    [MethodImpl(MethodImplOptions.NoInlining)] public static byte[] Decompress(byte[] bytes) { Calls++; return bytes; }
}
internal static class FixtureReader
{
    [MethodImpl(MethodImplOptions.NoInlining)] public static byte[] DecompressReader(byte[] bytes) => FixtureDecoder.Decompress(bytes);
}
internal sealed class FixtureTransport
{
    internal static int PollCalls, ResetCalls, AfterSendCode;
    [MethodImpl(MethodImplOptions.NoInlining)] public void Poll() { PollCalls++; }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void Reset() { ResetCalls++; }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Send(bool server, HSteamNetConnection handle, IntPtr data, uint length, int flags)
    {
        long number;
        var result = server ? SteamGameServerNetworkingSockets.SendMessageToConnection(handle, data, length, flags, out number)
            : SteamNetworkingSockets.SendMessageToConnection(handle, data, length, flags, out number);
        AfterSendCode = (int)result; return (int)result;
    }
}
internal static class HookProgram
{
    private static int _checks;
    private static int _foreignDecoderCalls;
    private static bool ForeignDecoder(byte[] __0, ref byte[] __result)
    { _foreignDecoderCalls++; __result = __0; return false; }
    private static bool ForeignPoll() => false;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); _checks++; }
    private static void Main()
    {
        try { Run(); }
        catch (Exception error)
        {
            for (var item = error; item != null; item = item.InnerException)
                Console.WriteLine(item.GetType().FullName + ": " + item.Message);
            Environment.ExitCode = 1;
        }
    }
    private static void Run()
    {
        var bindings = new MpBindings(); var hooks = new TransportHooks(); var runtime = new MpThreadRuntime(); TransportHooks.Runtime = runtime;
        var foreign = new Harmony("fixture.other");
        foreign.Patch(typeof(FixtureDecoder).GetMethod("Decompress"), prefix: new HarmonyMethod(typeof(HookProgram), nameof(ForeignDecoder)));
        hooks.Install(bindings);
        try
        {
            var transport = new FixtureTransport(); transport.Poll();
            Check(FixtureTransport.PollCalls == 1, "inactive receiver uses native poll exactly once");
            runtime.HandlePoll = true; transport.Poll();
            Check(FixtureTransport.PollCalls == 1, "active receiver prevents dual native polling");
            FixtureTransport.Reset();
            Check(runtime.ResetCalls == 1 && FixtureTransport.ResetCalls == 1, "worker barrier precedes original reset and does not suppress it");
            var bytes = new byte[] { 8, 7, 6, 5 }; var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                Check(FixtureTransport.Send(false, new HSteamNetConnection(91), pinned.AddrOfPinnedObject(), 4, 8) == 1, "client native send call redirected without invoking Steam");
                Check(runtime.QueueCalls == 1 && !runtime.LastServer && runtime.LastConnection == 91 && runtime.LastFlags == 8 && runtime.LastData[0] == 8, "connection, reliable flags and owned payload preserved");
                bytes[0] = 2; Check(runtime.LastData[0] == 8, "queued payload outlives original pinned buffer reuse");
                Check(FixtureTransport.Send(true, new HSteamNetConnection(92), pinned.AddrOfPinnedObject(), 4, 1) == 1 && runtime.LastServer, "game-server backend independently redirected");
                runtime.Code = 25;
                Check(FixtureTransport.Send(false, new HSteamNetConnection(91), pinned.AddrOfPinnedObject(), 4, 8) == 25 && FixtureTransport.AfterSendCode == 25, "queue pressure flows through original surrounding error path");
            }
            finally { pinned.Free(); }
            runtime.CurrentPrepared = new PreparedGzip { Compressed = new byte[] { 1, 2 }, Expanded = new byte[] { 9, 8 } };
            var decoded = FixtureReader.DecompressReader(new byte[] { 1, 2 });
            Check(decoded[0] == 9 && runtime.DecoderHits == 1 && FixtureDecoder.Calls == 0 && _foreignDecoderCalls == 0, "exact predecode bypasses competing pooled decoder before it can run");
            decoded[0] = 3;
            Check(FixtureReader.DecompressReader(new byte[] { 1, 2 })[0] == 9, "returned result cannot mutate cached predecode");
            FixtureReader.DecompressReader(new byte[] { 1, 3 });
            Check(_foreignDecoderCalls == 1 && FixtureDecoder.Calls == 0, "mismatch retains existing MP pooled decoder");
        }
        finally { hooks.Uninstall(); TransportHooks.Runtime = null; foreign.Unpatch(typeof(FixtureDecoder).GetMethod("Decompress"), HarmonyPatchType.All, "fixture.other"); }
        Check(SteamNetworkingSockets.Calls == 0 && SteamGameServerNetworkingSockets.Calls == 0, "both native API stand-ins remain uncalled while queues own the sends");
        new FixtureTransport().Poll();
        Check(FixtureTransport.PollCalls == 2 && !hooks.Active, "unpatch restores native receiver");
        var bad = typeof(BadTarget).GetMethod("Send");
        var harmony = new Harmony("fixture.fail");
        bool rejected = false;
        try { harmony.Patch(bad, transpiler: new HarmonyMethod(typeof(TransportHooks), "SendTranspiler")); }
        catch { rejected = true; }
        finally { harmony.Unpatch(bad, HarmonyPatchType.All, "fixture.fail"); }
        Check(rejected, "unknown native call layout is rejected before execution");
        foreign.Patch(bindings.Poll, prefix: new HarmonyMethod(typeof(HookProgram), nameof(ForeignPoll)));
        rejected = false;
        try { hooks.Install(bindings); } catch { rejected = true; }
        finally { hooks.Uninstall(); foreign.Unpatch(bindings.Poll, HarmonyPatchType.All, "fixture.other"); }
        Check(rejected && !hooks.Active, "another active poll replacement causes visible downgrade instead of dual consumers");
        Console.WriteLine("PASS " + _checks + " real Harmony detour checks; no native Steam calls or Unity execution");
    }
    private static class BadTarget { [MethodImpl(MethodImplOptions.NoInlining)] public static int Send() => 42; }
}
