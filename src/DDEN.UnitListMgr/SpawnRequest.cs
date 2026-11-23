using System;
using System.Collections.Generic;

namespace DDEN.UnitListMgr
{
    // Only these data fields cross the network. No client-supplied identity or code/type names.
    internal sealed class SpawnRequest
    {
        public int Protocol = 1;
        public string RequestId, ItemId;
        public int Count = 1;
        public bool AtCursor, DefaultOnly;
        public float X, Y;
        public float? Condition;
        public int? Rounds;
        public bool? HasMagazine;
        public string Battery;
        public List<LiquidAmount> Liquids;
    }

    internal sealed class LiquidAmount
    {
        public string Id;
        public float Amount;
    }

    internal static class SpawnRules
    {
        internal const int MaxCount = 20, MaxLiquids = 16;
        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        internal static bool Allowed(bool multiplayer, bool serverCheats, bool remote, bool clientCheats)
            => !multiplayer || serverCheats && (!remote || clientCheats);

        internal static string Validate(SpawnRequest r)
        {
            if (r == null || r.Protocol != 1 || !Guid.TryParseExact(r.RequestId, "N", out _)) return "Invalid request";
            if (string.IsNullOrWhiteSpace(r.ItemId) || r.ItemId.Length > 128) return "Invalid item ID";
            if (r.Count < 1 || r.Count > MaxCount) return "Count must be 1–20";
            if (!Finite(r.X) || !Finite(r.Y)) return "Invalid position";
            if (r.Condition.HasValue && (!Finite(r.Condition.Value) || r.Condition < 0 || r.Condition > 1)) return "Condition must be 0–100%";
            if (r.Rounds.HasValue && (r.Rounds < 0 || r.Rounds > 100000)) return "Invalid ammunition count";
            if (r.Battery != null && r.Battery.Length > 128) return "Invalid battery";
            if (r.AtCursor && (!r.DefaultOnly || r.Count != 1)) return "Quick menu only permits one default item";
            if (r.DefaultOnly && (r.Condition.HasValue || r.Rounds.HasValue || r.HasMagazine.HasValue || r.Battery != null || r.Liquids != null))
                return "Default items cannot include property overrides";
            if (r.Liquids != null)
            {
                if (r.Liquids.Count > MaxLiquids) return "Too many liquids";
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var liquid in r.Liquids)
                    if (liquid == null || string.IsNullOrWhiteSpace(liquid.Id) || liquid.Id.Length > 128
                        || !Finite(liquid.Amount) || liquid.Amount <= 0 || !ids.Add(liquid.Id)) return "Invalid liquid mixture";
            }
            return null;
        }

        // Eight marking-menu directions, clockwise from up. Inside the dead zone means cancel.
        internal static int Sector(float x, float y, float deadZone = 40)
        {
            if (x * x + y * y < deadZone * deadZone) return -1;
            double angle = Math.Atan2(x, -y);
            return ((int)Math.Floor(angle / (Math.PI / 4) + 0.5) + 8) % 8;
        }
    }

    /// <summary>Reserved extension point. No player-state writes are implemented by this module.</summary>
    public interface IPlayerStateEditor
    {
        string Id { get; }
        string DisplayName { get; }
        bool CanEdit(Body player);
        void Draw(UnityEngine.Rect area, Body player);
    }
}
