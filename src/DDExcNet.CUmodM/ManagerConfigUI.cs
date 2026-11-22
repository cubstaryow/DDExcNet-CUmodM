using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using DDEN.Shared;
using UnityEngine;

namespace DDExcNet.CUmodM
{
    /// <summary>Only the manager's own settings. All controls write the original entries, never translated proxies.</summary>
    internal static class ManagerConfigUI
    {
        private static readonly Dictionary<ConfigEntryBase, string> NumberBuffers = new Dictionary<ConfigEntryBase, string>();
        internal static bool Experimental(ConfigEntryBase entry) => DdenDocs.IsExperimental(entry);

        internal static float Draw(Rect area, IEnumerable<ConfigEntryBase> entries)
        {
            float y = 0;
            string section = null;
            foreach (var entry in entries)
            {
                string rawSection = entry.Definition.Section;
                if (rawSection != section)
                {
                    section = rawSection; y += 6;
                    string id = "section." + rawSection;
                    DdenUI.Section(new Rect(area.x, area.y + y, area.width, 16), ManagerText.Has(id) ? ManagerText.Get(id) : rawSection);
                    y += 20;
                }
                Row(new Rect(area.x, area.y + y, area.width, DdenUI.ConfigRowHeight), entry);
                y += DdenUI.ConfigRowHeight;
            }
            return y;
        }
        private static void Row(Rect r, ConfigEntryBase entry)
        {
            string key = ManagerText.ConfigKey(entry.Definition.Key);
            if (!ManagerText.Has("cfg." + key)) { DdenUI.ConfigRow(r, entry); return; }
            object value = entry.BoxedValue;
            bool on = value is bool enabled && enabled;
            DdenUI.Fill(r, new Color(1, 1, 1, on ? 0.035f : 0.015f));
            DdenUI.Fill(new Rect(r.x, r.y, 3, r.height), Experimental(entry) ? DdenUI.Warn : on ? DdenUI.Accent : new Color(1, 1, 1, 0.14f));
            float keyWidth = Mathf.Max(120, r.width - 144);
            DdenUI.Label(new Rect(r.x + 10, r.y + 3, keyWidth, 18),
                DdenUI.ElideToWidth(ManagerText.Get("cfg." + key), keyWidth, DdenUI.Row), DdenUI.Row);
            Rect field = new Rect(r.xMax - 128, r.y + 3, 120, 18);
            try
            {
                if (value is bool b)
                {
                    bool next = DdenUI.CheckBoxLabeled(field, b, ManagerText.Get(b ? "control.on" : "control.off"));
                    if (next != b) entry.BoxedValue = next;
                }
                else if (value is ManagerLanguage language)
                {
                    if (DdenUI.Button(field, language == ManagerLanguage.English ? "English" : "简体中文"))
                        entry.BoxedValue = language == ManagerLanguage.English ? ManagerLanguage.Chinese : ManagerLanguage.English;
                }
                else if (value is float f)
                {
                    string control = "dden.manager." + entry.Definition.Section + "/" + entry.Definition.Key;
                    if (!NumberBuffers.TryGetValue(entry, out var buffer) || GUI.GetNameOfFocusedControl() != control)
                        buffer = f.ToString("0.###", CultureInfo.InvariantCulture);
                    GUI.SetNextControlName(control);
                    buffer = DdenUI.TextField(field, buffer);
                    NumberBuffers[entry] = buffer;
                    if ((float.TryParse(buffer, NumberStyles.Float, CultureInfo.InvariantCulture, out float next)
                        || float.TryParse(buffer, NumberStyles.Float, CultureInfo.CurrentCulture, out next))
                        && !float.IsNaN(next) && !float.IsInfinity(next) && Math.Abs(next - f) > 0.0001f)
                        entry.BoxedValue = next;
                }
                else if (value is KeyboardShortcut shortcut)
                {
                    if (DdenUI.Button(field, KeyCapture.Active ? ManagerText.Get("control.capture") : shortcut.ToString())) KeyCapture.Begin(entry);
                }
                else DdenUI.Label(field, Convert.ToString(value), DdenUI.Sub);
            }
            catch (Exception ex) { DdenUI.Label(field, ManagerText.Get("control.editError", ex.Message), DdenUI.Sub); }
            string description = ManagerText.Get("desc." + key);
            DdenUI.Label(new Rect(r.x + 10, r.y + 23, r.width - 19, 17),
                DdenUI.ElideToWidth(description, r.width - 19, DdenUI.Sub), DdenUI.Sub);
            DdenUI.Hover(r, description);
            DdenUI.Fill(new Rect(r.x, r.yMax - 1, r.width, 1), new Color(1, 1, 1, 0.06f));
        }
    }
}
