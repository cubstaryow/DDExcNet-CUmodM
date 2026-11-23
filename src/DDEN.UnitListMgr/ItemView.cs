using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DDEN.Shared;
using UnityEngine;

namespace DDEN.UnitListMgr
{
    internal sealed class ItemView
    {
        private readonly SpawnService _service;
        private readonly string[] _slots;
        private readonly Action _saveSlots;
        private List<string> _all = new List<string>(), _filtered = new List<string>();
        private ItemSource _sources = ItemSource.All;
        private ItemKind _kinds;
        private readonly Dictionary<string, ItemSource> _origins = new Dictionary<string, ItemSource>();
        private readonly Dictionary<string, ItemKind> _types = new Dictionary<string, ItemKind>();
        private int _languageRevision = -1;
        private bool _showFilters;
        private bool _showJournal, _showErrorDetails;
        private Vector2 _errorDetailsScroll;
        private Vector2 _journalScroll, _recordScroll;
        private SpawnRecord _record;
        private string _search = "", _lastSearch, _selected, _error = "";
        private string _count = "1", _condition = "100", _rounds = "0", _battery = "";
        private bool _hasMag, _editBattery, _editLiquids;
        private ItemDefaults _spec;
        private Vector2 _listScroll, _detailScroll;
        private float _detailHeight = 900;
        private readonly List<(string Id, string Amount)> _liquids = new List<(string, string)>();
        private string _picker, _pickSearch = "";
        private int _liquidIndex;
        private float _nextRefresh;
        private int _knownCount = -1;
        private static string T(string zh, string en) => UnitListPlugin.T(zh, en);
        private static string Number(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);
        private static bool Float(string value, out float result) => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) && SpawnRules.Finite(result);
        internal ItemView(SpawnService service, string[] slots, Action saveSlots) { _service = service; _slots = slots; _saveSlots = saveSlots; }

