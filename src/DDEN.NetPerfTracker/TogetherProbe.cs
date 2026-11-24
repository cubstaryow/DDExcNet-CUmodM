using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx.Logging;
using DDEN.Shared;
using Newtonsoft.Json;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

namespace DDEN.NetPerfTracker
{
    // Passive observation only: no Harmony hooks, network packets or changes to Together's state.
    internal sealed class TogetherProbe : IDisposable
    {
        private sealed class Listener : ILogListener
        {
            private readonly ProbeInbox _inbox;
            private readonly float _origin;
            private readonly long _clock = Stopwatch.GetTimestamp();
            internal Listener(ProbeInbox inbox, float origin) { _inbox = inbox; _origin = origin; }
            public void LogEvent(object sender, LogEventArgs args)
            {
                try
                {
                    bool error = (args.Level & (LogLevel.Error | LogLevel.Fatal)) != 0;
                    bool warning = (args.Level & LogLevel.Warning) != 0;
                    _inbox.Add(args.Source?.SourceName, error, warning, args.Data,
                        _origin + (float)((Stopwatch.GetTimestamp() - _clock) / (double)Stopwatch.Frequency));
                }
                catch { /* A diagnostic listener must never recurse through the logger. */ }
            }
            public void Dispose() { }
        }

        private readonly ProbeInbox _inbox = new ProbeInbox();
        private readonly FrameTail _tail = new FrameTail();
        private readonly Dictionary<Type, PropertyInfo> _counts = new Dictionary<Type, PropertyInfo>();
        private Listener _listener;
        private Type _net;
        private PropertyInfo _managerProperty, _tickProperty;
        private FieldInfo _requests, _changed, _players, _chunkQueue, _playerDense, _entryValue;
        private MemberInfo _playerState, _ackTime;
        private object _lastManager;
        private int _lastTick = -1;
        private float _lastAt, _noTick;
        private bool _lastTickActive, _cpuUnsupported;
        private double _lastCpu = -1;
        private float _lastCpuAt;
        private readonly ProfilerRecorder[] _recorders = new ProfilerRecorder[2];
        private readonly double[] _sum = new double[2];
        private readonly float[] _peak = new float[2];
        private readonly int[] _reads = new int[2];
        private int _warmFrames;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        internal bool Active => _listener != null;
        internal string Identity => _net == null ? "unavailable" : _net.Assembly.GetName().Version + "/" + _net.Module.ModuleVersionId;

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetThreadTimes(IntPtr thread, out long created, out long exited, out long kernel, out long user);

