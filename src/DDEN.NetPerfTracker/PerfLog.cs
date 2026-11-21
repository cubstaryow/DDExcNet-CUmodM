using System;
using System.IO;

namespace DDEN.NetPerfTracker
{
    internal static class PerfLog
    {
        /// <summary>Only runs on explicit export. Census events may be discovered after newer frame events.</summary>
        internal static void WriteTimeline(TextWriter writer, SampleRing<PerfSample> samples,
            SampleRing<PerfEvent> history, Func<PerfSample, string> describe)
        {
            var events = new PerfEvent[history.Count];
            for (int i = 0; i < events.Length; i++) events[i] = history[i];
            Array.Sort(events, (a, b) => a.T.CompareTo(b.T));
            int at = 0;
            for (int i = 0; i < samples.Count; i++)
            {
                while (at < events.Length && events[at].T <= samples[i].T) writer.WriteLine("EVENT " + events[at++].Describe());
                writer.WriteLine(describe(samples[i]));
            }
            while (at < events.Length) writer.WriteLine("EVENT " + events[at++].Describe());
        }
    }
}
