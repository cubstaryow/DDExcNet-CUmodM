using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DDEN.Shared;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DDEN.SaveEditor
{
    internal sealed partial class EditorView
    {
        private Action<string> _chooseItemValue;
        private List<string> _itemChoices;
        private Func<string, string> _itemChoiceName;
        private string _itemChoiceTitle, _itemChoiceSearch = "";
        private Vector2 _itemChoiceScroll;

        // Keep this measurement paired with the sections below; there is no fixed inspector limit.
        private float ItemInspectorHeight()
        {
            var item = _doc.FindItem(_doc.SelectedItem);
            if (item == null) return 100;
            float height = 104 + (item.LiquidSection == null ? 96 : 56) + 26 + 34;
            if (!item.PropertiesAvailable) height += 48;
            height += (item.Favourited != null ? 48 : 0) + (item.Label != null ? 48 : 0);
            foreach (var section in item.Sections)
            {
                height += 26;
                if (section.Type == "WaterContainerItem") height += 24 + section.Liquids.Count * 92 + 34;
                else if (section.Type == "BatteryItem") height += 82 + section.Rows.Count(r => r.Label != "batteryType") * 48;
                else height += section.Rows.Count * 48 + (section.Type == "GunScript" || section.Type == "AmmoScript" ? 32 : 0);
            }
            return height + 8;
        }
        private void DrawItemInspector(Rect r)
        {
            var item = _doc.FindItem(_doc.SelectedItem);
            if (item == null) { DdenUI.Label(r, "点选物品，编辑弹药、电池、液体与其他属性。", DdenUI.Wrap); return; }
            float y = r.y;
            DdenUI.Label(new Rect(r.x, y, r.width, 22), DdenUI.ElideToWidth(EditorLabels.Item(item.Id), r.width - 8, DdenUI.Title), DdenUI.Title);
            y += 28;
            var icon = new Rect(r.x, y, 64, 64);
            DdenUI.Fill(icon, DdenUI.PanelAlt); DdenUI.Frame(icon, DdenUI.AccentDim);
            DdenUI.DrawItemIcon(new Rect(icon.x + 4, icon.y + 4, 56, 56), item.Id);
            DdenUI.Label(new Rect(r.x + 74, y + 3, r.width - 74, 20), DdenUI.ElideToWidth(item.SlotText, r.width - 78), DdenUI.Sub);
            DdenUI.Label(new Rect(r.x + 74, y + 28, r.width - 74, 20), "物品 #" + (item.Index + 1), DdenUI.Sub);
            DdenUI.Hover(icon, "物品编号：" + item.Id);
            y += 76;
            if (!item.PropertiesAvailable) { DdenUI.Label(new Rect(r.x, y, r.width, 44), "物品附加数据缺失或数量不符，暂时只能编辑基础信息。", ErrorStyle); y += 48; }
            DrawItemCondition(r, item, ref y);
            foreach (var section in item.Sections)
            {
                ItemHeading(r, ref y, section.Name);
                if (section.Type == "WaterContainerItem") { DrawItemLiquids(r, item, section, ref y); continue; }
                if (section.Type == "BatteryItem") DrawItemBattery(r, item, section, ref y);
                if (section.Type == "GunScript" || section.Type == "AmmoScript")
                {
                    string countKey = section.Type == "GunScript" ? "roundsInMag" : "rounds";
                    string capKey = section.Type == "GunScript" ? "magCapacity" : "maxRounds";
                    var count = section.Rows.FirstOrDefault(row => row.Label == countKey);
                    var cap = section.Rows.FirstOrDefault(row => row.Label == capKey);
                    bool enabled = GUI.enabled; GUI.enabled = enabled && count != null && cap != null;
                    if (DdenUI.Button(new Rect(r.x, y, (r.width - 6) / 2, 25), "装满弹匣")) { count.SetNumber(cap.Number); _doc.Dirty = true; }
                    if (DdenUI.Button(new Rect(r.x + (r.width + 6) / 2, y, (r.width - 6) / 2, 25), "清空弹匣")) { count.SetNumber(0); _doc.Dirty = true; }
                    GUI.enabled = enabled; y += 32;
                }
                foreach (var row in section.Rows)
                {
                    if (section.Type == "BatteryItem" && row.Label == "batteryType") continue;
                    if (section.Type == "appearance" && row.Label == "swatch") row.Maximum = Math.Max(0, global::Item.ColorSwatches.Length - 1);
                    if (section.Type == "GunScript" && row.Label == "roundInChamber") DrawChamber(new Rect(r.x, y, r.width, 45), row);
                    else DrawField(new Rect(r.x, y, r.width, 45), row, false);
                    DdenUI.Hover(new Rect(r.x, y, r.width, 45), "保存属性：" + row.Token.Path);
                    y += 48;
                }
            }
            ItemHeading(r, ref y, "物品标记");
            foreach (var row in new[] { item.Favourited, item.Label })
                if (row != null) { DrawField(new Rect(r.x, y, r.width, 45), row, false); y += 48; }
            if (DdenUI.Button(new Rect(r.x, y + 4, r.width, 26), "移除这件物品")) _pendingDelete = item;
        }
        private static void ItemHeading(Rect r, ref float y, string title)
        { DdenUI.Section(new Rect(r.x, y + 2, r.width, 18), title); y += 26; }
        private void DrawItemCondition(Rect r, ItemView item, ref float y)
        {
            double? standaloneBattery = ItemCatalog.BatteryCharge(item.Id);
            bool battery = item.BatterySection != null || standaloneBattery.HasValue;
            bool absent = item.BatterySection != null && string.IsNullOrEmpty(item.BatterySection.Raw.Value<string>("batteryType"));
            ItemHeading(r, ref y, item.LiquidSection != null ? "容器状态" : battery ? "剩余电量" : "耐久");
            if (item.LiquidSection != null)
            {
                DdenUI.Label(new Rect(r.x, y, r.width, 24), "液体内容与装量在下方分别编辑。", DdenUI.Sub); y += 30; return;
            }
            bool enabled = GUI.enabled; GUI.enabled = enabled && item.Condition != null && !absent;
            float condition = item.ConditionValue;
            float value = DdenUI.HorizontalSlider(new Rect(r.x, y + 5, r.width - 64, 18), condition, 0, 1);
            if (Math.Abs(value - condition) > .0001)
            { _doc.SetItemCondition(item, value); _conditionBuffer = null; }
            if (DdenUI.Button(new Rect(r.xMax - 58, y, 58, 24), battery ? "充满" : "修复")) { _doc.SetItemCondition(item, 1); _conditionBuffer = null; }
            y += 30;
            if (_conditionBufferFor != item.Index || _conditionBuffer == null)
            { _conditionBuffer = (item.ConditionValue * 100).ToString("0.##", CultureInfo.InvariantCulture); _conditionBufferFor = item.Index; }
            string text = DdenUI.TextField(new Rect(r.x, y, 86, 24), _conditionBuffer);
            if (text != _conditionBuffer)
            {
                _conditionBuffer = text;
                if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float percent) && !float.IsNaN(percent) && !float.IsInfinity(percent) && percent >= 0 && percent <= 100)
                    _doc.SetItemCondition(item, percent / 100);
                else item.Condition.Error = "请输入 0～100";
            }
            DdenUI.Label(new Rect(r.x + 94, y + 3, r.width - 94, 22), absent ? "未装入电池" : item.Condition?.Error ?? "%", item.Condition?.Error != null ? ErrorStyle : DdenUI.Sub);
            y += 40; GUI.enabled = enabled;
        }
        private void DrawItemBattery(Rect r, ItemView item, ItemSection section, ref float y)
        {
            string id = section.Raw.Value<string>("batteryType");
            if (DdenUI.Button(new Rect(r.x, y, r.width, 26), string.IsNullOrEmpty(id) ? "装入电池…" : "更换：" + EditorLabels.Item(id) + "…"))
                ChooseItemValue("选择电池", ItemCatalog.Batteries().Where(b => ItemCatalog.BatteryCharge(b) <= (ItemCatalog.BatteryLimit(item.Id) ?? double.MaxValue)).ToList(), EditorLabels.Item, selected =>
                {
                    double? charge = ItemCatalog.BatteryCharge(selected);
                    if (charge.HasValue) { _doc.SetBattery(item, selected, charge.Value); _conditionBuffer = null; }
                });
            y += 32;
            bool enabled = GUI.enabled; GUI.enabled = enabled && !string.IsNullOrEmpty(id);
            if (DdenUI.Button(new Rect(r.x, y, 82, 24), "移除电池")) { _doc.SetBattery(item, "", 0); _conditionBuffer = null; }
            GUI.enabled = enabled;
            double chargeNow = (section.Raw.Value<double?>("maxCharge") ?? 0) * item.ConditionValue;
            DdenUI.Label(new Rect(r.x + 90, y + 3, r.width - 90, 22), "剩余 " + chargeNow.ToString("0.##", CultureInfo.InvariantCulture), DdenUI.Sub);
            DdenUI.Hover(new Rect(r.x, y - 32, r.width, 26), ItemCatalog.BatteryLimit(item.Id).HasValue ? "只列出容量适用的电池。更换后电量设为充满。" : "更换后电量设为充满。此物品的电池适用容量未知，请确认型号。" );
            y += 50;
        }
        private void DrawItemLiquids(Rect r, ItemView item, ItemSection section, ref float y)
        {
            double? capacity = ItemCatalog.LiquidCapacity(item.Id);
            double total = section.Liquids.Sum(l => l.Amount?.Number ?? 0);
            DdenUI.Label(new Rect(r.x, y, r.width, 20), capacity.HasValue ? $"{total:0.##} / {capacity.Value:0.##} mL" : $"{total:0.##} mL · 容量未知", capacity.HasValue && total > capacity ? ErrorStyle : DdenUI.Sub);
            y += 24;
            LiquidEntry remove = null;
            foreach (var entry in section.Liquids)
            {
                var cell = new Rect(r.x, y, r.width, 86);
                DdenUI.Fill(cell, DdenUI.PanelAlt);
                bool enabled = GUI.enabled; GUI.enabled = enabled && entry.Kind != null;
                if (DdenUI.Button(new Rect(r.x + 5, y + 5, r.width - 10, 26), ItemCatalog.LiquidName(entry.Kind?.Buffer) + "…"))
                    ChooseItemValue("选择液体种类", ItemCatalog.LiquidsList(), ItemCatalog.LiquidName, id => { if (entry.Kind.ApplyText(id)) _doc.Dirty = true; });
                GUI.enabled = enabled && entry.Amount != null;
                DdenUI.Label(new Rect(r.x + 7, y + 42, 34, 20), "数量", DdenUI.Sub);
                string text = DdenUI.TextField(new Rect(r.x + 44, y + 38, Mathf.Max(40, r.width - 114), 24), entry.Amount?.Buffer ?? "");
                if (entry.Amount != null && entry.Amount.ApplyText(text)) { _doc.SyncLiquidCondition(item, capacity); _doc.Dirty = true; }
                GUI.enabled = enabled;
                if (DdenUI.Button(new Rect(r.xMax - 62, y + 38, 56, 24), "移除")) remove = entry;
                DdenUI.Label(new Rect(r.x + 7, y + 65, r.width - 14, 18), entry.Amount?.Error ?? "mL", entry.Amount?.Error != null ? ErrorStyle : DdenUI.Tiny);
                DdenUI.Hover(cell, "液体编号：" + entry.Kind?.Buffer);
                y += 92;
            }
            if (remove != null) _doc.RemoveLiquid(item, remove, capacity);
            bool usable = GUI.enabled; GUI.enabled = usable && section.Stack != null;
            if (DdenUI.Button(new Rect(r.x, y, r.width, 26), "添加一种液体…"))
                ChooseItemValue("添加液体", ItemCatalog.LiquidsList(), ItemCatalog.LiquidName, id => _doc.AddLiquid(item, id, capacity));
            GUI.enabled = usable; y += 34;
        }
        private void DrawChamber(Rect r, FieldRow row)
        {
            DdenUI.Fill(r, DdenUI.PanelAlt);
            DdenUI.Label(new Rect(r.x + 7, r.y + 6, 76, 22), "弹膛状态", DdenUI.Row);
            string[] keys = { "Round", "Casing", "None" }, names = { "实弹", "弹壳", "空膛" };
            int index = row.Kind == RowKind.Number ? (int)row.Number : Array.IndexOf(keys, row.Buffer);
            DdenUI.Label(new Rect(r.x + 112, r.y + 6, r.width - 144, 22), index >= 0 && index < names.Length ? names[index] : "未知", DdenUI.Row);
            int next = index;
            if (DdenUI.Button(new Rect(r.x + 83, r.y + 4, 24, 24), "<")) next = (Mathf.Clamp(index, 0, 2) + 2) % 3;
            if (DdenUI.Button(new Rect(r.xMax - 28, r.y + 4, 24, 24), ">")) next = (Mathf.Clamp(index, 0, 2) + 1) % 3;
            if (next != index && row.ApplyText(row.Kind == RowKind.Number ? next.ToString(CultureInfo.InvariantCulture) : keys[next])) _doc.Dirty = true;
        }
        private void ChooseItemValue(string title, List<string> options, Func<string, string> name, Action<string> apply)
        {
            _itemChoiceTitle = title; _itemChoices = options; _itemChoiceName = name; _chooseItemValue = apply;
            _itemChoiceSearch = ""; _itemChoiceScroll = Vector2.zero; GUI.FocusControl(null);
        }
        private void DrawItemChoice(Rect area)
        {
            var box = Modal(area, _itemChoiceTitle, 520);
            _itemChoiceSearch = DdenUI.TextField(new Rect(box.x + 10, box.y + 42, box.width - 20, 26), _itemChoiceSearch);
            var options = _itemChoices.Where(id => id.IndexOf(_itemChoiceSearch, StringComparison.OrdinalIgnoreCase) >= 0 || _itemChoiceName(id).IndexOf(_itemChoiceSearch, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            var list = new Rect(box.x + 10, box.y + 76, box.width - 20, Mathf.Max(1, box.height - 122));
            _itemChoiceScroll = DdenUI.BeginScrollView(list, _itemChoiceScroll, new Rect(0, 0, list.width - 18, Mathf.Max(list.height, options.Count * 42)));
            string selected = null;
            try
            {
                for (int i = 0; i < options.Count; i++)
                {
                    string id = options[i]; var row = new Rect(0, i * 42, list.width - 18, 36);
                    if (DdenUI.Button(row, DdenUI.ElideToWidth(_itemChoiceName(id), row.width - 16))) selected = id;
                    DdenUI.Hover(row, "编号：" + id);
                }
                if (options.Count == 0) DdenUI.Label(new Rect(6, 6, list.width - 30, 50), "没有可选项目，可检查搜索词或对应模组。", DdenUI.Wrap);
            }
            finally { DdenUI.EndScrollView(); }
            if (selected != null) { var apply = _chooseItemValue; _chooseItemValue = null; apply(selected); }
            if (DdenUI.Button(new Rect(box.xMax - 102, box.yMax - 35, 92, 25), "取消")) _chooseItemValue = null;
            DdenUI.Hover(new Rect(box.x + 10, box.y + 42, box.width - 20, 26), "搜索名称或编号");
        }
    }
}
