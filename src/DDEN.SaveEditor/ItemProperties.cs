using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace DDEN.SaveEditor
{
    internal sealed class LiquidEntry
    {
        internal JObject Raw;
        internal FieldRow Kind, Amount;
    }
    internal sealed class ItemSection
    {
        internal string Type, Name;
        internal JObject Raw;
        internal readonly List<FieldRow> Rows = new List<FieldRow>();
        internal readonly List<LiquidEntry> Liquids = new List<LiquidEntry>();
        internal JArray Stack => Raw["stack"] as JArray;
        internal void RefreshLiquids()
        {
            var previous = Liquids.ToArray();
            Liquids.Clear();
            if (Stack == null) return;
            foreach (JObject entry in Stack.OfType<JObject>())
            {
                var existing = previous.FirstOrDefault(e => ReferenceEquals(e.Raw, entry));
                if (existing != null) { Liquids.Add(existing); continue; }
                var amount = SaveDocument.MakeRow("liquid", "amount", entry["amount"]);
                if (amount != null) amount.Minimum = 0;
                Liquids.Add(new LiquidEntry { Raw = entry, Kind = SaveDocument.MakeRow("liquid", "liquidId", entry["liquidId"]), Amount = amount });
            }
        }
    }
    internal sealed partial class ItemView
    {
        internal readonly List<ItemSection> Sections = new List<ItemSection>();
        internal bool PropertiesAvailable;
        internal ItemSection BatterySection => Sections.FirstOrDefault(s => s.Type == "BatteryItem");
        internal ItemSection LiquidSection => Sections.FirstOrDefault(s => s.Type == "WaterContainerItem");
        internal IEnumerable<FieldRow> PropertyRows => Sections.SelectMany(s => s.Rows.Concat(s.Liquids.SelectMany(l => new[] { l.Kind, l.Amount }))).Where(r => r != null);
        internal void BuildProperties(JObject components)
        {
            Sections.Clear();
            PropertiesAvailable = true;
            foreach (JProperty component in components.Properties())
            {
                if (!(component.Value is JObject obj)) continue;
                string type = component.Name.Split(',')[0].Split('.').Last();
                var section = new ItemSection { Type = type, Raw = obj, Name = type == "GunScript" ? "枪械与弹药" : type == "AmmoScript" ? "弹匣与子弹" : type == "BatteryItem" ? "电池" : type == "WaterContainerItem" ? "混合液体" : "附加属性" };
                foreach (JValue value in obj.Descendants().OfType<JValue>())
                {
                    if (type == "WaterContainerItem" && value.Ancestors().Any(t => ReferenceEquals(t, obj["stack"]))) continue;
                    string key = (value.Parent as JProperty)?.Name ?? value.Path;
                    var row = SaveDocument.MakeRow(type, key, value);
                    if (row == null) continue;
                    if (type == "GunScript" && (key == "roundsInMag" || key == "magCapacity") || type == "AmmoScript" && (key == "rounds" || key == "maxRounds"))
                    { row.Minimum = 0; row.Maximum = int.MaxValue; }
                    if (type == "GunScript" && key == "roundInChamber" && row.Kind == RowKind.Number) { row.Minimum = 0; row.Maximum = 2; }
                    if (type == "BatteryItem" && key == "maxCharge") row.Minimum = 0;
                    section.Rows.Add(row);
                }
                section.RefreshLiquids();
                Sections.Add(section);
            }
            // SavedItem's color is independent of component data.
            var swatch = SaveDocument.MakeRow("item", "swatch", Raw["swatch"]);
            if (swatch != null) { swatch.Minimum = 0; swatch.Maximum = 7; Sections.Add(new ItemSection { Name = "外观", Type = "appearance", Raw = Raw, Rows = { swatch } }); }
        }
    }
    internal sealed partial class SaveDocument
    {
        internal Func<string, double?> LiquidCapacity = id => null;
        internal Func<string, bool> IsLiquidKnown;
        internal Func<string, double?> BatteryLimit = id => null;
        internal Func<string, bool> IsBatteryKnown;
        internal void SyncLiquidCondition(ItemView item, double? capacity)
        {
            if (item?.LiquidSection?.Stack == null || !capacity.HasValue || capacity <= 0) return;
            double total = item.LiquidSection.Liquids.Sum(l => l.Amount?.Number ?? 0);
            item.Condition?.SetNumber(total / capacity.Value);
        }
        internal void AddLiquid(ItemView item, string id, double? capacity)
        {
            var section = item?.LiquidSection;
            if (section?.Stack == null || string.IsNullOrEmpty(id)) return;
            double total = section.Liquids.Sum(l => l.Amount?.Number ?? 0);
            double amount = capacity.HasValue ? Math.Min(100, Math.Max(0, capacity.Value - total)) : 0;
            section.Stack.Add(new JObject { ["liquidId"] = id, ["amount"] = amount });
            section.RefreshLiquids(); SyncLiquidCondition(item, capacity); Dirty = true;
        }
        internal void RemoveLiquid(ItemView item, LiquidEntry entry, double? capacity)
        {
            if (item?.LiquidSection?.Stack == null || !item.LiquidSection.Stack.Any(t => ReferenceEquals(t, entry.Raw))) return;
            entry.Raw.Remove(); item.LiquidSection.RefreshLiquids(); SyncLiquidCondition(item, capacity); Dirty = true;
        }
        internal bool SetBattery(ItemView item, string id, double charge)
        {
            var battery = item?.BatterySection;
            if (!(battery?.Raw["batteryType"] is JValue type) || !(battery.Raw["maxCharge"] is JValue max)
                || item.Condition == null || charge < 0 || charge > float.MaxValue || double.IsNaN(charge) || double.IsInfinity(charge)) return false;
            if (!string.IsNullOrEmpty(id) && ((IsBatteryKnown != null && !IsBatteryKnown(id)) || charge <= 0 || charge > (BatteryLimit(item.Id) ?? double.MaxValue)))
            { SetStatus("电池不适用于这件物品。", true); return false; }
            type.Value = id ?? ""; max.Value = (float)charge;
            item.Condition.SetNumber(string.IsNullOrEmpty(id) ? 0 : 1);
            foreach (var row in battery.Rows) { row.Error = null; row.Refresh(); }
            if (string.IsNullOrEmpty(id) && battery.Raw["batteryWasFavourited"] is JValue fav) fav.Value = false;
            foreach (var row in battery.Rows) row.Refresh();
            Dirty = true; return true;
        }
        // Validate only components changed in this session: old mod data must survive unrelated edits.
        internal string ValidateItemProperties()
        {
            foreach (ItemView item in Items)
            foreach (ItemSection section in item.Sections)
            {
                JToken baseline = null;
                if (_baseline?["itemComponents"] is JArray previous && item.Index < previous.Count)
                {
                    var key = (section.Raw.Parent as JProperty)?.Name;
                    if (key != null) baseline = previous[item.Index]?[key];
                }
                if (JToken.DeepEquals(baseline, section.Raw)) continue;
                string count = section.Type == "AmmoScript" ? "rounds" : "roundsInMag";
                string maximum = section.Type == "AmmoScript" ? "maxRounds" : "magCapacity";
                if (section.Type == "AmmoScript" || section.Type == "GunScript")
                {
                    var rounds = section.Rows.FirstOrDefault(r => r.Label == count);
                    var cap = section.Rows.FirstOrDefault(r => r.Label == maximum);
                    if (rounds != null && cap != null && rounds.Number > cap.Number)
                        return "子弹数不能超过弹匣容量：" + EditorLabels.Item(item.Id);
                }
                if (section.Type == "WaterContainerItem")
                {
                    if (section.Stack == null) continue;
                    foreach (var liquid in section.Liquids)
                    {
                        if (liquid.Kind == null || liquid.Amount == null || string.IsNullOrWhiteSpace(liquid.Kind.Buffer) || liquid.Amount.Number < 0)
                            return "液体种类和数量不完整：" + EditorLabels.Item(item.Id);
                        string id = liquid.Kind.Token.Value<string>();
                        bool alreadySaved = (baseline?["stack"] as JArray)?.OfType<JObject>().Any(l => l.Value<string>("liquidId") == id) ?? false;
                        if (!alreadySaved && IsLiquidKnown != null && !IsLiquidKnown(id)) return "无法识别所选液体，请先启用对应模组。";
                    }
                    double? capacity = LiquidCapacity(item.Id);
                    if (capacity.HasValue && section.Liquids.Sum(l => l.Amount?.Number ?? 0) > capacity.Value + .001)
                        return "液体总量超过容器容量：" + EditorLabels.Item(item.Id);
                }
                if (section.Type == "BatteryItem")
                {
                    string type = section.Raw.Value<string>("batteryType");
                    double charge = section.Raw.Value<double?>("maxCharge") ?? 0;
                    if (string.IsNullOrEmpty(type) && (charge != 0 || item.ConditionValue != 0)) return "未装电池时，电量和容量必须为零。";
                    if (!string.IsNullOrEmpty(type) && (charge <= 0 || charge > (BatteryLimit(item.Id) ?? double.MaxValue))) return "电池容量不适用于这件物品。";
                    if (!string.IsNullOrEmpty(type) && type != baseline?.Value<string>("batteryType") && IsBatteryKnown != null && !IsBatteryKnown(type)) return "无法识别所选电池，请先启用对应模组。";
                }
            }
            return null;
        }
    }
}
