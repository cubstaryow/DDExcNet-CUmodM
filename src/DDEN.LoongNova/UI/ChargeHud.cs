using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using DDEN.Shared;

namespace DDEN.LoongNova
{
    [HarmonyPatch(typeof(PlayerCamera), "HandleGunMenu")]
    internal static class ChargeHudPatch
    {
        private static void Postfix(PlayerCamera __instance)
        {
            var held = __instance.body != null ? __instance.body.GetItem(__instance.body.handSlot) : null;
            var hud = __instance.GetComponent<ChargeHud>();
            if (held == null || held.id != Registration.ChargeGunId)
            { if (hud != null) hud.Hide(); return; }
            if (__instance == PlayerCamera.main && __instance.GetComponent<CannonRackControls>() == null)
                __instance.gameObject.AddComponent<CannonRackControls>();
            if (hud == null) hud = __instance.gameObject.AddComponent<ChargeHud>();
            hud.Draw(__instance, DragonGun.For(held));
        }
    }
    public sealed class ChargeHud : MonoBehaviour
    {
        private RectTransform _root, _fill;
        private Image _ammoIcon;
        internal void Hide() { if (_root != null) _root.gameObject.SetActive(false); }
        internal void Draw(PlayerCamera camera, DragonGun gun)
        {
            if (camera != PlayerCamera.main || !camera.gunMenu.activeInHierarchy || !camera.body.conscious
                || DdenHub.AnyPanelVisible || PauseHandler.paused)
            { Hide(); return; }
            if (_root == null && !Create(camera)) return;
            Position(camera);
            _root.gameObject.SetActive(true);
            float fraction = gun.ChargeVisible ? gun.ChargeProgress : 0;
            _fill.anchorMax = new Vector2(fraction, 1);
            bool known = !Mp.IsRunning || Mp.IsServer || gun.Known;
            _ammoIcon.sprite = known && gun.Ammo.Loaded ? Registration.RoundSprite(gun.Ammo.Id) : null;
            _ammoIcon.enabled = _ammoIcon.sprite != null;
            _ammoIcon.color = new Color(1, 1, 1, gun.Ammo.Chambered ? 1 : 0.35f);
            // Common sprite pixels-per-unit preserves caliber even when a cartridge is shorter.
            if (_ammoIcon.sprite != null) _ammoIcon.rectTransform.sizeDelta = (Vector2)_ammoIcon.sprite.bounds.size * 52;
        }
        private static RectTransform Box(string name, Transform parent, Vector2 min, Vector2 max, Vector2 insetMin, Vector2 insetMax, Color color)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = obj.GetComponent<RectTransform>(); rect.SetParent(parent, false);
            rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = insetMin; rect.offsetMax = insetMax;
            var graphic = obj.GetComponent<Image>(); graphic.color = color; graphic.raycastTarget = false;
            return rect;
        }
        private static RectTransform Frame(string name, Transform parent, Vector2 min, Vector2 max, Vector2 a, Vector2 b)
        {
            var outer = Box(name, parent, min, max, a, b, Color.white);
            return Box("Interior", outer, Vector2.zero, Vector2.one, new Vector2(1, 1), new Vector2(-1, -1), new Color(0.025f, 0.025f, 0.025f, 1));
        }
        private bool Create(PlayerCamera camera)
        {
            if (camera.gunBindText == null || camera.gunBindText.Length < 3 || camera.gunBindText[2] == null) return false;
            // Native controls supply positioning only; this HUD creates no text components.
            var obj = new GameObject("LoongNova cannon instrument", typeof(RectTransform), typeof(LayoutElement));
            obj.GetComponent<LayoutElement>().ignoreLayout = true;
            _root = obj.GetComponent<RectTransform>(); _root.SetParent(camera.gunMenu.transform, false);
            var parent = camera.gunMenu.GetComponent<RectTransform>();
            _root.anchorMin = _root.anchorMax = parent != null ? parent.pivot : new Vector2(0.5f, 0.5f);
            _root.pivot = new Vector2(0.5f, 1);
            var track = Frame("Charge frame", _root, Vector2.zero, Vector2.one, new Vector2(34, 13), new Vector2(0, -13));
            _fill = Box("Charge fill", track, Vector2.zero, new Vector2(0, 1), Vector2.zero, Vector2.zero, new Color(1, 1, 1, 0.3f));
            float[] times = { 0, 0.7f, 1, 2, 3 };
            Color[] colors = { Color.white, Color.green, Color.yellow, new Color(1, 0.5f, 0), Color.red };
            for (int tick = 0; tick < times.Length; tick++)
            {
                float at = times[tick] / (float)ChargeCycle.Maximum;
                Box("Timing tick", track, new Vector2(at, 0), new Vector2(at, 0), new Vector2(-0.5f, 0), new Vector2(0.5f, 6), colors[tick]);
            }
            var icon = Box("Loaded round", _root, new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, Vector2.zero, Color.white);
            icon.anchoredPosition = new Vector2(13, 0);
            _ammoIcon = icon.GetComponent<Image>(); _ammoIcon.preserveAspect = true; _ammoIcon.enabled = false;
            return true;
        }
        private void Position(PlayerCamera camera)
        {
            // Include the lowest native label; the new instrument's TOP stays below that boundary.
            var nativeBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(camera.gunMenu.transform, camera.gunMagButton.transform);
            nativeBounds.Encapsulate(RectTransformUtility.CalculateRelativeRectTransformBounds(camera.gunMenu.transform, camera.gunSafeImage.rectTransform));
            nativeBounds.Encapsulate(RectTransformUtility.CalculateRelativeRectTransformBounds(camera.gunMenu.transform, camera.gunRackImage.rectTransform));
            nativeBounds.Encapsulate(RectTransformUtility.CalculateRelativeRectTransformBounds(camera.gunMenu.transform, camera.gunBindText[2].rectTransform));
            _root.anchoredPosition = new Vector2(nativeBounds.center.x, nativeBounds.min.y - 8);
            _root.sizeDelta = new Vector2(Mathf.Max(220, nativeBounds.size.x) + 34, 52);
        }
        private void OnDisable() => Hide();
        private void OnDestroy() { if (_root != null) Destroy(_root.gameObject); }
    }
}
