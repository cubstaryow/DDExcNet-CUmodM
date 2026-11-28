using System;
using HarmonyLib;
using DDEN.Shared;
using UnityEngine;

namespace DDEN.LoongNova
{
    [HarmonyPatch(typeof(PlayerCamera), "HandleCameraPosition")]
    internal static class CannonSightPatch
    {
        internal static bool Active(PlayerCamera camera, out DragonGun gun)
        {
            gun = null;
            if (camera == null || camera != PlayerCamera.main || camera.body == null || !camera.body.conscious
                || DdenHub.AnyPanelVisible || PauseHandler.paused || camera.radialOpen || camera.woundView.activeSelf || camera.tradeMenu.activeSelf) return false;
            var held = camera.body.GetItem(camera.body.handSlot);
            if (held == null || held.id != Registration.ChargeGunId || held.condition <= 0) return false;
            gun = held.GetComponent<DragonGun>();
            return gun != null && (!Mp.IsRunning || Mp.IsServer || gun.Known) && gun.Ammo.Ready;
        }
        private static void Prefix(PlayerCamera __instance, out float __state)
        {
            __state = __instance.zoomTime;
            if (Active(__instance, out _) || TacticalGogglesPatches.Active(__instance)) __instance.zoomTime = Mathf.Max(__state, 0.5f);
        }
        private static Exception Finalizer(PlayerCamera __instance, float __state, Exception __exception)
        {
            // Scope the native goggles flag to this camera calculation; real worn goggles retain their timer.
            __instance.zoomTime = __state; return __exception;
        }
    }
}
