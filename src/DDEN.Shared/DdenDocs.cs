using System;
using System.Collections.Generic;
using BepInEx.Configuration;

namespace DDEN.Shared
{
    /// <summary>
    /// 配置项的“人话说明”（2026-11 用户口径）。
    ///
    /// 界面上**只显示一行**：这个开关**做什么**（不含怎么修、不含修过什么）。
    /// 鼠标移到那一行时，提示框才展开完整信息，固定五项：
    ///   功能 / 修复的问题 / 解决方案 / 实验性 / 性能影响
    ///
    /// 键 = `分区/键名`（BepInEx 的 Section + Key 拼起来），例如 `联机修正/修“挖方块”-伤害重算（SyncBlockBreak）`。
    /// 找不到条目时，提示框只显示配置自带的说明（不编造内容）。
    /// </summary>
    public static class DdenDocs
    {
        public sealed class Doc
        {
            public bool? IsExperimental;
            public DdenPerformanceImpact PerformanceImpact;
            public string ExperimentalNote, PerformanceNote;
            public string What;          // 功能：这个开关做什么
            public string Fixes;         // 修复的问题：不开会看到什么现象
            public string How;           // 解决方案：大致怎么做到的（一句话，不写代码细节）
            public string Experimental;  // 实验性
            public string Perf;          // 性能影响
            public bool NeedsRestart;     // 改完必须重启游戏才生效
        }

        private static readonly Dictionary<string, Doc> Table =
            new Dictionary<string, Doc>(StringComparer.Ordinal);

        // Keep the original eight-argument ABI for already compiled L1 providers and reflection registrations.
        public static void Add(string section, string key, string what, string fixes, string how,
                               string experimental = "否", string perf = "无", bool needsRestart = false)
        {
            Table[section + "/" + key] = new Doc { What = what, Fixes = fixes, How = how,
                Experimental = experimental, Perf = perf, NeedsRestart = needsRestart };
        }

        public static void AddStructured(string section, string key, string what, string fixes, string how,
                               bool isExperimental, DdenPerformanceImpact performanceImpact = DdenPerformanceImpact.Unspecified,
                               string experimentalNote = null, string performanceNote = null, bool needsRestart = false)
        {
            Table[section + "/" + key] = new Doc { What = what, Fixes = fixes, How = how,
                IsExperimental = isExperimental, PerformanceImpact = performanceImpact,
                ExperimentalNote = experimentalNote, PerformanceNote = performanceNote, NeedsRestart = needsRestart };
        }

        public static bool TryGet(ConfigEntryBase entry, out Doc doc)
        {
            doc = null;
            if (entry?.Definition == null) return false;
            return Table.TryGetValue(entry.Definition.Section + "/" + entry.Definition.Key, out doc);
        }

        public static DdenPerformanceImpact ParseImpact(string value)
        {
            switch (value?.Trim().ToLowerInvariant())
            {
                case "none": return DdenPerformanceImpact.None;
                case "low": return DdenPerformanceImpact.Low;
                case "medium": return DdenPerformanceImpact.Medium;
                case "high": return DdenPerformanceImpact.High;
                default: return DdenPerformanceImpact.Unspecified;
            }
        }
        private static DdenPerformanceImpact LegacyImpact(string value)
        {
            if (string.IsNullOrEmpty(value)) return DdenPerformanceImpact.Unspecified;
            foreach (string prefix in new[] { "极低", "无", "低", "中", "高", "None", "Low", "Medium", "High" })
            {
                if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (value.Length != prefix.Length && " （(，,·:：;；".IndexOf(value[prefix.Length]) < 0) continue;
                switch (prefix)
                {
                    case "无": case "None": return DdenPerformanceImpact.None;
                    case "极低": case "低": case "Low": return DdenPerformanceImpact.Low;
                    case "中": case "Medium": return DdenPerformanceImpact.Medium;
                    case "高": case "High": return DdenPerformanceImpact.High;
                }
            }
            return DdenPerformanceImpact.Unspecified;
        }
        private static string LegacyNote(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            int open = value.IndexOf('（'); if (open < 0) open = value.IndexOf('(');
            if (open >= 0)
            {
                string note = value.Substring(open + 1);
                return note.EndsWith("）", StringComparison.Ordinal) || note.EndsWith(")", StringComparison.Ordinal) ? note.Substring(0, note.Length - 1) : note;
            }
            foreach (string prefix in new[] { "Yes", "No", "是", "否", "极低", "无", "低", "中", "高" })
                if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return value.Substring(prefix.Length).TrimStart(' ', '·', '，', ',', ':', '：', '-', '—');
            return null;
        }
        private static string ImpactLabel(DdenPerformanceImpact impact)
        {
            if (DdenLocalization.Language == DdenLanguage.English) return impact.ToString();
            switch (impact)
            {
                case DdenPerformanceImpact.None: return "无";
                case DdenPerformanceImpact.Low: return "低";
                case DdenPerformanceImpact.Medium: return "中";
                case DdenPerformanceImpact.High: return "高";
                default: return "";
            }
        }
        private static string WithNote(ConfigEntryBase entry, string label, string note, IDdenConfigPolicy policy)
            => string.IsNullOrEmpty(note) ? label : label + (DdenLocalization.Language == DdenLanguage.English ? ", " : "，") + DdenLocalization.ConfigText(entry, note, policy);

