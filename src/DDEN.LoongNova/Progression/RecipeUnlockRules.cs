using System;
using System.Collections.Generic;

namespace DDEN.LoongNova
{
    internal static class RecipeUnlockRules
    {
        internal static int Clamp(int stage) => Math.Max(0, Math.Min(2, stage));
        internal static int Required(string resultId)
        {
            if (resultId == Registration.WrenchId) return 1;
            return resultId != null && resultId.StartsWith("dden.loongnova.", StringComparison.Ordinal) ? 2 : 0;
        }
        internal static bool Allows(int stage, string resultId, bool supplyCrate = false) => Clamp(stage) >= Required(resultId)
            && (resultId != "dden.loongnova.micro-space-supply-crate" || supplyCrate);
        internal static int Acquire(int stage, string itemId)
        {
            stage = Clamp(stage);
            if (itemId == Registration.WrenchId) return 2;
            if (itemId == Registration.DragonId) return Math.Max(stage, 1);
            return stage;
        }
        internal static int Observe(int stage, IEnumerable<string> items)
        {
            stage = Clamp(stage);
            if (items == null || stage == 2) return stage;
            foreach (string item in items) { stage = Acquire(stage, item); if (stage == 2) break; }
            return stage;
        }
    }
}
