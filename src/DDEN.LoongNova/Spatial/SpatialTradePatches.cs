using HarmonyLib;

namespace DDEN.LoongNova
{
    [HarmonyPatch]
    internal static class SpatialTradePatches
    {
        [HarmonyPatch(typeof(TraderScript), "GenerateInventory"), HarmonyPostfix, HarmonyPriority(Priority.Last)]
        private static void Inventory(TraderScript __instance)
        {
            if (__instance.items == null) return;
            __instance.items.RemoveAll(item => item != null && item.id == SpatialCatalog.CrateId);
            foreach (var item in __instance.items) Keep(item);
        }
        [HarmonyPatch(typeof(TraderScript), nameof(TraderScript.ItemPrice)), HarmonyPrefix]
        private static void Preference(TraderItem item) => Keep(item);
        private static void Keep(TraderItem item)
        {
            if (item == null || item.id != SpatialCatalog.ChargerId) return;
            item.value = 60; item.preference = TraderScript.TraderItemPreference.WantsKeep;
        }
    }
}
