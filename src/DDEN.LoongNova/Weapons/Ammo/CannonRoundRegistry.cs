using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
namespace DDEN.LoongNova
{
    internal enum CannonEffect { None, Drill, Drain, Breach, Revive }
    internal readonly struct CannonRound
    {
        internal readonly float Structure, Creature, Spread, MaximumMultiplier, BonusStart, BlastRadius;
        internal readonly int Pellets, Range, Size;
        internal readonly CannonEffect Effect;
        internal CannonRound(float structure, float creature, int pellets, float spread, float maximumMultiplier = 4, float bonusStart = 1, float blastRadius = 0, CannonEffect effect = CannonEffect.None, int range = 200, int size = 0)
        { Structure = structure; Creature = creature; Pellets = pellets; Spread = spread; MaximumMultiplier = maximumMultiplier; BonusStart = bonusStart; BlastRadius = blastRadius; Effect = effect; Range = range; Size = size; }
        internal float DamageMultiplier(double seconds)
        {
            if (double.IsNaN(seconds) || seconds <= BonusStart) return 1;
            double progress = Math.Min(1, (seconds - BonusStart) / (ChargeCycle.Maximum - BonusStart));
            return (float)(1 + progress * (MaximumMultiplier - 1));
        }
        internal JObject Parameters() => new JObject
        {
            ["structureDamage"] = Structure, ["creatureDamage"] = Creature, ["pellets"] = Pellets,
            ["spreadDegrees"] = Spread, ["chargeMultiplierMax"] = MaximumMultiplier, ["chargeBonusStartSeconds"] = BonusStart, ["blastRadius"] = BlastRadius, ["effect"] = (int)Effect, ["range"] = Range, ["size"] = Size
        };
    }
    internal static class CannonRoundRegistry
    {
        internal const string DataKey = "loongnova.cannonRound";
        private static readonly Dictionary<string, CannonRound> Rounds = new Dictionary<string, CannonRound>(StringComparer.Ordinal);
        internal static bool Contains(string id) => id != null && Rounds.ContainsKey(id);
        internal static CannonRound Get(string id) => id != null && Rounds.TryGetValue(id, out var profile) ? profile : default;
        internal static void Register(string id, JToken parameters)
        {
            if (string.IsNullOrWhiteSpace(id) || !(parameters is JObject)) throw new ArgumentException("Missing cannon round metadata");
            float structure = parameters.Value<float?>("structureDamage") ?? float.NaN;
            float creature = parameters.Value<float?>("creatureDamage") ?? float.NaN;
            int pellets = parameters.Value<int?>("pellets") ?? 0;
            float spread = parameters.Value<float?>("spreadDegrees") ?? float.NaN;
            float maximum = parameters.Value<float?>("chargeMultiplierMax") ?? float.NaN;
            float delay = parameters.Value<float?>("chargeBonusStartSeconds") ?? float.NaN;
            float blast = parameters.Value<float?>("blastRadius") ?? 0;
            int effectValue = parameters.Value<int?>("effect") ?? 0;
            int range = parameters.Value<int?>("range") ?? 200;
            int size = parameters.Value<int?>("size") ?? 0;
            var effect = (CannonEffect)effectValue;
            bool utility = effect != CannonEffect.None;
            if (effectValue < 0 || effectValue > (int)CannonEffect.Revive || range < 1 || range > 200
                || utility && (pellets != 1 || spread != 0 || blast != 0 || structure != 0 || creature != 0 || maximum != 1)
                || effect == CannonEffect.Drill && (range != 150 || size != 4)
                || effect == CannonEffect.Breach && size != 25
                || (effect == CannonEffect.None || effect == CannonEffect.Drain || effect == CannonEffect.Revive) && size != 0)
                throw new ArgumentException("Invalid utility round metadata: " + id);
            if (!Finite(structure) || !Finite(creature) || structure < 0 || creature < 0 || !utility && structure + creature <= 0
                || pellets < 1 || pellets > 30 || !Finite(spread) || spread < 0 || spread > 45
                || !Finite(maximum) || maximum < 1 || maximum > 16 || !Finite(delay) || delay < 0 || delay >= ChargeCycle.Maximum
                || !Finite(blast) || blast < 0 || blast > 32 || blast > 0 && (pellets != 1 || spread != 0))
                throw new ArgumentException("Invalid cannon round metadata: " + id);
            Rounds[id] = new CannonRound(structure, creature, pellets, spread, maximum, delay, blast, effect, range, size);
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
