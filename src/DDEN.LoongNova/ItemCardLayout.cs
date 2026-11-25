using System;

namespace DDEN.LoongNova
{
    internal static class ItemCardLayout
    {
        internal const float Gap = 6;
        internal const float Height = 72;
        internal static int Columns(float width) => width >= 660 ? 3 : width >= 420 ? 2 : 1;
        internal static float CardWidth(float width) => Math.Max(0, (width - Gap * (Columns(width) - 1)) / Columns(width));
        internal static float GridHeight(float width, int count) => count <= 0 ? 0
            : ((count + Columns(width) - 1) / Columns(width)) * (Height + Gap) - Gap;
    }
}