        internal void Start(float t)
        {
            if (Active) return;
            _lastCpu = -1; _lastTick = -1; _lastManager = null; _lastTickActive = false; _noTick = 0;
            _tail.Clear(); _warmFrames = 0; ResetWindow();
            // Evidence survives a pause, but a new interval must not correlate old errors with new frames.
            _inbox.Drain();
            _inbox.Enable(true);
            var listener = new Listener(_inbox, t);
            try { BepInEx.Logging.Logger.Listeners.Add(listener); _listener = listener; }
            catch { _inbox.Enable(false); throw; }
            try
            {
                var available = new List<ProfilerRecorderHandle>();
                ProfilerRecorderHandle.GetAvailable(available);
                foreach (var handle in available)
                {
                    var info = ProfilerRecorderHandle.GetDescription(handle);
                    int index = info.Name == "WaitForJobGroupID" ? 0 : info.Name == "GC.Collect" ? 1 : -1;
                    if (index < 0 || info.UnitType != ProfilerMarkerDataUnit.TimeNanoseconds || _recorders[index].Valid) continue;
                    _recorders[index] = new ProfilerRecorder(handle, 1, ProfilerRecorderOptions.Default
                        | ProfilerRecorderOptions.StartImmediately | ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
                }
            }
            catch { StopCounters(); }
        }
        internal void Stop()
        {
            _inbox.Enable(false);
            if (_listener != null) BepInEx.Logging.Logger.Listeners.Remove(_listener);
            _listener = null; StopCounters(); _lastCpu = -1; _lastTick = -1;
        }
        private void StopCounters()
        {
            for (int i = 0; i < _recorders.Length; i++)
            { if (_recorders[i].Valid) _recorders[i].Dispose(); _recorders[i] = default; }
        }
        internal void Clear()
        {
            _inbox.Clear(); ResetContext();
        }
        internal void ResetContext()
        { _tail.Clear(); _lastTick = -1; _lastManager = null; _lastCpu = -1; _noTick = 0; _lastTickActive = false; ResetWindow(); }
        internal void Frame(float t, float ms)
        {
            if (!Active) return;
            _tail.Add(t, ms); _warmFrames++;
            if (_warmFrames <= 8) return;
            for (int i = 0; i < _recorders.Length; i++)
            {
                var recorder = _recorders[i];
                if (!recorder.Valid || recorder.Count == 0) continue;
                float value = recorder.LastValue / 1000000f;
                if (value < 0 || float.IsNaN(value) || float.IsInfinity(value)) continue;
                _sum[i] += value; _peak[i] = Math.Max(_peak[i], value); _reads[i]++;
            }
        }
        internal void ResetWindow()
        { Array.Clear(_sum, 0, _sum.Length); Array.Clear(_peak, 0, _peak.Length); Array.Clear(_reads, 0, _reads.Length); }
        private void Resolve()
        {
            if (_net != null) return;
            if (!Compat.PluginLoaded("CasualtiesMP")) return;
            _net = Reflect.Type("Together.Net");
            if (_net == null) return;
            _managerProperty = _net.GetProperty("ServerEntityManager", Static);
            var type = _managerProperty?.PropertyType;
            _tickProperty = type?.GetProperty("Tick", Instance);
            _requests = type?.GetField("_pendingClientRequests", Instance);
            _changed = type?.GetField("_changedEntities", Instance);
            _players = type?.GetField("_netPlayers", Instance);
            // SparseMap.GetByIndex returns ref T; Mono reflection cannot reliably invoke ref returns.
            // Read the dense storage without invoking any Together/LES game or networking method.
            _playerDense = _players?.FieldType.GetField("_dense", Instance);
            _entryValue = _playerDense?.FieldType.GetElementType()?.GetField("Value", Instance);
            var playerType = _entryValue?.FieldType;
            _playerState = (MemberInfo)playerType?.GetField("State", Instance) ?? playerType?.GetProperty("State", Instance);
            _ackTime = (MemberInfo)playerType?.GetField("ServerTickChangedTime", Instance) ?? playerType?.GetProperty("ServerTickChangedTime", Instance);
            _chunkQueue = Reflect.Type("Together.WorldChunkSync")?.GetField("server_chunksync_queue", Static);
        }
        private static object Read(MemberInfo member, object target) => member is FieldInfo field ? field.GetValue(target)
            : member is PropertyInfo property ? property.GetValue(target) : null;
        private int Count(object value)
        {
            if (value == null) return -1;
            if (value is ICollection collection) return collection.Count;
            var type = value.GetType();
            if (!_counts.TryGetValue(type, out var property))
            { property = type.GetProperty("Count", Instance); _counts[type] = property; }
            return property?.GetValue(value) is int count ? count : -1;
        }
        private float Cpu(float t)
        {
            if (_cpuUnsupported) return -1;
            try
            {
                if (Environment.OSVersion.Platform != PlatformID.Win32NT
                    || !GetThreadTimes(new IntPtr(-2), out _, out _, out long kernel, out long user))
                { _cpuUnsupported = true; return -1; }
                double cpu = (kernel + user) / 10000.0;
                float result = _lastCpu >= 0 && t > _lastCpuAt
                    ? (float)Math.Max(0, Math.Min(100, (cpu - _lastCpu) / ((t - _lastCpuAt) * 10))) : -1;
                _lastCpu = cpu; _lastCpuAt = t;
                return result;
            }
            catch { _cpuUnsupported = true; return -1; }
        }
        internal TogetherSnapshot Capture(float t)
        {
            var snapshot = new TogetherSnapshot
            {
                Enabled = Active, Running = Mp.IsRunning, Server = Mp.IsRunning && Mp.IsServer,
                Tick = -1, TickDelta = -1, Queue = -1, LesPlayers = -1, PendingBaseline = -1, AckAgeSeconds = -1,
                ChangedEntities = -1, QueuedChunks = -1, X = float.NaN, Y = float.NaN, Height = float.NaN,
                MainCpuPct = -1, LowOnePercent = -1, JobWaitMs = -1, JobWaitPeakMs = -1, GcMs = -1, GcPeakMs = -1,
                Status = Active ? "unavailable" : "off"
            };
            if (!Active) return snapshot;
            snapshot.Logs = _inbox.Drain(out snapshot.TotalLogs); snapshot.MainCpuPct = Cpu(t);
            snapshot.LowOnePercent = _tail.LowOnePercent(t, out snapshot.TailFrames);
            snapshot.JobWaitMs = _reads[0] > 0 ? (float)(_sum[0] / _reads[0]) : -1;
            snapshot.JobWaitPeakMs = _reads[0] > 0 ? _peak[0] : -1;
            snapshot.GcMs = _reads[1] > 0 ? (float)(_sum[1] / _reads[1]) : -1;
            snapshot.GcPeakMs = _reads[1] > 0 ? _peak[1] : -1;
            ResetWindow();
            try
            {
                var world = WorldGeneration.world;
                var body = PlayerCamera.main != null ? PlayerCamera.main.body : null;
                if (world != null && body != null && world.worldExists)
                {
                    var position = body.transform.position; snapshot.X = position.x; snapshot.Y = position.y;
                    snapshot.Height = world.WorldToBlockPos(position).y / (float)Math.Max(1, world.height - 1);
                }
                Resolve();
                if (!snapshot.Running) { snapshot.Status = "idle"; _lastManager = null; _lastTick = -1; _noTick = 0; return snapshot; }
                if (!snapshot.Server) { snapshot.Status = "client"; return snapshot; }
                object manager = _managerProperty?.GetValue(null);
                if (manager == null) return snapshot;
                snapshot.Status = "server";
                if (_tickProperty?.GetValue(manager) is ushort tick)
                {
                    snapshot.StateAvailable = true; snapshot.Tick = tick;
                    bool activeTick = world != null && world.worldExists && !world.generatingWorld && Time.timeScale > 0;
                    if (ReferenceEquals(manager, _lastManager) && _lastTick >= 0 && t >= _lastAt)
                    {
                        snapshot.TickDelta = (tick - _lastTick) & 0xffff;
                        _noTick = activeTick && _lastTickActive && snapshot.TickDelta == 0 ? _noTick + t - _lastAt : 0;
                    }
                    else _noTick = 0;
                    snapshot.NoTickSeconds = _noTick; _lastManager = manager; _lastTick = tick; _lastAt = t; _lastTickActive = activeTick;
                }
                snapshot.Queue = _requests == null ? -1 : Count(_requests.GetValue(manager));
                snapshot.ChangedEntities = _changed == null ? -1 : Count(_changed.GetValue(manager));
                snapshot.QueuedChunks = _chunkQueue == null ? -1 : Count(_chunkQueue.GetValue(null));
                var players = _players?.GetValue(manager);
                snapshot.LesPlayers = Count(players);
                var dense = players == null ? null : _playerDense?.GetValue(players) as Array;
                snapshot.CompletePeers = snapshot.LesPlayers >= 0 && snapshot.LesPlayers <= 32
                    && dense != null && dense.Length >= snapshot.LesPlayers && _entryValue != null && _playerState != null;
                if (dense == null || _entryValue == null || _playerState == null || snapshot.LesPlayers < 0) return snapshot;
                snapshot.PendingBaseline = 0;
                DateTime now = DateTime.UtcNow;
                for (int i = 0; i < Math.Min(32, Math.Min(snapshot.LesPlayers, dense.Length)); i++)
                {
                    object player = _entryValue.GetValue(dense.GetValue(i)); snapshot.InspectedPeers++;
                    if (Read(_playerState, player)?.ToString() == "RequestBaseline") snapshot.PendingBaseline++;
                    if (Read(_ackTime, player) is DateTime ack && ack != DateTime.MinValue)
                        snapshot.AckAgeSeconds = Math.Max(snapshot.AckAgeSeconds, (float)Math.Max(0, (now - ack).TotalSeconds));
                }
            }
            catch { snapshot.Status = "partial"; snapshot.CompletePeers = false; }
            return snapshot;
        }
        internal ProbeMessage[] Messages() => _inbox.Messages();
        internal void WriteEvidence(TextWriter writer)
        {
            var totals = _inbox.Totals;
            writer.WriteLine("# togetherProbe passive=1 patches=0 packetSend=0 intervalStateSampling=1 detailLimit=8/second firstErrorExtraSlot=1 detailHistory=96 detailChars=2048 low1WindowSeconds=10 low1BinMs=2 low1MinFrames=120 peerReadLimit=32");
            writer.WriteLine("# cpu=" + JsonConvert.SerializeObject(SystemInfo.processorType) + " logicalProcessors=" + Environment.ProcessorCount
                + " gpu=" + JsonConvert.SerializeObject(SystemInfo.graphicsDeviceName) + " unity=" + Application.unityVersion + " togetherVersionMvid=" + Identity);
            writer.WriteLine("# probeErrorsTotal=" + totals.Errors + " warningsTotal=" + totals.Warnings + " observedBaselineLogs=" + totals.BaselineLogs
                + " skippedDetailTotal=" + totals.DetailSkipped + "; log counts are observed messages, not unique causes; absent verbose baseline logs do not prove no resync");
            writer.WriteLine("# mainThreadCpuPct uses Windows thread CPU time / observed wall interval, not per-core utilization; it cannot distinguish Ready/Waiting. Job/GC markers are main-thread-only, may be unavailable or delayed. Low1 is a histogram estimate; normalizedHeight is unbounded.");
            foreach (var message in Messages())
                writer.WriteLine("PROBE_MESSAGE t=" + message.T.ToString("0.000", CultureInfo.InvariantCulture) + " error=" + (message.Error ? 1 : 0)
                    + " source=" + JsonConvert.SerializeObject(message.Source) + " text=" + JsonConvert.SerializeObject(message.Text));
        }
        public void Dispose() => Stop();
    }
}
