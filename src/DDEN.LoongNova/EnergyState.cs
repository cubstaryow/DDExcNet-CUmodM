using System;
using Newtonsoft.Json.Linq;

namespace DDEN.LoongNova
{
    internal sealed class EnergyState
    {
        internal const float Capacity = 1000;
        internal const float ShotCost = 50;
        internal bool Loaded;
        internal float Energy;
        internal bool Favourite;
        internal long Revision;

        internal bool TrySpendShot()
        {
            if (!Loaded || Energy < ShotCost) return false;
            Set(true, Energy - ShotCost, Favourite);
            return true;
        }

        internal void Set(bool loaded, float energy, bool favourite)
        {
            Loaded = loaded;
            Energy = loaded && !float.IsNaN(energy) && !float.IsInfinity(energy) ? Math.Max(0, Math.Min(Capacity, energy)) : 0;
            Favourite = loaded && favourite;
            Revision++;
        }
        internal JObject Capture() => new JObject
        {
            ["loaded"] = Loaded, ["energy"] = Energy, ["favourite"] = Favourite, ["revision"] = Revision
        };
        internal bool Restore(JToken data)
        {
            if (!(data is JObject)) return false;
            if (data["loaded"]?.Type != JTokenType.Boolean || data["favourite"]?.Type != JTokenType.Boolean) return false;
            float? energy = data.Value<float?>("energy");
            if (!energy.HasValue || float.IsNaN(energy.Value) || float.IsInfinity(energy.Value) || energy < 0 || energy > Capacity) return false;
            Set(data.Value<bool>("loaded"), energy.Value, data.Value<bool>("favourite"));
            Revision = Math.Max(0, data.Value<long?>("revision") ?? 0);
            return true;
        }
    }

}