        internal void Draw(Rect area)
        {
            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + 2;
                int count = Item.GlobalItems?.Count ?? 0;
                if (count != _knownCount) Refresh();
            }
            if (_languageRevision != DdenLocalization.Revision) { _languageRevision = DdenLocalization.Revision; _lastSearch = null; }
            if (DdenUI.Button(new Rect(area.x, area.y, 102, 28), T("物品", "Items"), !_showJournal)) _showJournal = false;
            bool wasEnabled = GUI.enabled;
            GUI.enabled = false;
            DdenUI.Button(new Rect(area.x + 108, area.y, 122, 28), T("生物 · 预留", "Creatures · Later"));
            DdenUI.Button(new Rect(area.x + 236, area.y, 122, 28), T("单位 · 预留", "Units · Later"));
            GUI.enabled = wasEnabled;
            if (DdenUI.Button(new Rect(area.x + 364, area.y, 112, 28), T("生成记录", "Spawn history"), _showJournal)) _showJournal = true;
            area = new Rect(area.x, area.y + 38, area.width, Mathf.Max(40, area.height - 38));
            if (!string.IsNullOrEmpty(_service.Status))
            {
                bool failed = _service.Notice == NoticeKind.Failure;
                ResultNotice.Draw(new Rect(area.x, area.y, area.width - (failed ? 66 : 0), 30), _service.Status, _service.Notice);
                if (failed && DdenUI.Button(new Rect(area.xMax - 60, area.y, 60, 30), T("详情", "Details"), _showErrorDetails)) _showErrorDetails = !_showErrorDetails;
                area = new Rect(area.x, area.y + 38, area.width, Mathf.Max(40, area.height - 38));
                if (failed && _showErrorDetails)
                {
                    var details = new Rect(area.x, area.y, area.width, Mathf.Min(100, area.height * 0.3f));
                    var style = new GUIStyle(DdenUI.Sub) { wordWrap = true };
                    float height = Mathf.Max(details.height, style.CalcHeight(new GUIContent(_service.Status), details.width - 28));
                    _errorDetailsScroll = DdenUI.BeginScrollView(details, _errorDetailsScroll, new Rect(0, 0, details.width - 20, height));
                    try { DdenUI.Label(new Rect(4, 0, details.width - 28, height), _service.Status, style); }
                    finally { DdenUI.EndScrollView(); }
                    area = new Rect(area.x, area.y + details.height + 8, area.width, Mathf.Max(40, area.height - details.height - 8));
                }
            }
            if (_showJournal) { DrawJournal(area); return; }
            float leftWidth = Mathf.Max(160, Mathf.Min(area.width * 0.62f, area.width - 290));
            var left = new Rect(area.x, area.y, leftWidth, area.height);
            var right = new Rect(left.xMax + 12, area.y, Mathf.Max(150, area.width - leftWidth - 12), area.height);
            DrawList(left);
            DdenUI.Fill(right, DdenUI.Panel);
            var viewport = new Rect(right.x + 8, right.y + 8, right.width - 16, right.height - 16);
            _detailScroll = DdenUI.BeginScrollView(viewport, _detailScroll, new Rect(0, 0, viewport.width - 20, _detailHeight));
            try { DrawDetail(viewport.width - 20); }
            finally { DdenUI.EndScrollView(); }
        }
        private void Refresh()
        {
            _all = Catalog.Items(); _knownCount = Item.GlobalItems?.Count ?? 0; _lastSearch = null;
            var ccl = Catalog.CclIds(); _origins.Clear(); _types.Clear();
            foreach (string id in _all)
            {
                _origins[id] = ccl.Contains(id) ? ItemSource.Ccl : NativeCatalog.Contains(id) ? ItemSource.Native : ItemSource.Other;
                _types[id] = Catalog.Kind(id);
            }
        }
        private void DrawList(Rect box)
        {
            _search = DdenUI.TextField(new Rect(box.x, box.y, box.width - 128, 27), _search);
            if (DdenUI.Button(new Rect(box.xMax - 122, box.y, 58, 27), T("筛选", "Filters"), _showFilters)) _showFilters = !_showFilters;
            if (DdenUI.Button(new Rect(box.xMax - 58, box.y, 58, 27), T("刷新", "Refresh"))) Refresh();
            float top = box.y + 35;
            if (_lastSearch != _search)
            {
                _lastSearch = _search;
                _filtered = _all.Where(id => ItemFilters.Matches(_origins[id], _types[id], _sources, _kinds)
                    && (id.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0 || Catalog.Name(id).IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
                _listScroll = Vector2.zero;
            }
            DdenUI.Label(new Rect(box.x, top, box.width - 85, 22), T("物品 ", "Items ") + _filtered.Count + " / " + _all.Count, DdenUI.Sub);
            if (DdenUI.Button(new Rect(box.xMax - 80, top, 80, 22), T("重置筛选", "Reset filters"))) { _sources = ItemSource.All; _kinds = 0; _search = ""; _lastSearch = null; }
            top += 28;
            var view = new Rect(box.x, top, box.width, Mathf.Max(25, box.yMax - top));
            const float pitch = 78;
            int columns = Mathf.Max(1, (int)((view.width - 20) / 94));
            float cellWidth = (view.width - 20) / columns;
            int rows = Mathf.CeilToInt(_filtered.Count / (float)columns);
            bool gridEnabled = GUI.enabled;
            GUI.enabled = gridEnabled && !_showFilters;
            _listScroll = DdenUI.BeginScrollView(view, _listScroll, new Rect(0, 0, view.width - 20, rows * pitch));
            try
            {
                int firstRow = Mathf.Max(0, (int)(_listScroll.y / pitch));
                int end = Mathf.Min(_filtered.Count, (firstRow + Mathf.CeilToInt(view.height / pitch) + 2) * columns);
                var nameStyle = new GUIStyle(DdenUI.Sub) { alignment = TextAnchor.MiddleCenter };
                for (int i = firstRow * columns; i < end; i++)
                {
                    string id = _filtered[i];
                    var cell = new Rect(i % columns * cellWidth, i / columns * pitch, cellWidth - 4, pitch - 4);
                    bool selected = id == _selected;
                    bool hovered = cell.Contains(Event.current.mousePosition);
                    DdenUI.Fill(cell, selected || hovered ? DdenUI.PanelAlt : DdenUI.Panel);
                    DdenUI.Frame(cell, selected ? DdenUI.Accent : DdenUI.AccentDim);
                    if (selected) DdenUI.Fill(new Rect(cell.x, cell.y, 3, cell.height), DdenUI.Accent);
                    DdenUI.DrawItemIcon(new Rect(cell.center.x - 19, cell.y + 6, 38, 38), id);
                    DdenUI.Label(new Rect(cell.x + 4, cell.y + 49, cell.width - 8, 20), DdenUI.ElideToWidth(Catalog.Name(id), cell.width - 8, nameStyle), nameStyle);
                    DdenUI.Hover(cell, Catalog.Name(id) + "\n" + id);
                    if (GUI.Button(cell, GUIContent.none, GUIStyle.none)) Select(id);
                }
                if (_filtered.Count == 0) DdenUI.Label(new Rect(8, 8, view.width - 30, 55), T("没有符合条件的物品。\n可取消勾选用途，或重置筛选。", "No matching items.\nClear use filters or reset filters."), DdenUI.Sub);
            }
            finally { DdenUI.EndScrollView(); GUI.enabled = gridEnabled; }
            if (_showFilters) DrawFilterPopup(new Rect(box.x, box.y + 34, box.width, Mathf.Min(72 + Mathf.CeilToInt(9f / Mathf.Max(2, (int)((box.width - 20) / 125))) * 27, box.height - 34)));
        }
        private void DrawFilterPopup(Rect area)
        {
            DdenUI.Fill(area, DdenUI.Bg);
            DdenUI.Frame(area, DdenUI.AccentDim);
            var box = new Rect(area.x + 10, area.y + 8, area.width - 20, area.height - 16);
            float top = box.y;
                var sourceLabels = new[] { T("原版", "Vanilla"), T("CCL 注册", "CCL registered"), T("其他 / 未识别", "Other / Unknown") };
                for (int i = 0; i < 3; i++)
                {
                    ItemSource flag = (ItemSource)(1 << i);
                    var row = new Rect(box.x + i * box.width / 3, top, box.width / 3 - 4, 24);
                    bool selected = (_sources & flag) != 0;
                    bool next = DdenUI.CheckBoxLabeled(row, selected, sourceLabels[i]);
                    if (next != selected) { _sources ^= flag; _lastSearch = null; }
                }
                top += 32;
                var kindLabels = new[] { T("容器", "Containers"), T("工具", "Tools"), T("基础材料", "Materials"), T("医疗 / 药物", "Medical / Drugs"), T("食物", "Food"), T("液体", "Liquids"), T("穿戴", "Wearables"), T("功能用品", "Utility"), T("其他", "Other") };
                int filterColumns = Mathf.Max(2, (int)(box.width / 125));
                float filterWidth = box.width / filterColumns;
                for (int i = 0; i < kindLabels.Length; i++)
                {
                    ItemKind flag = (ItemKind)(1 << i);
                    var row = new Rect(box.x + i % filterColumns * filterWidth, top + i / filterColumns * 27, filterWidth - 4, 24);
                    bool selected = (_kinds & flag) != 0;
                    bool next = DdenUI.CheckBoxLabeled(row, selected, kindLabels[i]);
                    if (next != selected) { _kinds ^= flag; _lastSearch = null; }
                }
                top += Mathf.CeilToInt(kindLabels.Length / (float)filterColumns) * 27 + 4;

            if (DdenUI.Button(new Rect(box.x, top, box.width, 24), T("完成筛选", "Done"))) _showFilters = false;
            if (Event.current.type == EventType.MouseDown && !area.Contains(Event.current.mousePosition)
                && Event.current.mousePosition.y > area.y) _showFilters = false;
        }
        private void Select(string id)
        {
            _selected = id; _error = ""; _spec = null; _picker = null; _detailScroll = Vector2.zero;
            try { _spec = Catalog.Inspect(id); Reset(); }
            catch (Exception ex) { _error = ex.GetBaseException().Message; }
        }
        private void Reset()
        {
            if (_spec == null) return;
            _condition = Number(_spec.Condition * 100); _rounds = (_spec.Rounds ?? 0).ToString(CultureInfo.InvariantCulture);
            _battery = _spec.Battery ?? ""; _hasMag = _spec.HasMagazine; _editBattery = _editLiquids = false;
            _liquids.Clear();
            foreach (var liquid in _spec.Liquids) _liquids.Add((liquid.Id, Number(liquid.Amount)));
            _error = "";
        }
        private void DrawDetail(float width)
        {
            float y = 0;
            void Label(string text, GUIStyle style = null, float height = 23)
            {
                DdenUI.Label(new Rect(0, y, width, height), text, style ?? DdenUI.Row); y += height + 3;
            }
            void InputRow(string label, ref string text)
            {
                DdenUI.Label(new Rect(0, y, width * 0.5f, 25), label, DdenUI.Row);
                text = DdenUI.TextField(new Rect(width * 0.52f, y, width * 0.48f, 25), text); y += 31;
            }
            Label(T("物品工具", "Item tools"), DdenUI.Title);
            if (_selected == null) { Label(T("点击左侧物品查看默认属性。", "Select an item to inspect its defaults.")); return; }
            if (_spec == null) { Label(_selected); Label(_error, DdenUI.Sub, 60); return; }
            DdenUI.DrawItemIcon(new Rect(0, y, 54, 54), _selected);
            DdenUI.Label(new Rect(64, y, width - 64, 28), _spec.Name, DdenUI.Title);
            DdenUI.Label(new Rect(64, y + 28, width - 64, 22), _selected, DdenUI.Sub); y += 65;
            Label(T("默认属性", "Default properties"), DdenUI.Title);
            Label(T("耐久 / 电量：", "Condition / charge: ") + Number(_spec.Condition * 100) + "%", DdenUI.Sub);
            if (_spec.Rounds.HasValue) Label(T("默认弹药：", "Default rounds: ") + _spec.Rounds + " / " + _spec.MaxRounds, DdenUI.Sub);
            if (_spec.HasGun) Label(T("默认枪械：无弹夹，枪膛为空", "Default gun: no magazine, empty chamber"), DdenUI.Sub);
            if (_spec.HasBattery) Label(T("默认电池：", "Default battery: ") + (string.IsNullOrEmpty(_spec.Battery) ? T("无", "None") : Catalog.Name(_spec.Battery)), DdenUI.Sub);
            if (_spec.Capacity > 0)
            {
                Label(T("液体容量：", "Liquid capacity: ") + Number(_spec.Capacity), DdenUI.Sub);
                Label(T("默认内容物：", "Default contents: ") + (_spec.Liquids.Count == 0 ? T("空", "Empty")
                    : string.Join(" + ", _spec.Liquids.Select(x => Catalog.LiquidName(x.Id) + " " + Number(x.Amount)))), DdenUI.Sub, 46);
            }
            y += 5;
            if (DdenUI.Button(new Rect(0, y, width, 25), T("恢复默认属性", "Reset properties to defaults"))) Reset(); y += 34;
            InputRow(T("生成数量（1–20）", "Count (1–20)"), ref _count);
            if (_spec.Capacity <= 0) InputRow(T(_spec.HasBattery ? "电量（%）" : "耐久（%）", _spec.HasBattery ? "Charge (%)" : "Condition (%)"), ref _condition);
            if (_spec.Rounds.HasValue) InputRow(T("子弹数", "Rounds"), ref _rounds);
            if (_spec.HasGun && _spec.UsesMagazine)
            {
                _hasMag = DdenUI.CheckBoxLabeled(new Rect(0, y, width, 24), _hasMag, T("安装弹夹", "Install magazine")); y += 30;
            }
            if (_spec.HasBattery)
            {
                _editBattery = DdenUI.CheckBoxLabeled(new Rect(0, y, width, 24), _editBattery, T("自定义电池种类", "Override battery type")); y += 30;
                if (_editBattery)
                {
                    if (DdenUI.Button(new Rect(0, y, width, 25), string.IsNullOrEmpty(_battery) ? T("无电池", "No battery") : Catalog.Name(_battery)))
                    { _picker = "battery"; _pickSearch = ""; }
                    y += 31;
                }
            }
            if (_spec.Capacity > 0)
            {
                _editLiquids = DdenUI.CheckBoxLabeled(new Rect(0, y, width, 24), _editLiquids, T("编辑液体混合物", "Edit liquid mixture")); y += 30;
                if (_editLiquids)
                {
                    for (int i = 0; i < _liquids.Count; i++)
                    {
                        var entry = _liquids[i];
                        if (DdenUI.Button(new Rect(0, y, width * 0.55f, 25), Catalog.LiquidName(entry.Id))) { _picker = "liquid"; _liquidIndex = i; _pickSearch = ""; }
                        string value = DdenUI.TextField(new Rect(width * 0.57f, y, width * 0.32f - 5, 25), entry.Amount);
                        _liquids[i] = (entry.Id, value);
                        if (DdenUI.Button(new Rect(width * 0.89f, y, width * 0.11f, 25), "×")) { _liquids.RemoveAt(i); _picker = null; i--; }
                        y += 31;
                    }
                    if (_liquids.Count < SpawnRules.MaxLiquids && DdenUI.Button(new Rect(0, y, width, 25), T("添加液体", "Add liquid")))
                    {
                        string id = Catalog.LiquidIds().FirstOrDefault();
                        if (id != null) _liquids.Add((id, "1"));
                    }
                    y += 31;
                    Label(T("留空即为空容器；总量不得超过容量。", "Empty list means empty container; total must fit capacity."), DdenUI.Sub, 38);
                }
            }
            if (_picker != null) DrawPicker(width, ref y);
            y += 6;
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && !_service.Busy && SpawnService.LocalBody != null && SpawnService.CanGive(Mp.IsRunning && !Mp.IsServer);
            try
            {
                if (DdenUI.Button(new Rect(0, y, width, 34), T("生成物品 · 本玩家脚下", "Spawn item · at your feet"), true)) Generate();
            }
            finally { GUI.enabled = enabled; }
            y += 41;
            if (Mp.IsRunning) Label(T("多人：须开启主机作弊；客机另须允许客机作弊。", "MP: host cheats required; guests also need client cheats."), DdenUI.Sub, 42);

            Label(T("快捷轮盘 · 按住 ", "Quick menu · hold ") + UnitListPlugin.QuickKeyLabel, DdenUI.Title, 27);
            Label(T("只使用默认属性，在鼠标位置生成；中心 / Esc 取消。", "Default properties only; spawns at cursor. Center / Esc cancels."), DdenUI.Sub, 42);
            for (int i = 0; i < 8; i++)
            {
                string text = (i + 1) + "  " + (string.IsNullOrWhiteSpace(_slots[i]) ? T("空槽 · 点击分配当前物品", "Empty · assign selected item") : Catalog.Name(_slots[i]));
                if (DdenUI.Button(new Rect(0, y, width - 33, 25), text)) { _slots[i] = _selected; _saveSlots(); }
                if (DdenUI.Button(new Rect(width - 28, y, 28, 25), "×")) { _slots[i] = ""; _saveSlots(); }
                y += 31;
            }
            Label(T("玩家状态编辑：接口预留", "Player-state editing: reserved interface"), DdenUI.Sub);
            _detailHeight = y + 10;
        }
        private void DrawPicker(float width, ref float y)
        {
            _pickSearch = DdenUI.TextField(new Rect(0, y, width - 60, 25), _pickSearch);
            if (DdenUI.Button(new Rect(width - 55, y, 55, 25), T("关闭", "Close"))) _picker = null;
            y += 31;
            if (_picker == null) return;
            var ids = _picker == "battery" ? Catalog.Batteries().Where(id => ((BatteryInfo)Item.GlobalItems[id]).maxCharge <= _spec.BatteryLimit).Prepend("") : Catalog.LiquidIds();
            // Bounded selection page, with a filter for long CCL registries.
            foreach (var id in ids.Where(id => id.IndexOf(_pickSearch, StringComparison.OrdinalIgnoreCase) >= 0
                || (_picker == "battery" ? Catalog.Name(id) : Catalog.LiquidName(id)).IndexOf(_pickSearch, StringComparison.OrdinalIgnoreCase) >= 0).Take(24))
            {
                string title = id.Length == 0 ? T("无电池", "No battery") : (_picker == "battery" ? Catalog.Name(id) : Catalog.LiquidName(id)) + " · " + id;
                if (DdenUI.Button(new Rect(0, y, width, 24), title))
                {
                    if (_picker == "battery") _battery = id;
                    else if (_liquidIndex >= 0 && _liquidIndex < _liquids.Count) _liquids[_liquidIndex] = (id, _liquids[_liquidIndex].Amount);
                    _picker = null; y += 29; break;
                }
                y += 29;
            }
        }
        private void DrawJournal(Rect area)
        {
            var records = _service.Journal.Records;
            DdenUI.Label(new Rect(area.x, area.y, area.width - 90, 24), T("仅内存 · 主机记录全员 · 最多 500 条", "Memory only · host records all players · 500 entries") + " · " + records.Count, DdenUI.Sub);
            if (DdenUI.Button(new Rect(area.xMax - 78, area.y, 78, 24), T("清空记录", "Clear"))) { _service.Journal.Clear(); _record = null; }
            float detailsHeight = _record == null ? 0 : Mathf.Min(160, area.height * 0.4f);
            var view = new Rect(area.x, area.y + 30, area.width, Mathf.Max(40, area.height - 30 - detailsHeight));
            const float rowHeight = 55;
            _journalScroll = DdenUI.BeginScrollView(view, _journalScroll, new Rect(0, 0, view.width - 20, records.Count * rowHeight));
            try
            {
                int first = Mathf.Max(0, (int)(_journalScroll.y / rowHeight));
                int end = Mathf.Min(records.Count, first + Mathf.CeilToInt(view.height / rowHeight) + 2);
                for (int i = first; i < end; i++)
                {
                    var record = records[records.Count - 1 - i];
                    var row = new Rect(0, i * rowHeight, view.width - 20, rowHeight - 4);
                    DdenUI.Fill(row, ReferenceEquals(record, _record) ? DdenUI.PanelAlt : DdenUI.Panel);
                    DdenUI.Fill(new Rect(row.x, row.y, 3, row.height), record.Success ? new Color(0.3f, 0.8f, 0.5f) : DdenUI.Warn);
                    string line = record.Time.ToString("HH:mm:ss") + " · " + record.Player + " · " + record.ItemName + " · " + record.Created + "/" + record.Requested;
                    DdenUI.Label(new Rect(10, row.y + 3, row.width - 18, 23), DdenUI.ElideToWidth(line, row.width - 18), DdenUI.Row);
                    string result = record.Success ? T("成功", "Success") : T("失败 / 未完成：", "Failed / incomplete: ") + record.Detail;
                    DdenUI.Label(new Rect(10, row.y + 26, row.width - 18, 20), DdenUI.ElideToWidth(result, row.width - 18, DdenUI.Sub), DdenUI.Sub);
                    if (GUI.Button(row, GUIContent.none, GUIStyle.none)) { _record = record; _recordScroll = Vector2.zero; }
                }
            }
            finally { DdenUI.EndScrollView(); }
            if (_record == null) return;
            var details = new Rect(area.x, area.yMax - detailsHeight + 5, area.width, detailsHeight - 5);
            string text = _record.Time.ToString("yyyy-MM-dd HH:mm:ss") + " · " + _record.Player + " (#" + _record.Sender + ")\n"
                + _record.ItemName + " [" + _record.ItemId + "] · " + _record.Created + "/" + _record.Requested + " · " + _record.Mode + " (" + Number(_record.X) + ", " + Number(_record.Y) + ")\n"
                + _record.Detail + "\n" + _record.Parameters;
            var style = new GUIStyle(DdenUI.Sub) { wordWrap = true };
            float height = Mathf.Max(details.height, style.CalcHeight(new GUIContent(text), details.width - 28));
            _recordScroll = DdenUI.BeginScrollView(details, _recordScroll, new Rect(0, 0, details.width - 20, height));
            try { DdenUI.Label(new Rect(4, 0, details.width - 28, height), text, style); }
            finally { DdenUI.EndScrollView(); }
        }
        private void Generate()
        {
            _error = "";
            var request = new SpawnRequest { RequestId = Guid.NewGuid().ToString("N"), ItemId = _selected };
            if (!int.TryParse(_count, NumberStyles.Integer, CultureInfo.InvariantCulture, out request.Count)) { _error = T("数量需要整数。", "Count must be an integer."); _service.ShowFailure(_error); return; }
            if (_spec.Capacity <= 0)
            {
                if (!Float(_condition, out float percent)) { _error = T("耐久 / 电量无效。", "Invalid condition / charge."); _service.ShowFailure(_error); return; }
                if (Math.Abs(percent - _spec.Condition * 100) > 0.0001f) request.Condition = percent / 100;
            }
            if (_spec.Rounds.HasValue)
            {
                if (!int.TryParse(_rounds, out int rounds)) { _error = T("子弹数需要整数。", "Rounds must be an integer."); _service.ShowFailure(_error); return; }
                if (rounds != _spec.Rounds) request.Rounds = rounds;
            }
            if (_spec.HasGun && _spec.UsesMagazine && _hasMag != _spec.HasMagazine) request.HasMagazine = _hasMag;
            if (_editBattery) request.Battery = _battery;
            if (_editLiquids)
            {
                request.Liquids = new List<LiquidAmount>();
                foreach (var entry in _liquids)
                {
                    if (!Float(entry.Amount, out float amount)) { _error = T("液体数量无效。", "Invalid liquid amount."); _service.ShowFailure(_error); return; }
                    request.Liquids.Add(new LiquidAmount { Id = entry.Id, Amount = amount });
                }
            }
            _service.Submit(request);
        }
    }
}
