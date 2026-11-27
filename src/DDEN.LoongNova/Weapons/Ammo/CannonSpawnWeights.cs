using System;
using System.Collections.Generic;

namespace DDEN.LoongNova
{
    internal static class CannonSpawnWeights
    {
        // Bio, demolition, explosive, drill, drain, EMP, mysterious. Awaiting the user's final rarity weights.
        private static readonly double[] DefaultWeights = { 1, 1, 1, 1, 1, 1, 1 };
        internal static string Pick(double roll) => Pick(roll, DefaultWeights);
        internal static string Pick(double roll, bool allowRevival) => Pick(roll, DefaultWeights, allowRevival);
        internal static string Pick(double roll, IReadOnlyList<double> weights, bool allowRevival = true)
        {
            if (double.IsNaN(roll) || roll < 0 || roll > 1 || weights == null || weights.Count != LootRules.RoundCount) return null;
            double total = 0;
            for (int i = 0; i < weights.Count; i++)
            {
                double weight = weights[i];
                if (double.IsNaN(weight) || double.IsInfinity(weight) || weight < 0) return null;
                if (!allowRevival && i == 6) continue;
                total += weight;
            }
            if (total <= 0 || double.IsInfinity(total)) return null;
            double cursor = roll * total;
            string last = null;
            for (int i = 0; i < weights.Count; i++)
            {
                if (weights[i] <= 0 || !allowRevival && i == 6) continue;
                last = LootRules.BonusRound(i);
                if (cursor < weights[i]) return last;
                cursor -= weights[i];
            }
            return last; // Random.value can equal one.
        }
    }
}
