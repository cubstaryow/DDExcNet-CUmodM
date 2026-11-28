using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using DDEN.Shared;
using HarmonyLib;

namespace DDEN.MPreducer.Integration
{
    // Change only the verified periodic threshold. Preserve the native accumulator,
    // player rotation, eligibility checks and every nonperiodic save entry point.
    internal sealed class CheckpointIntervalHooks : IDisposable
    {
        internal const string Owner = "com.dden.morethreadonmp.checkpoint.interval";
        private const string TestedMvid = "b3b58466-cc0d-42b6-910c-d744dd135db2";
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static CheckpointIntervalHooks _current;
        private readonly Harmony _harmony = new Harmony(Owner);
        private readonly int _mainThread = Thread.CurrentThread.ManagedThreadId;
        private readonly Func<string, Type> _resolve;
        private readonly Action<string> _report;
        private readonly Func<double> _clock;
        private readonly MethodInfo _tick;
        private readonly Func<bool> _running, _server;
        private bool _installed, _enabled, _disposed;
        private int _seconds = 60;
        private double _nextAudit;
        internal string Problem { get; private set; }
        internal bool Active => !_disposed && _installed && _enabled && Problem == null && Host();
        internal int EffectiveSeconds => Active ? _seconds : 30;

        internal CheckpointIntervalHooks(bool verifyIdentity = true, Func<string, Type> resolver = null,
            Action<string> report = null, Func<double> clock = null)
        {
            _resolve = resolver ?? Reflect.Type; _report = report;
            _clock = clock ?? (() => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);
            Type checkpoint = _resolve("Together.PlayerCheckpoint") ?? throw new TypeLoadException("Together.PlayerCheckpoint");
            Type net = _resolve("Together.Net") ?? throw new TypeLoadException("Together.Net");
            if (verifyIdentity && (checkpoint.Module.ModuleVersionId.ToString() != TestedMvid
                || net.Assembly != checkpoint.Assembly)) throw new NotSupportedException("Untested Together checkpoint binary");
            _tick = checkpoint.GetMethod("Tick", Static, null, Type.EmptyTypes, null);
            if (_tick == null || _tick.ReturnType != typeof(void) || _tick.GetMethodBody() == null)
                throw new MissingMethodException(checkpoint.FullName, "Tick");
            _running = Getter(net, "IsRunning"); _server = Getter(net, "IsServer");
        }

        internal void Install()
        {
            RequireMain();
            if (_disposed) throw new ObjectDisposedException(nameof(CheckpointIntervalHooks));
            if (_installed) return;
            if (_current != null) throw new InvalidOperationException("Checkpoint interval already installed");
            if (!Compatible(out string reason)) { Fail(reason); return; }
            _current = this;
            try
            {
                _harmony.Patch(_tick, transpiler: new HarmonyMethod(typeof(CheckpointIntervalHooks), nameof(IntervalTranspiler)) { priority = Priority.Last });
                _installed = true;
            }
            catch
            {
                _harmony.Unpatch(_tick, typeof(CheckpointIntervalHooks).GetMethod(nameof(IntervalTranspiler), Static));
                _current = null;
                throw;
            }
        }

