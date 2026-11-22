using BepInEx.Configuration;

// Only GUI drawing and clicks are simulated; the catalog, own-settings renderer and BepInEx entries are real.
namespace UnityEngine
{
    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }
    public struct Rect
    {
        public float x, y, width, height;
        public float xMax => x + width;
        public float yMax => y + height;
        public Rect(float x, float y, float width, float height) { this.x = x; this.y = y; this.width = width; this.height = height; }
    }
    public static class Mathf { public static float Max(float a, float b) => Math.Max(a, b); }
    public static class GUI
    {
        public static string Focused;
        public static string GetNameOfFocusedControl() => Focused;
        public static void SetNextControlName(string name) { }
    }
}
namespace DDEN.Shared
{
    public static class DdenHub { public static event Action Changed; public static void Notify() => Changed?.Invoke(); }
    public interface IDdenModule { string Id { get; } string Version { get; } }
    public enum DdenHealth { Ok, Warn, Severe }
    public interface IDdenConfigAvailability { string ConfigUnavailableReason(ConfigEntryBase entry); }
    public static class ConfigExt { public static IEnumerable<ConfigEntryBase> Entries(ConfigFile cfg) => cfg.Select(pair => pair.Value); }
    public static class Log { public static void Debug(string text) {} public static void Info(string text) {} }
    public interface IDdenConfigPolicy
    {
        bool CanEditConfig(ConfigEntryBase entry, bool normallyInteractive);
        object ConfigDisplayValue(ConfigEntryBase entry);
        string ConfigNotice { get; }
    }
    public sealed class PresentationPolicy : IDdenConfigPolicy, IDdenConfigPresentation
    {
        public DdenConfigMetadata Data;
        public bool CanEditConfig(ConfigEntryBase entry, bool normallyInteractive) => normallyInteractive;
        public object ConfigDisplayValue(ConfigEntryBase entry) => entry.BoxedValue;
        public string ConfigNotice => null;
        public DdenConfigMetadata Metadata(ConfigEntryBase entry) => Data;
        public string Localize(string original, string language) => language == "en" && original == "仅在打开界面时" ? "Only when opening the UI" : original;
    }
    public static class KeyCapture
    {
        public static bool Active;
        public static ConfigEntryBase Target;
        public static void Begin(ConfigEntryBase target) { Target = target; Active = true; }
    }
    public static class DdenUI
    {
        public const float ConfigRowHeight = 46;
        public static object Row = new(), Sub = new();
        public static UnityEngine.Color Warn = new(1, .6f, .3f), Accent = new(.3f, .7f, 1);
        public static string NextText;
        public static bool ClickButton, ClickCheck;
        public static List<string> Labels = new();
        public static void Section(UnityEngine.Rect r, string text) => Labels.Add(text);
        public static void Label(UnityEngine.Rect r, string text, object style = null) => Labels.Add(text);
        public static void Fill(UnityEngine.Rect r, UnityEngine.Color c) { }
        public static float ConfigRow(UnityEngine.Rect r, ConfigEntryBase e) => ConfigRowHeight;
        public static string ElideToWidth(string text, float w, object style = null) => text;
        public static bool CheckBoxLabeled(UnityEngine.Rect r, bool value, string text) { Labels.Add(text); bool clicked = ClickCheck; ClickCheck = false; return clicked ? !value : value; }
        public static bool Button(UnityEngine.Rect r, string text) { Labels.Add(text); bool clicked = ClickButton; ClickButton = false; return clicked; }
        public static string TextField(UnityEngine.Rect r, string text) { var next = NextText; NextText = null; return next ?? text; }
        public static void Hover(UnityEngine.Rect r, string text) { }
    }
}

// L1 fixture has the documented BepInEx/string-only method signatures.
public static class ExternalBridgeFixture
{
    public static ConfigFile DdenConfig;
    public static int LanguageCalls;
    public static string Language;
    public static IDictionary<string,string> DdenConfigMetadata(ConfigEntryBase entry)
        => new Dictionary<string,string> { ["experimental"] = "true", ["performance"] = "high", ["performanceNote"] = "fixture note" };
    public static string DdenLocalize(string text, string language) => language == "en" && text == "fixture note" ? "English fixture note" : text;
    public static void DdenSetLanguage(string language) { Language = language; LanguageCalls++; }
}

public sealed class VersionedModuleFixture : DDEN.Shared.IDdenModule, DDEN.Shared.IDdenModuleVersion
{
    public string Id { get; set; }
    public string Version { get; set; }
    public string MinimumManagerVersion { get; set; }
    public string TestedManagerVersion { get; set; }
    public string MinimumSharedVersion { get; set; }
    public string TestedSharedVersion { get; set; }
    public int RequiredSharedApiVersion { get; set; }
}

public sealed class LegacyVersionFixture : DDEN.Shared.IDdenModule
{
    public string Id => "mpfix";
    public string Version => throw new Exception("must not read a legacy version while checking requirements");
}
public sealed class BrokenVersionFixture : DDEN.Shared.IDdenModule, DDEN.Shared.IDdenModuleVersion
{
    public int Reads;
    public string Id => "broken";
    public string Version => "1.0";
    public string MinimumManagerVersion { get { Reads++; throw new Exception("fixture"); } }
    public string TestedManagerVersion => "2.0";
    public string MinimumSharedVersion => "2.0";
    public string TestedSharedVersion => "2.0";
    public int RequiredSharedApiVersion => 1;
}

public sealed class EmptyVersionFixture : DDEN.Shared.IDdenModule, DDEN.Shared.IDdenModuleVersion
{
    public string Id => "mpfix";
    public string Version => throw new Exception("no requirements; version must not be read");
    public string MinimumManagerVersion => null;
    public string TestedManagerVersion => null;
    public string MinimumSharedVersion => null;
    public string TestedSharedVersion => null;
    public int RequiredSharedApiVersion => 0;
}
