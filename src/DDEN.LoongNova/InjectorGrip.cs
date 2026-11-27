using HarmonyLib;
using UnityEngine;

namespace DDEN.LoongNova
{
    [HarmonyPatch(typeof(Body), "HandleVisuals")]
    internal static class InjectorGrip
    {
        [HarmonyPostfix, HarmonyPriority(Priority.First)]
        internal static void Pose(Body __instance)
        {
            var item = __instance.GetItem(__instance.handSlot);
            if (item == null || item.id != MedicalCatalog.InjectorId || !__instance.HoldingItem(__instance.handSlot)) return;
            // HandleVisuals already computes gunangle for the native pistol arm pose, including torso rotation.
            __instance.armsAnimator.SetBool("gun", true);
            if (__instance.forceNoMovement) item.transform.localEulerAngles = new Vector3(0, 0, item.Stats.slotRotation);
            else
            {
                float side = __instance.isRight ? 1 : -1;
                float angle = Vector2.SignedAngle(Vector2.right * side, (__instance.targetLookPos - item.transform.position).normalized);
                item.transform.eulerAngles = new Vector3(0, 0, angle);
            }
        }
    }
}
