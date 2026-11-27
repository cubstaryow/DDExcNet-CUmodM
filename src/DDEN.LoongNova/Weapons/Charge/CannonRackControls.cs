using DDEN.Shared;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace DDEN.LoongNova
{
    [HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.GunRack))]
    public sealed class CannonRackControls : MonoBehaviour
    {
        private readonly ButtonLease _rack = new ButtonLease(), _magazine = new ButtonLease();

        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static bool Rack(PlayerCamera __instance)
        {
            var body = __instance.body;
            var held = body != null ? body.GetItem(body.handSlot) : null;
            if (held == null || held.id != Registration.ChargeGunId) return true;
            if (__instance == PlayerCamera.main && body.conscious && body.allowUseItem
                && !DdenHub.AnyPanelVisible && !PauseHandler.paused)
                DragonGun.For(held).Request("rack", body);
            return false;
        }

        private void LateUpdate()
        {
            var camera = GetComponent<PlayerCamera>();
            var body = camera != null ? camera.body : null;
            var held = body != null ? body.GetItem(body.handSlot) : null;
            if (camera != PlayerCamera.main || held == null || held.id != Registration.ChargeGunId
                || camera.gunRackImage == null || !camera.gunMenu.activeInHierarchy)
            { Restore(); return; }
            var button = camera.gunRackImage.GetComponent<Button>() ?? camera.gunRackImage.GetComponentInParent<Button>();
            var gun = DragonGun.For(held);
            bool known = !Mp.IsRunning || Mp.IsServer || gun.Known;
            bool usable = known
                && body.conscious && body.allowUseItem && !DdenHub.AnyPanelVisible && !PauseHandler.paused;
            _rack.Apply(button, usable && gun.Ammo.Loaded);
            _magazine.Apply(camera.gunMagButton, usable && (gun.Ammo.CanUnload || gun.State.Loaded));
        }

        private void Restore()
        {
            _rack.Restore(); _magazine.Restore();
        }
        private sealed class ButtonLease
        {
            private Button _button;
            private bool _originalEnabled, _originalInteractable, _writtenInteractable;
            internal void Apply(Button button, bool interactable)
            {
                if (button == null) { Restore(); return; }
                if (_button != button)
                {
                    Restore(); _button = button;
                    _originalEnabled = button.enabled; _originalInteractable = button.interactable;
                }
                _button.enabled = true;
                _button.interactable = _writtenInteractable = interactable;
            }
            internal void Restore()
            {
                if (_button == null) return;
                // Do not overwrite later changes owned by another component.
                if (_button.enabled) _button.enabled = _originalEnabled;
                if (_button.interactable == _writtenInteractable) _button.interactable = _originalInteractable;
                _button = null;
            }
        }
        private void OnDisable() => Restore();
        private void OnDestroy() => Restore();
    }
}
