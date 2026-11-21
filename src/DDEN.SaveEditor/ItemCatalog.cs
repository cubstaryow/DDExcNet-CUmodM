using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DDEN.SaveEditor
{
    internal static class ItemCatalog
    {
        internal static double? LiquidCapacity(string id)
        {
            if (global::Item.GlobalItems != null && global::Item.GlobalItems.TryGetValue(id, out var info) && info is LiquidItemInfo liquid) return liquid.capacity;
            return null;
        }
        internal static bool IsLiquidKnown(string id) => id != null && Liquids.Registry != null && Liquids.Registry.ContainsKey(id);
        internal static List<string> LiquidsList() => Liquids.Registry?.Keys.OrderBy(LiquidName, StringComparer.CurrentCulture).ToList() ?? new List<string>();
        internal static List<string> Batteries() => global::Item.GlobalItems?.Where(p => p.Value is BatteryInfo).Select(p => p.Key).OrderBy(EditorLabels.Item, StringComparer.CurrentCulture).ToList() ?? new List<string>();
        internal static double? BatteryCharge(string id)
        {
            if (global::Item.GlobalItems != null && global::Item.GlobalItems.TryGetValue(id, out var info) && info is BatteryInfo battery) return battery.maxCharge;
            return null;
        }
        private static readonly Dictionary<string, double> BatteryLimits = new Dictionary<string, double>(StringComparer.Ordinal);
        internal static double? BatteryLimit(string id)
        {
            if (BatteryLimits.TryGetValue(id, out double known)) return known;
            // Read the template only. Never instantiate a game item to inspect a save.
            var prefab = Resources.Load<GameObject>(id);
            var battery = prefab != null ? prefab.GetComponent<BatteryItem>() : null;
            if (battery == null) return null;
            double limit = battery.preset == BatteryItem.BatteryPreset.Small ? 50 : battery.preset == BatteryItem.BatteryPreset.Medium ? 100 : 300;
            BatteryLimits[id] = limit; return limit;
        }
        internal static string LiquidName(string id)
        {
            try
            {
                if (Liquids.Registry != null && Liquids.Registry.TryGetValue(id ?? "", out var liquid))
                {
                    string name = liquid.localeFromItem ? Locale.GetItem(id) : Locale.GetOther(liquid.localeName);
                    if (!string.IsNullOrWhiteSpace(name) && !name.StartsWith("MISSING", StringComparison.OrdinalIgnoreCase)) return Regex.Replace(name, "<[^>]*>", "");
                }
            }
            catch { }
            return string.IsNullOrEmpty(id) ? "未选择液体" : id;
        }
    }
}
