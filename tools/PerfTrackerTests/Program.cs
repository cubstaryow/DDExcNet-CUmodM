using System;
using System.Collections.Generic;
using DDEN.NetPerfTracker;

internal static class Program
{
    private static int _checks;
    private static void Check(bool condition, string description)
    { if (!condition) throw new Exception(description); _checks++; }

    private static void Main()
    {
        var ring = new SampleRing<int>(7);
        var expected = new List<int>();
        int capacity = 7;
        for (int value = 0; value < 10000; value++)
        {
            if (value % 113 == 0)
            {
                capacity = value % 31 + 1; ring.SetCapacity(capacity);
                if (expected.Count > capacity) expected.RemoveRange(0, expected.Count - capacity);
            }
            ring.Add(value); expected.Add(value);
            if (expected.Count > capacity) expected.RemoveAt(0);
            Check(ring.Count == expected.Count, "bounded count");
            for (int i = 0; i < expected.Count; i++) Check(ring[i] == expected[i], "resize/wrap preserves chronological newest records");
        }
        ring.Clear(); Check(ring.Count == 0, "clear"); ring.Add(42); Check(ring[0] == 42, "restart after clear");
        bool invalid = false;
        try { _ = ring[1]; } catch (ArgumentOutOfRangeException) { invalid = true; }
        Check(invalid, "unused capacity is not a record");
        ring.SetCapacity(0); ring.Add(43); Check(ring.Count == 1 && ring[0] == 43, "capacity minimum");

        float[] times = { 1, 2.5f, 3, 100, 101.2f, 0, 0 };
        Check(PerfMath.AtOrBefore(times, 5, 0) == -1, "no invented data before first record");
        Check(PerfMath.AtOrBefore(times, 5, 3) == 2, "exact irregular time");
        Check(PerfMath.AtOrBefore(times, 5, 99) == 2, "pause gap uses recorded timestamp");
        Check(PerfMath.AtOrBefore(times, 5, 100) == 3, "resume timestamp");
        Check(PerfMath.AtOrBefore(times, 5, 1000) == 4, "ignore spare capacity");
        Check(PerfMath.AtOrBefore(times, 0, 1000) == -1, "empty history");
        Check(Math.Abs(PerfMath.PerFrame(50, 60) - 0.833333f) < 0.0001f, "50 Hz at 60 fps is 0.83 steps/frame");
        Check(Math.Abs(PerfMath.PerFrame(372, 100) - 3.72f) < 0.0001f, "lower render rate raises scheduling ratio");
        Check(Math.Abs(PerfMath.PerSecond(372, 7.44) - 50) < 0.0001f, "3.72 steps/frame can still be ordinary 50 Hz");
        Check(PerfMath.PerFrame(0, 0) == -1 && PerfMath.PerSecond(0, 0) == -1, "missing denominator stays unknown");
        Check(PerfMath.ExpectedHz(1, 0.02f) == 50, "normal scheduling");
        Check(PerfMath.ExpectedHz(0.5f, 0.02f) == 25, "time scale affects expected wall-clock Hz");
        Check(PerfMath.ExpectedHz(0, 0.02f) == 0, "paused physics");
        Check(PerfMath.ExpectedHz(1, 0) == -1, "invalid fixed step stays unknown");

        var samples = new SampleRing<PerfSample>(2);
        var point = new PerfSample { T = 1, Target = 60, Scale = 1, PhysicsMs = -1,
            Objects = new ObjectCounts { Valid = true, Total = 100, Started = 0.5f, Ended = 0.9f, WorkMs = 2, MaxSliceMs = 0.4f } };
        samples.Add(point);
        point.Target = 144; point.Scale = 0; point.Objects.Total = 200; point.Objects.WorkMs = 20; point.PhysicsMs = 0;
        samples.Add(point);
        Check(samples[0].Target == 60 && samples[0].Scale == 1, "historical settings are not replaced by current values");
        Check(samples[0].Objects.Total == 100 && samples[0].Objects.WorkMs == 2, "historical census costs are immutable snapshots");
        Check(samples[0].Objects.Ended == 0.9f, "census age is not refreshed by repeating the result");
        Check(samples[0].PhysicsMs == -1 && samples[1].PhysicsMs == 0, "unavailable is distinct from measured zero");
        Console.WriteLine("PASS " + _checks + " checks");
    }
}
