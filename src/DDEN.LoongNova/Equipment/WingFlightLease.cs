namespace DDEN.LoongNova
{
    // Cosmetic state only. Missing/reordered updates must never leave another player's wings permanently open.
    internal sealed class WingFlightLease
    {
        internal const double Lifetime = 70;
        private long _sequence = -1;
        private bool _flying;
        private double _until;

        internal bool Apply(long sequence, bool flying, double now)
        {
            if (sequence <= _sequence || sequence < 0 || !BeltPowerState.Finite(now)) return false;
            _sequence = sequence;
            _flying = flying;
            _until = now + Lifetime;
            return true;
        }
        internal bool IsFlying(double now) => BeltPowerState.Finite(now) && _flying && now <= _until;
        internal void Reset() { _sequence = -1; _flying = false; _until = 0; }
    }
}
