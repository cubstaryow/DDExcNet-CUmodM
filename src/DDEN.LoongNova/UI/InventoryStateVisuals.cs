using HarmonyLib;
using UnityEngine;

namespace DDEN.LoongNova
{
    [HarmonyPatch(typeof(InvButton), nameof(InvButton.UpdateGraphic))]
    internal static class InventoryStateVisuals
    {
        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        internal static void Refresh(InvButton __instance)
        {
            if (__instance == null || __instance.itemImg == null) return;
            var item = __instance.GetItem();
            if (item == null) return;
            if (item.id == SpatialCatalog.CrateId)
            {
                var box = MicroSpaceCrate.For(item); box.Settle();
                // Reuse the native indicator: no second wear/capacity bar or extra UI object.
                if (__instance.itemCond != null)
                {
                    float charge = box.State.Cell.Loaded ? Mathf.Clamp01((float)(box.State.Energy / EnergyState.Capacity)) : 0;
                    __instance.itemCond.fillAmount = charge;
                    __instance.itemCond.color = PlayerCamera.ConditionToColor(charge);
                }
                return;
            }
            if (Registration.IsGun(item.id)) DragonGun.For(item).RefreshSprite();
            else if (item.id == MedicalCatalog.InjectorId) MedicalInjector.For(item).RefreshSprite();
            else if (item.id == SpatialCatalog.ChargerId) MicroSpaceCrate.For(item).RefreshSprite();
            else return;
            // CCL's icon postfix can restore its registered/snapshot icon after the native renderer lookup.
            // Read the instance's current load state last, for hand slots, medical slots and container slots alike.
            var renderer = item.GetComponent<SpriteRenderer>();
            if (renderer == null || renderer.sprite == null) return;
            __instance.itemImg.sprite = renderer.sprite;
            __instance.itemImg.rectTransform.sizeDelta = PlayerCamera.ImageSizeDelta(renderer.sprite.texture, 3, __instance.maxImageSize);
        }
    }
}
