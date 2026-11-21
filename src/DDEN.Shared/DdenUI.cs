using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace DDEN.Shared
{
    /// <summary>DDEN 统一外观：皮肤、图标、常用控件、配置项通用渲染器。</summary>
    public static class DdenUI
    {
        // ===== 配色 =====
        public static readonly Color Bg = new Color(0.07f, 0.08f, 0.10f, 0.96f);
        public static readonly Color Panel = new Color(0.12f, 0.13f, 0.16f, 0.96f);
        public static readonly Color PanelAlt = new Color(0.16f, 0.17f, 0.21f, 0.96f);
        public static readonly Color Accent = new Color(0.36f, 0.75f, 0.98f, 1f);
        public static readonly Color AccentDim = new Color(0.36f, 0.75f, 0.98f, 0.28f);
        public static readonly Color Warn = new Color(1f, 0.62f, 0.30f, 1f);
        public static readonly Color Good = new Color(0.55f, 0.86f, 0.50f, 1f);
        public static readonly Color Bad = new Color(0.94f, 0.42f, 0.42f, 1f);
        public static readonly Color Text = new Color(0.88f, 0.90f, 0.94f, 1f);
        public static readonly Color TextDim = new Color(0.62f, 0.66f, 0.72f, 1f);

        private static GUIStyle _title, _sub, _row, _small, _wrap, _icon, _tab;

        public static GUIStyle Title => _title ??= Make(15, FontStyle.Bold, TextAnchor.MiddleLeft);
        public static GUIStyle Sub => _sub ??= Make(11, FontStyle.Normal, TextAnchor.MiddleLeft, TextDim);
        private static GUIStyle _tiny;
        /// <summary>小药丸里的文字（10px 居中）。</summary>
        public static GUIStyle Tiny => _tiny ??= Make(10, FontStyle.Normal, TextAnchor.MiddleCenter);

        private static GUIStyle _disabledRow;
        /// <summary>不可用状态的行文字（模块总开关关闭时用）。</summary>
        public static GUIStyle DisabledRow => _disabledRow ??= Make(12, FontStyle.Normal, TextAnchor.MiddleLeft,
            new Color(0.45f, 0.47f, 0.52f, 1f));
        public static GUIStyle Row => _row ??= Make(12, FontStyle.Normal, TextAnchor.MiddleLeft);
        public static GUIStyle Small => _small ??= Make(10, FontStyle.Normal, TextAnchor.MiddleRight, TextDim);
        public static GUIStyle Wrap => _wrap ??= Make(11, FontStyle.Normal, TextAnchor.UpperLeft, TextDim, true);
        public static GUIStyle IconStyle => _icon ??= Make(10, FontStyle.Normal, TextAnchor.LowerRight, TextDim);
        public static GUIStyle TabStyle => _tab ??= Make(12, FontStyle.Bold, TextAnchor.MiddleCenter);

        private static GUIStyle Make(int size, FontStyle style, TextAnchor anchor, Color? color = null, bool wrap = false)
        {
            var s = new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                fontStyle = style,
                alignment = anchor,
                wordWrap = wrap,
                richText = true,
                // 中文有上下伸展，用 Clip 会把字切掉一截；Overflow 不裁剪，允许略微溢出到行距里
                clipping = TextClipping.Overflow
            };
            s.padding = new RectOffset(0, 0, 1, 1);
            s.normal.textColor = color ?? Text;
            return s;
        }

        // ===== 基础绘制 =====

        public static void Fill(Rect r, Color c)
        {
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        public static void Frame(Rect r, Color c, float thickness = 1f)
        {
            Fill(new Rect(r.x, r.y, r.width, thickness), c);
            Fill(new Rect(r.x, r.yMax - thickness, r.width, thickness), c);
            Fill(new Rect(r.x, r.y, thickness, r.height), c);
            Fill(new Rect(r.xMax - thickness, r.y, thickness, r.height), c);
        }

        public static void Label(Rect r, string text, GUIStyle style = null)
        {
            GUI.Label(r, text ?? string.Empty, style ?? Row);
        }

        // ===== 按宽度截断（Overflow 不裁字，必须自己算宽度，否则长名字会压到旁边的控件上）=====

        private static readonly Dictionary<string, string> ElideCache = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> ElideCacheRow = new Dictionary<string, string>(StringComparer.Ordinal);

        public static string ElideToWidth(string text, float width, GUIStyle style = null)
        {
            if (string.IsNullOrEmpty(text) || width <= 6f) return string.Empty;
            style = style ?? Row;
            string key = ((int)width) + "|" + text;
            Dictionary<string, string> cache = style == Row ? ElideCacheRow : ElideCache;
            if (cache.TryGetValue(key, out string cached)) return cached;

            string result;
            if (style.CalcSize(new GUIContent(text)).x <= width)
            {
                result = text;
            }
            else
            {
                int lo = 0, hi = text.Length;
                while (lo < hi)
                {
                    int mid = (lo + hi + 1) / 2;
                    string probe = text.Substring(0, mid) + "…";
                    if (style.CalcSize(new GUIContent(probe)).x <= width) lo = mid;
                    else hi = mid - 1;
                }
                result = lo <= 0 ? "…" : text.Substring(0, lo) + "…";
            }

            if (cache.Count > 4096) cache.Clear();
            cache[key] = result;
            return result;
        }

        public static void Section(Rect r, string text)
        {
            Fill(new Rect(r.x, r.y + r.height * 0.5f - 1f, r.width, 1f), AccentDim);
            GUI.Label(new Rect(r.x, r.y, r.width, r.height), " " + (text ?? string.Empty), Sub);
        }

        // ===== 精灵（物品图标）=====

        private static readonly Dictionary<string, Sprite> IconCache = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private static readonly HashSet<string> IconMissing = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>取物品图标：先 CUCoreLib 注册表（含模组注册的图标），再退回 Resources。</summary>
        public static Sprite ItemIcon(string id)
        {
            if (string.IsNullOrEmpty(id) || IconMissing.Contains(id)) return null;
            if (IconCache.TryGetValue(id, out Sprite cached)) return cached;

            Sprite sprite = null;
            try
            {
                if (!CUCoreLib.Registries.ItemRegistry.TryGetIcon(id, out sprite)) sprite = null;
            }
            catch (Exception ex)
            {
                Log.Debug("ItemIcon(registry) 失败：" + ex.Message);
            }

            if (sprite == null)
            {
                try { sprite = Resources.Load<Sprite>("Sprites/" + id); }
                catch { /* ignored */ }
            }

            if (sprite != null) IconCache[id] = sprite;
            else IconMissing.Add(id);
            return sprite;
        }

        /// <summary>按 sprite 的 textureRect 画图；mirrorX 用 UV 翻转实现左右镜像（原版 WoundView 就是靠 scale.x=-1 镜像）。</summary>
        public static bool DrawSprite(Rect r, Sprite sprite, Color? tint = null, bool mirrorX = false)
        {
            if (sprite == null || sprite.texture == null) return false;
            Texture2D tex = sprite.texture;
            Rect tr = sprite.textureRect;
            var uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);
            return DrawTextureUv(r, tex, uv, tint, mirrorX);
        }

        public static bool DrawTextureUv(Rect r, Texture tex, Rect uv, Color? tint = null, bool mirrorX = false)
        {
            if (tex == null) return false;
            if (mirrorX) uv = new Rect(1f - uv.x - uv.width, uv.y, uv.width, uv.height);
            Color old = GUI.color;
            GUI.color = tint ?? Color.white;
            GUI.DrawTextureWithTexCoords(r, tex, uv, true);
            GUI.color = old;
            return true;
        }

        /// <summary>带边框的图标格；返回是否被点击。tooltip 用 Hover 登记，由 DrawTooltip 统一画在窗口内。</summary>
        public static bool IconCell(Rect r, string id, Color border, bool selected, out Sprite sprite, string tooltip = null)
        {
            Fill(r, selected ? PanelAlt : Panel);
            Frame(r, border, selected ? 2f : 1f);
            sprite = ItemIcon(id);
            Rect inner = new Rect(r.x + 4f, r.y + 4f, r.width - 8f, r.height - 8f);
            if (!DrawSprite(inner, sprite))
            {
                GUI.Label(inner, "?", TabStyle);
            }
            Hover(r, tooltip);
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        // ===== 悬停提示：自己管，不用 GUI.tooltip（那个会残留，表现为“鼠标后面跟着黑框”）=====

        private static string _tip;

        /// <summary>每帧开始清一次，避免上一帧的提示粘在鼠标上。</summary>
        public static void BeginFrame() => _tip = null;

        public static void Hover(Rect r, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try
            {
                if (r.Contains(Event.current.mousePosition)) _tip = text;
            }
            catch { /* ignored */ }
        }

        /// <summary>在窗口内画提示（自动贴边，不会跑到窗口外的游戏界面上）。支持多行（\n 分隔）。</summary>
        public static void DrawTooltip(Rect bounds)
        {
            if (string.IsNullOrEmpty(_tip) || Event.current == null) return;

            Vector2 mp = Event.current.mousePosition;
            string[] lines = _tip.Split('\n');
            const float lineH = 15f;

            float maxW = Mathf.Max(160f, Mathf.Min(420f, bounds.width - 16f));
            float w = 0f;
            foreach (string line in lines) w = Mathf.Max(w, Row.CalcSize(new GUIContent(line)).x);
            w = Mathf.Min(w + 14f, maxW);
            float h = lines.Length * lineH + 8f;

            float x = Mathf.Clamp(mp.x + 16f, bounds.x, Mathf.Max(bounds.x, bounds.xMax - w - 2f));
            float y = Mathf.Clamp(mp.y + 18f, bounds.y, Mathf.Max(bounds.y, bounds.yMax - h - 2f));
            var rect = new Rect(x, y, w, h);

            Fill(rect, new Color(0f, 0f, 0f, 0.92f));
            Frame(rect, AccentDim);
            for (int i = 0; i < lines.Length; i++)
            {
                // “标签：值”里的标签用强调色画，值用正常色，扫一眼就能分清五项
                string line = lines[i];
                int colon = line.IndexOf('：');
                if (colon > 0 && colon <= 8)
                {
                    GUI.Label(new Rect(rect.x + 7f, rect.y + 4f + i * lineH, 70f, lineH),
                        line.Substring(0, colon), Sub);
                    GUI.Label(new Rect(rect.x + 74f, rect.y + 4f + i * lineH, rect.width - 84f, lineH),
                        ElideToWidth(line.Substring(colon + 1), rect.width - 84f, Wrap), Wrap);
                }
                else
                {
                    GUI.Label(new Rect(rect.x + 7f, rect.y + 4f + i * lineH, rect.width - 14f, lineH),
                        ElideToWidth(line, rect.width - 14f, Wrap), Wrap);
                }
            }
        }

        // ===== 常用控件 =====
        //
        // 全部自绘：Unity 默认皮肤（GUI.skin.button / toggle / textField）是浅灰描边的圆角贴图，
        // 贴在 DDEN 的深色像素风面板上非常跳，而且缩放时会糊。这里统一成「填色块 + 1px 描边 + 直角」。

        private static GUIStyle _center, _field;

        /// <summary>居中文案（按钮内部用）。</summary>
        public static GUIStyle Center => _center ??= Make(12, FontStyle.Normal, TextAnchor.MiddleCenter);

        /// <summary>无背景的输入框样式：底与边框由我们自己画，这里只留文字与光标。</summary>
        public static GUIStyle FieldStyle => _field ??= MakeField();

        private static GUIStyle MakeField()
        {
            var s = new GUIStyle(GUI.skin.textField)
            {
                border = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(4, 4, 2, 2),
                margin = new RectOffset(0, 0, 0, 0),
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                richText = false,
                clipping = TextClipping.Clip
            };
            // 各状态的底图要逐个清掉（GUIStyle 没有统一的 background 属性），否则仍然会画出默认白框
            s.normal.background = null;
            s.hover.background = null;
            s.active.background = null;
            s.focused.background = null;
            s.normal.textColor = Text;
            s.hover.textColor = Text;
            s.active.textColor = Text;
            s.focused.textColor = Accent;
            return s;
        }

        private static readonly Color FieldBg = new Color(0f, 0f, 0f, 0.38f);
        private static readonly Color Hairline = new Color(1f, 1f, 1f, 0.14f);

        /// <summary>DDEN 风格按钮：直角块 + 描边，highlight=true 用强调色填充。</summary>
        public static bool Button(Rect r, string text, bool highlight = false)
        {
            Fill(r, highlight ? new Color(Accent.r, Accent.g, Accent.b, 0.32f) : new Color(1f, 1f, 1f, 0.07f));
            Frame(r, highlight ? Accent : Hairline);

            bool hovered = false;
            try { hovered = Event.current != null && r.Contains(Event.current.mousePosition); }
            catch { /* ignored */ }
            if (hovered) Fill(r, new Color(1f, 1f, 1f, 0.06f));

            GUI.Label(new Rect(r.x + 4f, r.y, r.width - 8f, r.height),
                ElideToWidth(text, r.width - 8f, Center), Center);
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        /// <summary>只画方框的复选框（像素风对勾用三段短横拼，不依赖字体里有没有 ✓）。返回点击后的值。</summary>
        public static bool CheckBox(Rect r, bool value) => DrawCheckBox(r, value, true);

        /// <summary>画复选框；`interactive=false` 时只画不响应点击（模块总开关关闭 → 整块变灰不可用）。</summary>
        public static bool DrawCheckBox(Rect r, bool value, bool interactive)
        {
            float size = Mathf.Min(18f, Mathf.Max(12f, r.height));
            var box = new Rect(r.x, r.y + (r.height - size) * 0.5f, size, size);

            Color on = interactive ? Accent : new Color(0.42f, 0.55f, 0.66f, 1f);
            Fill(box, value ? on : FieldBg);
            Frame(box, value ? on : new Color(1f, 1f, 1f, interactive ? 0.22f : 0.12f));

            if (value)
            {
                Color ink = new Color(0.05f, 0.08f, 0.10f, 1f);
                float u = Mathf.Max(1f, Mathf.Floor(size / 7f));
                float baseY = box.y + size * 0.58f;
                Fill(new Rect(box.x + size * 0.18f, baseY, u * 1.6f, u), ink);
                Fill(new Rect(box.x + size * 0.38f, baseY - u * 1.4f, u * 1.6f, u), ink);
                Fill(new Rect(box.x + size * 0.58f, baseY - u * 2.8f, u * 2.0f, u), ink);
            }

            if (!interactive) return value;
            return GUI.Button(r, GUIContent.none, GUIStyle.none) ? !value : value;
        }

        /// <summary>状态小药丸：运行中/监控中/关/错误 用不同底色，一格一格摆在“运行一览”里。</summary>
        public static void Chip(Rect r, string text, Color color)
        {
            Fill(r, new Color(color.r, color.g, color.b, 0.20f));
            Frame(r, color);
            Color old = GUI.color;
            GUI.color = color;
            GUI.Label(new Rect(r.x + 2f, r.y, r.width - 4f, r.height), text, Tiny);
            GUI.color = old;
        }

        /// <summary>复选框 + 右侧文字（整行可点）。</summary>
        public static bool CheckBoxLabeled(Rect r, bool value, string label)
        {
            bool nv = CheckBox(new Rect(r.x, r.y, 18f, r.height), value);
            if (!string.IsNullOrEmpty(label))
            {
                GUI.Label(new Rect(r.x + 24f, r.y, Mathf.Max(0f, r.width - 24f), r.height), label, Row);
            }
            return nv;
        }

        /// <summary>DDEN 风格单行输入框（底/边框自绘，只借用 TextField 的输入与光标）。</summary>
        public static string TextField(Rect r, string text)
        {
            Fill(r, FieldBg);
            Frame(r, Hairline);
            Fill(new Rect(r.x, r.y + 2f, 2f, Mathf.Max(0f, r.height - 4f)), AccentDim);
            var inner = new Rect(r.x + 5f, r.y + 1f, Mathf.Max(0f, r.width - 8f), Mathf.Max(0f, r.height - 2f));
            return GUI.TextField(inner, text ?? string.Empty, FieldStyle);
        }

        public static bool Toggle(Rect r, bool value, string label)
        {
            return CheckBoxLabeled(r, value, label);
        }

        /// <summary>数值输入（保留 token 原类型由调用方处理）；返回是否有改动。</summary>
        public static bool NumberField(Rect r, ref string buffer, out double value)
        {
            string nv = TextField(r, buffer ?? "0");
            value = 0;
            if (nv == buffer) return false;
            buffer = nv;
            return double.TryParse(nv, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        /// <summary>画一张普通 Texture（像素图会按 Texture.filterMode 放大：Point = 硬像素边）。</summary>
        public static bool DrawTex(Rect r, Texture tex, Color? tint = null)
        {
            if (tex == null) return false;
            Color old = GUI.color;
            GUI.color = tint ?? Color.white;
            GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, true);
            GUI.color = old;
            return true;
        }

        /// <summary>0..1 的条（耐久 / 血量）。</summary>
        public static void Bar(Rect r, float t, Color c, string text = null)
        {
            Fill(r, new Color(0f, 0f, 0f, 0.55f));
            float w = Mathf.Clamp01(t) * r.width;
            Fill(new Rect(r.x, r.y, w, r.height), c);
            if (!string.IsNullOrEmpty(text)) GUI.Label(r, " " + text, IconStyle);
        }

        public static Color HealthColor(float t)
        {
            if (t < 0.35f) return Bad;
            if (t < 0.7f) return Warn;
            return Good;
        }

        // ===== 通用配置项渲染 =====

        /// <summary>配置行的固定高度（上行：键 + 控件；下行：说明）。</summary>
        /// <summary>每条配置的行高：**固定单行说明**（用户口径 2026-11：描述单行）。</summary>
        public const float ConfigRowHeight = 46f;

        /// <summary>兼容旧调用：说明固定一行，所以行高是常量。</summary>
        public static float RowHeight(ConfigEntryBase entry, float width) => ConfigRowHeight;

        /// <summary>
        /// 一条配置的版式（2026-11 用户口径）：
        ///   · 最左一道 **状态色条**（开 = 亮蓝，关 = 暗灰，实验性 = 橙）+ 交替底色 → 一眼看出这行是哪个功能、什么状态；
        ///   · 上排「键 + 右侧固定控件」；
        ///   · 下排**单行**说明（只写这个开关做什么），放不下就截断，**不压控件、不压下一行**；
        ///   · 鼠标移到整行 → 提示框展开完整五项（功能 / 修复的问题 / 解决方案 / 实验性 / 性能影响）。
        /// </summary>
        public static float ConfigRow(Rect r, ConfigEntryBase entry, bool interactive = true)
        {
            if (entry == null) return 0f;

            const float ctrlW = 120f;
            const float ctrlH = 18f;
            const float barW = 3f;

            object value = entry.BoxedValue;
            bool isOn = value is bool b0 && b0;
            bool experimental = DdenDocs.TryGet(entry, out DdenDocs.Doc doc)
                                && !string.IsNullOrEmpty(doc.Experimental)
                                && doc.Experimental.StartsWith("是", StringComparison.Ordinal);

            Fill(r, new Color(1f, 1f, 1f, interactive ? (isOn ? 0.035f : 0.015f) : 0.008f));
            Fill(new Rect(r.x, r.y, barW, r.height),
                !interactive ? new Color(1f, 1f, 1f, 0.10f)
                             : (experimental ? Warn : (isOn ? Accent : new Color(1f, 1f, 1f, 0.14f))));

            float keyW = Mathf.Max(120f, r.width - ctrlW - 24f);
            string key = entry.Definition?.Key ?? "?";
            Rect keyRect = new Rect(r.x + barW + 7f, r.y + 3f, keyW, 18f);
            GUI.Label(keyRect, ElideToWidth(key, keyW, interactive ? (isOn ? Row : Sub) : DisabledRow),
                interactive ? (isOn ? Row : Sub) : DisabledRow);

            Rect field = new Rect(r.x + r.width - ctrlW - 8f, r.y + 3f, ctrlW, ctrlH);

            if (!interactive)
            {
                // 模块总开关关闭 → 这一行整体不可用：只画状态，不接受点击
                if (value is bool bo)
                {
                    DrawCheckBox(new Rect(field.x, field.y, 18f, ctrlH), bo, false);
                    GUI.Label(new Rect(field.x + 24f, field.y, field.width - 24f, ctrlH), bo ? "开" : "关", DisabledRow);
                }
                else
                {
                    GUI.Label(field, Convert.ToString(value), DisabledRow);
                }
            }
            else
            try
            {
                if (value is bool b)
                {
                    bool nv = CheckBoxLabeled(field, b, b ? "开" : "关");
                    if (nv != b) entry.BoxedValue = nv;
                }
                else if (value is int i && entry.SettingType == typeof(int))
                {
                    string txt = TextField(field, i.ToString());
                    if (int.TryParse(txt, out int ni) && ni != i) entry.BoxedValue = ni;
                }
                else if (value is float f)
                {
                    string txt = TextField(field, f.ToString("0.###"));
                    if (float.TryParse(txt, out float nf) && Math.Abs(nf - f) > 0.0001f) entry.BoxedValue = nf;
                }
                else if (value is KeyboardShortcut ks)
                {
                    if (Button(field, KeyCapture.Active ? "按下新键…" : ks.MainKey.ToString()))
                    {
                        KeyCapture.Begin(entry);
                    }
                }
                else
                {
                    string txt = TextField(field, Convert.ToString(value));
                    if (txt != Convert.ToString(value)) entry.BoxedValue = txt;
                }
            }
            catch (Exception ex)
            {
                GUI.Label(field, "无法编辑：" + ex.Message, Sub);
            }

            string desc = entry.Description?.Description;
            if (!string.IsNullOrEmpty(desc))
            {
                desc = desc.Replace("\r", " ").Replace("\n", " ");
                float descW = r.width - barW - 16f;
                GUI.Label(new Rect(r.x + barW + 7f, r.y + 23f, descW, 17f),
                    ElideToWidth(desc, descW, interactive ? Sub : DisabledRow), interactive ? Sub : DisabledRow);
            }

            // 整行悬停：提示框展开完整五项
            Hover(r, DdenDocs.Format(entry));

            Fill(new Rect(r.x, r.yMax - 1f, r.width, 1f), new Color(1f, 1f, 1f, 0.06f));
            return ConfigRowHeight;
        }

        /// <summary>按 Section 分组渲染一批配置（从 area 顶部开始，返回占用高度）。</summary>
        public static float DrawConfigGrouped(Rect area, IEnumerable<ConfigEntryBase> entries, bool interactive = true, string alwaysInteractiveKey = null)
        {
            if (entries == null) return 0f;

            float y = 0f;
            string section = null;

            foreach (ConfigEntryBase entry in entries)
            {
                string sec = entry.Definition?.Section ?? "General";
                if (sec != section)
                {
                    section = sec;
                    y += 6f;
                    Section(new Rect(area.x, area.y + y, area.width, 16f), "▸ " + sec);
                    y += 20f;
                }
                float h = RowHeight(entry, area.width);
                string fullKey = (entry.Definition?.Section ?? "") + "/" + (entry.Definition?.Key ?? "");
                bool rowInteractive = interactive
                                      || (!string.IsNullOrEmpty(alwaysInteractiveKey) && fullKey == alwaysInteractiveKey);
                ConfigRow(new Rect(area.x, area.y + y, area.width, h), entry, rowInteractive);
                y += h;
            }
            return y;
        }

        public static float ConfigListHeight(IEnumerable<ConfigEntryBase> entries)
        {
            if (entries == null) return 0f;
            var sections = new HashSet<string>(StringComparer.Ordinal);
            int n = 0;
            foreach (ConfigEntryBase e in entries)
            {
                n++;
                sections.Add(e.Definition?.Section ?? "General");
            }
            float h = 0f; foreach (ConfigEntryBase e in entries ?? Array.Empty<ConfigEntryBase>()) h += RowHeight(e, 600f);
            return h + sections.Count * 26f + 8f;
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            s = s.Replace("\n", " ");
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }

        // ===== 模块独立小窗（hub 缺席时兜底）=====

        public static void DrawStandalone(Rect window, IDdenModule module, ref Vector2 scroll, bool drawSettings)
        {
            Fill(new Rect(0, 0, window.width, window.height), Bg);
            GUI.Label(new Rect(16, 6, window.width - 32, 24), $"{module.DisplayName}  <size=10><color=#8fa>{module.Version}</color></size>", Title);
            Rect body = new Rect(12, 34, window.width - 24, window.height - 46);
            try
            {
                scroll = GUI.BeginScrollView(body, scroll, new Rect(0, 0, body.width - 20, Mathf.Max(body.height, 1200f)));
                Rect inner = new Rect(0, 0, body.width - 20, Mathf.Max(body.height, 1200f));
                if (drawSettings && !module.DrawSettings(inner))
                {
                    DrawConfigList(new Rect(inner.x, inner.y, inner.width, inner.height), module.ConfigEntries);
                }
                else if (!drawSettings)
                {
                    module.DrawPage(inner);
                }
                GUI.EndScrollView();
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "DrawStandalone");
                GUI.Label(body, "绘制失败：" + ex.Message, Wrap);
            }
        }

        public static void DrawConfigList(Rect area, IEnumerable<ConfigEntryBase> entries)
        {
            DrawConfigGrouped(area, entries);
        }
    }

    /// <summary>键盘改键捕获（配置里的 KeyboardShortcut）。</summary>
    public static class KeyCapture
    {
        private static ConfigEntryBase _target;
        private static float _until;

        public static bool Active => _target != null;

        public static void Begin(ConfigEntryBase entry)
        {
            _target = entry;
            _until = Time.realtimeSinceStartup + 5f;
        }

        public static void Tick()
        {
            if (_target == null) return;
            if (Time.realtimeSinceStartup > _until)
            {
                _target = null;
                return;
            }
            foreach (KeyCode key in Enum.GetValues(typeof(KeyCode)))
            {
                if (key == KeyCode.None) continue;
                if (Input.GetKeyDown(key))
                {
                    try { _target.BoxedValue = new KeyboardShortcut(key); }
                    catch (Exception ex) { Log.Warn("改键失败：" + ex.Message); }
                    _target = null;
                    return;
                }
            }
        }
    }
}
