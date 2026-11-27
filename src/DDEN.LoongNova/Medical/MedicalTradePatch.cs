using HarmonyLib;
using UnityEngine;

namespace DDEN.LoongNova
{
    [HarmonyPatch(typeof(ItemInfo), nameof(ItemInfo.GetValue))]
    internal static class MedicalTradePatch
    {
        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        internal static void Value(Item item, ref int __result)
        {
            if (item == null || item.id != MedicalCatalog.InjectorId) return;
            // Native SP and MP selling both validate GetValue; zero blocks selling an empty socket.
            __result = Mathf.RoundToInt(MedicalMarket.InjectorValue(MedicalInjector.For(item).State) * item.condition);
        }
    }
}
