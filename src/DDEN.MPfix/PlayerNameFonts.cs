using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DDEN.MPfix
{
    /// <summary>Borrow game fonts first. Only name labels receive private fallback chains.</summary>
    internal static class PlayerNameFonts
    {
        private sealed class LabelState
        {
            internal WeakReference<TMP_Text> Label;
            internal TMP_FontAsset Original, Applied;
            internal Material Material;
            internal string Text, Rendered;
            internal int Revision = -1;
            internal bool MissingGameGlyphs;
        }
        private static readonly List<TMP_FontAsset> GameFonts = new List<TMP_FontAsset>();
        private static readonly List<Font> GuiFonts = new List<Font>();
        private static readonly Dictionary<TMP_FontAsset, TMP_FontAsset> LocalFonts = new Dictionary<TMP_FontAsset, TMP_FontAsset>();
        private static readonly List<LabelState> Labels = new List<LabelState>();
        private static readonly HashSet<char> GuiCharacters = new HashSet<char>();
        private static readonly Dictionary<Font, Dictionary<string, string>> GuiNames = new Dictionary<Font, Dictionary<string, string>>();
        private static Font _guiOriginal, _guiChoice;
        private static bool _scanned, _guiMissing;
        private static int _scene, _revision, _charactersRevision, _guiRevision = -1, _guiCharactersRevision = -1;
        private static string _language;
        private static float _nextRefresh;

        internal static void Observe(string name)
        {
            foreach (char c in name ?? "")
            {
                if (char.IsControl(c) || char.IsSurrogate(c) || char.IsWhiteSpace(c)) continue;
                if (GuiCharacters.Count >= 4096) { GuiCharacters.Clear(); _charactersRevision++; }
                if (GuiCharacters.Add(c)) _charactersRevision++;
            }
        }
        internal static Font GuiFont(Font original)
        {
            RefreshGameFonts(_guiMissing);
            if (_guiOriginal == original && _guiRevision == _revision && _guiCharactersRevision == _charactersRevision && _guiChoice != null) return _guiChoice;
            Font chosen = original;
            int score = Coverage(original);
            foreach (var font in GuiFonts)
            {
                int coverage = Coverage(font);
                if (coverage > score || chosen == null) { chosen = font; score = coverage; }
            }
            _guiMissing = score < GuiCharacters.Count;
            _guiOriginal = original; _guiChoice = chosen; _guiRevision = _revision; _guiCharactersRevision = _charactersRevision;
            return chosen;
        }
        private static int Coverage(Font font)
        {
            if (font == null) return -1;
            // A TMP atlas is not a Unity GUI font. Only real source Font assets can serve GUI lists.
            int count = 0;
            foreach (char c in GuiCharacters) if (font.HasCharacter(c)) count++;
            return count;
        }
        internal static string GuiName(string name, Font original)
        {
            Observe(name);
            var font = GuiFont(original);
            if (font == null) return NameGlyphRules.ReplaceMissing(name, _ => false);
            if (!GuiNames.TryGetValue(font, out var cache)) GuiNames[font] = cache = new Dictionary<string, string>(StringComparer.Ordinal);
            name ??= "";
            if (cache.TryGetValue(name, out string cached)) return cached;
            if (cache.Count >= 256) cache.Clear();
            return cache[name] = NameGlyphRules.ReplaceMissing(name, code => code <= char.MaxValue && font.HasCharacter((char)code));
        }
        private static bool Owned(TMP_FontAsset font)
        {
            foreach (var local in LocalFonts.Values) if (local == font) return true;
            return false;
        }
        private static void AddGameFont(TMP_FontAsset font)
        {
            if (font == null || Owned(font) || GameFonts.Contains(font)) return;
            GameFonts.Add(font);
            var source = font.sourceFontFile;
            if (source != null && !GuiFonts.Contains(source)) GuiFonts.Add(source);
            if (font.fallbackFontAssetTable != null) foreach (var next in font.fallbackFontAssetTable) AddGameFont(next);
        }
        private static void RefreshGameFonts(bool missing)
        {
            int scene = SceneManager.GetActiveScene().handle;
            string language = Locale.currentLangName;
            if (_scanned && _scene == scene && _language == language && (!missing || Time.unscaledTime < _nextRefresh)) return;
            _scene = scene; _language = language; _scanned = true; _nextRefresh = Time.unscaledTime + 10;
            GameFonts.Clear(); GuiFonts.Clear(); GuiNames.Clear();
            AddGameFont(TMP_Settings.defaultFontAsset);
            if (TMP_Settings.fallbackFontAssets != null) foreach (var font in TMP_Settings.fallbackFontAssets) AddGameFont(font);
            foreach (var font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>()) AddGameFont(font);
            _revision++;
        }
        private static TMP_FontAsset LocalFont(TMP_FontAsset original)
        {
            if (LocalFonts.TryGetValue(original, out var font) && font != null) return font;
            font = UnityEngine.Object.Instantiate(original);
            font.name = "DDEN Names - " + original.name;
            font.hashCode = TMP_TextUtilities.GetSimpleHashCode(font.name);
            font.hideFlags = HideFlags.HideAndDontSave;
            // Share the borrowed atlas read-only. Dynamic additions go to the original game font.
            font.atlasPopulationMode = AtlasPopulationMode.Static;
            LocalFonts[original] = font;
            return font;
        }
        internal static void Prepare(TMP_Text label) => Prepare(label, true);
        private static void Prepare(TMP_Text label, bool fresh)
        {
            if (label == null || label.font == null || string.IsNullOrEmpty(label.text)) return;
            LabelState state = null;
            foreach (var entry in Labels) if (entry.Label.TryGetTarget(out var target) && target == label) { state = entry; break; }
            if (state == null)
            {
                state = new LabelState { Label = new WeakReference<TMP_Text>(label), Original = label.font, Material = label.fontSharedMaterial };
                Labels.Add(state);
            }
            else if (label.font != state.Applied && label.font != state.Original)
            { state.Original = label.font; state.Material = label.fontSharedMaterial; state.Applied = null; state.Revision = -1; }
            else state.Material = label.fontSharedMaterial;
            RefreshGameFonts(state.MissingGameGlyphs);
            string source = fresh || state.Rendered == null || label.text != state.Rendered ? label.text : state.Text;
            if (state.Text == source && state.Revision == _revision && state.Rendered == label.text && (state.Applied == null || label.font == state.Applied)) return;
            state.Text = source; state.Revision = _revision;
            if (state.Original == null) return;
            if (state.Original.HasCharacters(source, out uint[] _, searchFallbacks: true, tryAddCharacter: true))
            { Restore(state, label); label.text = source; state.Rendered = source; state.MissingGameGlyphs = false; return; }

            var local = LocalFont(state.Original);
            var chain = new List<TMP_FontAsset> { state.Original };
            foreach (var game in GameFonts) if (game != state.Original && game != null) chain.Add(game);
            local.fallbackFontAssetTable = chain;
            state.MissingGameGlyphs = !local.HasCharacters(source, out uint[] _, searchFallbacks: true, tryAddCharacter: true);
            string rendered = state.MissingGameGlyphs ? NameGlyphRules.ReplaceMissing(source,
                code => local.HasCharacters(char.ConvertFromUtf32((int)code), out uint[] _, searchFallbacks: true, tryAddCharacter: true)) : source;
            label.font = local;
            if (state.Material != null) label.fontSharedMaterial = state.Material;
            label.text = rendered; state.Rendered = rendered;
            state.Applied = local; label.SetAllDirty();
        }
        private static void Restore(LabelState state, TMP_Text label)
        {
            if (state.Applied != null && label != null && label.font == state.Applied && state.Original != null)
            {
                label.font = state.Original;
                if (state.Material != null) label.fontSharedMaterial = state.Material;
                if (label.text == state.Rendered) label.text = state.Text;
                label.SetAllDirty();
            }
            state.Applied = null;
        }
        internal static void Tick()
        {
            for (int i = Labels.Count - 1; i >= 0; i--)
            {
                if (!Labels[i].Label.TryGetTarget(out var label) || label == null) { Labels.RemoveAt(i); continue; }
                Prepare(label, false);
            }
        }
        internal static void Clear()
        {
            foreach (var state in Labels) if (state.Label.TryGetTarget(out var label)) Restore(state, label);
            Labels.Clear();
            foreach (var local in LocalFonts.Values) if (local != null) UnityEngine.Object.Destroy(local);
            LocalFonts.Clear(); GameFonts.Clear(); GuiFonts.Clear(); GuiCharacters.Clear(); GuiNames.Clear();
            _guiOriginal = _guiChoice = null; _scanned = _guiMissing = false; _guiRevision = _guiCharactersRevision = -1;
            // Borrowed game fonts, atlases and materials are never destroyed or reconfigured.
        }
        internal static void Dispose()
        {
            Clear();
        }
    }
}
