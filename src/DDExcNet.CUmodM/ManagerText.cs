using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace DDExcNet.CUmodM
{
    internal enum ManagerLanguage { Chinese, English }

    /// <summary>Manager presentation only. Never translate configuration definitions, policy data, routes or user text.</summary>
    internal static class ManagerText
    {
        private static readonly Dictionary<string, string> Chinese = Load("manager.zh-CN.txt");
        private static readonly Dictionary<string, string> English = Load("manager.en.txt");
        private static readonly string[] NoteKeys = { "note.legacy", "note.qol", "note.mp", "note.core", "note.legacyFile" };
        internal static ManagerLanguage Language
        {
            get => DDEN.Shared.DdenLocalization.Language == DDEN.Shared.DdenLanguage.English ? ManagerLanguage.English : ManagerLanguage.Chinese;
            set => DDEN.Shared.DdenLocalization.Language = value == ManagerLanguage.English ? DDEN.Shared.DdenLanguage.English : DDEN.Shared.DdenLanguage.Chinese;
        }
        internal static bool IsEnglish => Language == ManagerLanguage.English;
        internal static IEnumerable<string> Keys => Chinese.Keys;
        internal static bool HasEnglish(string key) => English.ContainsKey(key);
        internal static bool Has(string key) => Chinese.ContainsKey(key);
        internal static string Pattern(string key, ManagerLanguage language) => Resolve(Chinese, English, key, language);
        internal static string Resolve(IDictionary<string, string> chinese, IDictionary<string, string> english, string key, ManagerLanguage language)
        {
            if (language == ManagerLanguage.English && english.TryGetValue(key, out var en) && !string.IsNullOrEmpty(en)) return en;
            return chinese.TryGetValue(key, out var zh) && !string.IsNullOrEmpty(zh) ? zh : "…";
        }
        internal static string Get(string key, params object[] args)
        {
            string pattern = Pattern(key, Language);
            if (args == null || args.Length == 0) return pattern;
            try { return string.Format(CultureInfo.InvariantCulture, pattern, args); }
            catch (FormatException)
            {
                try { return string.Format(CultureInfo.InvariantCulture, Pattern(key, ManagerLanguage.Chinese), args); }
                catch (FormatException) { return Pattern(key, ManagerLanguage.Chinese); }
            }
        }
        internal static Dictionary<string, string> Parse(string text)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string raw in (text ?? "").Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                int eq = line.IndexOf('=');
                if (eq > 0) result[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim().Replace("\\n", "\n");
            }
            return result;
        }
        private static Dictionary<string, string> Load(string suffix)
        {
            try
            {
                var assembly = typeof(ManagerText).Assembly;
                string name = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(suffix, StringComparison.Ordinal));
                if (name != null)
                    using (var stream = assembly.GetManifestResourceStream(name))
                    using (var reader = new StreamReader(stream)) return Parse(reader.ReadToEnd());
            }
            catch { }
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
        internal static string ModuleName(string id, string original)
        {
            switch (id)
            {
                case "perfdeep": case "unitlistmgr": case "loongnova": return Get("module." + id);
                case "mpfix": return Get("module.mpfix");
                case "saveeditor": return Get("module.saveeditor");
                case "netperf": case "perf": case "netperftracker": return Get("module.perf");
                default: return original ?? "";
            }
        }
        internal static string ConfigKey(string raw)
        {
            if (raw == "忽略核对环境版本（IgnoreEnvVersion）") return "IgnoreEnvVersion";
            if (raw == "详细日志（Verbose）") return "Verbose";
            return raw;
        }
        internal static string CompatibilityNote(string raw)
        {
            foreach (var id in NoteKeys)
                if (raw == Pattern(id, ManagerLanguage.Chinese)) return Get(id);
            return raw ?? ""; // Third-party names, diagnostics and paths stay verbatim.
        }
        internal static string CompatibilityVersion(string raw)
        {
            const string suffix = " 个模组同时改它";
            if (IsEnglish && raw != null && raw.EndsWith(suffix, StringComparison.Ordinal)
                && int.TryParse(raw.Substring(0, raw.Length - suffix.Length), out int count)) return Get("compat.patchCount", count);
            return raw ?? "";
        }
    }
}
