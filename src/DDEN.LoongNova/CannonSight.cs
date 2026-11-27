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
            if (Active(__instance, out _)) __instance.zoomTime = Mathf.Max(__state, 0.5f);
        }
        private static Exception Finalizer(PlayerCamera __instance, float __state, Exception __exception)
        {
            // Scope the native goggles flag to this camera calculation; real worn goggles retain their timer.
            __instance.zoomTime = __state; return __exception;
        }
    }
    public sealed class CannonSight : MonoBehaviour
    {
        private LineRenderer _guide;
        private Material _material;
        private void LateUpdate()
        {
            if (!CannonSightPatch.Active(PlayerCamera.main, out var gun) || gun.gameObject != gameObject)
            { Hide(); return; }
            if (_guide == null)
            {
                var shader = Shader.Find("Sprites/Default");
                if (shader == null) return;
                _material = new Material(shader);
                _guide = new GameObject("LoongNova local cannon guide").AddComponent<LineRenderer>();
                _guide.sharedMaterial = _material; _guide.useWorldSpace = true; _guide.positionCount = 2;
                _guide.startWidth = _guide.endWidth = 0.025f;
                _guide.startColor = new Color(1, 1, 1, 0.55f); _guide.endColor = new Color(1, 1, 1, 0.2f);
                _guide.sortingOrder = 99;
            }
            _guide.gameObject.SetActive(true);
            var body = PlayerCamera.main.body;
            Vector2 start = gun.Gun.barrel.position;
            Vector2 direction = gun.Gun.transform.right * (body.isRight ? 1 : -1);
            float distance = Mathf.Clamp(Vector2.Distance(start, body.targetLookPos), 1, 200);
            _guide.SetPosition(0, start); _guide.SetPosition(1, start + direction * distance);
        }
        private void Hide() { if (_guide != null) _guide.gameObject.SetActive(false); }
        private void OnDisable() => Hide();
        private void OnDestroy()
        {
            if (_guide != null) Destroy(_guide.gameObject);
            if (_material != null) Destroy(_material);
        }
    }
}
