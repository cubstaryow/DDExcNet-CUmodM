using HarmonyLib;
using UnityEngine;

namespace DDEN.LoongNova
{
    [HarmonyPatch]
    internal static class BeltWearPatches
    {
        [HarmonyPatch(typeof(Wearable), nameof(Wearable.CreateSprites)), HarmonyPostfix, HarmonyPriority(Priority.Last)]
        private static void BackLayer(Wearable __instance, Body body)
        {
            var item = __instance.GetComponent<Item>();
            if (item == null || item.id != Registration.BeltId || body == null
                || __instance.secondaryObjects == null || __instance.secondaryLimbs == null) return;
            var torso = body.LimbByName("UpTorso");
            var torsoRenderer = torso != null ? torso.GetComponent<SpriteRenderer>() : null;
            if (torsoRenderer == null) return;
            int backOrder = torsoRenderer.sortingOrder;
            if (body.limbs != null)
                foreach (var limb in body.limbs)
                    if (limb != null && limb.TryGetComponent<SpriteRenderer>(out var renderer))
                        backOrder = Mathf.Min(backOrder, renderer.sortingOrder);
            // This is visual layering only; the native wearable owns these objects and clears them on removal.
            for (int i = 0; i < __instance.secondaryObjects.Length && i < __instance.secondaryLimbs.Length; i++)
            {
                var obj = __instance.secondaryObjects[i];
                if (__instance.secondaryLimbs[i] != "UpTorso" || obj == null) continue;
                var renderer = obj.GetComponent<SpriteRenderer>();
                if (renderer == null) continue;
                renderer.sortingLayerID = torsoRenderer.sortingLayerID;
                renderer.sortingOrder = backOrder - 2;
                var pose = obj.GetComponent<BeltWingVisual>() ?? obj.AddComponent<BeltWingVisual>();
                pose.Initialize(GravityBelt.For(item));
            }
        }
    }
}
