using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace HeatingCageFix
{
    internal readonly struct CageKey : IEquatable<CageKey>
    {
        internal readonly ushort Id;
        internal readonly byte Version;
        internal CageKey(ushort id, byte version) { Id = id; Version = version; }
        public bool Equals(CageKey other) => Id == other.Id && Version == other.Version;
        public override bool Equals(object obj) => obj is CageKey key && Equals(key);
        public override int GetHashCode() => Id | (Version << 16);
    }
    internal static class CageWire
    {
        internal const int Batch = 128;
        private static bool Key(JToken value, out CageKey key)
        {
            key = default;
            if (!(value is JObject) || value["id"]?.Type != JTokenType.Integer || value["version"]?.Type != JTokenType.Integer) return false;
            if (!long.TryParse(value["id"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long id)
                || !long.TryParse(value["version"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long version)) return false;
            if (id <= 0 || id > ushort.MaxValue || version < 0 || version > byte.MaxValue) return false;
            key = new CageKey((ushort)id, (byte)version); return true;
        }
        internal static JObject Request(string nonce, IEnumerable<CageKey> keys)
        {
            var array = new JArray();
            foreach (var key in keys) array.Add(new JObject { ["id"] = key.Id, ["version"] = key.Version });
            return new JObject { ["nonce"] = nonce, ["items"] = array };
        }
        // Read-only queries: no client-provided switch state is ever applied on the host.
        internal static JObject Reply(JToken request, bool enabled, Func<CageKey, bool?> read)
        {
            if (!(request is JObject) || request["nonce"]?.Type != JTokenType.String
                || !System.Guid.TryParseExact(request.Value<string>("nonce"), "N", out _)
                || !(request["items"] is JArray items) || items.Count > Batch) return new JObject();
            var array = new JArray(); var seen = new HashSet<CageKey>();
            foreach (var entry in items)
            {
                if (!Key(entry, out var key) || !seen.Add(key)) return new JObject();
                bool? on = enabled ? read(key) : null;
                if (on.HasValue) array.Add(new JObject { ["id"] = key.Id, ["version"] = key.Version, ["on"] = on.Value });
            }
            return new JObject { ["nonce"] = request.Value<string>("nonce"), ["enabled"] = enabled, ["items"] = array };
        }
        internal static bool Response(JToken response, string nonce, ISet<CageKey> expected, out Dictionary<CageKey, bool> states, out bool enabled)
        {
            states = new Dictionary<CageKey, bool>(); enabled = false;
            if (!(response is JObject) || response["nonce"]?.Type != JTokenType.String || response.Value<string>("nonce") != nonce
                || response["enabled"]?.Type != JTokenType.Boolean || !(response["items"] is JArray items) || items.Count > Batch) return false;
            enabled = response.Value<bool>("enabled");
            if (!enabled && items.Count != 0) return false;
            foreach (var entry in items)
            {
                if (!Key(entry, out var key) || !expected.Contains(key) || entry["on"]?.Type != JTokenType.Boolean || states.ContainsKey(key)) return false;
                states.Add(key, entry.Value<bool>("on"));
            }
            return true;
        }
    }
}
