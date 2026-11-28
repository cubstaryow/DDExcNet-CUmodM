using System;

namespace DDEN.Diagnostics
{
    // JSON bridge v1. Both assemblies compile this data-only contract; neither references the other.
    internal sealed class DeepWindow
    {
        public int Schema { get; set; } = 1;
        public bool Enabled { get; set; }
        public bool Active { get; set; }
        public string Status { get; set; }
        public string Identity { get; set; }
        public int Frames { get; set; }
        public int Patched { get; set; }
        public int Expected { get; set; }
        public long OffThread { get; set; }
        public double Started { get; set; }
        public double Ended { get; set; }
        public double SetupMs { get; set; }
        public DeepRow[] Rows { get; set; } = Array.Empty<DeepRow>();
        public DeepCount[] Counts { get; set; } = Array.Empty<DeepCount>();
    }
    internal sealed class DeepRow
    {
        public string Name { get; set; }
        public string Group { get; set; }
        public string Note { get; set; }
        public bool Available { get; set; }
        public bool Inclusive { get; set; }
        public long Calls { get; set; }
        public long Errors { get; set; }
        public double TotalMs { get; set; } = -1;
        public double PerFrameMs { get; set; } = -1;
        public double MeanCallMs { get; set; } = -1;
        public double PeakCallMs { get; set; } = -1;
        public double PeakFrameMs { get; set; } = -1;
    }
    internal sealed class DeepCount
    {
        public string Name { get; set; }
        public int Value { get; set; } = -1;
    }
    internal sealed class DeepFrameTrace
    {
        public int Schema { get; set; } = 1;
        public string Scope { get; set; } = "Main-thread PlayerLoop wall time; method/stage times overlap. Observer commit is outside LoopMs. Recent and spike-context frames overlap; do not concatenate for percentiles. Histogram excludes warmup and manual-export frames, thresholds 16.666667/33.333333ms. GC values are process collection counts, not GC duration. PhysicsMs is the 2D fixed stage, not solver-only CPU time.";
        public bool Active { get; set; }
        public string Status { get; set; }
        public string[] Targets { get; set; }
        public DeepFrame[] Recent { get; set; }
        public DeepFrame[] SpikeContext { get; set; }
        public long MeasuredFrames { get; set; }
        public long Over16Ms { get; set; }
        public long Over33Ms { get; set; }
        public double P95UpperMs { get; set; } = -1;
        public double P99UpperMs { get; set; } = -1;
        public double HistogramResolutionMs { get; set; } = .1;
    }
    internal sealed class DeepFrame
    {
        public int Frame { get; set; }
        public double T { get; set; }
        public double LoopMs { get; set; }
        public bool Warmup { get; set; }
        public bool ManualExport { get; set; }
        public int Role { get; set; } // 0=offline, 1=server, 2=client, -1=unknown
        public int FixedSteps { get; set; }
        public int GC0 { get; set; }
        public int GC1 { get; set; }
        public int GC2 { get; set; }
        public double ScriptUpdateMs { get; set; } = -1;
        public double ScriptFixedMs { get; set; } = -1;
        public double ScriptLateMs { get; set; } = -1;
        public double PhysicsMs { get; set; } = -1;
        public long[] Calls { get; set; }
        public double[] MethodMs { get; set; }
        public double[] PeakCallMs { get; set; }
    }
}
