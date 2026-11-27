using UnityEngine;

namespace DDEN.LoongNova
{
    internal sealed class BeltWingVisual : MonoBehaviour
    {
        private GravityBelt _belt;
        private SpriteRenderer _renderer;
        private bool _open, _initialized;

        internal void Initialize(GravityBelt belt)
        {
            _belt = belt;
            _renderer = GetComponent<SpriteRenderer>();
            Apply(false);
        }

        private void LateUpdate() => Apply(_belt != null && _belt.WingsOpen);

        private void Apply(bool open)
        {
            if (_renderer == null || _initialized && open == _open) return;
            var sprite = open ? Registration.WingOpenSprite : Registration.WingFoldedSprite;
            if (sprite == null) return;
            _renderer.sprite = sprite;
            // Positive local rotation flattens a rear-facing wing; mirroring is inherited from the body.
            transform.localRotation = Quaternion.Euler(0, 0, open ? 20 : 0);
            _open = open;
            _initialized = true;
        }
    }
}