        internal void Configure(bool enabled, int seconds)
        {
            RequireMain(); if (_disposed) return;
            seconds = Math.Max(30, Math.Min(300, seconds));
            if (_enabled == enabled && _seconds == seconds) return;
            _enabled = enabled; _seconds = seconds; _nextAudit = 0;
            Tick();
        }
        internal void Tick()
        {
            RequireMain();
            if (_disposed || !_installed || !_enabled || Problem != null || _clock() < _nextAudit) return;
            _nextAudit = _clock() + 1;
            if (!Compatible(out string reason)) Fail(reason);
        }
        private bool Host()
        {
            if (Thread.CurrentThread.ManagedThreadId != _mainThread) return false;
            try { return _running() && _server(); } catch { return false; }
        }
        private static float IntervalSeconds() => _current?.EffectiveSeconds ?? 30;
        internal static IEnumerable<CodeInstruction> IntervalTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var matches = code.Select((instruction, index) => new { instruction, index })
                .Where(row => row.instruction.opcode == OpCodes.Ldc_R4 && Equals(row.instruction.operand, 30f)).ToArray();
            if (matches.Length != 1) throw new NotSupportedException("Expected one periodic checkpoint interval");
            int index = matches[0].index;
            if (index < 1 || index + 1 >= code.Count || code[index - 1].opcode != OpCodes.Ldsfld
                || !(code[index - 1].operand is FieldInfo wait) || wait.Name != "_wait" || wait.FieldType != typeof(float)
                || wait.DeclaringType?.FullName != "Together.PlayerCheckpoint"
                || (code[index + 1].opcode != OpCodes.Bge_Un_S && code[index + 1].opcode != OpCodes.Bge_Un
                    && code[index + 1].opcode != OpCodes.Blt_S && code[index + 1].opcode != OpCodes.Blt))
                throw new NotSupportedException("Unreviewed checkpoint interval comparison");
            // Mutate the original instruction so its labels and exception blocks survive.
            code[index].opcode = OpCodes.Call;
            code[index].operand = typeof(CheckpointIntervalHooks).GetMethod(nameof(IntervalSeconds), Static);
            return code;
        }
        private bool Compatible(out string reason)
        {
            reason = null;
            try
            {
                Patches patches = Harmony.GetPatchInfo(_tick);
                return patches == null || Check(patches.Prefixes, "prefix", out reason)
                    && Check(patches.Postfixes, "postfix", out reason)
                    && Check(patches.Transpilers, "transpiler", out reason)
                    && Check(patches.Finalizers, "finalizer", out reason);
            }
            catch (Exception error) { reason = "Checkpoint interval audit failed: " + error.Message; return false; }
        }
        private bool Check(IEnumerable<Patch> patches, string kind, out string reason)
        {
            reason = null;
            foreach (Patch patch in patches)
            {
                MethodInfo method = patch.PatchMethod;
                if (patch.owner == Owner && kind == "transpiler"
                    && method == typeof(CheckpointIntervalHooks).GetMethod(nameof(IntervalTranspiler), Static)) continue;
                if (patch.owner == CheckpointHooks.Owner &&
                    ((kind == "prefix" && method == typeof(CheckpointHooks).GetMethod("TickBefore", Static))
                    || (kind == "finalizer" && method == typeof(CheckpointHooks).GetMethod("TickAfter", Static)))) continue;
                if (TimingOnly(patch, kind)) continue;
                reason = "Unreviewed checkpoint interval patch: " + patch.owner + "/" + method?.Name + " (" + kind + ")";
                return false;
            }
            return true;
        }
        private bool TimingOnly(Patch patch, string kind)
        {
            MethodInfo method = patch.PatchMethod;
            if (patch.owner != "com.dden.perfdeep" || method == null || method.ReturnType != typeof(void)
                || !method.IsStatic || method.DeclaringType != _resolve("DDEN.NetPerfTracker.Enhanced.DeepHooks")) return false;
            Type stamp = method.DeclaringType.Assembly.GetType("DDEN.NetPerfTracker.Enhanced.CallStamp", false);
            if (stamp == null || !stamp.IsValueType) return false;
            var args = method.GetParameters();
            return kind == "prefix" && method.Name == "Before" && args.Length == 2
                && args[0].ParameterType == typeof(MethodBase) && args[1].IsOut && args[1].ParameterType == stamp.MakeByRefType()
                || kind == "finalizer" && method.Name == "After" && args.Length == 2
                && args[0].ParameterType == stamp && args[1].ParameterType == typeof(Exception);
        }
        private static Func<bool> Getter(Type type, string name)
        {
            FieldInfo field = type.GetField(name, Static);
            Expression value = field != null ? Expression.Field(null, field)
                : Expression.Call(type.GetProperty(name, Static)?.GetGetMethod(true) ?? throw new MissingMemberException(type.FullName, name));
            return Expression.Lambda<Func<bool>>(value).Compile();
        }
        private void Fail(string reason)
        {
            Problem = reason;
            try { _report?.Invoke(reason); } catch { }
        }
        private void RequireMain()
        { if (Thread.CurrentThread.ManagedThreadId != _mainThread) throw new InvalidOperationException("Checkpoint interval requires the main thread"); }
        public void Dispose()
        {
            RequireMain(); if (_disposed) return;
            _enabled = false;
            try
            {
                if (_installed) _harmony.Unpatch(_tick, typeof(CheckpointIntervalHooks).GetMethod(nameof(IntervalTranspiler), Static));
            }
            finally
            {
                if (ReferenceEquals(_current, this)) _current = null;
                _installed = false; _disposed = true;
            }
        }
    }
}
