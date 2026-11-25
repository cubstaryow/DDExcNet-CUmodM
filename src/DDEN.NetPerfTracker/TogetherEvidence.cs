using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace DDEN.NetPerfTracker
{
    internal sealed class ProbeMessage
    {
        internal float T;
        internal string Source, Text;
        internal bool Error;
    }

    internal struct ProbeLogCounts
    {
        internal long Errors, Warnings, BaselineLogs, DetailSkipped, UnityErrors;
    }

    // Background log callbacks never access Unity, reflect into Together or emit another log.
    internal sealed class ProbeInbox
    {
        private readonly object _gate = new object();
        private readonly SampleRing<ProbeMessage> _messages = new SampleRing<ProbeMessage>(96);
        private ProbeLogCounts _window, _total;
        private long _second = -1;
        private int _details;
        private bool _enabled;
        private ProbeMessage _firstError;
        internal void Enable(bool value) { lock (_gate) _enabled = value; }
        internal void Clear()
        {
            lock (_gate) { _messages.Clear(); _window = _total = default; _second = -1; _details = 0; _firstError = null; }
        }
        private static bool Has(string text, string value) => text != null
            && text.IndexOf(value, 0, Math.Min(4096, text.Length), StringComparison.OrdinalIgnoreCase) >= 0;
        private static bool Related(string text) => Has(text, "Together.") || Has(text, "KrokV5Optimization.")
            || Has(text, "LiteEntitySystem") || Has(text, "Net.DoUpdate:") || Has(text, "CpuOptimizationAdv.");
        private static bool BepMirror(string text)
        {
            if (string.IsNullOrEmpty(text) || text[0] != '[') return false;
            int colon = text.IndexOf(':', 0, Math.Min(14, text.Length));
            int close = text.IndexOf(']', 0, Math.Min(100, text.Length));
            if (colon < 0 || close < colon) return false;
            return text.StartsWith("[Error", StringComparison.Ordinal) || text.StartsWith("[Fatal", StringComparison.Ordinal)
                || text.StartsWith("[Warning", StringComparison.Ordinal) || text.StartsWith("[Info", StringComparison.Ordinal)
                || text.StartsWith("[Message", StringComparison.Ordinal) || text.StartsWith("[Debug", StringComparison.Ordinal);
        }
        internal void Add(string source, bool error, bool warning, object data, float t)
        {
            string text = data as string;
            bool relevant = string.Equals(source, "Together", StringComparison.OrdinalIgnoreCase)
                || string.Equals(source, "LES", StringComparison.OrdinalIgnoreCase)
                || string.Equals(source, "CasualtiesMP", StringComparison.OrdinalIgnoreCase);
            if (!relevant && !(error && Related(text))) return;
            bool baseline = !error && !warning && (Has(text, "Request baseline") || Has(text, "[SEM] SendWorld") || Has(text, "full baseline player"));
            if (!error && !warning && !baseline) return;
            AddCore(source, error, warning, baseline, data, null, t, false, true);
        }
        internal void AddUnity(string message, string stack, float t)
        {
            // BepInEx mirrors carry their rendered source prefix and are already counted by the Bep listener.
            if (BepMirror(message)) return;
            AddCore("Unity Log", true, false, false, message ?? "", stack ?? "", t, true, Related(message) || Related(stack));
        }
        private void AddCore(string source, bool error, bool warning, bool baseline, object data, string stack, float t, bool unity, bool attributed)
        {
            if (float.IsNaN(t) || float.IsInfinity(t) || t < 0) return;
            lock (_gate)
            {
                if (!_enabled) return;
                if (unity) { _window.UnityErrors++; _total.UnityErrors++; }
                if (error && attributed) { _window.Errors++; _total.Errors++; }
                else if (warning) { _window.Warnings++; _total.Warnings++; }
                if (baseline) { _window.BaselineLogs++; _total.BaselineLogs++; }
                long second = (long)t;
                // Delayed callbacks must not reopen a previous second's detail allowance.
                if (second > _second) { _second = second; _details = 0; }
                if (_details >= 8 && (!error || _firstError != null)) { _window.DetailSkipped++; _total.DetailSkipped++; return; }
                _details++;
                string text = data as string;
                // Do not invoke arbitrary user-defined ToString methods from a logger callback.
                if (text == null) text = data is Exception exception ? exception.GetType().FullName + ": " + exception.Message + "\n" + exception.StackTrace
                    : data == null ? "(null)" : data.GetType().FullName;
                bool truncated = text.Length > 2048;
                if (truncated) text = text.Substring(0, 2048);
                if (stack != null)
                {
                    int room = Math.Max(0, 2048 - text.Length - 1);
                    truncated |= stack.Length > room;
                    if (room > 0 && stack.Length > 0) text += "\n" + stack.Substring(0, Math.Min(room, stack.Length));
                }
                if (truncated) text += " [truncated]";
                if (source != null && source.Length > 80) source = source.Substring(0, 80);
                var message = new ProbeMessage { T = t, Source = source ?? "", Text = text, Error = error };
                if (error && _firstError == null) _firstError = message;
                _messages.Add(message);
            }
        }
        internal ProbeLogCounts Drain() => Drain(out _);
        internal ProbeLogCounts Drain(out ProbeLogCounts totals)
        { lock (_gate) { var value = _window; _window = default; totals = _total; return value; } }
        internal ProbeLogCounts Totals { get { lock (_gate) return _total; } }
        internal ProbeMessage[] Messages()
        {
            lock (_gate)
            {
                bool retained = false;
                for (int i = 0; i < _messages.Count; i++) if (ReferenceEquals(_messages[i], _firstError)) retained = true;
                int extra = _firstError != null && !retained ? 1 : 0;
                var result = new ProbeMessage[_messages.Count + extra];
                if (extra != 0) result[0] = _firstError;
                for (int i = 0; i < _messages.Count; i++) result[i + extra] = _messages[i];
                return result;
            }
        }
    }

    // Fixed-size histogram: no per-frame allocations or sorting. Slowest 1% mean uses 2ms buckets.
    internal sealed class FrameTail
    {
        private const int Seconds = 10, Bins = 256;
        private readonly int[,] _counts = new int[Seconds, Bins];
        private readonly double[,] _sums = new double[Seconds, Bins];
        private readonly int[] _epochs = new int[Seconds];
        internal FrameTail() => Clear();
        internal void Clear() { Array.Clear(_counts, 0, _counts.Length); Array.Clear(_sums, 0, _sums.Length); Array.Fill(_epochs, -1); }
        internal void Add(float t, float ms)
        {
            if (!Finite(t) || !Finite(ms) || t < 0 || t > int.MaxValue - 1 || ms <= 0) return;
            int second = (int)t, slot = second % Seconds;
            if (_epochs[slot] != second)
            {
                for (int i = 0; i < Bins; i++) { _counts[slot, i] = 0; _sums[slot, i] = 0; }
                _epochs[slot] = second;
            }
            int bin = ms >= (Bins - 1) * 2 ? Bins - 1 : (int)(ms * 0.5f);
            _counts[slot, bin]++; _sums[slot, bin] += ms;
        }
        internal float LowOnePercent(float t, out int frames)
        {
            frames = 0;
            if (!Finite(t) || t < 0 || t > int.MaxValue - 1) return -1;
            int second = (int)t;
            for (int slot = 0; slot < Seconds; slot++)
                if (_epochs[slot] <= second && _epochs[slot] > second - Seconds)
                    for (int bin = 0; bin < Bins; bin++) frames += _counts[slot, bin];
            if (frames < 120) return -1;
            int left = Math.Max(1, (int)Math.Ceiling(frames * 0.01)), needed = left;
            double sum = 0;
            for (int bin = Bins - 1; bin >= 0 && left > 0; bin--)
            {
                int count = 0; double binSum = 0;
                for (int slot = 0; slot < Seconds; slot++)
                    if (_epochs[slot] <= second && _epochs[slot] > second - Seconds)
                    { count += _counts[slot, bin]; binSum += _sums[slot, bin]; }
                if (count == 0) continue;
                int take = Math.Min(left, count); sum += binSum * take / count; left -= take;
            }
            return sum > 0 ? (float)(1000 * needed / sum) : -1;
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    internal struct TogetherSnapshot
    {
        internal bool Enabled, Running, Server, StateAvailable, CompletePeers;
        internal int Tick, TickDelta, Queue, LesPlayers, InspectedPeers, PendingBaseline;
        internal int ChangedEntities, QueuedChunks, TailFrames;
        internal float AckAgeSeconds, NoTickSeconds, MainCpuPct, LowOnePercent;
        internal float JobWaitMs, JobWaitPeakMs, GcMs, GcPeakMs, X, Y, Height;
        internal ProbeLogCounts Logs, TotalLogs;
        internal string Status;
        internal string Describe()
        {
            var c = CultureInfo.InvariantCulture;
            return " probe=" + (Enabled ? 1 : 0) + " probeStatus=" + (Status ?? "unavailable") + " mpRunning=" + (Running ? 1 : 0) + " mpServer=" + (Server ? 1 : 0)
                + " lesStateAvailable=" + (StateAvailable ? 1 : 0) + " lesTick=" + Tick + " lesTickDelta=" + TickDelta
                + " lesNoProgressSeconds=" + NoTickSeconds.ToString("0.000", c) + " lesPlayers=" + LesPlayers
                + " inspectedPeers=" + InspectedPeers + " completePeers=" + (CompletePeers ? 1 : 0)
                + " pendingBaseline=" + PendingBaseline + " maxAckAgeSeconds=" + AckAgeSeconds.ToString("0.000", c)
                + " queuedRequests=" + Queue + " changedEntities=" + ChangedEntities + " queuedChunks=" + QueuedChunks
                + " mainThreadCpuPct=" + MainCpuPct.ToString("0.00", c) + " low1EstimateFps=" + LowOnePercent.ToString("0.00", c)
                + " low1Frames=" + TailFrames + " jobWaitAvgMs=" + JobWaitMs.ToString("0.000", c) + " jobWaitPeakMs=" + JobWaitPeakMs.ToString("0.000", c)
                + " gcMarkerAvgMs=" + GcMs.ToString("0.000", c) + " gcMarkerPeakMs=" + GcPeakMs.ToString("0.000", c)
                + " mpErrors=" + Logs.Errors + " mpWarnings=" + Logs.Warnings + " baselineLogs=" + Logs.BaselineLogs
                + " mpErrorsTotal=" + TotalLogs.Errors + " mpWarningsTotal=" + TotalLogs.Warnings + " baselineLogsTotal=" + TotalLogs.BaselineLogs
                + " unityErrors=" + Logs.UnityErrors + " unityErrorsTotal=" + TotalLogs.UnityErrors
                + " detailSkipped=" + Logs.DetailSkipped + " playerX=" + X.ToString("0.00", c) + " playerY=" + Y.ToString("0.00", c)
                + " normalizedHeight=" + Height.ToString("0.000", c);
        }
    }
}
