using System;

namespace DDEN.NetPerfTracker
{
    internal sealed class SampleRing<T>
    {
        private T[] _data;
        private int _start;
        internal int Count { get; private set; }
        internal SampleRing(int capacity) { _data = new T[Math.Max(1, capacity)]; }
        internal T this[int index]
        {
            get
            {
                if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
                return _data[(_start + index) % _data.Length];
            }
        }
        internal void Add(T value)
        {
            if (Count < _data.Length) { _data[(_start + Count) % _data.Length] = value; Count++; }
            else { _data[_start] = value; _start = (_start + 1) % _data.Length; }
        }
        internal void SetCapacity(int capacity)
        {
            capacity = Math.Max(1, capacity);
            if (_data.Length == capacity) return;
            int keep = Math.Min(Count, capacity);
            var next = new T[capacity];
            for (int i = 0; i < keep; i++) next[i] = this[Count - keep + i];
            _data = next; Count = keep; _start = 0;
        }
        internal void Clear() { Array.Clear(_data, 0, _data.Length); _start = Count = 0; }
    }

    internal static class PerfMath
    {
        internal static float PerFrame(int steps, int frames) => frames > 0 ? (float)steps / frames : -1f;
        internal static float PerSecond(int count, double seconds) => seconds > 0 ? (float)(count / seconds) : -1f;
        internal static float ExpectedHz(float scale, float fixedDt) => fixedDt > 0 ? Math.Max(0f, scale) / fixedDt : -1f;
        internal static int AtOrBefore(float[] times, int count, float time)
        {
            int low = 0, high = count - 1, result = -1;
            while (low <= high)
            {
                int mid = low + (high - low) / 2;
                if (times[mid] <= time) { result = mid; low = mid + 1; } else high = mid - 1;
            }
            return result;
        }
    }

    internal struct ObjectCounts
    {
        internal float Started, Ended, WorkMs, MaxSliceMs;
        internal int Total, Active, Free, Simulated, Awake, Sleeping, Downward, NoSolid;
        internal bool CollisionChecked, Valid;
    }
    internal struct SceneCounts
    {
        internal float Started, Ended, WorkMs, MaxSliceMs;
        internal int Visited, Rigid, Dynamic, Awake, Sleeping, Kinematic, Colliders;
        internal bool Valid;
    }
    internal struct PerfSample
    {
        internal float T, Fps, MeanMs, WorstMs, Cpu, PhysicsMs, PhysicsPeakMs, SelfMs, SelfPeakMs;
        internal float StepsPerFrame, StepsPerSecond, ExpectedStepsPerSecond, Scale, FixedDt, HeapMb;
        internal float WindowSeconds, CensusBudgetMs, LastCensusSliceMs, SampleInterval, CensusInterval;
        internal float ReferenceFps, WarningFps, SpikeRatio, SpikeMinMs, SpikeCooldown;
        internal int SpikeCount;
        internal int Target, VSync, Players, GC0, GC1, GC2, LongFrames, Sync;
        internal int Frames, FixedSteps, PhysicsFrames;
        internal bool Detailed, Collision;
        internal bool Focused;
        internal byte Event;
        internal ObjectCounts Objects;
        internal SceneCounts Scene;
        internal StageTimings Stages;
        internal bool FluctuationAlerts;
        internal TogetherSnapshot Probe;
        internal DDEN.Diagnostics.DeepWindow Deep;
    }
    internal struct TimingValue
    {
        internal float Mean, Peak;
        internal int Frames;
        internal static TimingValue Unknown => new TimingValue { Mean = -1, Peak = -1 };
    }
    internal struct StageTimings
    {
        internal TimingValue Total, Main, Render, Gpu, Present, Limit, Loop, Physics2DLoop, ScriptFixed, ScriptUpdate, ScriptLate;
        internal bool Enabled, Warming;
    }
}
