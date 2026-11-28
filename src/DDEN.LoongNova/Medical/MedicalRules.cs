using System;
using System.Collections.Generic;

namespace DDEN.LoongNova
{
    internal static class MedicalRules
    {
        internal const float ComaSeconds = 60;
        internal const float GrowthSeconds = 45;
        internal const int GrowthLimit = 2;
        internal const float RadiationRecoveryPerSecond = 1;
        internal const float BloodRecoveryPerSecond = 2.5f;
        internal const float RapidBloodPerSecond = 20;
        internal const float RapidPressurePerSecond = 20;
        internal const float NormalBlood = 100, NormalPressure = 120;
        internal const float BloodCostFraction = 0.2f;
        internal const float InjectionRetained = 0.25f;
        internal const float NewLimbHealth = 10;
        internal const float TreatmentSeconds = 10;
        internal const float BurstHealthPerSecond = 20;
        internal const float BurstInfectionPerSecond = 40;
        internal const float BurstBleedPerSecond = 30;
        internal const float BurstPainPerSecond = 50;
        internal const float BlessingSeconds = 120;
        internal const float BlessingHealthPerSecond = 0.5f;
        internal const float BlessingInfectionPerSecond = 0.5f;
        internal const float BlessingPainPerSecond = 5;
        internal const float DisinfectSeconds = 600;

        // Vanilla bloodVolumePercentage = 0.5 + bloodVolume / 200; normal volume is field value 100.
        internal static float RegenerationBlood(float field) => Math.Max(-100, (field + 100) * (1 - BloodCostFraction) - 100);
        internal static float QuarterHealth(float value) => Math.Max(0, Math.Min(100, value)) * InjectionRetained;
        internal static float HealHealth(float value, float amount) => Math.Max(0, Math.Min(100, value + amount));
        internal static float TreatmentStep(float remaining, float delta) => float.IsNaN(remaining) || float.IsInfinity(remaining)
            || float.IsNaN(delta) || float.IsInfinity(delta) || delta <= 0 ? 0 : Math.Max(0, Math.Min(remaining, delta));
        internal static float AdvanceComa(float remaining, float delta)
        {
            if (float.IsNaN(remaining) || float.IsInfinity(remaining) || remaining <= 0) return 0;
            if (float.IsNaN(delta) || float.IsInfinity(delta) || delta <= 0) return Math.Min(ComaSeconds, remaining);
            return Math.Max(0, Math.Min(ComaSeconds, remaining) - delta);
        }
        // Only a severed outward branch directly adjoining the injected stump is eligible.
        internal static List<T> MissingBranches<T>(T selected, Func<T, IEnumerable<T>> neighbors,
            Func<T, bool> missing, Func<T, bool> organic, Func<T, int> depth) where T : class
        {
            var result = new List<T>();
            var connected = neighbors(selected);
            if (connected == null) return result;
            foreach (var next in connected)
                if (next != null && missing(next) && organic(next) && depth(next) > depth(selected) && !result.Contains(next)) result.Add(next);
            return result;
        }
        internal static List<T> GrowthPlan<T>(T selected, Func<T, IEnumerable<T>> neighbors,
            Func<T, bool> missing, Func<T, bool> organic, Func<T, int> depth, int maximum) where T : class
        {
            var result = new List<T>();
            var queued = new HashSet<T>();
            var pending = new Queue<T>(MissingBranches(selected, neighbors, missing, organic, depth));
            while (pending.Count > 0 && result.Count < Math.Min(GrowthLimit, maximum))
            {
                var part = pending.Dequeue();
                if (!queued.Add(part)) continue;
                result.Add(part);
                foreach (var child in MissingBranches(part, neighbors, missing, organic, depth)) pending.Enqueue(child);
            }
            return result;
        }
    }
}
