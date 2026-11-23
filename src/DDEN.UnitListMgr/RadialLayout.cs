using System;

namespace DDEN.UnitListMgr
{
    internal readonly struct RadialLayout
    {
        internal readonly float Scale, X, Y;
        internal float Radius => 78 * Scale;
        internal float HalfWidth => 36 * Scale;
        internal float HalfHeight => 36 * Scale;
        internal RadialLayout(float width, float height, float anchorX, float anchorY)
        {
            Scale = Math.Min(1, Math.Min(width / 260, height / 260));
            float margin = 122 * Scale;
            X = Math.Max(margin, Math.Min(width - margin, anchorX));
            Y = Math.Max(margin, Math.Min(height - margin, anchorY));
        }
        internal float CellX(int index) => X + (index >= 1 && index <= 3 ? Radius : index >= 5 && index <= 7 ? -Radius : 0) - HalfWidth;
        internal float CellY(int index) => Y + (index <= 1 || index == 7 ? -Radius : index >= 3 && index <= 5 ? Radius : 0) - HalfHeight;
    }
}
