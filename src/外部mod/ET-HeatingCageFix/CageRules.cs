using Newtonsoft.Json.Linq;

namespace HeatingCageFix
{
    internal static class CageRules
    {
        internal const string ItemId = "heatingcage";
        internal static bool HeatRequest(bool specific, bool liquid, string quality) => !specific && !liquid && quality == "heatsource";
        internal static float DecayMultiplier(bool on) => on ? 1f : 0f;
        internal static JToken Save(JToken original, bool on)
        {
            if (original != null && !(original is JObject)) return original;
            var result = original as JObject ?? new JObject();
            result["cagefix"] = new JObject { ["version"] = 1, ["on"] = on };
            return result;
        }
        internal static bool? Load(JToken payload)
        {
            if (!(payload is JObject root) || !(root["cagefix"] is JObject data) || data["version"]?.Type != JTokenType.Integer
                || data["version"].ToString() != "1" || data["on"]?.Type != JTokenType.Boolean) return null;
            return data.Value<bool>("on");
        }
    }
}
