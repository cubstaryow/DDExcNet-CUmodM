using System;
using System.Collections.Generic;
using BepInEx.Configuration;

namespace DDEN.MPfix
{
    /// <summary>Missing dependencies constrain local and host-provided values alike.</summary>
    internal sealed class FeatureDependencies
    {
        private readonly Dictionary<ConfigEntryBase, string> _blocked = new Dictionary<ConfigEntryBase, string>();
        internal FeatureDependencies(IEnumerable<ConfigEntryBase> entries, Func<string, bool> loaded)
        {
            bool mp = loaded("CasualtiesMP"), core = loaded("net.cucorelib");
            foreach (var entry in entries)
            {
                string section = entry.Definition.Section, key = entry.Definition.Key;
                bool functional = section != "界面" && section != "调试" && section != "总开关";
                bool authority = section == "总开关" && key.Contains("UseHostAuthorityConfig");
                if (!functional && !authority) continue;
                bool needsCore = authority || section == "无芯片同步" || section == "穿戴兜底" || section == "联机合成"
                    || key.Contains("TraderSnapshotOnOpen")
                    || key.Contains("QuietTraderUIModItems") || key.Contains("AggressiveConverge") || key.Contains("BlockBreakMinFraction");
                string reason = !mp ? "缺少多人联机组件，已停用。" : needsCore && !core ? "缺少社区前置，已停用。" : null;
                if (reason != null) _blocked.Add(entry, reason);
            }
        }
        internal string Reason(ConfigEntryBase entry) => entry != null && _blocked.TryGetValue(entry, out var reason) ? reason : null;
        internal bool Available(ConfigEntryBase entry) => Reason(entry) == null;
        internal int BlockedCount => _blocked.Count;
        internal bool Enforce(ConfigEntryBase entry)
        {
            if (Available(entry)) return false;
            if (entry.SettingType == typeof(bool) && entry.BoxedValue is bool enabled && enabled) entry.BoxedValue = false;
            return true;
        }
        internal void EnforceAll() { foreach (var entry in _blocked.Keys) Enforce(entry); }
    }
}
