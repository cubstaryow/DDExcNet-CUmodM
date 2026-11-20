using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DDEN.Shared;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DDEN.SaveEditor
{
    internal sealed class LimbPart
    {
        public int Index;
        public string Name;
        /// <summary>归一化矩形：左上原点，0..1（用于按面板大小等比铺开）。</summary>
        public Rect Norm;
        public Sprite Sprite;
    }

    /// <summary>
    /// 肢体“人体图”布局来源，优先级：
    /// ① 世界内直接读 WoundView（原版肢体界面）的图标与位置 → 完全复刻原版；
    /// ② 读上次在世界内缓存下来的 dden.limbs.json → 主菜单也能显示同款构图（无美术资源，用色块）；
    /// ③ 都没有 → 内置的 15 部位人工布局兜底。
    /// </summary>
    internal static class LimbLayout
    {
        public static readonly List<LimbPart> Parts = new List<LimbPart>();
        public static float Aspect = 0.42f;      // 宽/高
        public static bool FromLive;
        public static string Source = "内置布局";
        /// <summary>内置布局没有头（游戏里 head 不属于 limbs），画个装饰头让轮廓更像人。</summary>
        public static bool DrawHead;

        private static readonly string CachePath = Path.Combine(BepInEx.Paths.ConfigPath, "dden.limbs.json");
        private static bool _initialized;

        public static void Ensure()
        {
            if (_initialized && Parts.Count > 0) return;
            _initialized = true;

            if (TryCaptureLive()) return;
            if (TryLoadCache()) return;
            BuildSynthetic();
        }

        /// <summary>是否值得尝试重新抓取（进/退出世界后布局会变）。</summary>
        public static void Invalidate() => _initialized = false;

        // ===== ① 直接读原版 WoundView =====

        private static bool TryCaptureLive()
        {
            try
            {
                Type woundType = Reflect.Type("WoundView");
                if (woundType == null) return false;
                object view = Reflect.GetStatic(woundType, "view");
                if (view == null) return false;

                Image[] images = Reflect.GetInstance(view, "limbImages") as Image[];
                Vector2[] positions = Reflect.GetInstance(view, "origPositions") as Vector2[];
                if (images == null || positions == null || images.Length == 0) return false;

                var raw = new List<(int index, string name, Rect rect, Sprite sprite)>();
                Body body = Game.LocalBody;

                for (int i = 0; i < images.Length; i++)
                {
                    Image img = images[i];
                    if (img == null) continue;

                    Vector2 size = img.rectTransform.sizeDelta;
                    Vector2 native = img.sprite != null ? img.sprite.rect.size : Vector2.zero;
                    // 取较大者：sizeDelta 有时被父级压得很小，直接画会显得“头特别小”
                    size = new Vector2(Mathf.Max(size.x, native.x), Mathf.Max(size.y, native.y));
                    if (size.x <= 1f || size.y <= 1f) size = new Vector2(40f, 40f);
                    Vector2 center = i < positions.Length ? positions[i] : img.rectTransform.anchoredPosition;

                    var rect = new Rect(center.x - size.x * 0.5f, center.y - size.y * 0.5f, size.x, size.y);
                    string name = ResolveName(body, i);
                    raw.Add((i, name, rect, img.sprite));
                }

                if (raw.Count == 0) return false;

                // 统一包围盒 → 归一化（uGUI 的 y 向上，转成屏幕的 y 向下）
                float minX = raw.Min(r => r.rect.xMin);
                float maxX = raw.Max(r => r.rect.xMax);
                float minY = raw.Min(r => r.rect.yMin);
                float maxY = raw.Max(r => r.rect.yMax);
                float w = Mathf.Max(1f, maxX - minX);
                float h = Mathf.Max(1f, maxY - minY);

                Parts.Clear();
                foreach (var r in raw)
                {
                    var norm = new Rect(
                        (r.rect.xMin - minX) / w,
                        1f - (r.rect.yMax - minY) / h,
                        r.rect.width / w,
                        r.rect.height / h);
                    Parts.Add(new LimbPart { Index = r.index, Name = r.name, Norm = norm, Sprite = r.sprite });
                }

                Aspect = w / h;
                FromLive = true;
                DrawHead = false;
                Source = "原版 WoundView";
                SaveCache();
                Log.Info($"[肢体图] 已从原版界面抓取 {Parts.Count} 个部位（比例 {Aspect:0.00}）");
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("TryCaptureLive 失败：" + ex.Message);
                return false;
            }
        }

        private static string ResolveName(Body body, int index)
        {
            try
            {
                if (body != null && body.limbs != null && index < body.limbs.Length && body.limbs[index] != null)
                {
                    Limb limb = body.limbs[index];
                    if (!string.IsNullOrEmpty(limb.shortName)) return limb.shortName;
                    if (!string.IsNullOrEmpty(limb.fullName)) return limb.fullName;
                }
            }
            catch { /* ignored */ }
            return "#" + index;
        }

        // ===== ② 缓存 =====

        private static void SaveCache()
        {
            try
            {
                var arr = new JArray();
                foreach (LimbPart p in Parts)
                {
                    arr.Add(new JObject
                    {
                        ["i"] = p.Index,
                        ["name"] = p.Name,
                        ["x"] = p.Norm.x,
                        ["y"] = p.Norm.y,
                        ["w"] = p.Norm.width,
                        ["h"] = p.Norm.height
                    });
                }
                var root = new JObject { ["version"] = 1, ["aspect"] = Aspect, ["parts"] = arr };
                File.WriteAllText(CachePath, root.ToString(Newtonsoft.Json.Formatting.Indented));
            }
            catch (Exception ex)
            {
                Log.Debug("保存肢体布局缓存失败：" + ex.Message);
            }
        }

        private static bool TryLoadCache()
        {
            try
            {
                if (!File.Exists(CachePath)) return false;
                JObject root = JObject.Parse(File.ReadAllText(CachePath));
                if (!(root["parts"] is JArray arr) || arr.Count == 0) return false;

                Parts.Clear();
                foreach (JToken t in arr)
                {
                    Parts.Add(new LimbPart
                    {
                        Index = t.Value<int?>("i") ?? Parts.Count,
                        Name = t.Value<string>("name") ?? "#",
                        Norm = new Rect(
                            t.Value<float?>("x") ?? 0f,
                            t.Value<float?>("y") ?? 0f,
                            t.Value<float?>("w") ?? 0.1f,
                            t.Value<float?>("h") ?? 0.1f)
                    });
                }
                Aspect = root.Value<float?>("aspect") ?? 0.42f;
                FromLive = false;
                Source = "缓存布局";
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("读取肢体布局缓存失败：" + ex.Message);
                return false;
            }
        }

        // ===== ③ 内置兜底：按常见人形排 15 块 =====

        private static void BuildSynthetic()
        {
            // 15 个部位：头、胸、腹、左右上臂、左右前臂、左右手、左右大腿、左右小腿、左右脚
            // 注意：游戏没有“脖子”，limbs[0] 就是头（原版 WoundView.limbImages[0] 也是头）
            (int i, string name, float x, float y, float w, float h)[] boxes =
            {
                (0, "#0 头",      0.37f, 0.00f, 0.26f, 0.10f),
                (1, "#1 胸",      0.34f, 0.10f, 0.32f, 0.18f),
                (2, "#2 腹/腰",   0.36f, 0.28f, 0.28f, 0.12f),
                (3, "#3 左上臂",  0.16f, 0.11f, 0.15f, 0.18f),
                (4, "#4 右上臂",  0.69f, 0.11f, 0.15f, 0.18f),
                (5, "#5 左前臂",  0.12f, 0.29f, 0.14f, 0.16f),
                (6, "#6 右前臂",  0.74f, 0.29f, 0.14f, 0.16f),
                (7, "#7 左手",    0.12f, 0.45f, 0.10f, 0.08f),
                (8, "#8 右手",    0.78f, 0.45f, 0.10f, 0.08f),
                (9, "#9 左大腿",  0.34f, 0.41f, 0.14f, 0.22f),
                (10, "#10 右大腿",0.52f, 0.41f, 0.14f, 0.22f),
                (11, "#11 左小腿",0.34f, 0.63f, 0.13f, 0.21f),
                (12, "#12 右小腿",0.53f, 0.63f, 0.13f, 0.21f),
                (13, "#13 左脚",  0.33f, 0.84f, 0.14f, 0.08f),
                (14, "#14 右脚",  0.53f, 0.84f, 0.14f, 0.08f)
            };

            Parts.Clear();
            foreach (var b in boxes)
            {
                Parts.Add(new LimbPart
                {
                    Index = b.i,
                    Name = b.name,
                    Norm = new Rect(b.x, b.y, b.w, b.h)
                });
            }
            Aspect = 0.42f;
            FromLive = false;
            DrawHead = false;
            Source = "内置布局";
        }
    }
}
