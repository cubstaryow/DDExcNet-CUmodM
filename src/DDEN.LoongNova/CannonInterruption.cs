using HarmonyLib;
using DDEN.Shared;
using UnityEngine;
namespace DDEN.LoongNova
{
    [HarmonyPatch]
    internal static class CannonInterruption
    {
        private static void Interrupt(Item item)
        {
            if (item == null || item.id != Registration.ChargeGunId) return;
            var gun = item.GetComponent<DragonGun>();
            if (gun == null) return;
            if (!Mp.IsRunning || Mp.IsServer) CannonService.ForceCommitted(gun);
            gun.QueueChargeCancel();
        }
        [HarmonyPatch(typeof(Body), nameof(Body.DropItem), new[] { typeof(Item) }), HarmonyPrefix]
        private static void BeforeDrop(Item item) => Interrupt(item);
        [HarmonyPatch(typeof(Container), nameof(Container.LoadItem)), HarmonyPrefix]
        private static void BeforeStore(Item item) => Interrupt(item);
        [HarmonyPatch(typeof(Body), nameof(Body.SwapSlots)), HarmonyPrefix]
        private static void BeforeSwap(Body __instance, int slot1, int slot2)
        { Interrupt(__instance.GetItem(slot1)); Interrupt(__instance.GetItem(slot2)); }
        [HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.SwitchHands)), HarmonyPrefix]
        private static void BeforeSwitch(PlayerCamera __instance)
        { if (__instance.body != null) Interrupt(__instance.body.GetItem(__instance.body.handSlot)); }
    }
}
