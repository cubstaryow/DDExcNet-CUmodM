using System;
using System.Collections.Generic;

namespace DDEN.LoongNova
{
    internal readonly struct GridCell
    {
        internal readonly int X, Y;
        internal GridCell(int x, int y) { X = x; Y = y; }
    }

    // Coordinates are block-array coordinates, including the fractional muzzle position.
    internal static class UtilityGeometry
    {
        internal static IEnumerable<GridCell> Ray(double x, double y, double dx, double dy, double range, int width, int height)
        {
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 0.000001 || double.IsNaN(length) || double.IsInfinity(length)) yield break;
            dx /= length; dy /= length;
            int cx = (int)Math.Floor(x), cy = (int)Math.Floor(y);
            int sx = Math.Sign(dx), sy = Math.Sign(dy);
            double tx = sx == 0 ? double.PositiveInfinity : (sx > 0 ? cx + 1 - x : x - cx) / Math.Abs(dx);
            double ty = sy == 0 ? double.PositiveInfinity : (sy > 0 ? cy + 1 - y : y - cy) / Math.Abs(dy);
            double stepX = sx == 0 ? double.PositiveInfinity : 1 / Math.Abs(dx);
            double stepY = sy == 0 ? double.PositiveInfinity : 1 / Math.Abs(dy);
            double distance = 0;
            while (distance <= range && cx >= 0 && cy >= 0 && cx < width && cy < height)
            {
                yield return new GridCell(cx, cy);
                if (tx < ty) { distance = tx; tx += stepX; cx += sx; }
                else if (ty < tx) { distance = ty; ty += stepY; cy += sy; }
                else { distance = tx; tx += stepX; ty += stepY; cx += sx; cy += sy; }
            }
        }

        internal static IEnumerable<GridCell> Drill(ushort[,] blocks, double x, double y, double dx, double dy, int range, int size, Action<GridCell> step = null)
        {
            int width = blocks.GetLength(0), height = blocks.GetLength(1);
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 0.000001 || double.IsNaN(length) || double.IsInfinity(length)) yield break;
            dx /= length; dy /= length;
            var visited = new HashSet<int>();
            int low = -(size / 2), high = low + size;
            foreach (var center in Ray(x, y, dx, dy, range, width, height))
            {
                // Inspect the whole leading brush before writing any part of this step.
                for (int ox = low; ox < high; ox++)
                for (int oy = low; oy < high; oy++)
                {
                    int px = center.X + ox, py = center.Y + oy;
                    double forward = (px + 0.5 - x) * dx + (py + 0.5 - y) * dy;
                    if (forward < 0 || forward >= range) continue;
                    if (px < 0 || py < 0 || px >= width - 1 || py >= height - 1 || blocks[px, py] == 14) yield break;
                }
                step?.Invoke(center);
                for (int ox = low; ox < high; ox++)
                for (int oy = low; oy < high; oy++)
                {
                    int px = center.X + ox, py = center.Y + oy;
                    double forward = (px + 0.5 - x) * dx + (py + 0.5 - y) * dy;
                    if (forward < 0 || forward >= range) continue;
                    if (visited.Add(px + py * width) && blocks[px, py] != 0) yield return new GridCell(px, py);
                }
            }
        }

        internal static bool InSquare(int x, int y, int centerX, int centerY, int size)
        {
            int low = -(size / 2);
            return x >= centerX + low && x < centerX + low + size && y >= centerY + low && y < centerY + low + size;
        }
    }

    internal sealed class ConnectedFluid
    {
        private readonly byte[,] _snapshot;
        private readonly int _width, _height;
        private int _cursor;
        internal readonly List<GridCell> Cells = new List<GridCell>();
        internal ConnectedFluid(byte[,] fluid, int x, int y)
        {
            _snapshot = (byte[,])fluid.Clone(); _width = fluid.GetLength(0); _height = fluid.GetLength(1);
            Visit(x, y);
        }
        internal bool Step(int budget)
        {
            for (int i = 0; i < budget && _cursor < Cells.Count; i++)
            {
                var cell = Cells[_cursor++];
                Visit(cell.X - 1, cell.Y); Visit(cell.X + 1, cell.Y); Visit(cell.X, cell.Y - 1); Visit(cell.X, cell.Y + 1);
            }
            return _cursor == Cells.Count;
        }
        private void Visit(int x, int y)
        {
            if (x < 0 || y < 0 || x >= _width || y >= _height || _snapshot[x, y] == 0) return;
            _snapshot[x, y] = 0; Cells.Add(new GridCell(x, y));
        }
    }
}
