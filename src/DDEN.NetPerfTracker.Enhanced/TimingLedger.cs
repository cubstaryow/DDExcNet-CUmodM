using System;
using DDEN.Diagnostics;

namespace DDEN.NetPerfTracker.Enhanced
{
    internal readonly struct TargetSpec
    {
        internal readonly string Type, Method, Group, Assembly;
        internal readonly bool Instance;
        internal readonly bool Inclusive;
        internal readonly Type[] Parameters;
        internal TargetSpec(string type, string method, string group, bool inclusive = false, string assembly = "CasualtiesMP", bool instance = false, Type[] parameters = null)
        { Type = type; Method = method; Group = group; Inclusive = inclusive; Assembly = assembly; Instance = instance; Parameters = parameters ?? System.Type.EmptyTypes; }
        internal string Name => Type + "." + Method;
    }
    internal static class TimingTargets
    {
        internal static readonly TargetSpec[] All =
        {
            new TargetSpec("Together.ChunkSleep", "Tick", "chunks"),
            new TargetSpec("Together.BagSleep", "Tick", "chunks"),
            new TargetSpec("Together.HeaterMultiplayerRewrite", "Tick", "chunks"),
            new TargetSpec("Together.BagSleep", "WriteHost", "fixed"),
            new TargetSpec("Together.FallCatch", "Tick", "fixed"),
            new TargetSpec("KrokV5Optimization.SafetyNet", "Tick", "opt"),
            new TargetSpec("KrokV5Optimization.SlowStamp", "Tick", "opt"),
            new TargetSpec("Together.BodyViewParticles", "Tick", "opt"),
            new TargetSpec("Together.WallholeZones", "Tick", "opt"),
            new TargetSpec("KrokV5Optimization.BagPlace", "Tick", "opt"),
            new TargetSpec("KrokV5Optimization.FixInvSync", "Tick", "opt"),
            new TargetSpec("KrokV5Optimization.OptimizerMain", "Tick", "opt", true),
            new TargetSpec("KrokV5Optimization.FixInvSync", "Sweep", "inventory", true),
            new TargetSpec("Together.Net", "DoUpdate", "network", true),
            new TargetSpec("Together.TransportSteamworks", "Update", "network", true, instance: true),
            new TargetSpec("Together.TransportSteamworks", "_Update_PollMessages", "network", true, instance: true),
            new TargetSpec("Together.TransportLiteNetLib", "Together.NetTransportBase.Update", "network", true, instance: true),
            new TargetSpec("LiteEntitySystem.EntityManager", "Update", "les", true, "LiteEntitySystem", true),
            new TargetSpec("LiteEntitySystem.ServerEntityManager", "OnLogicTick", "les", true, "LiteEntitySystem", true),
            new TargetSpec("LiteEntitySystem.ClientEntityManager", "Update", "les", true, "LiteEntitySystem", true),
            new TargetSpec("LiteEntitySystem.ClientEntityManager", "OnLogicTick", "les", true, "LiteEntitySystem", true),
            new TargetSpec("Together.SyncInfo", "Update", "sync", true, instance: true),
            new TargetSpec("Together.SyncInfo", "WriteObjectIntoPacket", "sync", true, instance: true),
            new TargetSpec("Together.SyncInfo", "ApplyPacketToObject", "sync", true, instance: true),
            new TargetSpec("Together.Plugin", "Update", "plugin", true, instance: true),
            new TargetSpec("Together.Plugin", "FixedUpdate", "plugin", true, instance: true),
            new TargetSpec("Together.Plugin", "LateUpdate", "plugin", true, instance: true),
            new TargetSpec("Together.ServerMain", "Update", "server", true, instance: true),
            new TargetSpec("Together.ServerMain", "LateUpdate", "server", true, instance: true),
            new TargetSpec("Item", "Update", "objects", true, "Assembly-CSharp", true),
            new TargetSpec("Item", "HandleDecay", "objects", false, "Assembly-CSharp", true),
            new TargetSpec("Body", "FixedUpdate", "objects", true, "Assembly-CSharp", true),
            new TargetSpec("Body", "Update", "objects", true, "Assembly-CSharp", true),
            new TargetSpec("Body", "HandlePhysics", "objects", false, "Assembly-CSharp", true),
            new TargetSpec("CpuOptimizationAdv.CullWorld", "Tick", "culling", true, "CpuOptimizationAdv"),
            new TargetSpec("Together.SyncInfoGameObjectTracker", "FixedUpdate", "objectSync", true, instance: true),
            new TargetSpec("Together.SyncInfoGameObjectTracker", "TickSync", "objectSync", true, instance: true),
            new TargetSpec("LiquidAffect", "FixedUpdate", "objects", false, "Assembly-CSharp", true),
            new TargetSpec("IKHandle", "Update", "objects", true, "Assembly-CSharp", true),
            new TargetSpec("IKHandle", "UpdateIK", "objects", false, "Assembly-CSharp", true),
            new TargetSpec("Item", "Awake", "lifecycle", true, "Assembly-CSharp", true),
            new TargetSpec("Item", "OnDestroy", "lifecycle", true, "Assembly-CSharp", true),
            new TargetSpec("Together.SyncInfo", "DestroyTrackedGameObject", "lifecycle", true, instance: true),
            new TargetSpec("Wearable", "Awake", "lifecycle", true, "Assembly-CSharp", true),
            new TargetSpec("Together.SyncInfoGameObjectTracker", "FlushFrameSamples", "objectSync", true),
            new TargetSpec("Together.PlayerCheckpoint", "Tick", "checkpoint", true),
            new TargetSpec("Together.FindTheBugReports", "Tick", "diagnostics", true),
            new TargetSpec("Together.ChunkSleep", "EnsureRenderers", "visibility", true),
            new TargetSpec("Together.ChunkSleep", "WakeFreshChunks", "wake", true),
            new TargetSpec("Together.SyncInfoGameObjectTracker", "UpdateDistanceChecks", "distance", true, instance: true),
            new TargetSpec("Together.NetObjectRegistry", "Update", "registry", true, instance: true),
            new TargetSpec("Together.NetObjectRegistry", "_ObjectSyncUpdateLoopUniversal", "registry", true, parameters: new[] { typeof(bool) }),
            new TargetSpec("Together.LayerBuildingStream", "HostRegister", "registry", true)
        };
    }
    internal struct CallStamp
    {
        internal long Started;
        internal int Slot, Generation;
        internal bool Active;
    }
    // Main-thread only. Calls accumulate in the current frame; only completed frames enter a window.
    internal sealed class TimingLedger
    {
        private struct Accumulator { internal long Calls, Errors, Ticks, PeakCall, PeakFrame; }
        private readonly Accumulator[] _frame, _window;
        private readonly bool[] _available;
        private readonly string[] _notes;
        private readonly double _tickMs;
        private int _generation, _frames;
        private bool _renderOpen;
        private double _started, _ended;
        internal readonly FrameTrace Trace;
        internal bool Active { get; private set; }
        internal TimingLedger(long frequency)
        {
            if (frequency <= 0) throw new ArgumentOutOfRangeException(nameof(frequency));
            _tickMs = 1000.0 / frequency;
            Trace = new FrameTrace(frequency);
            int count = TimingTargets.All.Length;
            _frame = new Accumulator[count]; _window = new Accumulator[count];
            _available = new bool[count]; _notes = new string[count];
        }
        internal void SetAvailable(int slot, bool value, string note)
        { _available[slot] = value; _notes[slot] = note; }
        internal void Start(double now)
        { _generation++; Active = true; Clear(now); }
        internal void Stop(double now)
        { _generation++; Active = false; _renderOpen = false; Array.Clear(_frame, 0, _frame.Length); ResetWindow(now); }
        internal void Clear(double now)
        { _generation++; _renderOpen = false; Array.Clear(_frame, 0, _frame.Length); ResetWindow(now); Trace.Clear(); }
        internal void BeginRenderedFrame()
        { if (Active) { _generation++; _renderOpen = true; Array.Clear(_frame, 0, _frame.Length); } }
        private void ResetWindow(double now)
        { Array.Clear(_window, 0, _window.Length); _frames = 0; _started = _ended = now; }
        internal CallStamp Begin(int slot, long now) => new CallStamp
        { Slot = slot, Started = now, Generation = _generation, Active = Active && _available[slot] };
        internal void End(CallStamp stamp, long now, bool error)
        {
            if (!Active || !stamp.Active || stamp.Generation != _generation || now < stamp.Started) return;
            ref var frame = ref _frame[stamp.Slot];
            long ticks = now - stamp.Started;
            frame.Calls++; if (error) frame.Errors++; frame.Ticks += ticks;
            if (ticks > frame.PeakCall) frame.PeakCall = ticks;
        }
        internal void CompleteFrame(double now)
        { Complete(now, default, false); }
        internal void CompleteRenderedFrame(FrameMeta meta)
        { if (_renderOpen) { _renderOpen = false; Complete(meta.T, meta, true); } }
        private void Complete(double now, FrameMeta meta, bool trace)
        {
            if (!Active) return;
            if (trace) Trace.Begin(meta);
            for (int i = 0; i < _frame.Length; i++)
            {
                ref var frame = ref _frame[i]; ref var window = ref _window[i];
                window.Calls += frame.Calls; window.Errors += frame.Errors; window.Ticks += frame.Ticks;
                window.PeakCall = Math.Max(window.PeakCall, frame.PeakCall);
                window.PeakFrame = Math.Max(window.PeakFrame, frame.Ticks);
                if (trace) Trace.Method(i, frame.Calls, _available[i] ? frame.Ticks : -1, _available[i] && frame.Calls > 0 ? frame.PeakCall : -1);
                frame = default;
            }
            _frames++; _ended = now;
            if (trace) Trace.Commit();
        }
        internal DeepFrameTrace CaptureFrames(string status) => Trace.Capture(Active, status, _available);
        internal DeepWindow Drain(double now)
        {
            var snapshot = new DeepWindow { Active = Active, Frames = _frames, Started = _started, Ended = _ended,
                Expected = _available.Length, Rows = new DeepRow[_available.Length] };
            for (int i = 0; i < _available.Length; i++)
            {
                var spec = TimingTargets.All[i]; var a = _window[i]; bool valid = _available[i] && _frames > 0;
                snapshot.Rows[i] = new DeepRow { Name = spec.Name, Group = spec.Group, Inclusive = spec.Inclusive,
                    Available = _available[i], Note = _notes[i], Calls = a.Calls, Errors = a.Errors,
                    TotalMs = valid ? a.Ticks * _tickMs : -1,
                    PerFrameMs = valid ? a.Ticks * _tickMs / _frames : -1,
                    MeanCallMs = valid && a.Calls > 0 ? a.Ticks * _tickMs / a.Calls : -1,
                    PeakCallMs = valid && a.Calls > 0 ? a.PeakCall * _tickMs : -1,
                    PeakFrameMs = valid ? a.PeakFrame * _tickMs : -1 };
                if (_available[i]) snapshot.Patched++;
            }
            // The unfinished frame stays in _frame and belongs to the next completed-frame window.
            ResetWindow(_ended);
            return snapshot;
        }
    }
}
