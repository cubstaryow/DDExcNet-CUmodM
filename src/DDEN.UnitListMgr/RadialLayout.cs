using System;

namespace DDEN.UnitListMgr
{
    internal readonly struct RadialLayout
    {
        internal readonly float Scale, X, Y;
        internal float Radius => 210 * Scale;
        internal float HalfWidth => 72 * Scale;
        internal float HalfHeight => 34 * Scale;
        internal RadialLayout(float width, float height, float anchorX, float anchorY)
        {
            Scale = Math.Min(1, Math.Min(width / 620, height / 580));
            float mx = 292 * Scale, my = 254 * Scale;
            X = Math.Max(mx, Math.Min(width - mx, anchorX));
            Y = Math.Max(my, Math.Min(height - my, anchorY));
        }
        internal float CellX(int index) => X + (float)Math.Sin(index * Math.PI / 4) * Radius - HalfWidth;
        internal float CellY(int index) => Y - (float)Math.Cos(index * Math.PI / 4) * Radius - HalfHeight;
    }
}
