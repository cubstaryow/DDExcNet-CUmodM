using System;
using System.Collections.Generic;
using BepInEx.Configuration;

namespace DDEN.MPfix
{
    /// <summary>Availability is a live execution constraint, independent of the manager and local/host preferences.</summary>
    internal sealed class FeatureDependencies
    {
        private readonly List<ConfigEntryBase> _entries;
        private readonly Func<string, bool> _loaded;
        private readonly Func<bool> _mpReady, _coreReady;
        internal FeatureDependencies(IEnumerable<ConfigEntryBase> entries, Func<string, bool> loaded,
            Func<bool> mpReady = null, Func<bool> coreReady = null)
        {
            _entries = new List<ConfigEntryBase>(entries); _loaded = loaded;
            _mpReady = mpReady ?? (() => loaded("CasualtiesMP"));
            _coreReady = coreReady ?? (() => loaded("net.cucorelib"));
        }
        internal string Reason(ConfigEntryBase entry)
        {
            if (entry == null) return null;
            string section = entry.Definition.Section, key = entry.Definition.Key;
            bool functional = section != "界面" && section != "调试" && section != "总开关";
            bool authority = section == "总开关" && key.Contains("UseHostAuthorityConfig");
            if (!functional && !authority) return null;
            if (!_loaded("CasualtiesMP")) return "缺少多人联机组件，已停用。";
            if (!_mpReady()) return "Together 未就绪";
            bool needsCore = authority || section == "无芯片同步" || section == "联机合成"
                || key.Contains("TraderSnapshotOnOpen") || key.Contains("QuietTraderUIModItems");
            if (needsCore && !_loaded("net.cucorelib")) return "缺少社区前置，已停用。";
            if (needsCore && !_coreReady()) return "CCL MP 未就绪";
            return null;
        }
        internal bool Available(ConfigEntryBase entry) => Reason(entry) == null;
        internal int BlockedCount { get { int count = 0; foreach (var entry in _entries) if (!Available(entry)) count++; return count; } }
        internal bool Enforce(ConfigEntryBase entry)
        {
            if (Available(entry)) return false;
            // Missing installations retain the existing saved-off behavior. A transient late
            // initialization only masks effective values; never erase preferences while waiting.
            if (Reason(entry)?.StartsWith("缺少", StringComparison.Ordinal) == true
                && entry.SettingType == typeof(bool) && entry.BoxedValue is bool enabled && enabled) entry.BoxedValue = false;
            return true;
        }
        internal void EnforceAll() { foreach (var entry in _entries) Enforce(entry); }
    }
}
