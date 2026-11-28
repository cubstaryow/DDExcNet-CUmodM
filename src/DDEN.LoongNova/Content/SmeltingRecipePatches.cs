using HarmonyLib;
namespace DDEN.LoongNova
{
    [HarmonyPatch]
    internal static class SmeltingRecipePatches
    {
        [HarmonyPatch(typeof(RecipeItem),nameof(RecipeItem.UseItem)),HarmonyPostfix]
        private static void SettleWear(RecipeItem __instance,Item item)
        {
            if(item!=null&&item.id==Registration.SmelterId&&__instance.quality?.id==Registration.SmeltingQuality&&item.condition<0.00001f)item.condition=0;
        }
        private static string Name(Recipe recipe)
        {
            if(!RecipeUnlocks.IsSmelting(recipe))return null;
            return Registration.Text(recipe.result.id=="titaniumslab"?"smeltTitanium":recipe.result.id=="processedcopper"?"smeltCopper":"smeltCarbon");
        }
        [HarmonyPatch(typeof(Recipe),"get_simpleName"),HarmonyPostfix]
        private static void Simple(Recipe __instance,ref string __result){var name=Name(__instance);if(name!=null)__result=name;}
        [HarmonyPatch(typeof(Recipe),"get_fullName"),HarmonyPostfix]
        private static void Full(Recipe __instance,ref string __result){var name=Name(__instance);if(name!=null)__result=(__instance.hasMadeBefore?"":"<sprite index=25>")+name;}
    }
}
