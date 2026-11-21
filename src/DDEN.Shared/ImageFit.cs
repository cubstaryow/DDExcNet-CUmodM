using System;

namespace DDEN.Shared
{
    public readonly struct ImageFit
    {
        public readonly float X, Y, Width, Height;
        public ImageFit(float boxWidth, float boxHeight, float imageWidth, float imageHeight)
        {
            float scale = imageWidth > 0 && imageHeight > 0 ? Math.Min(Math.Max(0, boxWidth) / imageWidth, Math.Max(0, boxHeight) / imageHeight) : 0;
            Width = imageWidth * scale; Height = imageHeight * scale;
            X = (boxWidth - Width) / 2; Y = (boxHeight - Height) / 2;
        }
    }
}
