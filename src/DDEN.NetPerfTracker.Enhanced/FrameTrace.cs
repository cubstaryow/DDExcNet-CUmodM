using System;
using DDEN.Diagnostics;

namespace DDEN.NetPerfTracker.Enhanced
{
    internal struct FrameMeta
    {
        internal int Frame, Role, FixedSteps, GC0, GC1, GC2;
        internal bool Warmup, ManualExport;
        internal double T, LoopMs, ScriptUpdateMs, ScriptFixedMs, ScriptLateMs, PhysicsMs;
    }
    // Fixed storage: no per-frame object/array allocations and no writes outside manual export.
    internal sealed class FrameTrace
    {
        private sealed class Slot
        {
            internal FrameMeta Meta;
            internal readonly long[] Calls, Ticks, Peaks;
            internal Slot(int targets) { Calls = new long[targets]; Ticks = new long[targets]; Peaks = new long[targets]; }
        }
        private readonly Slot[] _recent = new Slot[256], _spikes = new Slot[128];
        private readonly long[] _histogram = new long[10001];
        private readonly double _tickMs;
        private int _recentNext, _recentCount, _spikeNext, _spikeCount, _after;
        private int _lastContextFrame = -1;
        private long _frames, _over16, _over33;
        internal FrameTrace(long frequency)
        {
            _tickMs = 1000.0 / frequency;
            for (int i = 0; i < _recent.Length; i++) _recent[i] = new Slot(TimingTargets.All.Length);
            for (int i = 0; i < _spikes.Length; i++) _spikes[i] = new Slot(TimingTargets.All.Length);
        }
        internal void Clear()
        {
            _recentNext = _recentCount = _spikeNext = _spikeCount = _after = 0; _lastContextFrame = -1;
            _frames = _over16 = _over33 = 0; Array.Clear(_histogram, 0, _histogram.Length);
        }
        internal void Begin(FrameMeta meta) { _recent[_recentNext].Meta = meta; }
        internal void Method(int index, long calls, long ticks, long peak)
        {
            var slot = _recent[_recentNext]; slot.Calls[index] = calls; slot.Ticks[index] = ticks; slot.Peaks[index] = peak;
        }
        internal void Commit()
        {
            var current = _recent[_recentNext]; var meta = current.Meta;
            bool spike = !meta.Warmup && !meta.ManualExport && meta.LoopMs > 33.333333;
            if (spike && _after == 0)
            {
                int count = Math.Min(4, _recentCount);
                for (int i = count; i > 0; i--) CopyContext(_recent[(_recentNext - i + _recent.Length) % _recent.Length]);
            }
            if (spike || _after > 0) { CopyContext(current); _after = spike ? 4 : _after - 1; }
            if (!meta.Warmup && !meta.ManualExport && meta.LoopMs >= 0)
            {
                _frames++;
                if (meta.LoopMs > 16.666667) _over16++;
                if (meta.LoopMs > 33.333333) _over33++;
                _histogram[Math.Min(10000, (int)Math.Min(10000, Math.Ceiling(meta.LoopMs * 10)))]++;
            }
            _recentNext = (_recentNext + 1) % _recent.Length; _recentCount = Math.Min(_recent.Length, _recentCount + 1);
        }
        private void CopyContext(Slot source)
        {
            if (source.Meta.Frame <= _lastContextFrame) return;
            var target = _spikes[_spikeNext]; target.Meta = source.Meta;
            Array.Copy(source.Calls, target.Calls, source.Calls.Length); Array.Copy(source.Ticks, target.Ticks, source.Ticks.Length); Array.Copy(source.Peaks, target.Peaks, source.Peaks.Length);
            _lastContextFrame = source.Meta.Frame; _spikeNext = (_spikeNext + 1) % _spikes.Length; _spikeCount = Math.Min(_spikes.Length, _spikeCount + 1);
        }
        private double Percentile(double quantile)
        {
            if (_frames == 0) return -1;
            long wanted = (long)Math.Ceiling(_frames * quantile), sum = 0;
            for (int i = 0; i < _histogram.Length; i++) { sum += _histogram[i]; if (sum >= wanted) return i == 10000 ? -1 : i * .1; }
            return -1;
        }
        internal DeepFrameTrace Capture(bool active, string status, bool[] available)
        {
            var names = new string[TimingTargets.All.Length]; for (int i = 0; i < names.Length; i++) names[i] = TimingTargets.All[i].Name;
            return new DeepFrameTrace { Active = active, Status = status, Targets = names,
                Recent = Export(_recent, _recentNext, _recentCount, available), SpikeContext = Export(_spikes, _spikeNext, _spikeCount, available),
                MeasuredFrames = _frames, Over16Ms = _over16, Over33Ms = _over33, P95UpperMs = Percentile(.95), P99UpperMs = Percentile(.99) };
        }
        private DeepFrame[] Export(Slot[] source, int next, int count, bool[] available)
        {
            var result = new DeepFrame[count];
            for (int j = 0; j < count; j++)
            {
                var slot = source[(next - count + j + source.Length) % source.Length]; var m = slot.Meta;
                var frame = new DeepFrame { Frame = m.Frame, T = m.T, LoopMs = m.LoopMs, Warmup = m.Warmup, ManualExport = m.ManualExport, Role = m.Role, FixedSteps = m.FixedSteps,
                    GC0 = m.GC0, GC1 = m.GC1, GC2 = m.GC2, ScriptUpdateMs = m.ScriptUpdateMs, ScriptFixedMs = m.ScriptFixedMs, ScriptLateMs = m.ScriptLateMs, PhysicsMs = m.PhysicsMs,
                    Calls = (long[])slot.Calls.Clone(), MethodMs = new double[available.Length], PeakCallMs = new double[available.Length] };
                for (int i = 0; i < available.Length; i++)
                {
                    // Negative sentinels were stored at capture time; later unpatching must not rewrite history.
                    frame.MethodMs[i] = slot.Ticks[i] < 0 ? -1 : slot.Ticks[i] * _tickMs;
                    frame.PeakCallMs[i] = slot.Peaks[i] < 0 ? -1 : slot.Peaks[i] * _tickMs;
                }
                result[j] = frame;
            }
            return result;
        }
    }
}
