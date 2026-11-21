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
    internal sealed partial class EditorView
    {
        private readonly SaveDocument _doc = new SaveDocument { LiquidCapacity = ItemCatalog.LiquidCapacity, IsLiquidKnown = ItemCatalog.IsLiquidKnown,
            BatteryLimit = ItemCatalog.BatteryLimit, IsBatteryKnown = id => ItemCatalog.BatteryCharge(id).HasValue };
        private readonly List<TargetRow> _targets = new List<TargetRow>();

        private string _tab = "items";
        private string _selectedPath;
        private string _search = string.Empty;
        private string _conditionBuffer;
        private int _conditionBufferFor = -1;
        private Vector2 _targetScroll, _gridScroll, _rowsScroll, _limbScroll;
        private string _flash = string.Empty;

        private static readonly string[] Tabs = { "items", "limbs", "body", "skills", "run", "progress" };
        

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

        /// <summary>界面冒烟：停在人体图上，方便截图核对（不再自动轮播）。</summary>
        public void SmokeLimb()
        {
            _smoke = false;
            _tab = "limbs";
            _section = "character"; _characterPage = "limbs";
            _limbDiagram = true;
        }

        public void RefreshTargets()
        {
            _targetsScanned = true;
            _targets.Clear();
            _targets.AddRange(SaveDocument.BuildTargets());
            if (string.IsNullOrEmpty(_selectedPath) && _targets.Count > 0)
            {
                TargetRow preferred = _targets.FirstOrDefault(t => t.IsCurrent) ?? _targets[0];
                _selectedPath = preferred.Path;
            }
        }

        private void DrawItems(Rect r)
        {
            DdenUI.Section(new Rect(r.x + 8f, r.y + 2f, r.width - 16f, 16f),
                $"物品 {_doc.Items.Count} 件 · {_doc.VisibleGroups.Count} 组");

            Rect grid = new Rect(r.x + 6f, r.y + 22f, r.width - 12f, r.height - 26f);
            const float headH = 30f;
            const float cellW = 62f;
            const float cellH = 70f;
            int cols = Mathf.Max(1, Mathf.FloorToInt((grid.width - 36f) / cellW));

            float total = 0f;
            foreach (ItemGroup g in _doc.VisibleGroups)
            {
                int children = g.IsContainer ? g.Count - 1 : 0;
                total += headH + Mathf.CeilToInt(children / (float)cols) * cellH + 6f;
            }

            _gridScroll = DdenUI.BeginScrollView(grid, _gridScroll, new Rect(0, 0, grid.width - 18f, Mathf.Max(total, grid.height)));
            try
            {

            float y = 0f;
            foreach (ItemGroup group in _doc.VisibleGroups)
            {
                bool headSelected = _doc.SelectedItem == group.Head.Index;
                var head = new Rect(0, y, grid.width - 18f, headH - 4f);
                DdenUI.Fill(head, headSelected ? DdenUI.AccentDim : DdenUI.PanelAlt);
                DdenUI.Frame(head, headSelected ? DdenUI.Accent : DdenUI.AccentDim);

                DdenUI.DrawItemIcon(new Rect(head.x + 3f, head.y + 2f, 22f, 22f), group.Head.Id);
                string count = group.IsContainer ? $" · {group.Count} 件" : string.Empty;
                float labelW = Mathf.Max(50f, head.width - 184f);
                GUI.Label(new Rect(head.x + 30f, head.y + 5f, labelW, 18f),
                    DdenUI.ElideToWidth(EditorLabels.Item(group.Head.Id) + count, labelW, DdenUI.Row), DdenUI.Row);
                DdenUI.Hover(head, EditorLabels.Item(group.Head.Id) + "\n编号：" + group.Head.Id + " · " + group.Head.SlotText);
                DdenUI.Bar(new Rect(head.xMax - 142f, head.y + 9f, 66f, 8f), group.Head.ConditionValue,
                    DdenUI.HealthColor(group.Head.ConditionValue));
                GUI.Label(new Rect(head.xMax - 72f, head.y + 5f, 68f, 16f), group.Head.SlotText, DdenUI.IconStyle);

                if (GUI.Button(head, GUIContent.none, GUIStyle.none))
                {
                    _doc.SelectedItem = group.Head.Index; if (_compact) _detail = true; _detailScroll = Vector2.zero;
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

            }
            finally { DdenUI.EndScrollView(); }
        }

        private void DrawItemCell(Rect cell, ItemView item)
        {
            float cond = item.ConditionValue;
            Color border = DdenUI.HealthColor(cond);
            bool selected = _doc.SelectedItem == item.Index;

            var iconRect = new Rect(cell.x + 3f, cell.y + 2f, cell.width - 6f, 46f);
            if (DdenUI.IconCell(iconRect, item.Id, border, selected, out _, EditorLabels.Item(item.Id) + "\n编号：" + item.Id + " · " + item.SlotText))
            {
                _doc.SelectedItem = item.Index; if (_compact) _detail = true; _detailScroll = Vector2.zero;
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
            DdenUI.Section(new Rect(r.x + 8f, r.y + 2f, r.width - 110f, 16f),
                _limbDiagram ? "点选人体部位" : "肢体健康 · 皮肤 / 肌肉");

            // 图示 / 列表 切换 + 左右侧额外翻转
            if (DdenUI.Button(new Rect(r.xMax - 96f, r.y + 1f, 46f, 18f), _limbDiagram ? "列表" : "图示"))
            {
                _limbDiagram = !_limbDiagram;
            }
            if (_limbDiagram)
            {
                if (DdenUI.Button(new Rect(r.xMax - 190f, r.y + 1f, 44f, 18f),
                        LimbLayout.FlipLeftSide ? "左翻✓" : "左翻", LimbLayout.FlipLeftSide))
                {
                    LimbLayout.ToggleFlip(false);
                }
                if (DdenUI.Button(new Rect(r.xMax - 142f, r.y + 1f, 44f, 18f),
                        LimbLayout.FlipRightSide ? "右翻✓" : "右翻", LimbLayout.FlipRightSide))
                {
                    LimbLayout.ToggleFlip(true);
                }
            }

            if (!_limbDiagram)
            {
                DrawLimbGrid(r);
                return;
            }

            EnsureLimbLayout();

            Rect area = new Rect(r.x + 6f, r.y + 22f, r.width - 12f, r.height - 28f);
            float availH = Mathf.Max(120f, area.height - 74f);
            float boxW = area.width - 8f;
            float boxH = boxW / Mathf.Max(0.15f, LimbLayout.Aspect);
            if (boxH > availH)
            {
                boxH = availH;
                boxW = boxH * Mathf.Max(0.15f, LimbLayout.Aspect);
            }

            var canvas = new Rect(area.x + (area.width - boxW) * 0.5f, area.y, boxW, boxH);
            DdenUI.Fill(canvas, new Color(0f, 0f, 0f, 0.28f));
            DdenUI.Frame(canvas, DdenUI.AccentDim);

            foreach (LimbPart part in LimbLayout.Parts)
            {
                SaveDocument.LimbView limb = _doc.Limbs.FirstOrDefault(l => l.Index == part.Index);
                if (limb == null) continue;

                var pr = new Rect(
                    canvas.x + part.Norm.x * canvas.width,
                    canvas.y + part.Norm.y * canvas.height,
                    Mathf.Max(6f, part.Norm.width * canvas.width),
                    Mathf.Max(6f, part.Norm.height * canvas.height));

                float skin = limb.Skin != null ? (float)limb.Skin.Number / 100f : 1f;
                float muscle = limb.Muscle != null ? (float)limb.Muscle.Number / 100f : 1f;
                float hp = Mathf.Clamp01(Mathf.Min(skin, muscle));
                Color col = DdenUI.HealthColor(hp);
                bool selected = _doc.SelectedLimb == limb.Index;

                if (part.Tex != null || part.Sprite != null)
                {
                    // 有原版美术就叠一层状态色；左右镜像用预翻好的贴图（IMGUI 负宽度不可靠）
                    var tint = Color.Lerp(Color.white, col, 0.40f);
                    Texture2D tex = LimbLayout.TexFor(part);
                    bool drew = tex != null
                        ? DdenUI.DrawTextureUv(pr, tex, part.Uv, tint, false)
                        : DdenUI.DrawSprite(pr, part.Sprite, tint, LimbLayout.MirrorFor(part));
                    if (!drew)
                    {
                        DdenUI.Fill(pr, new Color(col.r, col.g, col.b, 0.30f + 0.25f * (1f - hp)));
                    }
                }
                else
                {
                    DdenUI.Fill(pr, new Color(col.r, col.g, col.b, 0.30f + 0.25f * (1f - hp)));
                }
                DdenUI.Frame(pr, selected ? DdenUI.Accent : new Color(col.r, col.g, col.b, 0.85f), selected ? 2f : 1f);

                string tip = $"{part.Name}  #{limb.Index}\n皮肤 {skin * 100f:0}   肌肉 {muscle * 100f:0}";
                DdenUI.Hover(pr, tip);
                if (GUI.Button(pr, GUIContent.none, GUIStyle.none))
                {
                    _doc.SelectedLimb = limb.Index; if (_compact) _detail = true; _detailScroll = Vector2.zero;
                    _flash = tip.Replace("\n", "  ");
                }
            }

            // 图例 + 选中部位简报
            float ly = canvas.yMax + 4f;
            DdenUI.Label(new Rect(area.x, ly, area.width, 16f),
                "<color=#8cd97c>■</color> 健康  <color=#f0a>■</color> 受损  <color=#f66>■</color> 危重", DdenUI.Sub);

            SaveDocument.LimbView sel = _doc.Limbs.FirstOrDefault(l => l.Index == _doc.SelectedLimb);
            if (sel != null && ly + 34f < area.yMax)
            {
                float skin = sel.Skin != null ? (float)sel.Skin.Number / 100f : 1f;
                float muscle = sel.Muscle != null ? (float)sel.Muscle.Number / 100f : 1f;
                GUI.Label(new Rect(area.x, ly + 16f, 150f, 16f), sel.Label + $"  #{sel.Index}", DdenUI.Row);
                DdenUI.Bar(new Rect(area.x + 152f, ly + 17f, area.width - 200f, 7f), skin, DdenUI.HealthColor(skin));
                DdenUI.Bar(new Rect(area.x + 152f, ly + 26f, area.width - 200f, 7f), muscle, DdenUI.HealthColor(muscle));
            }

        }

        private void EnsureLimbLayout()
        {
            bool inWorld = Game.InWorld;
            if (inWorld != _wasInWorld)
            {
                _wasInWorld = inWorld;
                LimbLayout.Invalidate();
            }
            LimbLayout.Ensure();
        }

        private bool _limbDiagram = true;
        private bool _wasInWorld;

        /// <summary>列表模式：保留原来的 15 格血条视图（图示看位置，列表看数值）。</summary>
        private void DrawLimbGrid(Rect r)
        {
            Rect grid = new Rect(r.x + 6f, r.y + 22f, r.width - 12f, Mathf.Min(240f, r.height * 0.55f));
            const float cw = 92f;
            const float ch = 52f;
            int cols = Mathf.Max(1, Mathf.FloorToInt((grid.width - 20f) / cw));
            int rows = Mathf.CeilToInt(_doc.Limbs.Count / (float)cols);

            _limbScroll = DdenUI.BeginScrollView(grid, _limbScroll,
                new Rect(0, 0, grid.width - 18f, Mathf.Max(rows * ch + 4f, grid.height)));
            try
            {

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

                if (GUI.Button(cell, GUIContent.none, GUIStyle.none)) { _doc.SelectedLimb = limb.Index; if (_compact) _detail = true; _detailScroll = Vector2.zero; }
            }

            }
            finally { DdenUI.EndScrollView(); }

            Rect hint = new Rect(r.x + 6f, grid.yMax + 4f, r.width - 12f, Mathf.Max(30f, r.yMax - grid.yMax - 8f));
            DdenUI.Label(hint, "右侧可精修选中部位的数值与状态（骨折/脱臼/感染/截断…）。\n未进入世界时部位用序号显示，进世界后会换成游戏内部位名。", DdenUI.Wrap);
        }

    }
}
