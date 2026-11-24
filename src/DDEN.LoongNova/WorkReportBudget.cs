using System;

namespace DDEN.LoongNova
{
    // Cumulative, cell-scoped reports make retries idempotent. A bounded time budget limits client bursts.
    internal sealed class WorkReportBudget
    {
        private string _epoch;
        private double _clock, _budget;

        internal bool Apply(BeltPowerState state, string epoch, double total, double now)
        {
            if (!state.Cell.Loaded || epoch != state.Epoch || !BeltPowerState.Finite(total)
                || !BeltPowerState.Finite(now) || total < state.WorkTotal - 1e-6) return false;
            if (_epoch != state.Epoch) { _epoch = state.Epoch; _clock = now; _budget = 2; }
            _budget = Math.Min(2, _budget + Math.Max(0, now - _clock));
            _clock = now;
            double difference = Math.Max(0, total - state.WorkTotal);
            if (difference > _budget + 0.05) return false;
            _budget -= difference; // Carry tolerated clock rounding forward; never grant it again per packet.
            state.Advance(difference);
            return true;
        }
    }
}
