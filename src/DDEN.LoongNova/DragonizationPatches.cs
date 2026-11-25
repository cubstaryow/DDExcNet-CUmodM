using HarmonyLib;

namespace DDEN.LoongNova
{
    [HarmonyPatch]
    internal static class DragonizationPatches
    {
        [HarmonyPatch(typeof(RecipeItem), nameof(RecipeItem.UseItem)), HarmonyPrefix]
        internal static bool Spend(RecipeItem __instance, Item item)
        {
            if (__instance.specific || __instance.isLiquid || __instance.destroyItem
                || __instance.quality?.id != Registration.DragonQuality || item == null || item.id != Registration.WrenchId)
                return true;
            // Keep the displayed 100-unit requirement and the explicitly requested 1% wear independent.
            // Replacing this one debit avoids also paying the native units / quality-capacity subtraction.
            if (DragonizationRules.TrySpend(item.condition, __instance.quality.amount, out float remaining))
                item.condition = remaining;
            return false;
        }
    }
}
