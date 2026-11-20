using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DDEN.Shared;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DDEN.SaveEditor
{
    /// <summary>
    /// 可视化存档编辑界面：左侧存档位 → 中间用图标/血条直接点选 → 右侧精修。
    /// 图标来自 CUCoreLib 注册表（含模组注册的物品图标）与游戏原资源。
    /// </summary>
    internal sealed class EditorView
    {
        private readonly SaveDocument _doc = new SaveDocument();
        private readonly List<TargetRow> _targets = new List<TargetRow>();

        private string _tab = "items";
        private string _selectedPath;
        private string _search = string.Empty;
        private string _conditionBuffer;
        private int _conditionBufferFor = -1;
        private Vector2 _targetScroll, _gridScroll, _rowsScroll, _inspectScroll, _limbScroll;
        private string _flash = string.Empty;

        private static readonly string[] Tabs = { "items", "limbs", "body", "run", "progress" };
        private static readonly string[] TabNames = { "物品", "肢体", "身体", "运行设置", "进度/成就" };

        public SaveDocument Document => _doc;

        private bool _smoke;

        /// <summary>界面冒烟测试用：读取当前存档位，并让所有标签页轮流绘制。</summary>
        public void SmokeLoad()
        {
            RefreshTargets();
            if (!string.IsNullOrEmpty(_selectedPath) && !_doc.Dirty) _doc.Load(_selectedPath);
            _smoke = true;
        }

        public void SmokeStop() => _smoke = false;

        public void RefreshTargets()
        {
            _targets.Clear();
            _targets.AddRange(SaveDocument.BuildTargets());
            if (string.IsNullOrEmpty(_selectedPath) && _targets.Count > 0)
            {
                TargetRow preferred = _targets.FirstOrDefault(t => t.IsCurrent) ?? _targets[0];
                _selectedPath = preferred.Path;
            }
        }

        public void Draw(Rect area)
        {
            if (_targets.Count == 0) RefreshTargets();
            if (_smoke) _tab = Tabs[(Time.frameCount / 45) % Tabs.Length];

            DrawToolbar(new Rect(area.x, area.y, area.width, 30f));
            Rect body = new Rect(area.x, area.y + 34f, area.width, area.height - 34f);

            const float leftW = 290f;
            const float rightW = 310f;
            Rect left = new Rect(body.x, body.y, leftW, body.height);
            Rect right = new Rect(body.xMax - rightW, body.y, rightW, body.height);
            Rect mid = new Rect(left.xMax + 8f, body.y, right.x - left.xMax - 16f, body.height);

            DrawTargets(left);
            DrawTabs(mid);
            DrawInspector(right);
            DdenUI.DrawTooltip();
        }

        // ===== 工具条 =====

        private void DrawToolbar(Rect r)
        {
            DdenUI.Fill(r, DdenUI.Panel);

            float x = r.x + 6f;
            var h = 24f;
            float y = r.y + 3f;

            if (DdenUI.Button(new Rect(x, y, 62f, h), "读取", true))
            {
                if (!string.IsNullOrEmpty(_selectedPath)) _flash = _doc.Load(_selectedPath) ? _doc.Status : _doc.Status;
            }
            x += 66f;

            bool has = _doc.Doc != null;
            GUI.enabled = has;
            if (DdenUI.Button(new Rect(x, y, 76f, h), _doc.Dirty ? "保存 *" : "保存"))
            {
                _flash = _doc.Save();
            }
            x += 80f;

            if (DdenUI.Button(new Rect(x, y, 74f, h), "满治疗")) { _doc.PresetFullHeal(); _flash = _doc.Status; }
            x += 78f;
            if (DdenUI.Button(new Rect(x, y, 74f, h), "计时归零")) { _doc.PresetZeroTime(); _flash = _doc.Status; }
            x += 78f;
            if (DdenUI.Button(new Rect(x, y, 82f, h), "负值清零")) { _doc.PresetCleanNegative(); _flash = _doc.Status; }
            x += 86f;
            if (DdenUI.Button(new Rect(x, y, 110f, h), "同步到角色"))
            {
                int n = _doc.ApplyToLiveBody();
                _flash = n < 0 ? "当前不在世界内，无法写回。"
                    : $"已写回 {n} 个字段到当前角色（内存，不改存档文件）" + (Mp.IsRunning ? "；联机中可能被主机回滚" : "");
            }
            x += 114f;
            GUI.enabled = true;

            if (DdenUI.Button(new Rect(x, y, 60f, h), "刷新")) RefreshTargets();
            x += 64f;

            GUI.Label(new Rect(x, y + 4f, 210f, 18f),
                Path.GetFileName(_selectedPath ?? "-") + (_doc.Dirty ? "  <color=#f0a>*未保存</color>" : string.Empty), DdenUI.Sub);
            x += 216f;

            Rect searchRect = new Rect(r.xMax - 210f, y + 2f, 200f, h - 4f);
            GUI.Label(new Rect(searchRect.x - 44f, y + 4f, 42f, 18f), "筛选", DdenUI.Small);
            string nv = GUI.TextField(searchRect, _search ?? string.Empty);
            if (nv != _search)
            {
                _search = nv;
                if (_selectedPath != null) { _doc.ItemFilter = nv; _doc.RefreshVisibleItems(); }
            }
        }

        // ===== 左：目标 =====

        private void DrawTargets(Rect r)
        {
            DdenUI.Fill(r, DdenUI.Panel);
            DdenUI.Frame(r, DdenUI.AccentDim);
            DdenUI.Section(new Rect(r.x + 6f, r.y + 2f, r.width - 12f, 16f), "存档位（QoL 多存档）");

            Rect list = new Rect(r.x + 4f, r.y + 20f, r.width - 8f, r.height - 24f);
            float rowH = 40f;
            _targetScroll = GUI.BeginScrollView(list, _targetScroll,
                new Rect(0, 0, list.width - 16f, Mathf.Max(list.height, _targets.Count * rowH + 8f)));

            float y = 0f;
            foreach (TargetRow t in _targets)
            {
                bool selected = string.Equals(t.Path, _selectedPath, StringComparison.OrdinalIgnoreCase);
                var cell = new Rect(0, y, list.width - 16f, rowH - 4f);
                DdenUI.Fill(cell, selected ? DdenUI.AccentDim : DdenUI.PanelAlt);
                if (selected) DdenUI.Frame(cell, DdenUI.Accent);

                DdenUI.Label(new Rect(cell.x + 6f, cell.y + 3f, cell.width - 12f, 16f),
                    DdenUI.ElideToWidth(t.Label, cell.width - 16f, DdenUI.Row) + (t.IsCurrent ? "  <color=#8cd97c>← 当前</color>" : string.Empty), DdenUI.Row);
                DdenUI.Label(new Rect(cell.x + 6f, cell.y + 19f, cell.width - 12f, 14f), t.Kind, DdenUI.Sub);

                if (GUI.Button(cell, GUIContent.none, GUIStyle.none))
                {
                    if (_doc.Dirty)
                    {
                        _flash = "当前存档有未保存修改，先保存或读取别的档。";
                    }
                    else
                    {
                        _selectedPath = t.Path;
                        _doc.ItemFilter = _search;
                        _flash = _doc.Load(t.Path) ? _doc.Status : _doc.Status;
                    }
                }
                y += rowH;
            }
            GUI.EndScrollView();
        }

        // ===== 中：标签页 =====

        private void DrawTabs(Rect r)
        {
            float tabW = r.width / Tabs.Length;
            for (int i = 0; i < Tabs.Length; i++)
            {
                var tabRect = new Rect(r.x + i * tabW, r.y, tabW - 3f, 26f);
                bool on = _tab == Tabs[i];
                DdenUI.Fill(tabRect, on ? DdenUI.AccentDim : DdenUI.Panel);
                DdenUI.Frame(tabRect, on ? DdenUI.Accent : DdenUI.AccentDim);
                DdenUI.Label(tabRect, TabNames[i], DdenUI.TabStyle);
                if (GUI.Button(tabRect, GUIContent.none, GUIStyle.none)) _tab = Tabs[i];
            }

            Rect content = new Rect(r.x, r.y + 30f, r.width, r.height - 30f);
            DdenUI.Fill(content, DdenUI.Panel);

            if (_doc.Doc == null)
            {
                DdenUI.Label(new Rect(content.x + 10f, content.y + 10f, content.width - 20f, 60f),
                    "左侧选一个存档位 → 点工具条“读取”。\n物品页可以直接点图标改耐久；进度页可以改“是否禁用成就”。", DdenUI.Wrap);
                return;
            }

            switch (_tab)
            {
                case "items": DrawItems(content); break;
                case "limbs": DrawLimbs(content); break;
                case "body": DrawRowList(content, _doc.BodyRows.Concat(_doc.SkillRows).ToList(), "身体 / 技能"); break;
                case "run": DrawRowList(content, _doc.RunSettings, "运行设置"); break;
                default: DrawProgress(content); break;
            }
        }

        // ===== 物品（图标网格）=====

        private void DrawItems(Rect r)
        {
            DdenUI.Section(new Rect(r.x + 8f, r.y + 2f, r.width - 16f, 16f),
                $"物品 {_doc.Items.Count} 件 / {_doc.VisibleGroups.Count} 组（按容器归组：背包里的东西挂在背包下面）");

            Rect grid = new Rect(r.x + 6f, r.y + 22f, r.width - 12f, r.height - 26f);
            const float headH = 30f;
            const float cellW = 62f;
            const float cellH = 70f;
            int cols = Mathf.Max(1, Mathf.FloorToInt((grid.width - 20f) / cellW));

            float total = 0f;
            foreach (ItemGroup g in _doc.VisibleGroups)
            {
                int children = g.IsContainer ? g.Count - 1 : 0;
                total += headH + Mathf.CeilToInt(children / (float)cols) * cellH + 6f;
            }

            _gridScroll = GUI.BeginScrollView(grid, _gridScroll, new Rect(0, 0, grid.width - 18f, Mathf.Max(total, grid.height)));

            float y = 0f;
            foreach (ItemGroup group in _doc.VisibleGroups)
            {
                bool headSelected = _doc.SelectedItem == group.Head.Index;
                var head = new Rect(0, y, grid.width - 18f, headH - 4f);
                DdenUI.Fill(head, headSelected ? DdenUI.AccentDim : DdenUI.PanelAlt);
                DdenUI.Frame(head, headSelected ? DdenUI.Accent : DdenUI.AccentDim);

                DdenUI.DrawSprite(new Rect(head.x + 3f, head.y + 2f, 22f, 22f), DdenUI.ItemIcon(group.Head.Id));
                string count = group.IsContainer ? $"  <color=#8fa>×{group.Count}</color>" : string.Empty;
                float labelW = head.width - 252f;
                GUI.Label(new Rect(head.x + 30f, head.y + 5f, labelW, 18f),
                    DdenUI.ElideToWidth(group.Label, labelW, DdenUI.Row) + count, DdenUI.Row);
                DdenUI.Bar(new Rect(head.xMax - 172f, head.y + 9f, 96f, 8f), group.Head.ConditionValue,
                    DdenUI.HealthColor(group.Head.ConditionValue));
                GUI.Label(new Rect(head.xMax - 72f, head.y + 5f, 68f, 16f), group.Head.SlotText, DdenUI.IconStyle);

                if (GUI.Button(head, GUIContent.none, GUIStyle.none))
                {
                    _doc.SelectedItem = group.Head.Index;
                    _conditionBuffer = null;
                    _flash = $"{group.Head.Id}（{group.Head.SlotText}）";
                }
                y += headH;

                if (!group.IsContainer) continue;

                for (int i = 1; i < group.Count; i++)
                {
                    ItemView child = group.Items[i];
                    int slotIndex = i - 1;
                    int cx = slotIndex % cols;
                    int cy = slotIndex / cols;
                    DrawItemCell(new Rect(14f + cx * cellW + 2f, y + cy * cellH + 2f, cellW - 6f, cellH - 8f), child);
                }
                int rows = Mathf.CeilToInt((group.Count - 1) / (float)cols);
                y += rows * cellH + 6f;
            }

            GUI.EndScrollView();
        }

        private void DrawItemCell(Rect cell, ItemView item)
        {
            float cond = item.ConditionValue;
            Color border = DdenUI.HealthColor(cond);
            bool selected = _doc.SelectedItem == item.Index;

            var iconRect = new Rect(cell.x + 3f, cell.y + 2f, cell.width - 6f, 46f);
            if (DdenUI.IconCell(iconRect, item.Id, border, selected, out _, item.Id + "  ·  " + item.SlotText))
            {
                _doc.SelectedItem = item.Index;
                _conditionBuffer = null;
                _flash = $"{item.Id}（{item.SlotText}）";
            }

            DdenUI.Bar(new Rect(cell.x + 3f, cell.y + 49f, cell.width - 6f, 6f), cond, border, $"{cond * 100f:0}%");
            GUI.Label(new Rect(cell.x, cell.y + 56f, cell.width, 14f),
                DdenUI.ElideToWidth(item.Id, cell.width - 2f, DdenUI.IconStyle), DdenUI.IconStyle);
        }

        /// <summary>长名字中间截断（保留尾部便于区分），宽度由调用方给出。</summary>
        private static string Elide(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s ?? string.Empty;
            int head = Mathf.Max(3, max - 4);
            return s.Substring(0, head) + "…" + s.Substring(s.Length - 3);
        }

        // ===== 肢体（血条网格）=====

        private void DrawLimbs(Rect r)
        {
            DdenUI.Section(new Rect(r.x + 8f, r.y + 2f, r.width - 16f, 16f), "肢体（外=皮肤 内=肌肉；点格子选中）");

            Rect grid = new Rect(r.x + 6f, r.y + 22f, r.width - 12f, Mathf.Min(240f, r.height * 0.55f));
            const float cw = 92f;
            const float ch = 52f;
            int cols = Mathf.Max(1, Mathf.FloorToInt(grid.width / cw));
            int rows = Mathf.CeilToInt(_doc.Limbs.Count / (float)cols);

            _limbScroll = GUI.BeginScrollView(grid, _limbScroll,
                new Rect(0, 0, grid.width - 18f, Mathf.Max(rows * ch + 4f, grid.height)));

            for (int i = 0; i < _doc.Limbs.Count; i++)
            {
                SaveDocument.LimbView limb = _doc.Limbs[i];
                int cx = i % cols, cy = i / cols;
                var cell = new Rect(cx * cw + 2f, cy * ch + 2f, cw - 6f, ch - 6f);
                bool selected = _doc.SelectedLimb == limb.Index;

                DdenUI.Fill(cell, selected ? DdenUI.AccentDim : DdenUI.PanelAlt);
                DdenUI.Frame(cell, selected ? DdenUI.Accent : DdenUI.AccentDim);

                float skin = limb.Skin != null ? (float)limb.Skin.Number / 100f : 1f;
                float muscle = limb.Muscle != null ? (float)limb.Muscle.Number / 100f : 1f;

                GUI.Label(new Rect(cell.x + 4f, cell.y + 1f, cell.width - 8f, 14f), limb.Label, DdenUI.Row);
                DdenUI.Bar(new Rect(cell.x + 4f, cell.y + 18f, cell.width - 8f, 7f), skin, DdenUI.HealthColor(skin));
                DdenUI.Bar(new Rect(cell.x + 4f, cell.y + 28f, cell.width - 8f, 7f), muscle, DdenUI.HealthColor(muscle));
                GUI.Label(new Rect(cell.x + 4f, cell.y + 36f, cell.width - 8f, 12f),
                    $"{skin * 100f:0} / {muscle * 100f:0}", DdenUI.IconStyle);

                if (GUI.Button(cell, GUIContent.none, GUIStyle.none)) _doc.SelectedLimb = limb.Index;
            }

            GUI.EndScrollView();

            Rect hint = new Rect(r.x + 6f, grid.yMax + 4f, r.width - 12f, Mathf.Max(30f, r.yMax - grid.yMax - 8f));
            DdenUI.Label(hint, "右侧可精修选中部位的数值与状态（骨折/脱臼/感染/截断…）。\n未进入世界时部位用序号显示，进世界后会换成游戏内部位名。", DdenUI.Wrap);
        }

        // ===== 通用数值列表 =====

        private void DrawRowList(Rect r, List<FieldRow> rows, string title)
        {
            DdenUI.Section(new Rect(r.x + 8f, r.y + 2f, r.width - 16f, 16f), $"{title}（{rows.Count} 项）");

            Rect list = new Rect(r.x + 6f, r.y + 22f, r.width - 12f, r.height - 26f);
            const float rowH = 24f;
            _rowsScroll = GUI.BeginScrollView(list, _rowsScroll,
                new Rect(0, 0, list.width - 18f, Mathf.Max(list.height, rows.Count * rowH + 6f)));

            float y = 0f;
            foreach (FieldRow row in rows)
            {
                var line = new Rect(0, y, list.width - 18f, rowH - 3f);
                if (((int)(y / rowH)) % 2 == 0) DdenUI.Fill(line, DdenUI.PanelAlt);

                GUI.Label(new Rect(line.x + 4f, line.y + 2f, line.width * 0.56f, 18f), DdenUI.ElideToWidth(row.Label, line.width * 0.56f - 8f, DdenUI.Row), DdenUI.Row);

                switch (row.Kind)
                {
                    case RowKind.Bool:
                    {
                        bool nv = GUI.Toggle(new Rect(line.x + line.width * 0.58f, line.y + 1f, 90f, 18f), row.BoolValue,
                            row.BoolValue ? " 开" : " 关", GUI.skin.toggle);
                        if (row.ApplyBool(nv)) _doc.Dirty = true;
                        break;
                    }
                    case RowKind.Number:
                    {
                        string nv = GUI.TextField(new Rect(line.x + line.width * 0.58f, line.y, 120f, 19f), row.Buffer ?? "0");
                        if (row.ApplyText(nv)) _doc.Dirty = true;
                        break;
                    }
                    default:
                    {
                        string nv = GUI.TextField(new Rect(line.x + line.width * 0.58f, line.y, line.width * 0.4f, 19f), row.Buffer ?? string.Empty);
                        if (row.ApplyText(nv)) _doc.Dirty = true;
                        break;
                    }
                }
                y += rowH;
            }
            GUI.EndScrollView();
        }

        // ===== 进度 + 成就 =====

        private void DrawProgress(Rect r)
        {
            float y = r.y + 4f;

            // 成就卡片
            var card = new Rect(r.x + 6f, y, r.width - 12f, 82f);
            DdenUI.Fill(card, DdenUI.PanelAlt);
            DdenUI.Frame(card, DdenUI.Warn);

            GUI.Label(new Rect(card.x + 8f, card.y + 6f, card.width - 168f, 20f), "是否禁用成就（cheatedSave）", DdenUI.Row);

            if (_doc.CheatFlag != null)
            {
                bool v = _doc.CheatFlag.BoolValue;
                bool nv = GUI.Toggle(new Rect(card.xMax - 152f, card.y + 5f, 144f, 20f), v,
                    v ? " 已禁用成就" : " 成就可用", GUI.skin.toggle);
                if (nv != v && _doc.CheatFlag.ApplyBool(nv))
                {
                    _doc.Dirty = true;
                    _flash = nv ? "已标记：本档禁用成就" : "已解除：本档恢复计入成就";
                }
            }
            else
            {
                GUI.Label(new Rect(card.xMax - 152f, card.y + 5f, 144f, 20f), "该档无此字段", DdenUI.Sub);
            }

            GUI.Label(new Rect(card.x + 8f, card.y + 28f, card.width - 16f, 46f),
                "本编辑器不会自动改它：想保住成就就别勾；勾上＝这份存档被判定为作弊档。\n改动只在你点保存后生效。", DdenUI.Wrap);

            y += 88f;

            if (DdenUI.Button(new Rect(r.x + 6f, y, 120f, 24f), "刷新到界面", false))
            {
                _doc.Rebuild();
                _flash = "已从文档重建界面（未写盘）";
            }
            y += 30f;

            Rect list = new Rect(r.x + 6f, y, r.width - 12f, r.yMax - y - 4f);
            const float rowH = 24f;
            _rowsScroll = GUI.BeginScrollView(list, _rowsScroll,
                new Rect(0, 0, list.width - 18f, Mathf.Max(list.height, _doc.Progress.Count * rowH + 6f)));

            float ry = 0f;
            foreach (FieldRow row in _doc.Progress)
            {
                var line = new Rect(0, ry, list.width - 18f, rowH - 3f);
                if (((int)(ry / rowH)) % 2 == 0) DdenUI.Fill(line, DdenUI.PanelAlt);
                GUI.Label(new Rect(line.x + 4f, line.y + 2f, line.width * 0.56f, 18f), DdenUI.ElideToWidth(row.Label, line.width * 0.56f - 8f, DdenUI.Row), DdenUI.Row);
                if (row.Kind == RowKind.Bool)
                {
                    bool nv = GUI.Toggle(new Rect(line.x + line.width * 0.58f, line.y + 1f, 90f, 18f), row.BoolValue,
                        row.BoolValue ? " 开" : " 关", GUI.skin.toggle);
                    if (row.ApplyBool(nv)) _doc.Dirty = true;
                }
                else
                {
                    string nv = GUI.TextField(new Rect(line.x + line.width * 0.58f, line.y, 130f, 19f), row.Buffer ?? "0");
                    if (row.ApplyText(nv)) _doc.Dirty = true;
                }
                ry += rowH;
            }
            GUI.EndScrollView();
        }

        // ===== 右：检查器 =====

        private void DrawInspector(Rect r)
        {
            DdenUI.Fill(r, DdenUI.Panel);
            DdenUI.Frame(r, DdenUI.AccentDim);

            if (_doc.Doc == null)
            {
                DdenUI.Label(new Rect(r.x + 8f, r.y + 8f, r.width - 16f, 40f), "没有已读取的存档。", DdenUI.Wrap);
                return;
            }

            Rect inner = new Rect(r.x + 8f, r.y + 6f, r.width - 16f, r.height - 12f);

            if (_tab == "items") DrawItemInspector(inner);
            else if (_tab == "limbs") DrawLimbInspector(inner);
            else DrawFileInspector(inner);

            if (!string.IsNullOrEmpty(_flash))
            {
                GUI.Label(new Rect(r.x + 8f, r.yMax - 46f, r.width - 16f, 40f), _flash, DdenUI.Wrap);
            }
        }

        private void DrawItemInspector(Rect r)
        {
            ItemView item = _doc.FindItem(_doc.SelectedItem);
            if (item == null)
            {
                DdenUI.Label(r, "在左侧点一个物品图标来编辑。\n\n可直接改耐久、收藏、标签，或整件移除。", DdenUI.Wrap);
                if (DdenUI.Button(new Rect(r.x, r.y + 70f, r.width, 26f), "全部物品耐久 100%"))
                {
                    _doc.AllItemsFullCondition();
                    _flash = _doc.Status;
                }
                return;
            }

            float y = r.y;
            DdenUI.Label(new Rect(r.x, y, r.width, 20f), DdenUI.ElideToWidth(item.Id, r.width - 8f, DdenUI.Title), DdenUI.Title);
            y += 24f;

            var iconBox = new Rect(r.x, y, 84f, 84f);
            DdenUI.Fill(iconBox, DdenUI.PanelAlt);
            DdenUI.Frame(iconBox, DdenUI.HealthColor(item.ConditionValue));
            if (!DdenUI.DrawSprite(new Rect(iconBox.x + 4f, iconBox.y + 4f, iconBox.width - 8f, iconBox.height - 8f), DdenUI.ItemIcon(item.Id)))
            {
                GUI.Label(iconBox, "无图标", DdenUI.Small);
            }

            GUI.Label(new Rect(r.x + 94f, y, r.width - 94f, 18f), "位置：" + item.SlotText, DdenUI.Sub);
            GUI.Label(new Rect(r.x + 94f, y + 20f, r.width - 94f, 18f), "索引：#" + item.Index, DdenUI.Sub);
            GUI.Label(new Rect(r.x + 94f, y + 40f, r.width - 94f, 18f), $"耐久 {item.ConditionValue * 100f:0.0}%", DdenUI.Sub);
            y += 92f;

            DdenUI.Section(new Rect(r.x, y, r.width, 16f), "耐久");
            y += 20f;

            float cond = item.ConditionValue;
            float nv = GUI.HorizontalSlider(new Rect(r.x, y + 6f, r.width - 60f, 16f), cond, 0f, 1f);
            if (Math.Abs(nv - cond) > 0.001f)
            {
                _doc.SetItemCondition(item, nv);
                _conditionBuffer = null;
            }
            if (DdenUI.Button(new Rect(r.xMax - 54f, y, 54f, 22f), "拉满")) _doc.SetItemCondition(item, 1f);
            y += 28f;

            if (_conditionBufferFor != item.Index || _conditionBuffer == null)
            {
                _conditionBuffer = cond.ToString("0.###");
                _conditionBufferFor = item.Index;
            }
            string txt = GUI.TextField(new Rect(r.x, y, 90f, 20f), _conditionBuffer);
            if (txt != _conditionBuffer)
            {
                _conditionBuffer = txt;
                if (float.TryParse(txt, out float v)) _doc.SetItemCondition(item, Mathf.Clamp01(v));
            }
            GUI.Label(new Rect(r.x + 96f, y + 1f, 160f, 18f), "0=报废 1=全新", DdenUI.Small);
            y += 28f;

            DdenUI.Section(new Rect(r.x, y, r.width, 16f), "标记");
            y += 20f;

            if (item.Favourited != null)
            {
                bool fav = GUI.Toggle(new Rect(r.x, y, r.width, 20f), item.Favourited.BoolValue, " 收藏（favourited）", GUI.skin.toggle);
                if (item.Favourited.ApplyBool(fav)) _doc.Dirty = true;
            }
            y += 24f;

            if (item.Label != null)
            {
                GUI.Label(new Rect(r.x, y, 60f, 18f), "标签", DdenUI.Sub);
                string label = GUI.TextField(new Rect(r.x + 62f, y - 1f, r.width - 62f, 20f), item.Label.Buffer ?? string.Empty);
                if (item.Label.ApplyText(label)) _doc.Dirty = true;
            }
            y += 28f;

            if (DdenUI.Button(new Rect(r.x, y, r.width, 24f), "移除这件物品（含组件槽位同步删除）"))
            {
                _doc.DeleteItem(item);
                _flash = _doc.Status;
            }
        }

        private void DrawLimbInspector(Rect r)
        {
            SaveDocument.LimbView limb = _doc.Limbs.FirstOrDefault(l => l.Index == _doc.SelectedLimb);
            if (limb == null)
            {
                DdenUI.Label(r, "在左侧点一个肢体格子来精修。", DdenUI.Wrap);
                return;
            }

            DdenUI.Label(new Rect(r.x, r.y, r.width, 20f), limb.Label + $"  <size=10><color=#89a>#{limb.Index}</color></size>", DdenUI.Title);

            Rect list = new Rect(r.x, r.y + 26f, r.width, r.height - 30f);
            _inspectScroll = GUI.BeginScrollView(list, _inspectScroll,
                new Rect(0, 0, list.width - 18f, Mathf.Max(list.height, limb.Rows.Count * 26f)));

            float y = 0f;
            foreach (FieldRow row in limb.Rows)
            {
                GUI.Label(new Rect(0, y + 2f, list.width * 0.5f, 18f), DdenUI.ElideToWidth(row.Label, list.width * 0.5f - 6f, DdenUI.Row), DdenUI.Row);
                if (row.Kind == RowKind.Bool)
                {
                    bool nv = GUI.Toggle(new Rect(list.width * 0.52f, y + 1f, 80f, 18f), row.BoolValue,
                        row.BoolValue ? " 是" : " 否", GUI.skin.toggle);
                    if (row.ApplyBool(nv)) _doc.Dirty = true;
                }
                else if (row.Kind == RowKind.Number)
                {
                    string nv = GUI.TextField(new Rect(list.width * 0.52f, y, 100f, 19f), row.Buffer ?? "0");
                    if (row.ApplyText(nv)) _doc.Dirty = true;
                }
                y += 26f;
            }
            GUI.EndScrollView();
        }

        private void DrawFileInspector(Rect r)
        {
            float y = r.y;
            GUI.Label(new Rect(r.x, y, r.width, 20f), "存档信息", DdenUI.Title);
            y += 26f;

            var info = new List<string> { "文件：" + Path.GetFileName(_doc.Path ?? "-") };
            try
            {
                if (_doc.Path != null && File.Exists(_doc.Path))
                {
                    var fi = new FileInfo(_doc.Path);
                    info.Add($"大小：{fi.Length / 1024f:0.0} KB");
                    info.Add("修改：" + fi.LastWriteTime.ToString("MM-dd HH:mm"));
                }
                info.Add("多人 bundle：" + (SaveCodec.GetMpBundleDir(_doc.Path) != null ? "有（含每人快照）" : "无"));
            }
            catch { /* ignored */ }

            foreach (string line in info)
            {
                GUI.Label(new Rect(r.x, y, r.width, 18f), line, DdenUI.Sub);
                y += 18f;
            }

            y += 8f;
            DdenUI.Section(new Rect(r.x, y, r.width, 16f), "提示");
            y += 22f;
            GUI.Label(new Rect(r.x, y, r.width, 120f),
                "· 改完点“保存”，会先备份成 *.sv.cumod.bak。\n" +
                "· 正在游玩的档改完请回主菜单再读档。\n" +
                "· 联机时只改本地文件，主机世界不受影响。\n" +
                "· 成就标记只在“进度/成就”页手动改。", DdenUI.Wrap);
        }
    }
}
