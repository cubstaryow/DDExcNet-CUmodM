using System;
using Newtonsoft.Json.Linq;
namespace DDEN.LoongNova
{
    internal sealed class CannonAmmoState
    {
        internal const string BioId = "dden.loongnova.bio-erasure-round";
        internal const string DemolitionId = "dden.loongnova.demolition-round";
        internal const string ExplosiveId = "dden.loongnova.explosive-round";
        internal const string DrillId = "dden.loongnova.drill-round";
        internal const string DrainId = "dden.loongnova.spatial-drain-round";
        internal const string BreachId = "dden.loongnova.emp-breach-round";
        internal const string RevivalId = "dden.loongnova.revival-round";
        internal const string CarrierId = "dden.loongnova.universal-ammo-carrier";
        // Keep the old primary fields: they denote the chamber when chambered, otherwise the magazine.
        internal string Id = "";
        internal float Condition;
        internal bool Favourite, Chambered;
        internal bool SpawnRoundGranted;
        private string _reserveId = "";
        private float _reserveCondition;
        private bool _reserveFavourite;
        internal long Revision;
        internal bool Loaded => IsRound(Id) || Spent;
        internal bool Spent => Id == CarrierId;
        internal bool Ready => IsRound(Id) && Chambered;
        internal bool MagazineLoaded => IsRound(Id) && !Chambered || IsRound(_reserveId);
        internal bool CanLoad => !MagazineLoaded;
        internal bool CanUnload => MagazineLoaded;
        internal string MagazineId => Chambered ? _reserveId : Id;
        internal float MagazineCondition => Chambered ? _reserveCondition : Condition;
        internal bool MagazineFavourite => Chambered ? _reserveFavourite : Favourite;
        internal bool UnloadMagazine()
        {
            if (!CanUnload) return false;
            if (Chambered) { _reserveId = ""; _reserveCondition = 0; _reserveFavourite = false; }
            else { Id = ""; Condition = 0; Favourite = false; }
            Revision++; return true;
        }
        internal static bool IsRound(string id) => CannonRoundRegistry.Contains(id);
        internal static CannonRound Profile(string id) => CannonRoundRegistry.Get(id);
        internal bool TryGrantSpawnRound(string id)
        {
            if (SpawnRoundGranted || Loaded || !Load(id, 1, false)) return false;
            SpawnRoundGranted = true; return true;
        }
        internal bool Load(string id, float condition, bool favourite)
        {
            if (!CanLoad || !Valid(id, condition, favourite) || id == "") return false;
            if (Chambered)
            { _reserveId = id; _reserveCondition = condition; _reserveFavourite = favourite; }
            else
            { Id = id; Condition = condition; Favourite = favourite; }
            Revision++; return true;
        }
        // The service must successfully create any ejected chamber round BEFORE committing this transition.
        internal bool Rack()
        {
            if (!Loaded) return false;
            if (!Chambered) Chambered = true;
            else Advance(true);
            Revision++; return true;
        }
        private void Advance(bool chamberNext)
        {
            Id = _reserveId; Condition = _reserveCondition; Favourite = _reserveFavourite;
            Chambered = chamberNext && IsRound(Id);
            _reserveId = ""; _reserveCondition = 0; _reserveFavourite = false;
        }
        internal void Clear()
        {
            _reserveId = ""; _reserveCondition = 0; _reserveFavourite = false;
            Advance(false); Revision++;
        }
        internal bool Consume(out string id)
        {
            id = Id;
            if (!Ready) return false;
            // Firing keeps a physical spent shell in the closed chamber; the magazine is untouched.
            Id = CarrierId; Condition = 1; Favourite = false;
            Revision++; return true;
        }
        internal JObject Capture() => new JObject
        {
            ["id"] = Id, ["condition"] = Condition, ["favourite"] = Favourite,
            ["chambered"] = Chambered, ["revision"] = Revision, ["spawnRoundGranted"] = SpawnRoundGranted,
            ["reserve"] = new JObject { ["id"] = _reserveId, ["condition"] = _reserveCondition, ["favourite"] = _reserveFavourite }
        };
        internal bool Restore(JToken data)
        {
            if (!(data is JObject) || data["id"]?.Type != JTokenType.String || data["favourite"]?.Type != JTokenType.Boolean
                || data["chambered"] != null && data["chambered"].Type != JTokenType.Boolean
                || data["spawnRoundGranted"] != null && data["spawnRoundGranted"].Type != JTokenType.Boolean) return false;
            string id = data.Value<string>("id");
            float condition = data.Value<float?>("condition") ?? float.NaN;
            bool favourite = data.Value<bool>("favourite");
            bool chambered = data.Value<bool?>("chambered") ?? false;
            if (!(Valid(id, condition, favourite) || id == CarrierId && condition == 1 && !favourite)
                || id == "" && chambered || id == CarrierId && !chambered) return false;
            string reserveId = ""; float reserveCondition = 0; bool reserveFavourite = false;
            // Old single-round saves omit reserve and keep their exact magazine/chamber state.
            if (data["reserve"] != null)
            {
                var reserve = data["reserve"];
                if (!(reserve is JObject) || reserve["id"]?.Type != JTokenType.String || reserve["favourite"]?.Type != JTokenType.Boolean) return false;
                reserveId = reserve.Value<string>("id");
                reserveCondition = reserve.Value<float?>("condition") ?? float.NaN;
                reserveFavourite = reserve.Value<bool>("favourite");
                if (!Valid(reserveId, reserveCondition, reserveFavourite) || reserveId != "" && !chambered) return false;
            }
            Id = id; Condition = condition; Favourite = favourite; Chambered = chambered;
            SpawnRoundGranted = data.Value<bool?>("spawnRoundGranted") ?? false;
            _reserveId = reserveId; _reserveCondition = reserveCondition; _reserveFavourite = reserveFavourite;
            Revision = Math.Max(0, data.Value<long?>("revision") ?? 0); return true;
        }
        private static bool Valid(string id, float condition, bool favourite) => !float.IsNaN(condition) && !float.IsInfinity(condition)
            && (id == "" ? condition == 0 && !favourite : IsRound(id) && condition > 0 && condition <= 1);
    }
}
