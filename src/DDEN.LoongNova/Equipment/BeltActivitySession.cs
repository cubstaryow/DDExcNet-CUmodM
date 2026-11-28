using System;
using Newtonsoft.Json.Linq;
namespace DDEN.LoongNova
{
    // Only transition/checkpoint packets allocate snapshots; host ticks merely advance energy.
    internal sealed class BeltActivitySession
    {
        private JObject _checkpoint;
        private string _epoch;
        private double _clock, _confirmedAt;
        internal int Mode { get; private set; }
        internal bool Running => Mode != 0;
        internal void Tick(BeltPowerState state, double now, bool allowed)
        {
            if (_epoch != state.Epoch || !BeltPowerState.Finite(now)) { Reset(); return; }
            if (Running && now >= _clock)
                state.Advance(Math.Max(0, Math.Min(now, _confirmedAt + 65) - _clock) * Mode);
            _clock = now;
            if (!allowed || now - _confirmedAt > 65 || state.Remaining <= 0) Mode = 0;
        }
        internal bool Report(BeltPowerState state, WorkReportBudget budget, string epoch, double work, int mode, double now)
        {
            if (mode != 0 && mode != 1 && mode != 16 || !BeltPowerState.Finite(now)) return false;
            var previous = state.Capture(); long revision = state.Revision;
            bool same = _checkpoint != null && _epoch == epoch && epoch == state.Epoch;
            if (same && !state.Restore(_checkpoint)) return false;
            double limit = same ? BeltPowerState.MaxUnconfirmedWork + Math.Max(0, now - _confirmedAt) * BeltPowerState.FlightWorkRate
                : BeltPowerState.MaxUnconfirmedWork;
            if (!budget.Apply(state, epoch, work, now, limit)) { state.Restore(previous); return false; }
            state.Revision = Math.Max(revision, state.Revision) + 1;
            _epoch = epoch; _clock = _confirmedAt = now; Mode = state.Remaining > 0 ? mode : 0;
            _checkpoint = state.Capture(); return true;
        }
        internal void Reset() { _checkpoint = null; _epoch = null; Mode = 0; }
    }
}
