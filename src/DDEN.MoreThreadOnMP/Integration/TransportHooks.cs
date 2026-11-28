using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using DDEN.Shared;
using HarmonyLib;
using Steamworks;

namespace DDEN.MoreThreadOnMP.Integration
{
    internal sealed class TransportHooks
    {
        internal const string Owner = "com.dden.morethreadonmp";
        private readonly Harmony _harmony = new Harmony(Owner);
        private readonly List<MethodBase> _installed = new List<MethodBase>();
        internal static MpThreadRuntime Runtime;
        private static MethodInfo _decoderMethod;
        private static Func<byte[], byte[]> _nativeDecoder;
        internal bool Active => _installed.Count > 0;
        internal void Install(MpBindings bindings)
        {
            try
            {
                var existing = Harmony.GetPatchInfo(bindings.Poll);
                if (existing != null && existing.Prefixes.Any(patch => patch.owner != Owner && patch.PatchMethod.ReturnType == typeof(bool)))
                    throw new NotSupportedException("Another plugin already replaces Steam polling");
                Patch(bindings.Poll, prefix: nameof(PollPrefix));
                Patch(bindings.Send, transpiler: nameof(SendTranspiler));
                Patch(bindings.Reset, prefix: nameof(ResetPrefix));
                Patch(typeof(SteamAPI).GetMethod("Shutdown"), prefix: nameof(SteamShutdownPrefix));
                Patch(typeof(GameServer).GetMethod("Shutdown"), prefix: nameof(SteamShutdownPrefix));
                var util = Reflect.Type("CasualtiesTogetherUtils.Util");
                var decompress = util?.GetMethod("Decompress", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(byte[]) }, null)
                    ?? throw new MissingMethodException("Util.Decompress(byte[])");
                if (decompress.ReturnType != typeof(byte[])) throw new NotSupportedException("Util.Decompress return type changed");
                _decoderMethod = decompress;
                _nativeDecoder = (Func<byte[], byte[]>)Delegate.CreateDelegate(typeof(Func<byte[], byte[]>), decompress);
                var reader = Reflect.Type("Together.MyLiteNetLibExtensions")?.GetMethod("DecompressReader", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    ?? throw new MissingMethodException("MyLiteNetLibExtensions.DecompressReader");
                Patch(reader, transpiler: nameof(DecompressTranspiler));
            }
            catch { Uninstall(); throw; }
        }
        private void Patch(MethodBase method, string prefix = null, string transpiler = null, int priority = Priority.Last)
        {
            if (method == null) throw new MissingMethodException("Missing transport hook");
            _installed.Add(method); // Include partially installed detours in rollback.
            _harmony.Patch(method, prefix: prefix == null ? null : new HarmonyMethod(typeof(TransportHooks), prefix) { priority = priority },
                transpiler: transpiler == null ? null : new HarmonyMethod(typeof(TransportHooks), transpiler));
        }
        internal void Uninstall()
        {
            for (int i = _installed.Count - 1; i >= 0; i--)
            { _harmony.Unpatch(_installed[i], HarmonyPatchType.All, Owner); _installed.RemoveAt(i); }
        }
        private static bool PollPrefix(object __instance) => Runtime == null || !Runtime.Poll(__instance);
        private static void ResetPrefix() { Runtime?.ResetSession(); }
        private static void SteamShutdownPrefix() { Runtime?.ResetSession(); }
        private static byte[] PreparedDecompress(byte[] data)
        {
            var prepared = Runtime?.CurrentPrepared;
            if (prepared == null || !prepared.Matches(data)) return _nativeDecoder(data);
            Runtime.DecoderHits++; return (byte[])prepared.Expanded.Clone();
        }
        // HarmonyX can run another prefix even after a prefix returns false. Redirect the caller
        // instead: exact prepared hits avoid decoder detours; misses invoke the existing decoder chain.
        internal static IEnumerable<CodeInstruction> DecompressTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList(); int found = 0;
            foreach (var instruction in code)
                if (instruction.opcode == OpCodes.Call && instruction.operand is MethodInfo call && call == _decoderMethod)
                { instruction.operand = typeof(TransportHooks).GetMethod(nameof(PreparedDecompress), BindingFlags.Static | BindingFlags.NonPublic); found++; }
            if (found != 1) throw new NotSupportedException("Expected one gzip decoder call, found " + found);
            return code;
        }
        // Preserve native flags, pointer ownership and the surrounding MP error-reporting path.
        // Only these two call sites are redirected, never the global Steam send API.
        internal static IEnumerable<CodeInstruction> SendTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList(); int found = 0;
            foreach (var instruction in code)
            {
                if (instruction.opcode != OpCodes.Call || !(instruction.operand is MethodInfo call) || call.Name != "SendMessageToConnection") continue;
                string replacement = call.DeclaringType == typeof(SteamNetworkingSockets) ? nameof(SendClient)
                    : call.DeclaringType == typeof(SteamGameServerNetworkingSockets) ? nameof(SendServer) : null;
                if (replacement == null) continue;
                var target = typeof(TransportHooks).GetMethod(replacement, BindingFlags.NonPublic | BindingFlags.Static);
                if (!call.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(target.GetParameters().Select(parameter => parameter.ParameterType))
                    || call.ReturnType != target.ReturnType) throw new NotSupportedException("Steam send signature changed");
                instruction.operand = target; found++;
            }
            if (found != 2) throw new NotSupportedException("Expected two MP Steam send call sites, found " + found);
            return code;
        }
        private static EResult SendClient(HSteamNetConnection connection, IntPtr data, uint bytes, int flags, out long number)
        {
            if (Runtime != null && Runtime.QueueSend(connection.m_HSteamNetConnection, false, data, bytes, flags, out var code)) { number = 0; return (EResult)code; }
            return SteamNetworkingSockets.SendMessageToConnection(connection, data, bytes, flags, out number);
        }
        private static EResult SendServer(HSteamNetConnection connection, IntPtr data, uint bytes, int flags, out long number)
        {
            if (Runtime != null && Runtime.QueueSend(connection.m_HSteamNetConnection, true, data, bytes, flags, out var code)) { number = 0; return (EResult)code; }
            return SteamGameServerNetworkingSockets.SendMessageToConnection(connection, data, bytes, flags, out number);
        }
    }
}
