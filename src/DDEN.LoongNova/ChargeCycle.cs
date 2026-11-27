using System;
namespace DDEN.LoongNova
{
    // Authority owns time and owner identity. Charging is transient, never serialized.
    internal sealed class ChargeCycle
    {
        internal const double Minimum = 0.7, Maximum = 3, Commit = 1, Lifetime = 30;
        internal object Owner { get; private set; }
        private double _started;
        internal bool Active => Owner != null;
        internal bool Begin(object owner, double now)
        {
            if (owner == null || !Finite(now) || Active) return false;
            Owner = owner; _started = now; return true;
        }
        internal double Elapsed(double now) => Active && Finite(now) ? Math.Max(0, now - _started) : 0;
        internal bool Committed(double now) => Active && Elapsed(now) > Commit;
        internal bool Release(object owner, double now, out float fraction)
        {
            fraction = 0;
            if (!Active || !ReferenceEquals(Owner, owner)) return false;
            double elapsed = Elapsed(now);
            Cancel();
            if (!Finite(now) || elapsed + 1e-6 < Minimum || elapsed > Lifetime) return false;
            fraction = (float)Math.Min(1, elapsed / Maximum);
            return true;
        }
        internal void Cancel() { Owner = null; _started = 0; }


        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}