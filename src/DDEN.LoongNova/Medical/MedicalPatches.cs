using HarmonyLib;
using UnityEngine;

namespace DDEN.LoongNova
{
    [HarmonyPatch]
    internal static class MedicalPatches
    {
        [HarmonyPatch(typeof(Body), nameof(Body.CanCombine)), HarmonyPrefix, HarmonyPriority(Priority.First)]
        internal static bool CanLoad(Item it1, Item it2, ref bool __result)
        {
            if (it1?.id != MedicalCatalog.InjectorId && it2?.id != MedicalCatalog.InjectorId) return true;
            var injector = it1?.id == MedicalCatalog.InjectorId ? it1 : it2;
            var other = injector == it1 ? it2 : it1;
            bool loaded = MedicalInjector.For(injector).State.Loaded;
            __result = other != null && MedicalCatalog.IsAmpoule(other.id) && other.condition > 0 && !loaded;
            return false;
        }
        [HarmonyPatch(typeof(Body), nameof(Body.CombineItems)), HarmonyPrefix, HarmonyPriority(Priority.First)]
        internal static bool Load(Body __instance, Item it1, Item it2)
        {
            if (it1?.id != MedicalCatalog.InjectorId && it2?.id != MedicalCatalog.InjectorId) return true;
            var injector = it1?.id == MedicalCatalog.InjectorId ? it1 : it2;
            var other = injector == it1 ? it2 : it1;
            if (other != null && MedicalCatalog.IsAmpoule(other.id)) MedicalInjector.For(injector).Request("load", __instance, other);
            return false;
        }
        [HarmonyPatch(typeof(Item), nameof(Item.GetItem)), HarmonyPostfix]
        private static void RepairDelegate(string id, ref ItemInfo __result)
        {
            if (__result != null && id == MedicalCatalog.InjectorId)
            { __result.useLimbAction = MedicalInjector.Inject; __result.useAction = MedicalInjector.Unload; }
        }
        [HarmonyPatch(typeof(Item), "Start"), HarmonyPostfix]
        private static void EnsureComponent(Item __instance)
        {
            if (__instance.id == MedicalCatalog.InjectorId) MedicalInjector.For(__instance);
        }
        [HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.ItemHoverDescription)), HarmonyPostfix]
        private static void SlotHint(Item item, ref (string, string) __result)
        {
            if (item?.id != MedicalCatalog.InjectorId) return;
            var injector = MedicalInjector.For(item);
            __result.Item2 = "<color=#5eff00><sprite index=" + (injector.State.Ready ? "15" : "16")
                + " tint=1>" + injector.Hint() + "</color>\n\n" + __result.Item2;
        }
    }
}
