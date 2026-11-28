using System.Collections.Generic;
using DDEN.Shared;
using UnityEngine;

namespace DDExcNet.CUmodM
{
    public partial class HubPlugin
    {
        private GUISkin _managerScrollSkin, _managerScrollSource;
        private readonly List<Texture2D> _managerScrollTextures = new List<Texture2D>();
        private GUISkin ScrollSkin(GUISkin source)
        {
            if (_managerScrollSkin != null && _managerScrollSource == source) return _managerScrollSkin;
            ReleaseScrollSkin();
            _managerScrollSource = source;
            _managerScrollSkin = Object.Instantiate(source);
            _managerScrollSkin.hideFlags = HideFlags.HideAndDontSave;
            var track = new GUIStyle(source.horizontalScrollbar)
            { fixedHeight = 12, border = new RectOffset(), margin = new RectOffset(), padding = new RectOffset() };
            SetScrollBackgrounds(track, ScrollPixel(DdenUI.Bg), ScrollPixel(DdenUI.Bg), ScrollPixel(DdenUI.Bg));
            var thumb = new GUIStyle(source.horizontalScrollbarThumb)
            { fixedHeight = 8, border = new RectOffset(), margin = new RectOffset(0, 0, 2, 2), padding = new RectOffset() };
            SetScrollBackgrounds(thumb, ScrollPixel(new Color(.25f, .41f, .51f)), ScrollPixel(DdenUI.Accent), ScrollPixel(new Color(.55f, .84f, 1)));
            _managerScrollSkin.horizontalScrollbar = track;
            _managerScrollSkin.horizontalScrollbarThumb = thumb;
            _managerScrollSkin.horizontalScrollbarLeftButton = new GUIStyle { fixedWidth = 0, fixedHeight = 12, margin = new RectOffset(), padding = new RectOffset() };
            _managerScrollSkin.horizontalScrollbarRightButton = new GUIStyle(_managerScrollSkin.horizontalScrollbarLeftButton);
            return _managerScrollSkin;
        }
        private Texture2D ScrollPixel(Color color)
        {
            var texture = new Texture2D(1, 1) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixel(0, 0, color); texture.Apply(); _managerScrollTextures.Add(texture); return texture;
        }
        private static void SetScrollBackgrounds(GUIStyle style, Texture2D normal, Texture2D hover, Texture2D active)
        {
            style.normal.background = style.onNormal.background = style.focused.background = style.onFocused.background = normal;
            style.hover.background = style.onHover.background = hover;
            style.active.background = style.onActive.background = active;
        }
        private void ReleaseScrollSkin()
        {
            if (_managerScrollSkin != null) Object.Destroy(_managerScrollSkin);
            _managerScrollSkin = _managerScrollSource = null;
            foreach (var texture in _managerScrollTextures) if (texture != null) Object.Destroy(texture);
            _managerScrollTextures.Clear();
        }
    }
}
