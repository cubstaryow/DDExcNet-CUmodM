namespace DDEN.LoongNova
{
    internal sealed class LowEnergyNotice
    {
        internal const float Threshold = 100; // Ten percent of the 1000-unit cell.
        internal const float RepeatSeconds = 30;
        private string _epoch;
        private double _next;

        internal bool ShouldWarn(string epoch, bool loaded, float energy, double now)
        {
            if (!loaded || string.IsNullOrEmpty(epoch) || !BeltPowerState.Finite(energy) || energy < 0
                || !BeltPowerState.Finite(now)) return false;
            if (_epoch != epoch) { _epoch = epoch; _next = double.NegativeInfinity; }
            if (energy > Threshold) { _next = double.NegativeInfinity; return false; }
            if (now < _next) return false;
            _next = now + RepeatSeconds;
            return true;
        }
    }
}
