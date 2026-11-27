using System;
using Newtonsoft.Json.Linq;

namespace DDEN.LoongNova
{
    internal static class MedicalCatalog
    {
        internal const string InjectorId = "dden.loongnova.medical-injector";
        internal const string RegenerationId = "dden.loongnova.regeneration-ampoule";
        internal const string MedicalId = "dden.loongnova.medical-ampoule";
        internal const string EmptyId = "dden.loongnova.empty-ampoule";
        internal const string DataKey = "loongnova.medicalAmpoule";
        internal static bool IsAmpoule(string id) => id == RegenerationId || id == MedicalId;
        internal static string NameKey(string id) => id == RegenerationId ? "regenerationName" : id == MedicalId ? "medicalName" : id == EmptyId ? "emptyAmpouleName" : "";
    }

    internal sealed class AmpouleState
    {
        internal string Id { get; private set; } = "";
        internal float Condition { get; private set; }
        internal bool Favourite { get; private set; }
        internal long Revision { get; private set; }
        internal bool SpawnDoseGranted { get; private set; }
        internal bool Ready => MedicalCatalog.IsAmpoule(Id);
        internal bool Spent => Id == MedicalCatalog.EmptyId;
        internal bool Loaded => Ready || Spent;
        internal bool Load(string id, float condition, bool favourite)
        {
            if (Loaded || !MedicalCatalog.IsAmpoule(id) || !Valid(condition) || Revision == long.MaxValue) return false;
            Id = id; Condition = condition; Favourite = favourite; Revision++; return true;
        }
        internal bool Consume(out string id)
        {
            id = "";
            if (!Ready || Revision == long.MaxValue) return false;
            id = Id; Id = MedicalCatalog.EmptyId; Condition = 1; Favourite = false; Revision++; return true;
        }
        internal bool TryGrantSpawnDose()
        {
            if (SpawnDoseGranted || Loaded || !Load(MedicalCatalog.MedicalId, 1, false)) return false;
            SpawnDoseGranted = true; return true;
        }
        internal void Clear() { Id = ""; Condition = 0; Favourite = false; if (Revision < long.MaxValue) Revision++; }
        internal JObject Capture() => new JObject
        {
            ["schema"] = "medical-ampoule.v1", ["id"] = Id, ["condition"] = Condition,
            ["favourite"] = Favourite, ["revision"] = Revision, ["spawnDoseGranted"] = SpawnDoseGranted
        };
        internal bool Restore(JToken data)
        {
            if (!(data is JObject) || data.Value<string>("schema") != "medical-ampoule.v1") return false;
            string id = data.Value<string>("id");
            var cond = data["condition"]; var fav = data["favourite"]; var rev = data["revision"];
            if (id == null || cond == null || (cond.Type != JTokenType.Float && cond.Type != JTokenType.Integer)
                || fav?.Type != JTokenType.Boolean || rev?.Type != JTokenType.Integer) return false;
            float condition = cond.Value<float>(); bool favourite = fav.Value<bool>(); long revision = rev.Value<long>();
            var grant = data["spawnDoseGranted"];
            if (grant != null && grant.Type != JTokenType.Boolean) return false;
            if (revision < 0 || (id == "" ? condition != 0 || favourite : !(MedicalCatalog.IsAmpoule(id) || id == MedicalCatalog.EmptyId) || !Valid(condition))) return false;
            Id = id; Condition = condition; Favourite = favourite; Revision = revision; SpawnDoseGranted = grant?.Value<bool>() ?? false; return true;
        }
        private static bool Valid(float condition) => !float.IsNaN(condition) && !float.IsInfinity(condition) && condition > 0 && condition <= 1;
    }
}
