using HarmonyLib;

namespace DDEN.LoongNova
{
    [HarmonyPatch]
    internal static class LoongLocalePatches
    {
        [HarmonyPatch(typeof(Locale),nameof(Locale.GetOther)),HarmonyPostfix,HarmonyPriority(Priority.Last)]
        private static void QualityText(string __0,ref string __result)
        {
            // The native argument is named str; bind by position to avoid aborting plugin initialization.
            if(__0=="cq"+Registration.SmeltingQuality)__result=Registration.Text("smeltingQuality");
            else if(__0=="cq"+Registration.DragonQuality)__result=Registration.Text("dragonQuality");
        }

        [HarmonyPatch(typeof(Item), nameof(Item.GetItem)), HarmonyPostfix, HarmonyPriority(Priority.Last)]
        private static void ItemText(string id, ref ItemInfo __result) => LoongLocalization.RefreshItem(id, __result);

        [HarmonyPatch(typeof(Locale), nameof(Locale.LoadLanguage)), HarmonyPostfix, HarmonyPriority(Priority.Last), HarmonyAfter("net.cucorelib")]
        private static void LanguageLoaded() => LoongLocalization.RefreshItems();
    }
}
