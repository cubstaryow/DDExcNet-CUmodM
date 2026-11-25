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
}
