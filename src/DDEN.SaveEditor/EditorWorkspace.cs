using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DDEN.Shared;
using UnityEngine;

namespace DDEN.SaveEditor
{
    internal sealed partial class EditorView
    {
        private string _section = "items", _characterPage = "body", _fieldFilter = "";
        private bool _picker, _detail, _advanced, _review, _discard, _compact, _targetsScanned, _progressAdvanced;
        private string _pendingPath;
        private ItemView _pendingDelete;
        private List<SaveChange> _changes = new List<SaveChange>();
        private Vector2 _reviewScroll, _detailScroll;
        private static readonly string[] CommonBody = { "bloodOxygen", "bloodVolume", "brainHealth", "consciousness", "hunger", "thirst", "stamina", "energy", "temperature", "radiationSickness", "painShock" };

        public void Draw(Rect area)
        {
            bool enabled = GUI.enabled;
            try
            {
                if (!_targetsScanned) RefreshTargets();
                if (_smoke)
                {
                    _tab = Tabs[(Time.frameCount / 45) % Tabs.Length];
                    _section = _tab == "items" ? "items" : _tab == "run" ? "world" : _tab == "progress" ? "progress" : "character";
                    if (_section == "character") _characterPage = _tab;
                }
                if (area.width < 340 || area.height < 240)
                { DdenUI.Label(area, "请放大窗口后编辑存档。", DdenUI.Wrap); return; }
                DrawHeader(new Rect(area.x, area.y, area.width, 58));
                bool modal = _picker || _review || _discard || _pendingDelete != null || _chooseItemValue != null;
                GUI.enabled = enabled && !modal;
                var nav = new Rect(area.x, area.y + 64, area.width, 30);
                string[] sections = { "items", "character", "world", "progress" }, labels = { "物品", "角色", "世界设置", "进度与成就" };
                for (int i = 0; i < sections.Length; i++)
                {
                    float w = (nav.width - 9) / 4;
                    if (DdenUI.Button(new Rect(nav.x + i * (w + 3), nav.y, w, 28), labels[i], _section == sections[i]))
                    { _section = sections[i]; _detail = false; _fieldFilter = ""; _rowsScroll = Vector2.zero; }
                }
                var content = new Rect(area.x, nav.yMax + 6, area.width, area.yMax - nav.yMax - 36);
                GUI.enabled = enabled && !modal;
                if (!modal) DrawWorkspace(content); else DdenUI.Fill(content, DdenUI.Panel);
                GUI.enabled = enabled;
                string status = _doc.StatusIsError ? _doc.Status : !string.IsNullOrEmpty(_flash) ? _flash : _doc.Status;
                DdenUI.Label(new Rect(area.x + 4, area.yMax - 24, area.width - 8, 22), DdenUI.ElideToWidth(status, area.width - 12), _doc.StatusIsError ? ErrorStyle : DdenUI.Sub);
                if (_picker) DrawPicker(area);
                else if (_pendingDelete != null) DrawDelete(area);
                else if (_discard) DrawDiscard(area);
                else if (_review) DrawReview(area);
                else if (_chooseItemValue != null) DrawItemChoice(area);
                DdenUI.DrawTooltip(area);
            }
            catch (Exception ex) { SaveEditorPlugin.Log.Exception(ex, "绘制存档工作区"); }
            finally { GUI.enabled = enabled; }
        }
        private static GUIStyle _errorStyle;
        private static GUIStyle ErrorStyle => _errorStyle ??= new GUIStyle(DdenUI.Sub) { normal = { textColor = DdenUI.Bad } };
        private void DrawHeader(Rect r)
        {
            DdenUI.Fill(r, DdenUI.Panel); DdenUI.Frame(r, DdenUI.AccentDim);
            DdenUI.Label(new Rect(r.x + 10, r.y + 6, r.width - 180, 23), "存档工作区", DdenUI.Title);
            string file = _doc.Doc == null ? "选择一份存档开始编辑" : Path.GetFileName(Path.GetDirectoryName(_doc.Path)) + " / " + Path.GetFileName(_doc.Path);
            DdenUI.Label(new Rect(r.x + 10, r.y + 31, r.width - 190, 19), DdenUI.ElideToWidth(file, r.width - 200), DdenUI.Sub);
            bool modal = _picker || _review || _discard || _pendingDelete != null || _chooseItemValue != null;
            bool enabled = GUI.enabled; GUI.enabled = enabled && !modal;
            if (DdenUI.Button(new Rect(r.xMax - 166, r.y + 6, 76, 22), "选择存档")) { RefreshTargets(); _picker = true; }
            GUI.enabled = enabled && !modal && _doc.Doc != null;
            if (DdenUI.Button(new Rect(r.xMax - 86, r.y + 6, 76, 22), _doc.Dirty ? "查看修改 *" : "查看修改")) OpenReview();
            GUI.enabled = enabled && !modal && _doc.Doc != null && _doc.Dirty;
            if (DdenUI.Button(new Rect(r.xMax - 166, r.y + 32, 76, 20), "撤销修改")) _discard = true;
            if (DdenUI.Button(new Rect(r.xMax - 86, r.y + 32, 76, 20), "保存", true)) OpenReview();
            GUI.enabled = enabled;
        }
        private void DrawWorkspace(Rect r)
        {
            if (_doc.Doc == null)
            {
                DdenUI.Fill(r, DdenUI.Panel);
                float x = r.x + 24, y = r.y + 30;
                DdenUI.Label(new Rect(x, y, r.width - 48, 25), "物品、角色与世界，一处编辑", DdenUI.Title);
                DdenUI.Label(new Rect(x, y + 36, r.width - 48, 58), "选择存档后，点物品图标或人体部位即可编辑。\n修改会暂存在工作区，查看清单后再保存。", DdenUI.Wrap);
                if (DdenUI.Button(new Rect(x, y + 110, 150, 28), "选择并打开存档", true)) _picker = true;
                return;
            }
            if (_section == "world")
            {
                DrawFields(r, _doc.RunSettings, "世界设置", true); _tab = "run"; return;
            }
            if (_section == "progress") { DrawProgressWorkspace(r); _tab = "progress"; return; }
            float top = r.y;
            if (_section == "character")
            {
                string[] pages = { "body", "limbs", "skills" }, labels = { "身体状态", "肢体与伤势", "技能" };
                for (int i = 0; i < pages.Length; i++)
                    if (DdenUI.Button(new Rect(r.x + i * (r.width / 3), r.y, r.width / 3 - 4, 24), labels[i], _characterPage == pages[i]))
                    { _characterPage = pages[i]; _rowsScroll = Vector2.zero; _detail = false; }
                top += 30;
                _tab = _characterPage;
                if (_tab != "limbs")
                {
                    Rect fieldArea = new Rect(r.x, top, r.width, r.yMax - top);
                    DrawFields(fieldArea, _tab == "skills" ? _doc.SkillRows : _doc.BodyRows, _tab == "skills" ? "技能" : "身体状态", false);
                    return;
                }
            }
            else _tab = "items";
            var page = new Rect(r.x, top, r.width, r.yMax - top);
            bool split = page.width >= 760;
            _compact = !split;
            if (!split)
            {
                float w = (page.width - 6) / 2;
                if (DdenUI.Button(new Rect(page.x, page.y, w, 23), _tab == "items" ? "物品总览" : "人体图", !_detail)) _detail = false;
                if (DdenUI.Button(new Rect(page.x + w + 6, page.y, w, 23), "选中详情", _detail)) _detail = true;
                page.y += 29; page.height -= 29;
            }
            float side = split ? Mathf.Clamp(page.width * 0.34f, 280, 330) : page.width;
            var overview = new Rect(page.x, page.y, split ? page.width - side - 8 : page.width, page.height);
            var inspector = new Rect(split ? overview.xMax + 8 : page.x, page.y, side, page.height);
            if (split || !_detail)
            {
                if (_tab == "items")
                {
                    var search = new Rect(overview.x, overview.y, overview.width - 94, 24);
                    GUI.SetNextControlName("saveeditor.items.search");
                    string filter = DdenUI.TextField(search, _search);
                    if (string.IsNullOrEmpty(filter) && GUI.GetNameOfFocusedControl() != "saveeditor.items.search")
                        DdenUI.Label(new Rect(search.x + 5, search.y + 3, search.width - 10, 18), "搜索名称或编号", DdenUI.Sub);
                    if (filter != _search) { _search = filter; _doc.ItemFilter = filter; _doc.RefreshVisibleItems(); _gridScroll = Vector2.zero; }
                    if (DdenUI.Button(new Rect(overview.xMax - 88, overview.y, 88, 24), "全部修复")) { _doc.AllItemsFullCondition(); _conditionBuffer = null; _flash = _doc.Status; }
                    overview.y += 30; overview.height -= 30;
                    DrawItems(overview);
                    DdenUI.Hover(new Rect(overview.x, overview.y - 30, overview.width - 94, 24), "搜索物品名称、编号或自定义名称");
                }
                else DrawLimbs(overview);
            }
            if (split || _detail)
            {
                DdenUI.Fill(inspector, DdenUI.Panel); DdenUI.Frame(inspector, DdenUI.AccentDim);
                var inner = new Rect(inspector.x + 9, inspector.y + 8, inspector.width - 18, inspector.height - 16);
                float height = _tab == "items" ? ItemInspectorHeight() : Mathf.Max(inner.height, 30 + (_doc.Limbs.FirstOrDefault(l => l.Index == _doc.SelectedLimb)?.Rows.Count ?? 0) * 48);
                _detailScroll = DdenUI.BeginScrollView(inner, _detailScroll, new Rect(0, 0, inner.width - 18, Mathf.Max(inner.height, height)));
                try
                {
                    var details = new Rect(0, 0, inner.width - 18, Mathf.Max(inner.height, height));
                    if (_tab == "items") DrawItemInspector(details);
                    else DrawLocalizedLimb(details);
                }
                finally { DdenUI.EndScrollView(); }
            }
        }
        private void DrawFields(Rect r, IEnumerable<FieldRow> source, string title, bool world)
        {
            DdenUI.Fill(r, DdenUI.Panel);
            DdenUI.Label(new Rect(r.x + 10, r.y + 6, r.width - 170, 22), title, DdenUI.Title);
            bool full = world || _characterPage == "skills" || _advanced;
            if (!world && _characterPage != "skills" && DdenUI.Button(new Rect(r.xMax - 112, r.y + 5, 102, 23), _advanced ? "常用项目" : "详细字段", _advanced)) { _advanced = !_advanced; _rowsScroll = Vector2.zero; }
            if (!world && _characterPage == "body" && DdenUI.Button(new Rect(r.x + 10, r.y + 36, 110, 24), "恢复健康")) { _doc.PresetFullHeal(); _flash = _doc.Status; }
            float filterX = !world && _characterPage == "body" ? r.x + 130 : r.x + 10;
            var search = new Rect(filterX, r.y + 36, r.xMax - filterX - 10, 24);
            GUI.SetNextControlName("saveeditor.fields.search");
            _fieldFilter = DdenUI.TextField(search, _fieldFilter);
            if (string.IsNullOrEmpty(_fieldFilter) && GUI.GetNameOfFocusedControl() != "saveeditor.fields.search")
                DdenUI.Label(new Rect(search.x + 5, search.y + 3, search.width - 10, 18), "搜索项目", DdenUI.Sub);
            DdenUI.Hover(new Rect(filterX, r.y + 36, r.xMax - filterX - 10, 24), "按名称或字段编号筛选");
            var rows = source.Where(row => (row.Label != "cheatedSave" || _section == "progress") && (full || CommonBody.Contains(row.Label))
                && (string.IsNullOrWhiteSpace(_fieldFilter) || EditorLabels.Field(row).IndexOf(_fieldFilter, StringComparison.OrdinalIgnoreCase) >= 0 || row.Label.IndexOf(_fieldFilter, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            Rect list = new Rect(r.x + 8, r.y + 68, r.width - 16, Mathf.Max(1, r.height - 76));
            const float rowH = 48;
            _rowsScroll = DdenUI.BeginScrollView(list, _rowsScroll, new Rect(0, 0, list.width - 18, Mathf.Max(list.height, rows.Count * rowH)));
            try
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    float y = i * rowH;
                    DrawField(new Rect(0, y, list.width - 18, rowH - 3), rows[i], world);
                }
                if (rows.Count == 0) DdenUI.Label(new Rect(8, 10, list.width - 32, 50), "没有符合条件的项目。", DdenUI.Wrap);
            }
            finally { DdenUI.EndScrollView(); }
        }
        private void DrawField(Rect r, FieldRow row, bool world)
        {
            DdenUI.Fill(r, DdenUI.PanelAlt);
            string label = EditorLabels.Field(row);
            float controls = Mathf.Min(218, r.width * 0.48f);
            DdenUI.Label(new Rect(r.x + 7, r.y + 6, r.width - controls - 18, 22), DdenUI.ElideToWidth(label, r.width - controls - 20), DdenUI.Row);
            var field = new Rect(r.xMax - controls - 6, r.y + 4, controls, 24);
            if (row.Kind == RowKind.Bool)
            { if (row.ApplyBool(DdenUI.CheckBoxLabeled(field, row.BoolValue, row.BoolValue ? "开启" : "关闭"))) _doc.Dirty = true; }
            else if (world && EditorLabels.Setting(row.Label) is RunSettingDropdown choices)
            {
                int index = (int)row.Number;
                string name = index >= 0 && index < choices.choices.Length ? Locale.GetOther("runset" + row.Label + choices.choices[index]) : "未知选项 " + index;
                DdenUI.Label(new Rect(field.x + 28, field.y + 3, field.width - 56, 18), DdenUI.ElideToWidth(name, field.width - 58), DdenUI.Row);
                if (DdenUI.Button(new Rect(field.x, field.y, 24, 24), "<")) { row.SetNumber(Mathf.Clamp(index - 1, 0, choices.choices.Length - 1)); _doc.Dirty = true; }
                if (DdenUI.Button(new Rect(field.xMax - 24, field.y, 24, 24), ">")) { row.SetNumber(Mathf.Clamp(index + 1, 0, choices.choices.Length - 1)); _doc.Dirty = true; }
            }
            else
            {
                string text = DdenUI.TextField(field, row.Buffer ?? "");
                if (text != row.Buffer)
                {
                    if (world && EditorLabels.Setting(row.Label) is RunSettingFloat range && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                        && (v < range.limits.min || v > range.limits.max || range.wholeNum && v != Math.Truncate(v)))
                    { row.Buffer = text; row.Error = "请使用游戏允许的范围和数值类型"; }
                    else if (row.ApplyText(text)) _doc.Dirty = true;
                }
            }
            string detail = row.Error ?? (label == row.Label ? "扩展字段" : "");
            float detailWidth = world && EditorLabels.Setting(row.Label) is RunSettingFloat ? r.width - controls - 18 : r.width - 14;
            DdenUI.Label(new Rect(r.x + 7, r.y + 28, detailWidth, 15), DdenUI.ElideToWidth(detail, detailWidth), row.Error != null ? ErrorStyle : DdenUI.Tiny);
            if (world && row.Kind == RowKind.Number && EditorLabels.Setting(row.Label) is RunSettingFloat slider && slider.limits.max > slider.limits.min)
            {
                float value = DdenUI.HorizontalSlider(new Rect(field.x, r.y + 29, field.width, 12), (float)row.Number, slider.limits.min, slider.limits.max);
                if (slider.wholeNum) value = Mathf.Round(value);
                if (Math.Abs(value - row.Number) > 0.0001) { row.SetNumber(value); _doc.Dirty = true; }
            }
            DdenUI.Hover(r, EditorLabels.Help(row));
        }
        private void DrawLocalizedLimb(Rect r)
        {
            var limb = _doc.Limbs.FirstOrDefault(l => l.Index == _doc.SelectedLimb);
            if (limb == null) { DdenUI.Label(r, "点选人体部位后查看健康与伤势。", DdenUI.Wrap); return; }
            DdenUI.Label(new Rect(r.x, r.y, r.width, 24), EditorLabels.Limb(limb.Index), DdenUI.Title);
            float y = r.y + 30;
            foreach (var row in limb.Rows) { DrawField(new Rect(r.x, y, r.width, 45), row, false); y += 48; }
        }
        private void DrawProgressWorkspace(Rect r)
        {
            if (DdenUI.Button(new Rect(r.x, r.y, 110, 24), _progressAdvanced ? "常用记录" : "详细记录", _progressAdvanced))
            { _progressAdvanced = !_progressAdvanced; _rowsScroll = Vector2.zero; }
            var common = new[] { "runTime", "biome", "seed", "level", "cheatedSave" };
            var rows = _doc.Progress.Where(row => _progressAdvanced || common.Contains(row.Label));
            DrawFields(new Rect(r.x, r.y + 30, r.width, Mathf.Max(1, r.height - 30)), rows, "进度与成就", true);
        }
        private Rect Modal(Rect area, string title, float width = 640)
        {
            DdenUI.Fill(area, new Color(0, 0, 0, 0.72f));
            var box = new Rect(area.x + (area.width - Mathf.Min(width, area.width - 24)) / 2, area.y + 12, Mathf.Min(width, area.width - 24), area.height - 24);
            DdenUI.Fill(box, DdenUI.Bg); DdenUI.Frame(box, DdenUI.Accent);
            DdenUI.Label(new Rect(box.x + 12, box.y + 9, box.width - 24, 24), title, DdenUI.Title);
            return box;
        }
        private void DrawPicker(Rect area)
        {
            var box = Modal(area, "选择存档");
            if (_targets.Count == 0) DdenUI.Label(new Rect(box.x + 12, box.y + 48, box.width - 24, 80), "没有找到存档。可先在游戏中保存一次，再刷新列表。", DdenUI.Wrap);
            var list = new Rect(box.x + 10, box.y + 44, box.width - 20, box.height - 92);
            _targetScroll = DdenUI.BeginScrollView(list, _targetScroll, new Rect(0, 0, list.width - 18, Mathf.Max(list.height, _targets.Count * 51)));
            try
            {
                for (int i = 0; i < _targets.Count; i++)
                {
                    var target = _targets[i]; var row = new Rect(0, i * 51, list.width - 18, 46);
                    DdenUI.Fill(row, target.Path == _doc.Path ? DdenUI.AccentDim : DdenUI.PanelAlt);
                    DdenUI.Label(new Rect(8, row.y + 4, row.width - 16, 20), DdenUI.ElideToWidth(target.Label + (target.IsCurrent ? " · 当前存档" : ""), row.width - 16), DdenUI.Row);
                    DdenUI.Label(new Rect(8, row.y + 25, row.width - 16, 16), target.IsMain ? "世界与本机角色" : "联机玩家快照 · " + Path.GetFileName(Path.GetDirectoryName(target.Path)), DdenUI.Sub);
                    if (GUI.Button(row, GUIContent.none, GUIStyle.none))
                    {
                        if (_doc.Dirty) { _pendingPath = target.Path; _picker = false; _discard = true; }
                        else { LoadTarget(target.Path); _picker = false; }
                    }
                }
            }
            finally { DdenUI.EndScrollView(); }
            if (DdenUI.Button(new Rect(box.x + 10, box.yMax - 36, 86, 24), "刷新列表")) RefreshTargets();
            if (DdenUI.Button(new Rect(box.xMax - 96, box.yMax - 36, 86, 24), "关闭")) _picker = false;
        }
        private void LoadTarget(string path)
        {
            if (!_doc.Load(path)) return;
            _selectedPath = path; _conditionBuffer = null; _flash = _doc.Status;
            _gridScroll = _rowsScroll = _detailScroll = Vector2.zero;
        }
        private void OpenReview() { _changes = _doc.Changes(); _reviewScroll = Vector2.zero; _review = true; }
        private void DrawReview(Rect area)
        {
            var box = Modal(area, "修改清单 · " + _changes.Count + " 项");
            var list = new Rect(box.x + 10, box.y + 44, box.width - 20, box.height - 108);
            _reviewScroll = DdenUI.BeginScrollView(list, _reviewScroll, new Rect(0, 0, list.width - 18, Mathf.Max(list.height, _changes.Count * 64)));
            try
            {
                for (int i = 0; i < _changes.Count; i++)
                {
                    var c = _changes[i]; var cell = new Rect(0, i * 64, list.width - 18, 59);
                    DdenUI.Fill(cell, DdenUI.PanelAlt);
                    DdenUI.Label(new Rect(7, cell.y + 3, cell.width - 14, 19), DdenUI.ElideToWidth(ChangeTitle(c.Path), cell.width - 14), DdenUI.Row);
                    DdenUI.Hover(cell, c.Path + "\n原值：" + c.Before + "\n修改：" + c.After);
                    DdenUI.Label(new Rect(7, cell.y + 23, cell.width - 14, 15), DdenUI.ElideToWidth("原值  " + c.Before, cell.width - 14), DdenUI.Sub);
                    DdenUI.Label(new Rect(7, cell.y + 40, cell.width - 14, 15), DdenUI.ElideToWidth("修改  " + c.After, cell.width - 14), DdenUI.Sub);
                }
                if (_changes.Count == 0) DdenUI.Label(new Rect(8, 12, list.width - 32, 25), "没有尚未保存的修改。", DdenUI.Sub);
            }
            finally { DdenUI.EndScrollView(); }
            DdenUI.Label(new Rect(box.x + 12, box.yMax - 58, box.width - 24, 18), _doc.StatusIsError ? _doc.Status : "保存时保留原档备份；不会自动应用到游戏中的角色。", _doc.StatusIsError ? ErrorStyle : DdenUI.Sub);
            if (DdenUI.Button(new Rect(box.x + 10, box.yMax - 34, 92, 24), "继续编辑")) _review = false;
            bool enabled = GUI.enabled; GUI.enabled = enabled && _changes.Count > 0;
            if (DdenUI.Button(new Rect(box.xMax - 126, box.yMax - 34, 116, 24), "确认保存", true))
            { _flash = _doc.Save(); if (!_doc.StatusIsError) _review = false; }
            GUI.enabled = enabled;
        }
        private void DrawDiscard(Rect area)
        {
            var box = Modal(area, "尚未保存的修改", 460);
            DdenUI.Label(new Rect(box.x + 14, box.y + 50, box.width - 28, 70), _pendingPath == null ? "撤销本次编辑，恢复到打开存档时的内容？" : "切换存档将放弃当前尚未保存的修改。", DdenUI.Wrap);
            if (DdenUI.Button(new Rect(box.x + 12, box.yMax - 40, 110, 26), "继续编辑")) { _discard = false; _pendingPath = null; }
            if (DdenUI.Button(new Rect(box.xMax - 132, box.yMax - 40, 120, 26), _pendingPath == null ? "撤销修改" : "放弃并打开"))
            { if (_pendingPath != null) LoadTarget(_pendingPath); else _doc.DiscardChanges(); _conditionBuffer = null; _discard = false; _pendingPath = null; }
        }
        private string ChangeTitle(string path)
        {
            if (path == "items") return "物品列表 · 数量或结构变化";
            if (path == "itemComponents") return "物品附加数据 · 与物品同步调整";
            int open = path.IndexOf('['), close = path.IndexOf(']');
            string group = "", key = path;
            if (open >= 0 && close > open && int.TryParse(path.Substring(open + 1, close - open - 1), out int index))
            {
                key = path.Substring(close + 1).TrimStart('.');
                if (path.StartsWith("limbs[")) group = EditorLabels.Limb(index) + " · ";
                else if (path.StartsWith("items[") || path.StartsWith("itemComponents["))
                {
                    group = "物品 · " + EditorLabels.Item(_doc.FindItem(index)?.Id ?? "") + " · ";
                    var row = _doc.FindItem(index)?.PropertyRows.FirstOrDefault(p => p.Token.Path == path);
                    if (row != null) return group + EditorLabels.Field(row);
                    if (path.Contains(".stack")) return group + "混合液体内容";
                }
                else if (path.StartsWith("runSettings[") && _doc.Doc["runSettings"] is Newtonsoft.Json.Linq.JArray settings && index < settings.Count)
                    return "世界设置 · " + EditorLabels.Field(new FieldRow { Group = "run", Label = settings[index]["Item1"]?.ToString() ?? key });
            }
            else if (path.StartsWith("body.")) { group = "角色 · "; key = path.Substring(5); }
            return group + EditorLabels.Field(new FieldRow { Label = key });
        }
        private void DrawDelete(Rect area)
        {
            var box = Modal(area, "移除所选物品", 460);
            DdenUI.Label(new Rect(box.x + 14, box.y + 50, box.width - 28, 70), "将移除「" + EditorLabels.Item(_pendingDelete.Id) + "」。\n保存前仍可撤销本次修改。", DdenUI.Wrap);
            if (DdenUI.Button(new Rect(box.x + 12, box.yMax - 40, 110, 26), "取消")) _pendingDelete = null;
            if (_pendingDelete != null && DdenUI.Button(new Rect(box.xMax - 132, box.yMax - 40, 120, 26), "确认移除"))
            { _doc.DeleteItem(_pendingDelete); _flash = _doc.Status; _pendingDelete = null; }
        }
    }
}
