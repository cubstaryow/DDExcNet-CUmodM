using System;
namespace DDEN.LoongNova
{
    internal static class PickaxeWear
    {
        internal const int Durability = 1200;
        internal static float Spend(float condition)
        {
            float remaining = Math.Max(0, condition - 1f / Durability);
            return remaining < 0.00001f ? 0 : remaining;
        }
    }
}
