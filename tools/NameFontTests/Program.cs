using System;
using System.Collections.Generic;
using DDEN.MPfix;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class Program
{
    private static int _checks;
    private static void Check(bool ok, string message) { _checks++; if (!ok) throw new Exception(message); }
    private static TMP_FontAsset Reset()
    {
        PlayerNameFonts.Dispose(); Time.unscaledTime = 0; SceneManager.Handle = 1; Locale.currentLangName = "zh-CN";
        Resources.Loaded = Array.Empty<UnityEngine.Object>(); Resources.Scans = 0; Font.Created = TMP_FontAsset.Created = 0;
        TMP_Settings.fallbackFontAssets = new List<TMP_FontAsset>();
        return TMP_Settings.defaultFontAsset = new TMP_FontAsset { name = "Game Main" };
    }
    private static TMP_Text Name(TMP_FontAsset font, string text = "中文玩家") => new TMP_Text { font = font, text = text };
    private static void Main()
    {
        try { Run(); Console.WriteLine("PASS " + _checks + " checks: production name-font policy with simulated font APIs; game fonts only, missing glyphs, late loading, private chains and restoration."); }
        finally { PlayerNameFonts.Dispose(); }
    }
    private static void Run()
    {
        var original = Reset(); original.Glyphs.UnionWith("中文玩家");
        var label = Name(original); var material = label.fontSharedMaterial;
        PlayerNameFonts.Prepare(label);
        Check(label.font == original && label.fontSharedMaterial == material, "native supported label font and material remain unchanged");
        Check(Font.Created == 0 && TMP_FontAsset.Created == 0, "native coverage creates no OS font or atlas");
        original = Reset();
        var game = new TMP_FontAsset("中文玩家") { name = "Game Chinese", sourceFontFile = new Font("中文玩家") };
        TMP_Settings.fallbackFontAssets.Add(game);
        var originalChain = original.fallbackFontAssetTable;
        label = Name(original); material = label.fontSharedMaterial;
        PlayerNameFonts.Prepare(label); var local = label.font;
        Check(local != original && local.fallbackFontAssetTable.Contains(game) && label.text == "中文玩家", "private name chain borrows native Chinese glyphs");
        Check(original.fallbackFontAssetTable == originalChain && originalChain.Count == 0, "original global fallback table stays unchanged");
        Check(local.atlasTextures == original.atlasTextures && local.atlasPopulationMode == AtlasPopulationMode.Static, "private copy shares native atlas read-only");
        Check(label.fontSharedMaterial == material && Font.Created == 0, "game-font fallback preserves original name material");
        int scans = Resources.Scans; PlayerNameFonts.Tick();
        Check(Resources.Scans == scans && label.font == local, "unchanged name does not recreate font or rescan assets");
        var ascii = new Font();
        Check(PlayerNameFonts.GuiName("中文玩家", ascii) == "中文玩家" && PlayerNameFonts.GuiFont(ascii) == game.sourceFontFile, "GUI names reuse the game's real source Font");
        var nativeGui = new Font("中文玩家");
        Check(PlayerNameFonts.GuiFont(nativeGui) == nativeGui, "compatible original GUI font wins ties");
        material = new Material(); label.fontSharedMaterial = material; PlayerNameFonts.Tick();
        PlayerNameFonts.Clear();
        Check(label.font == original && label.text == "中文玩家" && local.Destroyed, "disable restores original label and destroys only private copy");
        Check(label.fontSharedMaterial == material, "native name material changes survive later cleanup");
        Check(!original.Destroyed && !game.Destroyed && !original.atlasTextures[0].Destroyed && !original.material.Destroyed, "borrowed native assets survive cleanup");

        original = Reset(); game = new TMP_FontAsset("中") { name = "Game A" };
        var second = new TMP_FontAsset("文玩家") { name = "Game B" };
        TMP_Settings.fallbackFontAssets.Add(game); Resources.Loaded = new UnityEngine.Object[] { second };
        label = Name(original); PlayerNameFonts.Prepare(label);
        Check(label.text == "中文玩家" && Font.Created == 0, "multiple game fonts jointly cover one name");
        Check(game.fallbackFontAssetTable.Count == 0 && second.fallbackFontAssetTable.Count == 0, "native tables remain untouched when combining fonts");

        original = Reset(); label = Name(original, "A中文😀"); PlayerNameFonts.Prepare(label); local = label.font;
        Check(label.text == "A???", "unsupported glyphs become one question mark per Unicode scalar");
        Check(Font.Created == 0 && TMP_FontAsset.Created == 0, "missing Chinese never creates or loads a system font");
        Check(PlayerNameFonts.GuiName("A中文😀", ascii) == "A???", "GUI missing glyphs also become question marks without losing ASCII");
        scans = Resources.Scans;
        game = new TMP_FontAsset("中文") { name = "Late Game Font", sourceFontFile = new Font("中文") };
        Resources.Loaded = new UnityEngine.Object[] { game, local };
        Time.unscaledTime = 5; PlayerNameFonts.Tick();
        Check(Resources.Scans == scans && label.text == "A???", "missing game-font retries are rate limited");
        Time.unscaledTime = 11; PlayerNameFonts.Tick();
        Check(label.text == "A中文?" && local.fallbackFontAssetTable.Contains(game), "late native glyphs restore the original Chinese source name");
        Check(!local.fallbackFontAssetTable.Contains(local), "discovery excludes private copies and self cycles");
        Check(PlayerNameFonts.GuiName("A中文😀", ascii) == "A中文?", "GUI placeholder cache refreshes when game font becomes available");
        PlayerNameFonts.Clear();
        Check(label.text == "A中文😀" && label.font == original && local.Destroyed, "disable restores unmasked presentation source and native font");
        Check(!game.Destroyed && !original.atlasTextures[0].Destroyed, "cleanup leaves source fonts and atlases alive");

        original = Reset(); label = Name(original, "中文"); PlayerNameFonts.Prepare(label);
        label.text = "??"; PlayerNameFonts.Prepare(label); // A fresh native update is distinct from our previous placeholders.
        Time.unscaledTime = 11; game = new TMP_FontAsset("中文"); Resources.Loaded = new UnityEngine.Object[] { game }; PlayerNameFonts.Tick();
        Check(label.text == "??", "a fresh literal-question-mark name cannot resurrect an old source name");

        original = Reset(); game = new TMP_FontAsset("中文玩家"); TMP_Settings.fallbackFontAssets.Add(game);
        label = Name(original); PlayerNameFonts.Prepare(label);
        var otherModFont = new TMP_FontAsset("中文玩家"); label.font = otherModFont; PlayerNameFonts.Tick(); PlayerNameFonts.Clear();
        Check(label.font == otherModFont, "disable respects another mod's later font replacement");

        original = Reset(); game = new TMP_FontAsset("中文玩家"); TMP_Settings.fallbackFontAssets.Add(game);
        Check(PlayerNameFonts.GuiName("中文", ascii) == "??", "baked TMP atlas alone is not treated as a GUI Font file");
        game.sourceFontFile = new Font("中文玩家"); SceneManager.Handle = 2;
        Check(PlayerNameFonts.GuiName("中文", ascii) == "中文", "scene change refreshes GUI Font source and masked-name cache");
        scans = Resources.Scans; Locale.currentLangName = "zh-TW"; PlayerNameFonts.GuiFont(ascii);
        Check(Resources.Scans > scans, "language changes invalidate font candidates");

        original = Reset(); game = new TMP_FontAsset("中文玩家"); original.fallbackFontAssetTable.Add(game); game.fallbackFontAssetTable.Add(original);
        label = Name(original); PlayerNameFonts.Prepare(label);
        Check(label.font == original && label.text == "中文玩家", "native cyclic fallback tables terminate safely");
        scans = Resources.Scans; PlayerNameFonts.Prepare(Name(original, ""));
        Check(Resources.Scans == scans, "empty names do not trigger discovery");
        Check(NameGlyphRules.ReplaceMissing("中😀文", code => code == 0x1f600) == "?😀?", "supported supplementary glyph is kept as a complete scalar");
        Check(NameGlyphRules.ReplaceMissing("A\ud800B\udc00", _ => true) == "A?B?", "isolated surrogates are represented safely");
    }
}
