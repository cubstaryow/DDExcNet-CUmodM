using System.Collections.Generic;
using HarmonyLib;
using UnityEngine.EventSystems;

namespace DDEN.LoongNova
{
    // The native inventory handler returns from its container branch before CanCombine.
    [HarmonyPatch]
    internal static class BeltInteractionPatches
    {
        [HarmonyPatch(typeof(PlayerCamera), "TryPerformInventoryAction"), HarmonyPrefix, HarmonyPriority(Priority.First)]
        internal static bool InventoryAction(PlayerCamera __instance, RaycastResult hit,
            List<RaycastResult> uiCasts, ref bool __result)
        {
            if (__instance.body == null || __instance.dragItem == null || hit.gameObject == null
                || !hit.gameObject.TryGetComponent<InvButton>(out var button) || !button.Overlaps(uiCasts)) return true;
            var target = button.GetItem();
            var dragged = __instance.dragItem;
            if (target == null || target == dragged) return true;
            if (target.id == MedicalCatalog.InjectorId)
            {
                var injector = MedicalInjector.For(target);
                if (MedicalCatalog.IsAmpoule(dragged.id)) injector.Request("load", __instance.body, dragged);
                else return true;
                __result = true; return false;
            }
            if (Registration.IsGun(target.id))
            {
                bool compatible = target.id == Registration.ChargeGunId ? CannonAmmoState.IsRound(dragged.id) : dragged.id == Registration.CellId;
                if (!compatible) return true;
                DragonGun.For(target).Request("load", __instance.body, dragged);
                __result = true; return false;
            }
            if (target.id != Registration.BeltId) return true;
            var belt = GravityBelt.For(target);
            if (dragged.id == Registration.CellId)
                belt.Request("load", __instance.body, dragged);
            else if (belt.State.Cell.Loaded && dragged.Stats.HasTag("tool"))
                belt.Request("eject", __instance.body);
            else return true;
            // Consume the UI action even while awaiting the host; never store the cell/tool too.
            __result = true;
            return false;
        }
    }
}
