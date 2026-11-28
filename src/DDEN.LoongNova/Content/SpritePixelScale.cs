using System;

namespace DDEN.LoongNova
{
    internal static class SpritePixelScale
    {
        // Retain source resolution while preserving the intended world-space dimensions.
        internal static float PixelsPerUnit(int width, int height, int logicalWidth,
            float referenceWidth, float worldLength = 0, float logicalPixelsPerUnit = 0)
            => logicalPixelsPerUnit > 0 ? logicalPixelsPerUnit * width / logicalWidth
                : worldLength > 0 ? Math.Max(width, height) / worldLength
                : width / referenceWidth;
    }
}
