using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityEngine
{
    // Font/GPU calls are simulated; the production font-selection and cleanup code is linked unchanged.
    public partial class Object
    {
        public string name;
        public HideFlags hideFlags;
        public bool Destroyed;
        public static void Destroy(Object obj) { if (obj != null) obj.Destroyed = true; }
        public static T Instantiate<T>(T obj) where T : Object => (T)(Object)((TMPro.TMP_FontAsset)(Object)obj).Clone();
    }
    public enum HideFlags { HideAndDontSave }
    public class Material : Object { }
    public class Texture2D : Object { }
    public static class Time { public static float unscaledTime; }
    public class Font : Object
    {
        public static int Created;
        public static string[] Installed = { "Microsoft YaHei" };
        public static Font LastCreated;
        public readonly HashSet<char> Characters = new HashSet<char>();
        public Font(string chars = "") { foreach (char c in chars) Characters.Add(c); }
        public bool HasCharacter(char c) => c < 128 || Characters.Contains(c);
        public static string[] GetOSInstalledFontNames() => throw new Exception("OS font access is forbidden");
        public static Font CreateDynamicFontFromOSFont(string name, int size)
        { Created++; throw new Exception("OS font creation is forbidden"); }
    }
    public static class Resources
    {
        public static Object[] Loaded = Array.Empty<Object>();
        public static int Scans;
        public static T[] FindObjectsOfTypeAll<T>() where T : Object { Scans++; return Loaded.OfType<T>().Where(o => !o.Destroyed).ToArray(); }
    }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene { public int handle; }
    public static class SceneManager { public static int Handle; public static Scene GetActiveScene() => new Scene { handle = Handle }; }
}
namespace TMPro
{
    using UnityEngine;
    public enum AtlasPopulationMode { Static, Dynamic }
    public class TMP_FontAsset : Object
    {
        public static int Created;
        public static TMP_FontAsset LastCreated;
        public int hashCode;
        public readonly HashSet<char> Glyphs = new HashSet<char>();
        public Font sourceFontFile;
        public Material material = new Material();
        public Texture2D[] atlasTextures = { new Texture2D() };
        public List<TMP_FontAsset> fallbackFontAssetTable = new List<TMP_FontAsset>();
        public AtlasPopulationMode atlasPopulationMode;
        public TMP_FontAsset(string chars = "") { foreach (char c in chars) Glyphs.Add(c); }
        public TMP_FontAsset Clone() => new TMP_FontAsset(new string(Glyphs.ToArray())) { name = name, material = material, atlasTextures = atlasTextures, sourceFontFile = sourceFontFile, atlasPopulationMode = atlasPopulationMode, fallbackFontAssetTable = fallbackFontAssetTable };
        public static TMP_FontAsset CreateFontAsset(Font font)
        { Created++; throw new Exception("Creating a fallback atlas is forbidden"); }
        public bool HasCharacters(string text, out uint[] missing, bool searchFallbacks = false, bool tryAddCharacter = false)
        {
            missing = text.Where(c => !Has(c, new HashSet<TMP_FontAsset>(), searchFallbacks, tryAddCharacter)).Select(c => (uint)c).ToArray();
            return missing.Length == 0;
        }
        private bool Has(char c, HashSet<TMP_FontAsset> seen, bool search, bool add)
        {
            if (!seen.Add(this)) return false;
            if (c < 128 || Glyphs.Contains(c)) return true;
            if (add && atlasPopulationMode == AtlasPopulationMode.Dynamic && sourceFontFile?.HasCharacter(c) == true) { Glyphs.Add(c); return true; }
            return search && fallbackFontAssetTable != null && fallbackFontAssetTable.Any(f => f != null && f.Has(c, seen, search, add));
        }
    }
    public static class TMP_Settings
    {
        public static TMP_FontAsset defaultFontAsset;
        public static List<TMP_FontAsset> fallbackFontAssets = new List<TMP_FontAsset>();
    }
    public static class TMP_TextUtilities { public static int GetSimpleHashCode(string text) => text.GetHashCode(StringComparison.Ordinal); }
    public partial class TMP_Text : UnityEngine.Object
    {
        public TMP_FontAsset font;
        public Material fontSharedMaterial = new Material();
        public string text;
        public void SetAllDirty() { }
    }
}
public static class Locale { public static string currentLangName = "zh-CN"; }
namespace DDEN.MPfix
{
    internal static class MPfixPlugin { internal static Logger Log = new Logger(); internal static bool Active = true; internal static Setting CfgFixPlayerNames = new Setting(); }
    internal sealed class Logger { internal int Warnings; internal void Warn(string text) { Warnings++; } internal void Debug(string text) { } internal void Exception(Exception ex, string text) { throw new Exception(text, ex); } }
}
