using System;
using HarmonyLib;
using DDEN.Shared;
using UnityEngine;

namespace DDEN.LoongNova
{
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
