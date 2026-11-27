using Newtonsoft.Json.Linq;
namespace DDEN.LoongNova
{
    internal static class CannonSaveState
    {
        internal static JObject Capture(CannonAmmoState ammo, EnergyState legacy) => new JObject
        {
            ["schema"] = "single-round.v1", ["ammo"] = ammo.Capture(), ["legacyCell"] = legacy.Capture(),
            ["revision"] = ammo.Revision + legacy.Revision
        };
        internal static bool Restore(CannonAmmoState ammo, EnergyState legacy, JToken data)
        {
            if (data is JObject && data["schema"]?.Value<string>() == "single-round.v1")
            {
                var roundCheck = new CannonAmmoState(); var legacyCheck = new EnergyState();
                if (!roundCheck.Restore(data["ammo"]) || !legacyCheck.Restore(data["legacyCell"])
                    || roundCheck.Loaded && legacyCheck.Loaded) return false;
                ammo.Restore(data["ammo"]); legacy.Restore(data["legacyCell"]); return true;
            }
            var oldCell = new EnergyState();
            if (!oldCell.Restore(data)) return false;
            ammo.Clear(); legacy.Restore(data); return true;
        }
    }
}
