using System;

namespace DDEN.LoongNova
{
    // Client-side budget for optional state queries only. Gameplay commands never use it.
    internal static class NetworkReadBudget
    {
        internal const double Rate = 2, Burst = 1;
        private static object _session;
        private static double _tokens = Burst, _time = double.NaN;
        internal static bool TryAcquire(double now, object session)
        {
            if (double.IsNaN(now) || double.IsInfinity(now)) return false;
            if (!ReferenceEquals(_session, session) || double.IsNaN(_time) || now < _time)
            { _session = session; _time = now; _tokens = Burst; }
            _tokens = Math.Min(Burst, _tokens + (now - _time) * Rate); _time = now;
            if (_tokens < 1) return false;
            _tokens--; return true;
        }
    }
}
