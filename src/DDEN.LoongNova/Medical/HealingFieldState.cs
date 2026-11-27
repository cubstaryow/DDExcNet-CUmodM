using System;
using System.Collections.Generic;

namespace DDEN.LoongNova
{
    internal sealed class HealingFieldState
    {
        internal const float Duration = 30, Radius = 6;
        internal sealed class Field
        {
            internal string Id;
            internal float X, Y, Remaining;
        }
        internal readonly List<Field> Active = new List<Field>();
        internal bool Add(string id, float x, float y, float remaining)
        {
            if (string.IsNullOrEmpty(id) || !Finite(x) || !Finite(y) || !Finite(remaining) || remaining <= 0 || remaining > Duration) return false;
            foreach (var field in Active)
                if (field.Id == id) { field.Remaining = Math.Min(field.Remaining, remaining); return false; }
            Active.Add(new Field { Id = id, X = x, Y = y, Remaining = remaining }); return true;
        }
        internal void Advance(float seconds)
        {
            if (!Finite(seconds) || seconds <= 0) return;
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                Active[i].Remaining = Math.Max(0, Active[i].Remaining - seconds);
                if (Active[i].Remaining <= 0) Active.RemoveAt(i);
            }
        }
        internal float RemainingAt(float x, float y)
        {
            float remaining = 0;
            foreach (var field in Active)
                if ((double)(x - field.X) * (x - field.X) + (double)(y - field.Y) * (y - field.Y) <= Radius * Radius)
                    remaining = Math.Max(remaining, field.Remaining);
            return remaining; // Overlapping fields never multiply healing speed.
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
