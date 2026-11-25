using System.Collections.Generic;
using UnityEngine;

namespace DDExcNet.CUmodM
{
    // Screen overlays draw outside GUI.Window. Scale geometry and glyphs explicitly once.
    internal static class ScreenNoticeStyle
    {
        private sealed class Cached
        {
            internal float Scale;
            internal int Size;
            internal Font Font;
            internal GUIStyle Style;
        }
        private static readonly Dictionary<GUIStyle, Cached> Styles = new Dictionary<GUIStyle, Cached>();
        internal static Rect Pixels(Rect logical, float scale)
            => new Rect(logical.x * scale, logical.y * scale, logical.width * scale, logical.height * scale);
        internal static GUIStyle FontAt(GUIStyle original, float scale)
        {
            if (!Styles.TryGetValue(original, out var cached) || cached.Scale != scale
                || cached.Size != original.fontSize || cached.Font != original.font)
            {
                if (Styles.Count >= 16) Styles.Clear();
                Styles[original] = cached = new Cached { Scale = scale, Font = original.font, Size = original.fontSize,
                    Style = new GUIStyle(original) { fontSize = Mathf.Max(1, Mathf.RoundToInt(original.fontSize * scale)) } };
            }
            return cached.Style;
        }
    }
}
