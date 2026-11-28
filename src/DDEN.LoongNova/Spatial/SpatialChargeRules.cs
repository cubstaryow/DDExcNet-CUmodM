using System;

namespace DDEN.LoongNova
{
    internal static class SpatialChargeRules
    {
        // Native BatteryRecharger.Update adds dt / batteryInfo.maxCharge * 1.35 to condition.
        internal const double EnergyPerSecond = 1.35 * 4;
        internal static float Charge(float condition, float capacity, double seconds)
        {
            if (float.IsNaN(condition) || float.IsInfinity(condition) || float.IsNaN(capacity) || float.IsInfinity(capacity)
                || capacity <= 0 || double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0) return condition;
            return (float)Math.Max(0, Math.Min(1, condition + seconds * EnergyPerSecond / capacity));
        }
    }
}
