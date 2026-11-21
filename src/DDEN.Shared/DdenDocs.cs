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
            public string What;          // 功能：这个开关做什么
            public string Fixes;         // 修复的问题：不开会看到什么现象
            public string How;           // 解决方案：大致怎么做到的（一句话，不写代码细节）
            public string Experimental;  // 实验性
            public string Perf;          // 性能影响
            public bool NeedsRestart;     // 改完必须重启游戏才生效
        }

        private static readonly Dictionary<string, Doc> Table =
            new Dictionary<string, Doc>(StringComparer.Ordinal);

        public static void Add(string section, string key, string what, string fixes, string how,
                               string experimental = "否", string perf = "无", bool needsRestart = false)
        {
            Table[section + "/" + key] = new Doc
            {
                What = what, Fixes = fixes, How = how, Experimental = experimental, Perf = perf,
                NeedsRestart = needsRestart
            };
        }

        public static bool TryGet(ConfigEntryBase entry, out Doc doc)
        {
            doc = null;
            if (entry?.Definition == null) return false;
            return Table.TryGetValue(entry.Definition.Section + "/" + entry.Definition.Key, out doc);
        }

        /// <summary>提示框正文：固定五项，缺哪项就不写哪项。</summary>
        public static string Format(ConfigEntryBase entry)
        {
            if (!TryGet(entry, out Doc d))
            {
                return entry?.Description?.Description ?? "";
            }

            var sb = new System.Text.StringBuilder();
            void Line(string label, string value)
            {
                if (string.IsNullOrEmpty(value)) return;
                sb.Append(label).Append('：').Append(value).Append('\n');
            }

            Line("功能", d.What);
            Line("修复的问题", d.Fixes);
            Line("解决方案", d.How);
            Line("实验性", d.Experimental);
            Line("性能影响", d.Perf);
            if (d.NeedsRestart) Line("生效方式", "改完需要重启游戏才生效");
            return sb.ToString().TrimEnd('\n');
        }
    }
}
