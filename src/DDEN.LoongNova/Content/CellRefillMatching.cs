using System.Collections.Generic;
using HarmonyLib;

namespace DDEN.LoongNova
{
    [HarmonyPatch]
    internal static class CellRefillMatching
    {
        // Native noMaxCondition stops at 99.1%; refill accepts every non-full loose cell.
        [HarmonyPatch(typeof(RecipeItem), nameof(RecipeItem.GetMatchingItem)), HarmonyPrefix]
        internal static bool Match(RecipeItem __instance, List<Item> items, List<Item> exclusions, ref Item __result)
        {
            if (!__instance.specific || __instance.specificId != Registration.CellId
                || !__instance.noMaxCondition || __instance.isLiquid) return true;
            __result = null;
            if (items != null)
            foreach (var item in items)
            {
                if (item == null || item.id != Registration.CellId || item.favourited
                    || exclusions != null && exclusions.Contains(item)) continue;
                if (item.condition >= 0 && item.condition < 1 && item.condition >= __instance.minimumCondition)
                { __result = item; break; }
            }
            return false;
        }
    }
}