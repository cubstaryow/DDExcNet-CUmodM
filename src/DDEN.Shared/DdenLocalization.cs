using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using Newtonsoft.Json;

namespace DDEN.Shared
{
    public enum DdenLanguage { Chinese, English }

    /// <summary>Presentation state only. Configuration identities and policy metadata remain verbatim.</summary>
    public enum DdenPerformanceImpact { Unspecified, None, Low, Medium, High }

    public class DdenConfigMetadata
    {
        public string DisplayName, Description, Problem, Method;
        public bool? RestartRequired;
        public bool? IsExperimental;
        public string ExperimentalNote;
        public DdenPerformanceImpact PerformanceImpact;
        public string PerformanceNote;
    }

    public interface IDdenConfigPresentation
    {
        DdenConfigMetadata Metadata(ConfigEntryBase entry);
        string Localize(string original, string language);
    }

    public static class DdenLocalization
    {
        private static DdenLanguage _language;
        public static int Revision { get; private set; }
        public static event Action Changed;
        public static DdenLanguage Language
        {
            get => _language;
            set
            {
                if (value != DdenLanguage.English) value = DdenLanguage.Chinese;
                if (_language == value) return;
                _language = value; Revision++;
                Changed?.Invoke();
            }
        }
        private static readonly Dictionary<ConfigFile, DdenTextCatalog> Catalogs = new Dictionary<ConfigFile, DdenTextCatalog>();
        public static void Register(ConfigFile config, DdenTextCatalog catalog) => Catalogs[config] = catalog;
        public static void Unregister(ConfigFile config) => Catalogs.Remove(config);
        public static string ConfigText(ConfigEntryBase entry, string text, IDdenConfigPolicy policy = null)
        {
            var presentation = policy as IDdenConfigPresentation;
            if (presentation != null) return presentation.Localize(text, Language == DdenLanguage.English ? "en" : "zh-CN") ?? text;
            return entry != null && Catalogs.TryGetValue(entry.ConfigFile, out var catalog) ? catalog.Text(text) : text;
        }
        public static string Common(string text)
        {
            if (Language != DdenLanguage.English) return text;
            switch (text)
            {
                case "开": return "On";
                case "关": return "Off";
                case "按下新键…": return "Press a key…";
                case "管理器 ≥ {0}（已校对 {1}）· Shared ≥ {2}（已校对 {3}）· API v{4}": return "Manager ≥ {0} (tested {1}) · Shared ≥ {2} (tested {3}) · API v{4}";
                case "功能": return "Function";
                case "修复的问题": return "Problem";
                case "解决方案": return "Method";
                case "实验性": return "Experimental";
                case "性能影响": return "Performance";
                case "生效方式": return "Activation";
                case "改完需要重启游戏才生效": return "Restart the game to apply changes";
                case "（主机权威模式）由主机统一配置。\n": return "(Host authority mode) Settings are controlled by the host.\n";
                default: return text;
            }
        }
    }

    /// <summary>Exact UI strings and composite format templates; never translate user input by replacement.</summary>
    public sealed class DdenTextCatalog
    {
        private readonly Dictionary<string, string> _english;
        public DdenTextCatalog(Assembly assembly, string resource)
        {
            using (var stream = assembly.GetManifestResourceStream(resource))
            using (var reader = stream == null ? null : new StreamReader(stream))
                _english = reader == null ? new Dictionary<string, string>()
                    : JsonConvert.DeserializeObject<Dictionary<string, string>>(reader.ReadToEnd()) ?? new Dictionary<string, string>();
        }
        public string Text(string chinese) => DdenLocalization.Language == DdenLanguage.English && chinese != null
            && _english.TryGetValue(chinese, out var english) && !string.IsNullOrEmpty(english) ? english : chinese;
        public string Format(FormattableString text)
        {
            try { return string.Format(CultureInfo.CurrentCulture, Text(text.Format), text.GetArguments()); }
            catch (FormatException) { return text.ToString(CultureInfo.CurrentCulture); }
        }
    }
}
