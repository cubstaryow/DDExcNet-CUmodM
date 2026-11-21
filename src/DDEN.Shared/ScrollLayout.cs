using System;

namespace DDEN.Shared
{
    // The thumb and content share one normalized range, including both endpoints.
    public readonly struct ScrollAxis
    {
        public readonly float Maximum, Thumb, Travel;
        public ScrollAxis(float track, float viewport, float content)
        {
            track = Math.Max(0, track);
            Maximum = Math.Max(0, content - viewport);
            Thumb = Math.Min(track, Math.Max(14, content > 0 ? track * viewport / content : track));
            Travel = Math.Max(0, track - Thumb);
        }
        public float Offset(float value) => Maximum > 0 ? Math.Min(Maximum, Math.Max(0, value)) / Maximum * Travel : 0;
        public float Value(float offset) => Travel > 0 ? Math.Min(Travel, Math.Max(0, offset)) / Travel * Maximum : 0;
    }
    public readonly struct ScrollLayout
    {
        public const float Bar = 12f;
        public readonly float Width, Height, MaxX, MaxY;
        public readonly bool Horizontal, Vertical;
        public ScrollLayout(float width, float height, float contentWidth, float contentHeight)
        {
            width = Math.Max(1, width); height = Math.Max(1, height);
            bool h = contentWidth > width + 0.5f, v = contentHeight > height + 0.5f;
            for (int i = 0; i < 2; i++)
            { h = contentWidth > width - (v ? Bar : 0) + 0.5f; v = contentHeight > height - (h ? Bar : 0) + 0.5f; }
            Horizontal = h; Vertical = v;
            Width = Math.Max(1, width - (v ? Bar : 0)); Height = Math.Max(1, height - (h ? Bar : 0));
            MaxX = Math.Max(0, contentWidth - Width); MaxY = Math.Max(0, contentHeight - Height);
        }
    }
}