        /// <summary>Display names never change the underlying configuration definition.</summary>
        public static string ConfigLabel(ConfigEntryBase entry, IDdenConfigPolicy policy = null)
            => (policy as IDdenConfigPresentation)?.Metadata(entry)?.DisplayName ?? entry?.Definition?.Key ?? "?";
        public static string ConfigDescription(ConfigEntryBase entry, IDdenConfigPolicy policy = null)
            => (policy as IDdenConfigPresentation)?.Metadata(entry)?.Description ?? entry?.Description?.Description;

        public static bool IsExperimental(ConfigEntryBase entry, IDdenConfigPolicy policy = null)
        {
            bool? explicitState = (policy as IDdenConfigPresentation)?.Metadata(entry)?.IsExperimental;
            if (explicitState.HasValue) return explicitState.Value;
            if (!TryGet(entry, out var doc)) return false;
            if (doc.IsExperimental.HasValue) return doc.IsExperimental.Value;
            return !string.IsNullOrEmpty(doc.Experimental) &&
                (doc.Experimental.StartsWith("是", StringComparison.Ordinal) || doc.Experimental.StartsWith("Yes", StringComparison.OrdinalIgnoreCase));
        }

        public static string Format(ConfigEntryBase entry) => Format(entry, null);

        public static string Format(ConfigEntryBase entry, IDdenConfigPolicy policy)
        {
            TryGet(entry, out Doc d);
            var metadata = (policy as IDdenConfigPresentation)?.Metadata(entry);
            if (d == null && metadata == null)
            {
                return DdenLocalization.ConfigText(entry, entry?.Description?.Description ?? "", policy);
            }

            var sb = new System.Text.StringBuilder();
            void Line(string label, string value)
            {
                if (string.IsNullOrEmpty(value)) return;
                sb.Append(DdenLocalization.Common(label)).Append(DdenLocalization.Language == DdenLanguage.English ? ": " : "：").Append(DdenLocalization.ConfigText(entry, DdenLocalization.Common(value), policy)).Append('\n');
            }

            Line("功能", metadata?.Description ?? d?.What ?? entry?.Description?.Description);
            Line("修复的问题", metadata?.Problem ?? d?.Fixes);
            Line("解决方案", metadata?.Method ?? d?.How);
            string experimental = DdenLocalization.Language == DdenLanguage.English ? (IsExperimental(entry, policy) ? "Yes" : "No") : (IsExperimental(entry, policy) ? "是" : "否");
            string experimentNote = metadata?.ExperimentalNote ?? d?.ExperimentalNote;
            if (experimentNote == null && metadata?.IsExperimental.HasValue != true) experimentNote = LegacyNote(d?.Experimental);
            Line("实验性", WithNote(entry, experimental, experimentNote, policy));
            var impact = metadata != null && metadata.PerformanceImpact != DdenPerformanceImpact.Unspecified
                ? metadata.PerformanceImpact : d?.PerformanceImpact ?? DdenPerformanceImpact.Unspecified;
            string perfNote = metadata?.PerformanceNote ?? d?.PerformanceNote;
            if (impact == DdenPerformanceImpact.Unspecified) impact = LegacyImpact(d?.Perf);
            if (perfNote == null) perfNote = LegacyNote(d?.Perf);
            Line("性能影响", impact == DdenPerformanceImpact.Unspecified ? d?.Perf : WithNote(entry, ImpactLabel(impact), perfNote, policy));
            if (metadata?.RestartRequired ?? d?.NeedsRestart == true) Line("生效方式", "改完需要重启游戏才生效");
            return sb.ToString().TrimEnd('\n');
        }
    }
}
