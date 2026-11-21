using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace DDEN.MPfix
{
    /// <summary>FIFO work with a frame budget. Exhausting the budget never discards queued work.</summary>
    internal sealed class FrameWorkQueue<T>
    {
        private readonly Queue<T> _queue = new Queue<T>();
        internal int Count => _queue.Count;
        internal void Enqueue(T value) => _queue.Enqueue(value);
        internal void Clear() => _queue.Clear();
        private static readonly Func<long> Clock = Stopwatch.GetTimestamp;

        internal int Drain(Action<T> action, int maxCount, double milliseconds) =>
            Drain(action, maxCount, (long)(milliseconds * Stopwatch.Frequency / 1000.0), Clock);

        internal int Drain(Action<T> action, int maxCount, long budgetTicks, Func<long> clock)
        {
            if (maxCount <= 0 || budgetTicks <= 0) return 0;
            int count = 0;
            long started = clock();
            while (count < maxCount && _queue.Count > 0)
            {
                action(_queue.Dequeue());
                count++;
                if (clock() - started >= budgetTicks) break;
            }
            return count;
        }
    }
}
