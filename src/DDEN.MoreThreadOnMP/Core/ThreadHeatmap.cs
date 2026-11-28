using System;
using System.Collections.Generic;
using System.Linq;

namespace DDEN.MoreThreadOnMP.Core
{
    internal static class ThreadHeatmap
    {
        internal const double ActivityThreshold = 0.01;
        internal static bool IsCore(string role) => role == "main";
        private static int Order(string role) => role == "main" ? 0 : 1;
        internal static ThreadLoad[] Select(ThreadSnapshot snapshot, bool showAll) => snapshot.Rows
            .Where(row => showAll || IsCore(row.Role) || row.RecentLoad >= ActivityThreshold)
            .OrderBy(row => Order(row.Role)).ThenBy(row => row.Id).ToArray();
        internal static int SampleInterval(int milliseconds) => milliseconds == 500 || milliseconds == 1000 ? milliseconds : 3000;
    }
    internal sealed class ThreadActivityWindow
    {
        private sealed class Entry { internal long Birth; internal readonly Queue<(double At, double Load)> Samples = new Queue<(double, double)>(); }
        private readonly Dictionary<int, Entry> _entries = new Dictionary<int, Entry>();
        internal void Update(ThreadLoad row, double nowMs)
        {
            if (!_entries.TryGetValue(row.Id, out var entry) || (row.Started != 0 && entry.Birth != row.Started))
                _entries[row.Id] = entry = new Entry { Birth = row.Started };
            while (entry.Samples.Count > 0 && entry.Samples.Peek().At < nowMs - 6000) entry.Samples.Dequeue();
            if (row.Available) entry.Samples.Enqueue((nowMs, row.Load));
            row.RecentLoad = entry.Samples.Count == 0 ? 0 : entry.Samples.Max(sample => sample.Load);
        }
        internal void Retain(IEnumerable<int> ids)
        { var live = new HashSet<int>(ids); foreach (int id in _entries.Keys.Where(id => !live.Contains(id)).ToArray()) _entries.Remove(id); }
    }
    internal readonly struct ThreadGrid
    {
        internal readonly int Columns, Rows;
        internal readonly float Cell, Gap, Height;
        internal ThreadGrid(int count, float width, float maxHeight)
        {
            Columns = 1; Rows = 0; Cell = Height = 0; Gap = 4;
            if (count <= 0) return;
            width = Math.Max(1, width); maxHeight = Math.Max(1, maxHeight);
            float best = 0, gap = count * 256 > width * maxHeight ? 2 : 4;
            // Fit every cell in the available rectangle, preserving square shape and no scrolling.
            for (int pass = 0; pass < 2; pass++, gap = 0)
            {
                for (int columns = 1; columns <= count; columns++)
                {
                    int rows = (count + columns - 1) / columns;
                    float cell = Math.Min(24, Math.Min((width - (columns - 1) * gap) / columns, (maxHeight - (rows - 1) * gap) / rows));
                    if (cell <= 0 || cell < best || (cell == best && rows >= Rows)) continue;
                    best = cell; Columns = columns; Rows = rows; Cell = cell; Gap = gap; Height = rows * cell + (rows - 1) * gap;
                }
                if (best > 0) break;
            }
        }
    }
}
