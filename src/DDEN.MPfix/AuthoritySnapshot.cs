using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace DDEN.MPfix
{
    internal sealed class AuthorityValue
    {
        internal string Type, Text;
    }
    internal sealed class AuthoritySnapshot
    {
        internal const int Protocol = 1;
        internal string Session;
        internal long Revision;
        internal bool Enabled;
        internal readonly Dictionary<string, AuthorityValue> Values = new Dictionary<string, AuthorityValue>(StringComparer.Ordinal);
        internal JObject ToJson(bool includeValues)
        {
            var result = new JObject { ["protocol"] = Protocol, ["session"] = Session, ["revision"] = Revision, ["enabled"] = Enabled };
            if (Enabled && includeValues)
            {
                var values = new JObject();
                foreach (var item in Values) values[item.Key] = new JObject { ["type"] = item.Value.Type, ["value"] = item.Value.Text };
                result["values"] = values;
            }
            return result;
        }
        internal static bool TryRead(JToken token, out AuthoritySnapshot snapshot)
        {
            snapshot = null;
            try
            {
                if (!(token is JObject o) || o.Value<int?>("protocol") != Protocol ||
                    o["enabled"]?.Type != JTokenType.Boolean || o["revision"]?.Type != JTokenType.Integer) return false;
                string session = o.Value<string>("session");
                long revision = o.Value<long>("revision");
                if (!Guid.TryParseExact(session, "N", out _) || revision < 1) return false;
                var next = new AuthoritySnapshot { Session = session, Revision = revision, Enabled = o.Value<bool>("enabled") };
                if (!next.Enabled) { if (o["values"] != null) return false; snapshot = next; return true; }
                if (!(o["values"] is JObject values) || values.Count > 64) return false;
                foreach (var p in values.Properties())
                {
                    if (p.Name.Length > 160 || !(p.Value is JObject v) || v["type"]?.Type != JTokenType.String || v["value"]?.Type != JTokenType.String) return false;
                    string type = v.Value<string>("type"), text = v.Value<string>("value");
                    if (type.Length > 100 || text.Length > 256) return false;
                    next.Values.Add(p.Name, new AuthorityValue { Type = type, Text = text });
                }
                snapshot = next; return true;
            }
            catch { return false; }
        }
    }
}
