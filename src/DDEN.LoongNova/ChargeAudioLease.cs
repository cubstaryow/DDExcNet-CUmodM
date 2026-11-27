using System;
namespace DDEN.LoongNova
{
    internal sealed class ChargeAudioLease
    {
        private long _revision = -1;
        private bool _active;
        private double _started, _until;
        internal bool Apply(long revision, bool active, double elapsed, double now)
        {
            if (revision <= _revision || revision < 0 || !Finite(elapsed) || elapsed < 0 || elapsed > ChargeCycle.Maximum || !Finite(now)) return false;
            _revision = revision; _active = active;
            _started = now - elapsed; _until = now + ChargeCycle.Maximum - elapsed + 0.35;
            return true;
        }
        internal bool Active(double now) => _active && Finite(now) && now < _until;
        internal float Progress(double now) => Finite(now) ? (float)Math.Max(0, Math.Min(1, (now - _started) / ChargeCycle.Maximum)) : 0;
        internal void Reset() { _revision = -1; _active = false; }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
