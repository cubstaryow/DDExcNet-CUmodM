using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using DDEN.Diagnostics;
using DDEN.Shared;
using HarmonyLib;

namespace DDEN.NetPerfTracker.Enhanced
{
    internal sealed class DeepHooks
    {
        private const string Owner = "com.dden.perfdeep";
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly Harmony _harmony = new Harmony(Owner);
        private readonly Dictionary<MethodBase, int> _slots = new Dictionary<MethodBase, int>();
        private readonly List<MethodBase> _installed = new List<MethodBase>();
        private readonly List<CountReader> _counts = new List<CountReader>();
        private readonly int _mainThread = Thread.CurrentThread.ManagedThreadId;
        internal readonly TimingLedger Ledger = new TimingLedger(Stopwatch.Frequency);
        private static DeepHooks _current;
        private long _offThread;
        private string _identity = "unavailable", _status = "disabled";
        private double _setupMs;
        internal bool Active => Ledger.Active;
        internal string Status => _status;
        private sealed class CountReader
        {
            internal string Name;
            internal FieldInfo Field;
            internal PropertyInfo Count;
            internal DeepCount Read()
            {
                int count = -1;
                try
                {
                    object value = Field?.GetValue(null);
                    if (value is int counter) count = counter;
                    else if (value is ICollection collection) count = collection.Count;
                    else if (value != null && Count?.GetValue(value) is int number) count = number;
                }
                catch { }
                return new DeepCount { Name = Name, Value = count };
            }
        }
        internal void Start(double now)
        {
            if (Active) return;
            // An incomplete previous unpatch must be removed before installing a second copy.
            if (_installed.Count > 0) { Stop(now); if (_installed.Count > 0) return; }
            long started = Stopwatch.GetTimestamp();
            _current = this; _slots.Clear(); _counts.Clear(); _offThread = 0;
            var assembly = Reflect.FindAssembly("CasualtiesMP");
            _identity = assembly == null ? "unavailable" : assembly.GetName().Version + "/" + assembly.ManifestModule.ModuleVersionId;
            var prefix = new HarmonyMethod(typeof(DeepHooks).GetMethod(nameof(Before), Static)) { priority = Priority.First };
            var finalizer = new HarmonyMethod(typeof(DeepHooks).GetMethod(nameof(After), Static)) { priority = Priority.Last };
            for (int i = 0; i < TimingTargets.All.Length; i++)
            {
                var target = TimingTargets.All[i];
                Ledger.SetAvailable(i, false, "missing method");
                var targetAssembly = Reflect.FindAssembly(target.Assembly);
                MethodInfo method = targetAssembly?.GetType(target.Type, false)?.GetMethod(target.Method,
                    (target.Instance ? Instance : Static) | BindingFlags.DeclaredOnly, null, target.Parameters, null);
                if (method == null || method.ReturnType != typeof(void) || method.ContainsGenericParameters || method.IsAbstract || method.GetMethodBody() == null) continue;
                try
                {
                    _slots[method] = i;
                    _harmony.Patch(method, prefix: prefix, finalizer: finalizer);
                    _installed.Add(method); Ledger.SetAvailable(i, true, "");
                }
                catch (Exception error)
                {
                    // Patch may have registered metadata before detour generation failed. Remove only our owner.
                    try { _harmony.Unpatch(method, HarmonyPatchType.All, Owner); }
                    catch { if (!_installed.Contains(method)) _installed.Add(method); }
                    _slots.Remove(method); Ledger.SetAvailable(i, false, error.GetType().Name);
                }
            }
            AddCount(assembly, "Together.ChunkSleep", "Entries");
            AddCount(assembly, "Together.BagSleep", "Rows");
            AddCount(assembly, "KrokV5Optimization.SlowStamp", "Jobs");
            AddCount(assembly, "Together.WallholeZones", "Holes");
            AddCount(assembly, "KrokV5Optimization.BagPlace", "Waiting");
            AddCount(assembly, "KrokV5Optimization.BagPlace", "Announcing");
            AddCount(assembly, "KrokV5Optimization.FixInvSync", "Pending");
            AddCount(assembly, "KrokV5Optimization.FixInvSync", "Exhausted");
            int patched = 0;
            foreach (var method in _installed) if (_slots.ContainsKey(method)) patched++;
            _status = patched == TimingTargets.All.Length ? "active" : patched > 0 ? "partial" : "unavailable";
            if (patched > 0) Ledger.Start(now);
            _setupMs = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
        }
        private void AddCount(Assembly assembly, string type, string field)
        {
            var member = assembly?.GetType(type, false)?.GetField(field, Static);
            _counts.Add(new CountReader { Name = type + "." + field, Field = member,
                Count = member?.FieldType.GetProperty("Count", Instance) });
        }
        internal void Stop(double now)
        {
            // Disable recording before touching detours; in-flight finalizers become stale.
            Ledger.Stop(now);
            for (int i = _installed.Count - 1; i >= 0; i--)
            {
                try { _harmony.Unpatch(_installed[i], HarmonyPatchType.All, Owner); _installed.RemoveAt(i); }
                catch { }
            }
            _slots.Clear();
            _status = _installed.Count == 0 ? "disabled" : "unpatch_failed";
            for (int i = 0; i < TimingTargets.All.Length; i++) Ledger.SetAvailable(i, false, _status);
        }
        internal DeepWindow Capture(double now, bool enabled)
        {
            var window = Ledger.Drain(now);
            window.Enabled = enabled; window.Status = _status; window.Identity = _identity; window.SetupMs = _setupMs;
            window.OffThread = Interlocked.Exchange(ref _offThread, 0);
            window.Counts = new DeepCount[Active ? _counts.Count : 0];
            for (int i = 0; i < window.Counts.Length; i++) window.Counts[i] = _counts[i].Read();
            return window;
        }
        // Static/instance managed void methods only. No argument, result or exception rewriting.
        private static void Before(MethodBase __originalMethod, out CallStamp __state)
        {
            __state = default;
            try
            {
                var current = _current;
                if (current == null || !current.Active) return;
                if (Thread.CurrentThread.ManagedThreadId != current._mainThread)
                { Interlocked.Increment(ref current._offThread); return; }
                if (current._slots.TryGetValue(__originalMethod, out int slot))
                    __state = current.Ledger.Begin(slot, Stopwatch.GetTimestamp());
            }
            catch { }
        }
        private static void After(CallStamp __state, Exception __exception)
        {
            try { if (__state.Active) _current?.Ledger.End(__state, Stopwatch.GetTimestamp(), __exception != null); }
            catch { }
        }
    }
}
