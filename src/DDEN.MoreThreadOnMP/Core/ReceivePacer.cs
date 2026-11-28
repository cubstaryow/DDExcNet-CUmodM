using System;

namespace DDEN.MoreThreadOnMP.Core
{
    // Main-thread only. Leaves message ownership, ordering and dispatch inside the native transport.
    internal sealed class ReceivePacer
    {
        private readonly Func<double> _now;
        private double _started, _perMessageMs = 0.25, _budget;
        private int _cap, _messages;
        internal bool InPoll { get; private set; }
        internal long Polls, Messages, LimitedCalls, SlowHandlers;
        internal double PeakPollMs, PeakHandlerMs;

        internal ReceivePacer(Func<double> milliseconds) { _now = milliseconds; }
        internal void Begin(double budgetMs, int cap)
        {
            _budget = Math.Max(0.25, Math.Min(20, budgetMs));
            _cap = Math.Max(1, Math.Min(255, cap));
            _messages = 0; _started = _now(); InPoll = true;
        }
        internal int Limit(int nativeCount)
        {
            if (!InPoll || nativeCount <= 0) return nativeCount;
            double left = _budget - Math.Max(0, _now() - _started);
            // At least one per peer per native sweep: busy earlier peers cannot starve later peers.
            int predicted = left <= 0 ? 1 : (int)Math.Min(255, Math.Max(1, left / _perMessageMs));
            int count = Math.Min(nativeCount, Math.Min(_cap, predicted));
            if (count < nativeCount) LimitedCalls++;
            return count;
        }
        internal void Message(double elapsedMs)
        {
            if (!InPoll) return;
            _messages++; Messages++;
            PeakHandlerMs = Math.Max(PeakHandlerMs, elapsedMs);
            if (elapsedMs >= 30) SlowHandlers++;
        }
        internal void End()
        {
            if (!InPoll) return;
            double elapsed = Math.Max(0, _now() - _started);
            if (_messages > 0)
                _perMessageMs = Math.Max(0.01, Math.Min(20, _perMessageMs * 0.75 + elapsed / _messages * 0.25));
            Polls++; PeakPollMs = Math.Max(PeakPollMs, elapsed); InPoll = false;
        }
        internal void Cancel() { InPoll = false; }
    }
}
