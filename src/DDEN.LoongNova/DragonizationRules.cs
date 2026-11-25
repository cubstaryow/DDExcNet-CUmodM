using System;

namespace DDEN.LoongNova
{
    internal static class DragonizationRules
    {
        internal const float RequiredUnits = 100;
        internal const float WearFraction = 0.01f;
        internal static float Cost(float units) => WearFraction * (units / RequiredUnits);

        internal static bool TrySpend(float condition, float units, out float remaining)
        {
            remaining = condition;
            float cost = Cost(units);
            if (!BeltPowerState.Finite(condition) || !BeltPowerState.Finite(cost)
                || condition < 0 || condition > 1 || cost <= 0 || condition < cost) return false;
            remaining = Math.Max(0, condition - cost);
            if (remaining <= 0.000001f) remaining = 0; // Remove only sub-durability floating-point residue.
            return true;
        }
    }
}
