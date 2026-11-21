using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DDEN.SaveEditor
{
    internal readonly struct SaveChange
    {
        internal readonly string Path, Before, After;
        internal SaveChange(string path, JToken before, JToken after)
        { Path = path; Before = Describe(before); After = Describe(after); }
        private static string Describe(JToken token)
        {
            if (token == null) return "（无）";
            if (token is JArray array) return array.Count + " 项";
            if (token.Type == JTokenType.Boolean) return token.Value<bool>() ? "开启" : "关闭";
            string value = token.Type == JTokenType.String ? token.Value<string>() : token.ToString(Formatting.None);
            return value.Length > 120 ? value.Substring(0, 117) + "…" : value;
        }
    }
    internal static class SaveChanges
    {
        internal static List<SaveChange> Compare(JObject before, JObject after)
        { var changes = new List<SaveChange>(); Visit("", before, after, changes); return changes; }
        private static void Visit(string path, JToken before, JToken after, List<SaveChange> changes)
        {
            if (JToken.DeepEquals(before, after)) return;
            if (before is JObject a && after is JObject b)
            {
                foreach (string key in a.Properties().Select(p => p.Name).Union(b.Properties().Select(p => p.Name)))
                    Visit((b[key] ?? a[key])?.Path ?? (path.Length == 0 ? key : path + "." + key), a[key], b[key], changes);
                return;
            }
            // Structural changes are one review entry, preventing index shifts from looking like hundreds of edits.
            if (before is JArray aa && after is JArray bb && aa.Count == bb.Count)
            { for (int i = 0; i < aa.Count; i++) Visit(path + "[" + i + "]", aa[i], bb[i], changes); return; }
            changes.Add(new SaveChange(path, before, after));
        }
    }
}
