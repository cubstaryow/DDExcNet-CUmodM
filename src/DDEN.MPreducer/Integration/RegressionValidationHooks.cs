using System;
using System.Collections;
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
    // A/B controls for two verified 5.5.3 -> 5.5.4 version changes. No transport, save,
    // registration, object enumeration, or replacement simulation implementation.
    internal sealed class RegressionValidationHooks : IDisposable
    {
        internal const string Owner = "com.dden.morethreadonmp.regression";
        internal const string TestedMvid = "b3b58466-cc0d-42b6-910c-d744dd135db2";
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static RegressionValidationHooks _current;
        private readonly Harmony _harmony = new Harmony(Owner);
        private readonly int _mainThread = Thread.CurrentThread.ManagedThreadId;
        private readonly Func<string, Type> _resolve;
        private readonly Action<string> _report;
        private readonly Func<double> _clock;
        private Func<bool> _running, _server, _rebuild;
        private Func<object> _world;
        private Func<bool[,]> _table;
        private Func<IDictionary> _diagnosticBags;
        private Func<IList> _diagnosticItems;
        private MethodInfo _mark, _rebuildMethod, _diagnostic, _decayNote;
        private MethodInfo[] _diagnosticMethods;
        private bool _installed, _disposed, _markPatched, _scanPatched, _decayPatched, _enabled, _suppress, _wasSuppressing;
        private int _requestedRadius = 4, _appliedRadius = 4;
        private string _radiusSetupProblem, _scanSetupProblem;
        private double _nextSample;
        private object _lastWorld;
        internal string RadiusProblem { get; private set; }
        internal string DiagnosticProblem { get; private set; }
        internal string SampleProblem { get; private set; }
        internal string Problem => RadiusProblem == null ? DiagnosticProblem : DiagnosticProblem == null
            ? RadiusProblem : RadiusProblem + "; " + DiagnosticProblem;
        internal int EffectiveRadius => Radius();
        internal bool DiagnosticSuppressionActive => _scanPatched && _decayPatched && _enabled && _suppress
            && DiagnosticProblem == null && _wasSuppressing && Host();
        internal long SkippedScans { get; private set; }
        internal long SkippedDecayNotes { get; private set; }
        internal int SimulatedChunks { get; private set; } = -1;
        internal double PeakSampleMs { get; private set; }
        internal string State => _disposed || !_enabled ? "off" : Problem != null ? "partial"
            : !Host() ? "host_only" : Radius() == 4 && !_suppress ? "original" : "active";

        internal RegressionValidationHooks(bool verifyIdentity = true, Func<string, Type> resolver = null,
            Action<string> report = null, Func<double> clock = null)
        {
            _resolve = resolver ?? Reflect.Type;
            _report = report;
            _clock = clock ?? (() => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);
            try
            {
                Type net = RequireType("Together.Net", verifyIdentity);
                _running = Getter<bool>(net, "IsRunning");
                _server = Getter<bool>(net, "IsServer");
            }
            catch (Exception error)
            {
                _radiusSetupProblem = _scanSetupProblem = Describe("Host identity unavailable", error);
            }
            try
            {
                Type range = RequireType("KrokV5Optimization.FixSimRange", verifyIdentity);
                Type shared = RequireType("Together.SharedMain", verifyIdentity);
                Type world = _resolve("WorldGeneration") ?? throw new TypeLoadException("WorldGeneration");
                Type position = _resolve("UnityEngine.Vector2") ?? throw new TypeLoadException("UnityEngine.Vector2");
                _mark = Method(range, "Mark", typeof(void), typeof(bool[,]), world, position);
                _rebuildMethod = Method(range, "Rebuild", typeof(bool));
                _rebuild = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), _rebuildMethod);
                _world = Getter<object>(world, "world");
                _table = Getter<bool[,]>(shared, "CHUNKS_VISIBLE_TO_PLAYERS");
            }
            catch (Exception error) { _radiusSetupProblem = Describe("Radius adapter unavailable", error); }
            try
            {
                Type reports = RequireType("Together.FindTheBugReports", verifyIdentity);
                Type item = _resolve("Item") ?? throw new TypeLoadException("Item");
                _diagnostic = Method(reports, "Tick", typeof(void));
                _decayNote = Method(reports, "NoteDecay", typeof(void), item);
                Type sample = reports.GetNestedType("BagSample", BindingFlags.Public | BindingFlags.NonPublic);
                if (sample == null || !sample.IsValueType) throw new MissingMemberException(reports.FullName, "BagSample");
                Container(reports, "Bags", typeof(Dictionary<,>).MakeGenericType(typeof(int), sample));
                Container(reports, "BagItems", typeof(List<>).MakeGenericType(item));
                _diagnosticBags = Getter<IDictionary>(reports, "Bags");
                _diagnosticItems = Getter<IList>(reports, "BagItems");
                // Skipping Tick bypasses its helpers; clearing their private diagnostic state
                // also requires those helpers to remain free of unknown business patches.
                _diagnosticMethods = reports.GetMethods(Static | BindingFlags.DeclaredOnly);
            }
            catch (Exception error) { _scanSetupProblem = Describe("Diagnostic adapter unavailable", error); }
            RadiusProblem = _radiusSetupProblem; DiagnosticProblem = _scanSetupProblem;
        }

        internal void Install()
        {
            RequireMain();
            if (_disposed) throw new ObjectDisposedException(nameof(RegressionValidationHooks));
            if (_installed) return;
            if (_current != null) throw new InvalidOperationException("Regression validation already installed");
            _current = this; _installed = true;
            if (_radiusSetupProblem == null)
            {
                try
                {
                    if (!RadiusCompatible(out string reason)) throw new NotSupportedException(reason);
                    _harmony.Patch(_mark, transpiler: new HarmonyMethod(typeof(RegressionValidationHooks), nameof(RadiusTranspiler)) { priority = Priority.Last });
                    _markPatched = true;
                }
                catch (Exception error)
                {
                    Remove(_mark);
                    RadiusProblem = _radiusSetupProblem = Describe("Radius adapter refused", error);
                }
            }
            if (_scanSetupProblem == null)
            {
                try
                {
                    if (!DiagnosticsCompatible(out string reason)) throw new NotSupportedException(reason);
                    _harmony.Patch(_diagnostic, prefix: new HarmonyMethod(typeof(RegressionValidationHooks), nameof(SkipDiagnostic)) { priority = Priority.Last });
                    _harmony.Patch(_decayNote, prefix: new HarmonyMethod(typeof(RegressionValidationHooks), nameof(SkipDecayNote)) { priority = Priority.Last });
                    _decayPatched = true;
                    _scanPatched = true;
                }
                catch (Exception error)
                {
                    Remove(_diagnostic);
                    Remove(_decayNote);
                    DiagnosticProblem = _scanSetupProblem = Describe("Diagnostic adapter refused", error);
                }
            }
        }

        internal void Configure(bool enabled, int radius, bool suppress)
        {
            RequireMain(); if (_disposed) return;
            if (_enabled == enabled && _requestedRadius == radius && _suppress == suppress) return;
            _enabled = enabled; _requestedRadius = radius; _suppress = suppress;
            Audit(); RefreshRadius(); SuppressingDiagnostics(); _nextSample = 0;
        }

        internal void Tick()
        {
            RequireMain(); if (_disposed || !_installed) return;
            double now = _clock();
            // Patch ownership and the existing bool grid are sampled once per second.
            // Configure audits synchronously; later external patch changes fail closed at
            // the next sample. No per-object Harmony queries are added to Mark.
            if (_enabled && now >= _nextSample)
            {
                _nextSample = now + 1;
                Audit(); RefreshRadius(); Sample();
            }
            else RefreshRadius();
            SuppressingDiagnostics();
        }

        private bool Host()
        {
            if (_disposed || Thread.CurrentThread.ManagedThreadId != _mainThread) return false;
            try { return _running != null && _server != null && _running() && _server(); }
            catch { return false; }
        }
        private int Radius() => _enabled && _markPatched && RadiusProblem == null && Host() ? _requestedRadius : 4;
        private static int RadiusForMark() => _current?.Radius() ?? 4;
        private static bool SkipDiagnostic()
        {
            var self = _current;
            if (self == null || !self.SuppressingDiagnostics()) return true;
            self.SkippedScans++; return false;
        }
        private static bool SkipDecayNote()
        {
            var self = _current;
            if (self == null || !self.SuppressingDiagnostics()) return true;
            self.SkippedDecayNotes++; return false;
        }
        private bool SuppressingDiagnostics()
        {
            if (Thread.CurrentThread.ManagedThreadId != _mainThread) return false;
            if (!_scanPatched || !_decayPatched || !_enabled || !_suppress || DiagnosticProblem != null || !Host())
            { _wasSuppressing = false; return false; }
            if (_wasSuppressing) return true;
            try
            {
                // ScanBags normally bounds Bags and releases old Item references. When its
                // Tick is disabled, stop only the diagnostic producer and clear its history
                // once. Item.HandleDecay and every gameplay update remain untouched.
                _diagnosticBags().Clear();
                _diagnosticItems().Clear();
                _wasSuppressing = true; return true;
            }
            catch (Exception error)
            {
                DiagnosticProblem = _scanSetupProblem = Describe("Diagnostic history cleanup failed", error);
                _wasSuppressing = false; Notify(DiagnosticProblem); return false;
            }
        }
        private void RefreshRadius()
        {
            int radius = Radius(); object world = null;
            try { world = _world?.Invoke(); }
            catch { }
            bool changed = radius != _appliedRadius;
            bool newWorld = !ReferenceEquals(world, _lastWorld);
            _lastWorld = world;
            if (!changed && !(newWorld && radius != 4)) return;
            _appliedRadius = radius;
            if (_rebuild == null || !Compatible(_rebuildMethod, out _)) return;
            try { _rebuild(); }
            catch (Exception error)
            {
                RadiusProblem = _radiusSetupProblem = Describe("Original range rebuild failed", error);
                _appliedRadius = 4;
                // The next original SharedMain.Update uses radius 4. Do not recursively
                // retry a failing game callback or mutate the grid ourselves.
                Notify(RadiusProblem);
            }
        }
        private void Sample()
        {
            long started = Stopwatch.GetTimestamp();
            try
            {
                bool[,] table = _table?.Invoke();
                if (!Host() || table == null) { SimulatedChunks = -1; return; }
                int count = 0;
                foreach (bool simulated in table) if (simulated) count++;
                SimulatedChunks = count; SampleProblem = null;
            }
            catch (Exception error) { SimulatedChunks = -1; SampleProblem = Describe("Range sample unavailable", error); }
            finally { PeakSampleMs = Math.Max(PeakSampleMs, (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency); }
        }
        private void Audit()
        {
            string radius = _radiusSetupProblem, scans = _scanSetupProblem;
            if (radius == null && _requestedRadius != 1 && _requestedRadius != 2 && _requestedRadius != 4)
                radius = "Host simulation radius must be 1, 2, or 4";
            if (radius == null && !RadiusCompatible(out radius)) { }
            if (scans == null && !DiagnosticsCompatible(out scans)) { }
            if (radius != null && radius != RadiusProblem) Notify(radius);
            if (scans != null && scans != DiagnosticProblem) Notify(scans);
            RadiusProblem = radius; DiagnosticProblem = scans;
        }
        private bool RadiusCompatible(out string reason) => Compatible(_mark, out reason) && Compatible(_rebuildMethod, out reason);
        private bool DiagnosticsCompatible(out string reason)
        {
            reason = null;
            if (_diagnosticMethods == null) { reason = "Diagnostic bindings unavailable"; return false; }
            foreach (MethodInfo method in _diagnosticMethods) if (!Compatible(method, out reason)) return false;
            return true;
        }
        private bool Compatible(MethodInfo target, out string reason)
        {
            reason = null;
            if (target == null) { reason = "Validation method unavailable"; return false; }
            try
            {
                Patches patches = Harmony.GetPatchInfo(target);
                if (patches == null) return true;
                return CheckPatches(target, patches.Prefixes, "prefix", out reason)
                    && CheckPatches(target, patches.Postfixes, "postfix", out reason)
                    && CheckPatches(target, patches.Transpilers, "transpiler", out reason)
                    && CheckPatches(target, patches.Finalizers, "finalizer", out reason);
            }
            catch (Exception error) { reason = Describe("Validation patch audit failed", error); return false; }
        }
        private bool CheckPatches(MethodInfo target, IEnumerable<Patch> patches, string kind, out string reason)
        {
            reason = null;
            foreach (Patch patch in patches)
            {
                MethodInfo method = patch.PatchMethod;
                if (patch.owner == Owner && method?.DeclaringType == typeof(RegressionValidationHooks)
                    && ((target == _mark && kind == "transpiler" && method.Name == nameof(RadiusTranspiler))
                        || (target == _diagnostic && kind == "prefix" && method.Name == nameof(SkipDiagnostic))
                        || (target == _decayNote && kind == "prefix" && method.Name == nameof(SkipDecayNote)))) continue;
                if (TimingOnly(patch, kind)) continue;
                reason = "Unreviewed validation patch: " + target.DeclaringType?.FullName + "." + target.Name
                    + " <- " + patch.owner + "/" + method?.Name + " (" + kind + ")";
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
                && args[0].ParameterType == typeof(MethodBase) && args[1].IsOut
                && args[1].ParameterType == stamp.MakeByRefType()
                || kind == "finalizer" && method.Name == "After" && args.Length == 2
                && args[0].ParameterType == stamp && args[1].ParameterType == typeof(Exception);
        }
        internal static IEnumerable<CodeInstruction> RadiusTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var code = instructions.ToList();
            var constants = code.Select((instruction, index) => new { instruction, index })
                .Where(row => row.instruction.opcode == OpCodes.Ldc_I4_4
                    || row.instruction.opcode == OpCodes.Ldc_I4 && Equals(row.instruction.operand, 4)
                    || row.instruction.opcode == OpCodes.Ldc_I4_S && Convert.ToInt32(row.instruction.operand) == 4).ToArray();
            if (constants.Length != 4 || constants.Any(row => row.index + 1 >= code.Count
                    || (code[row.index + 1].opcode != OpCodes.Add && code[row.index + 1].opcode != OpCodes.Sub))
                || constants.Count(row => code[row.index + 1].opcode == OpCodes.Add) != 2)
                throw new NotSupportedException("Expected exactly four +/-4 simulation radius constants");
            var getter = typeof(RegressionValidationHooks).GetMethod(nameof(RadiusForMark), Static);
            var radius = generator.DeclareLocal(typeof(int));
            foreach (var row in constants) { row.instruction.opcode = OpCodes.Ldloc; row.instruction.operand = radius; }
            // Read host status once per original Mark invocation, not at every loop bound.
            code.Insert(0, new CodeInstruction(OpCodes.Stloc, radius));
            code.Insert(0, new CodeInstruction(OpCodes.Call, getter));
            return code;
        }
        private Type RequireType(string name, bool verify)
        {
            Type type = _resolve(name) ?? throw new TypeLoadException(name);
            if (verify && type.Module.ModuleVersionId.ToString() != TestedMvid)
                throw new NotSupportedException("Untested Together binary: " + type.Module.ModuleVersionId);
            return type;
        }
        private static MethodInfo Method(Type type, string name, Type result, params Type[] parameters)
        {
            MethodInfo method = type.GetMethod(name, Static, null, parameters, null);
            if (method == null || method.ReturnType != result || method.ContainsGenericParameters || method.GetMethodBody() == null)
                throw new MissingMethodException(type.FullName, name);
            return method;
        }
        private static Func<T> Getter<T>(Type type, string name)
        {
            Expression value;
            FieldInfo field = type.GetField(name, Static);
            if (field != null) value = Expression.Field(null, field);
            else value = Expression.Call(type.GetProperty(name, Static)?.GetGetMethod(true) ?? throw new MissingMemberException(type.FullName, name));
            return Expression.Lambda<Func<T>>(Expression.Convert(value, typeof(T))).Compile();
        }
        private static void Container(Type type, string name, Type expected)
        {
            FieldInfo field = type.GetField(name, Static);
            if (field == null || !field.IsInitOnly || field.FieldType != expected)
                throw new MissingFieldException(type.FullName, name);
        }
        private static string Describe(string action, Exception error) => action + ": " + error.GetType().Name + " " + error.Message;
        private void Notify(string message) { try { _report?.Invoke(message); } catch { } }
        private void RequireMain()
        { if (Thread.CurrentThread.ManagedThreadId != _mainThread) throw new InvalidOperationException("Validation controls require the owning main thread"); }
        private void Remove(MethodInfo method)
        {
            if (method == null) return;
            try
            {
                // Remove only this exact patch method, including after partial installation.
                // An unexpected patch reusing our owner is still somebody else's behavior.
                string patch = method == _mark ? nameof(RadiusTranspiler) : method == _decayNote ? nameof(SkipDecayNote) : nameof(SkipDiagnostic);
                _harmony.Unpatch(method, typeof(RegressionValidationHooks).GetMethod(patch, Static));
            }
            catch (Exception error) { Notify(Describe("Validation hook removal failed", error)); }
        }
        public void Dispose()
        {
            RequireMain(); if (_disposed) return;
            if (!_installed || !ReferenceEquals(_current, this)) { _disposed = true; return; }
            _enabled = false; RefreshRadius();
            Remove(_diagnostic); Remove(_decayNote); Remove(_mark);
            _markPatched = _scanPatched = _decayPatched = false; _wasSuppressing = false; _disposed = true;
            if (ReferenceEquals(_current, this)) _current = null;
        }
    }
}
