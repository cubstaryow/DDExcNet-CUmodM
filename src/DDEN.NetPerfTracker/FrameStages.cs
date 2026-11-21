using System;
using System.Collections.Generic;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;

namespace DDEN.NetPerfTracker
{
    /// <summary>Optional exact counters, no PlayerLoop replacement or deep profiling. Values are not additive.</summary>
    internal sealed class FrameStages : IDisposable
    {
        private static readonly string[] Names = { "CPU Total Frame Time", "CPU Main Thread Frame Time", "CPU Render Thread Frame Time", "GPU Frame Time", "Gfx.WaitForPresentOnGfxThread", "WaitForTargetFPS", "PlayerLoop" };
        private readonly ProfilerRecorder[] _recorders = new ProfilerRecorder[7];
        private readonly double[] _sum = new double[7];
        private readonly float[] _peak = new float[7];
        private readonly int[] _counts = new int[7];
        internal readonly float[] Current = new float[7];
        internal int Reads { get; private set; }
        internal bool Started { get; private set; }
        internal void Start()
        {
            if (Started) return;
            Started = true; Reads = 0; ResetWindow();
            Array.Fill(Current, -1);
            var available = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(available);
            foreach (var h in available)
            {
                var d = ProfilerRecorderHandle.GetDescription(h);
                if (d.UnitType != ProfilerMarkerDataUnit.TimeNanoseconds) continue;
                int i = Array.IndexOf(Names, d.Name);
                if (i < 0 || _recorders[i].Valid) continue;
                _recorders[i] = new ProfilerRecorder(h, 1, ProfilerRecorderOptions.Default | ProfilerRecorderOptions.StartImmediately);
            }
        }
        internal void ReadFrame(bool focused)
        {
            if (!Started) return;
            Reads++;
            for (int i = 0; i < _recorders.Length; i++)
            {
                var r = _recorders[i]; Current[i] = -1;
                if (!focused && (i == 2 || i == 3)) continue; // Avoid diagnosing retained GPU/render values while rendering may be suspended.
                // FrameTiming counters can arrive several frames late. Zero CPU/GPU duration is not proof of support.
                if (Reads <= 8 || !r.Valid || r.Count == 0) continue;
                float ms = r.LastValue / 1000000f;
                if (!float.IsNaN(ms) && !float.IsInfinity(ms) && ms >= 0 && (i >= 4 || ms > 0))
                { Current[i] = ms; _sum[i] += ms; _peak[i] = Math.Max(_peak[i], ms); _counts[i]++; }
            }
        }
        internal StageTimings Snapshot() => new StageTimings { Enabled = Started, Warming = Started && Reads <= 8,
            Total = Value(0), Main = Value(1), Render = Value(2), Gpu = Value(3), Present = Value(4), Limit = Value(5), Loop = Value(6) };
        private TimingValue Value(int i) => _counts[i] == 0 ? TimingValue.Unknown : new TimingValue { Mean = (float)(_sum[i] / _counts[i]), Peak = _peak[i], Frames = _counts[i] };
        internal void ResetWindow() { Array.Clear(_sum, 0, 7); Array.Clear(_peak, 0, 7); Array.Clear(_counts, 0, 7); }
        public void Dispose()
        {
            for (int i = 0; i < _recorders.Length; i++) { if (_recorders[i].Valid) _recorders[i].Dispose(); _recorders[i] = default; }
            Started = false; Reads = 0; ResetWindow(); Array.Fill(Current, -1);
        }
    }
}
