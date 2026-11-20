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
        /// <summary>原版游戏里的 Sprite（只有在世界内才拿得到）。</summary>
        public Sprite Sprite;
        /// <summary>可绘制的贴图：优先用从原版 Sprite 抓下来并缓存成 PNG 的版本。</summary>
        public Texture2D Tex;
        /// <summary>Tex 的水平镜像副本（IMGUI 里负宽度不可靠，直接预先翻好像素最稳）。</summary>
        public Texture2D TexMirror;
        /// <summary>Tex 上对应的 UV（抓取时已经裁好，通常是 0,0,1,1）。</summary>
        public Rect Uv = new Rect(0f, 0f, 1f, 1f);
        /// <summary>原版 WoundView 用 localScale.x = -1 来镜像左右肢体，这里跟着镜像。</summary>
        public bool MirrorX;
    }

    /// <summary>
    /// 肢体“人体图”布局与美术来源，优先级：
    /// ① 世界内读 WoundView（原版肢体界面）的图标/位置/镜像，并把每块贴图**抓成 PNG 缓存**；
    /// ② 主菜单读同一份缓存（布局 + PNG），所以菜单里也能看原版美术；
    /// ③ 都没有 → 内置 15 部位人工布局（色块）。
    /// </summary>
    internal static class LimbLayout
    {
        public static readonly List<LimbPart> Parts = new List<LimbPart>();
        public static float Aspect = 0.42f;      // 宽/高
        public static bool FromLive;
        public static string Source = "内置布局";
        public static bool HasArt;

        /// <summary>
        /// 额外翻转开关：叠加在原版 localScale 镜像之上。
        /// 原版美术有时候是“同一个朝向”，需要按视角再翻一侧；面板上可实时切换并记住。
        /// </summary>
        public static bool FlipLeftSide;
        public static bool FlipRightSide = true;

        /// <summary>某个部位最终是否水平翻转。</summary>
        public static bool MirrorFor(LimbPart part)
        {
            if (part == null) return false;
            bool mirror = part.MirrorX;
            bool rightHalf = part.Norm.center.x > 0.5f;
            if (rightHalf ? FlipRightSide : FlipLeftSide) mirror = !mirror;
            return mirror;
        }

        public static void ToggleFlip(bool rightSide)
        {
            if (rightSide) FlipRightSide = !FlipRightSide;
            else FlipLeftSide = !FlipLeftSide;
            SaveCache();
        }

        private static readonly string CachePath = Path.Combine(BepInEx.Paths.ConfigPath, "dden.limbs.json");
        private static readonly string ArtDir = Path.Combine(BepInEx.Paths.ConfigPath, "dden.limbs");
        private static bool _initialized;

        public static void Ensure()
        {
            if (_initialized && Parts.Count > 0) return;
            _initialized = true;

            if (TryCaptureLive()) return;
            if (TryLoadCache()) return;
            BuildSynthetic();
        }

        /// <summary>进/退出世界后布局与美术都会变，允许重新抓。</summary>
        public static void Invalidate() => _initialized = false;

        // ===== ① 世界内：读原版 WoundView + 抓贴图 =====

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

                var raw = new List<(int index, string name, Rect rect, Sprite sprite, bool mirror)>();
                Body body = Game.LocalBody;

                for (int i = 0; i < images.Length; i++)
                {
                    Image img = images[i];
                    if (img == null) continue;

                    Vector2 size = img.rectTransform.sizeDelta;
                    Vector2 native = img.sprite != null ? img.sprite.rect.size : Vector2.zero;
                    size = new Vector2(Mathf.Max(size.x, native.x), Mathf.Max(size.y, native.y));
                    if (size.x <= 1f || size.y <= 1f) size = new Vector2(40f, 40f);

                    Vector2 center = i < positions.Length ? positions[i] : img.rectTransform.anchoredPosition;
                    bool mirror = img.rectTransform.localScale.x < 0f;

                    var rect = new Rect(center.x - size.x * 0.5f, center.y - size.y * 0.5f, size.x, size.y);
                    raw.Add((i, ResolveName(body, i), rect, img.sprite, mirror));
                }

                if (raw.Count == 0) return false;

                float minX = raw.Min(r => r.rect.xMin);
                float maxX = raw.Max(r => r.rect.xMax);
                float minY = raw.Min(r => r.rect.yMin);
                float maxY = raw.Max(r => r.rect.yMax);
                float w = Mathf.Max(1f, maxX - minX);
                float h = Mathf.Max(1f, maxY - minY);

                Directory.CreateDirectory(ArtDir);

                Parts.Clear();
                int saved = 0;
                foreach (var r in raw)
                {
                    var part = new LimbPart
                    {
                        Index = r.index,
                        Name = r.name,
                        Sprite = r.sprite,
                        MirrorX = r.mirror,
                        Norm = new Rect(
                            (r.rect.xMin - minX) / w,
                            1f - (r.rect.yMax - minY) / h,
                            r.rect.width / w,
                            r.rect.height / h)
                    };

                    if (TrySnapshot(part, out Texture2D tex))
                    {
                        part.Tex = tex;
                        part.Uv = new Rect(0f, 0f, 1f, 1f);
                        part.TexMirror = FlipHorizontal(tex);
                        saved++;
                    }
                    Parts.Add(part);
                }

                Aspect = w / h;
                FromLive = true;
                HasArt = saved > 0;
                Source = HasArt ? "原版界面+贴图" : "原版界面";
                SaveCache();
                Log.Info($"[肢体图] 抓取 {Parts.Count} 个部位，缓存贴图 {saved} 张 → {ArtDir}（翻转：左={FlipLeftSide} 右={FlipRightSide}）");
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
            return index == 0 ? "头" : "#" + index;
        }

        // ===== 贴图抓取：非可读贴图走 RenderTexture =====

        private static readonly Dictionary<int, Texture2D> ReadableCache = new Dictionary<int, Texture2D>();

        private static bool TrySnapshot(LimbPart part, out Texture2D cropped)
        {
            cropped = null;
            Sprite sprite = part.Sprite;
            if (sprite == null || sprite.texture == null) return false;

            try
            {
                Texture2D src = sprite.texture;
                if (!ReadableCache.TryGetValue(src.GetInstanceID(), out Texture2D readable) || readable == null)
                {
                    readable = MakeReadable(src);
                    if (readable == null) return false;
                    ReadableCache[src.GetInstanceID()] = readable;
                }

                Rect tr = sprite.textureRect;
                int x = Mathf.Clamp((int)tr.x, 0, readable.width - 1);
                int y = Mathf.Clamp((int)tr.y, 0, readable.height - 1);
                int cw = Mathf.Clamp((int)tr.width, 1, readable.width - x);
                int ch = Mathf.Clamp((int)tr.height, 1, readable.height - y);

                cropped = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
                cropped.SetPixels(readable.GetPixels(x, y, cw, ch));
                cropped.Apply();

                byte[] png = cropped.EncodeToPNG();
                if (png == null || png.Length == 0) return false;
                File.WriteAllBytes(ArtPath(part.Index), png);
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug($"抓取部位 {part.Index} 贴图失败：{ex.Message}");
                return false;
            }
        }

        /// <summary>把不可读的贴图复制成 CPU 可读的 Texture2D（Blit + ReadPixels）。</summary>
        private static Texture2D MakeReadable(Texture2D src)
        {
            RenderTexture rt = null;
            RenderTexture prev = RenderTexture.active;
            try
            {
                rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;

                var tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
                tex.Apply();
                return tex;
            }
            catch (Exception ex)
            {
                Log.Debug("MakeReadable 失败：" + ex.Message);
                return null;
            }
            finally
            {
                RenderTexture.active = prev;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static string ArtPath(int index) => Path.Combine(ArtDir, "part_" + index + ".png");

        /// <summary>生成水平镜像副本（IMGUI 的负宽度缩放不可靠，预翻像素最稳）。</summary>
        public static Texture2D FlipHorizontal(Texture2D src)
        {
            if (src == null) return null;
            try
            {
                int w = src.width, h = src.height;
                Color[] px = src.GetPixels();
                var outPx = new Color[px.Length];
                for (int y = 0; y < h; y++)
                {
                    int row = y * w;
                    for (int x = 0; x < w; x++)
                    {
                        outPx[row + x] = px[row + (w - 1 - x)];
                    }
                }
                var dst = new Texture2D(w, h, TextureFormat.RGBA32, false);
                dst.SetPixels(outPx);
                dst.Apply();
                return dst;
            }
            catch (Exception ex)
            {
                Log.Debug("FlipHorizontal 失败：" + ex.Message);
                return null;
            }
        }

        /// <summary>按当前翻转设置挑一张贴图（已翻好的副本优先）。</summary>
        public static Texture2D TexFor(LimbPart part)
        {
            if (part == null) return null;
            if (!MirrorFor(part)) return part.Tex;
            return part.TexMirror ?? part.Tex;
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
                        ["h"] = p.Norm.height,
                        ["mirror"] = p.MirrorX,
                        ["art"] = File.Exists(ArtPath(p.Index))
                    });
                }
                var root = new JObject
                {
                    ["version"] = 3,
                    ["aspect"] = Aspect,
                    ["flipLeft"] = FlipLeftSide,
                    ["flipRight"] = FlipRightSide,
                    ["parts"] = arr
                };
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
                int art = 0;
                foreach (JToken t in arr)
                {
                    int index = t.Value<int?>("i") ?? Parts.Count;
                    var part = new LimbPart
                    {
                        Index = index,
                        Name = t.Value<string>("name") ?? (index == 0 ? "头" : "#" + index),
                        MirrorX = t.Value<bool?>("mirror") ?? false,
                        Norm = new Rect(
                            t.Value<float?>("x") ?? 0f,
                            t.Value<float?>("y") ?? 0f,
                            t.Value<float?>("w") ?? 0.1f,
                            t.Value<float?>("h") ?? 0.1f)
                    };

                    if (t.Value<bool?>("art") ?? false)
                    {
                        part.Tex = LoadArt(index);
                        if (part.Tex != null) part.TexMirror = FlipHorizontal(part.Tex);
                    }
                    if (part.Tex != null) art++;
                    Parts.Add(part);
                }

                Aspect = root.Value<float?>("aspect") ?? 0.42f;
                FlipLeftSide = root.Value<bool?>("flipLeft") ?? false;
                FlipRightSide = root.Value<bool?>("flipRight") ?? true;
                FromLive = false;
                HasArt = art > 0;
                Source = HasArt ? "缓存美术" : "缓存布局";
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("读取肢体布局缓存失败：" + ex.Message);
                return false;
            }
        }

        private static Texture2D LoadArt(int index)
        {
            try
            {
                string path = ArtPath(index);
                if (!File.Exists(path)) return null;
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                return tex.LoadImage(File.ReadAllBytes(path)) ? tex : null;
            }
            catch (Exception ex)
            {
                Log.Debug($"加载部位 {index} 贴图失败：{ex.Message}");
                return null;
            }
        }

        // ===== ③ 内置兜底 =====

        private static void BuildSynthetic()
        {
            // 15 个部位：头、胸、腹、左右上臂、左右前臂、左右手、左右大腿、左右小腿、左右脚
            // 注意：游戏没有“脖子”，limbs[0] 就是头（原版 WoundView.limbImages[0] 也是头）
            (int i, string name, float x, float y, float w, float h)[] boxes =
            {
                (0, "头",    0.37f, 0.00f, 0.26f, 0.10f),
                (1, "胸",    0.34f, 0.10f, 0.32f, 0.18f),
                (2, "腹/腰", 0.36f, 0.28f, 0.28f, 0.12f),
                (3, "左上臂",0.16f, 0.11f, 0.15f, 0.18f),
                (4, "右上臂",0.69f, 0.11f, 0.15f, 0.18f),
                (5, "左前臂",0.12f, 0.29f, 0.14f, 0.16f),
                (6, "右前臂",0.74f, 0.29f, 0.14f, 0.16f),
                (7, "左手",  0.12f, 0.45f, 0.10f, 0.08f),
                (8, "右手",  0.78f, 0.45f, 0.10f, 0.08f),
                (9, "左大腿",0.34f, 0.41f, 0.14f, 0.22f),
                (10,"右大腿",0.52f, 0.41f, 0.14f, 0.22f),
                (11,"左小腿",0.34f, 0.63f, 0.13f, 0.21f),
                (12,"右小腿",0.53f, 0.63f, 0.13f, 0.21f),
                (13,"左脚",  0.33f, 0.84f, 0.14f, 0.08f),
                (14,"右脚",  0.53f, 0.84f, 0.14f, 0.08f)
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
            HasArt = false;
            Source = "内置布局";
        }
    }
}
