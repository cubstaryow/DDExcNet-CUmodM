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
                clipping = TextClipping.Clip
            };
            s.normal.textColor = color ?? Text;
            return s;
        }

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

        /// <summary>按 sprite 的 textureRect 画图（直接用 texture 会画出整张图集）。</summary>
        public static bool DrawSprite(Rect r, Sprite sprite, Color? tint = null)
        {
            if (sprite == null || sprite.texture == null) return false;
            Texture2D tex = sprite.texture;
            Rect tr = sprite.textureRect;
            var uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);
            Color old = GUI.color;
            GUI.color = tint ?? Color.white;
            GUI.DrawTextureWithTexCoords(r, tex, uv, true);
            GUI.color = old;
            return true;
        }

        /// <summary>带边框的图标格；返回是否被点击。</summary>
        public static bool IconCell(Rect r, string id, Color border, bool selected, out Sprite sprite)
        {
            Fill(r, selected ? PanelAlt : Panel);
            Frame(r, border, selected ? 2f : 1f);
            sprite = ItemIcon(id);
            Rect inner = new Rect(r.x + 4f, r.y + 4f, r.width - 8f, r.height - 8f);
            if (!DrawSprite(inner, sprite))
            {
                GUI.Label(inner, "?", TabStyle);
            }
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        // ===== 常用控件 =====

        public static bool Button(Rect r, string text, bool highlight = false)
        {
            Color old = GUI.backgroundColor;
            GUI.backgroundColor = highlight ? Accent : Color.white;
            bool clicked = GUI.Button(r, text, GUI.skin.button);
            GUI.backgroundColor = old;
            return clicked;
        }

        public static bool Toggle(Rect r, bool value, string label)
        {
            bool nv = GUI.Toggle(r, value, " " + label, GUI.skin.toggle);
            return nv;
        }

        /// <summary>数值输入（保留 token 原类型由调用方处理）；返回是否有改动。</summary>
        public static bool NumberField(Rect r, ref string buffer, out double value)
        {
            string nv = GUI.TextField(r, buffer ?? "0");
            value = 0;
            if (nv == buffer) return false;
            buffer = nv;
            return double.TryParse(nv, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value);
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

        /// <summary>把 BepInEx 的 ConfigEntry 画成一行；返回该行占用的高度。</summary>
        public static float ConfigRow(Rect r, ConfigEntryBase entry)
        {
            if (entry == null) return 0f;

            GUI.Label(new Rect(r.x, r.y, r.width * 0.52f, r.height), entry.Definition.Key, Row);

            object value = entry.BoxedValue;
            Rect field = new Rect(r.x + r.width * 0.52f, r.y, r.width * 0.48f - 4f, r.height);

            try
            {
                if (value is bool b)
                {
                    bool nv = GUI.Toggle(field, b, b ? " 开" : " 关", GUI.skin.toggle);
                    if (nv != b) entry.BoxedValue = nv;
                }
                else if (value is int i && entry.SettingType == typeof(int))
                {
                    string txt = GUI.TextField(field, i.ToString());
                    if (int.TryParse(txt, out int ni) && ni != i) entry.BoxedValue = ni;
                }
                else if (value is float f)
                {
                    string txt = GUI.TextField(field, f.ToString("0.###"));
                    if (float.TryParse(txt, out float nf) && Math.Abs(nf - f) > 0.0001f) entry.BoxedValue = nf;
                }
                else if (value is KeyboardShortcut ks)
                {
                    if (GUI.Button(field, ks.MainKey.ToString()))
                    {
                        KeyCapture.Begin(entry);
                    }
                }
                else
                {
                    string txt = GUI.TextField(field, Convert.ToString(value));
                    if (txt != Convert.ToString(value)) entry.BoxedValue = txt;
                }
            }
            catch (Exception ex)
            {
                GUI.Label(field, "<color=#f66>无法编辑：</color>" + ex.Message, Sub);
            }

            if (!string.IsNullOrEmpty(entry.Description?.Description))
            {
                GUI.Label(new Rect(r.x, r.y + r.height - 2f, r.width, 14f),
                    Truncate(entry.Description.Description, 110), Small);
            }
            return r.height;
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
            float y = area.y;
            if (entries != null)
            {
                foreach (ConfigEntryBase entry in entries)
                {
                    if (y + 24f > area.yMax) break;
                    ConfigRow(new Rect(area.x, y, area.width, 22f), entry);
                    y += 26f;
                }
            }
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
