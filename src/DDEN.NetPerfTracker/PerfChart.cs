using System;
using DDEN.Shared;
using UnityEngine;

namespace DDEN.NetPerfTracker
{
    /// <summary>Rasterize once per sample/resize; one texture draw per chart, no per-pixel IMGUI calls.</summary>
    internal sealed class PerfChart : IDisposable
    {
        private Texture2D _texture;
        private Color32[] _pixels;
        private int _version = -1, _width, _height;
        private float _start, _end, _maximum;
        private readonly float[][] _series;
        private readonly Color[] _colors;
        private readonly string[] _labels;
        private readonly string[] _legend;
        private string _axis = "", _hoverText;
        private int _hoverIndex = -1, _hoverVersion = -1;
        internal PerfChart(float[][] series, Color[] colors, string[] labels)
        { _series = series; _colors = colors; _labels = labels; _legend = new string[labels.Length]; }
        internal void SetSeries(int i, float[] values) => _series[i] = values;

        internal void Draw(Rect rect, string title, float[] times, byte[] events, int count,
            int version, float start, float end)
        {
            if (Event.current.type != EventType.Repaint) return;
            DdenUI.Fill(rect, new Color(0, 0, 0, 0.22f)); DdenUI.Frame(rect, DdenUI.AccentDim);
            GUI.Label(new Rect(rect.x + 5, rect.y + 1, rect.width - 10, 17), title, DdenUI.Sub);
            var plot = new Rect(rect.x + 34, rect.y + 20, Math.Max(20, rect.width - 130), Math.Max(8, rect.height - 29));
            int width = Math.Min(512, Math.Max(16, (int)plot.width));
            int height = Math.Min(160, Math.Max(8, (int)plot.height));
            if (_texture == null || _width != width || _height != height)
            {
                Dispose(); _width = width; _height = height;
                _texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                _pixels = new Color32[width * height]; _version = -1;
            }
            if (_version != version || _start != start || _end != end)
            { _version = version; _start = start; _end = end; Render(times, events, count); }
            GUI.DrawTexture(plot, _texture);
            GUI.Label(new Rect(rect.x, plot.y - 5, 32, 15), _axis, DdenUI.Small);
            GUI.Label(new Rect(rect.x, plot.yMax - 12, 30, 15), "0", DdenUI.Small);
            for (int i = 0; i < _labels.Length; i++)
            {
                float y = plot.y + i * 14;
                if (y > rect.yMax - 15) break;
                DdenUI.Fill(new Rect(plot.xMax + 6, y + 4, 5, 5), _colors[i]);
                GUI.Label(new Rect(plot.xMax + 15, y - 2, 80, 15), _legend[i], DdenUI.Small);
            }
            if (plot.Contains(Event.current.mousePosition) && count > 0)
            {
                float fraction = Mathf.Clamp01((Event.current.mousePosition.x - plot.x) / plot.width);
                int index = PerfMath.AtOrBefore(times, count, _start + (_end - _start) * fraction);
                if (index >= 0)
                {
                    float x = plot.x + plot.width * fraction;
                    DdenUI.Fill(new Rect(x, plot.y, 1, plot.height), DdenUI.TextDim);
                    if (_hoverIndex != index || _hoverVersion != version)
                    {
                        _hoverIndex = index; _hoverVersion = version;
                        _hoverText = "记录于 " + times[index].ToString("0.0") + "s";
                        for (int i = 0; i < _labels.Length; i++) _hoverText += "\n" + _labels[i] + " " + Format(_series[i][index]);
                    }
                    var tip = new Rect(Mathf.Clamp(x + 8, plot.x, Math.Max(plot.x, plot.xMax - 150)), plot.y, 150, 19 + _labels.Length * 15);
                    DdenUI.Fill(tip, new Color(0.04f, 0.05f, 0.07f, 0.98f));
                    GUI.Label(new Rect(tip.x + 5, tip.y + 2, tip.width - 10, tip.height - 4), _hoverText, DdenUI.Small);
                }
            }
        }

        private void Render(float[] times, byte[] events, int count)
        {
            Array.Clear(_pixels, 0, _pixels.Length);
            _maximum = 1f;
            int begin = Math.Max(0, PerfMath.AtOrBefore(times, count, _start));
            foreach (var values in _series)
                for (int i = begin; i < count; i++) if (values[i] > _maximum) _maximum = values[i];
            _maximum *= 1.1f;
            _axis = _maximum.ToString("0.#");
            for (int i = 0; i < _legend.Length; i++) _legend[i] = _labels[i] + " " + Format(count > 0 ? _series[i][count - 1] : -1);
            for (int y = 0; y < _height; y += Math.Max(1, _height / 2))
                for (int x = 0; x < _width; x++) Dot(x, y, new Color32(100, 100, 100, 45));
            for (int s = 0; s < _series.Length; s++)
            {
                float[] values = _series[s];
                int at = begin, previousY = -1;
                float last = begin > 0 ? values[begin - 1] : -1;
                for (int x = 0; x < _width; x++)
                {
                    float until = _start + (_end - _start) * (x + 1) / _width;
                    float low = float.MaxValue, high = -1;
                    bool gap = false;
                    while (at < count && times[at] <= until)
                    {
                        if ((events[at] & 32) != 0) gap = true;
                        last = values[at++];
                        if (last < 0) { gap = true; continue; }
                        low = Math.Min(low, last); high = Math.Max(high, last);
                    }
                    if (gap) previousY = -1;
                    if (high >= 0) Vertical(x, Y(low), Y(high), _colors[s]); // Preserve brief spikes in dense histories.
                    // The first post-resume record terminates an unobserved interval; do not extend the old value through it.
                    if (at < count && (events[at] & 32) != 0 && high < 0) { previousY = -1; continue; }
                    if (last < 0) { previousY = -1; continue; }
                    int y = Y(last);
                    if (previousY >= 0) Vertical(x, previousY, y, _colors[s]); else Dot(x, y, _colors[s]);
                    previousY = y;
                }
            }
            for (int i = begin; i < count; i++)
            {
                if ((events[i] & 155) == 0) continue; // players/world/connection/manual/anomaly marker.
                int x = (int)((times[i] - _start) / Math.Max(0.001f, _end - _start) * (_width - 1));
                if (x >= 0 && x < _width) for (int y = 0; y < _height; y += 3) Dot(x, y, new Color32(255, 180, 70, 160));
            }
            _texture.SetPixels32(_pixels); _texture.Apply(false, false);
        }
        private int Y(float value) => (int)(Mathf.Clamp01(value / _maximum) * (_height - 1));
        private void Vertical(int x, int a, int b, Color32 color)
        { for (int y = Math.Min(a, b); y <= Math.Max(a, b); y++) Dot(x, y, color); }
        private void Dot(int x, int y, Color32 color) { if (x >= 0 && x < _width && y >= 0 && y < _height) _pixels[y * _width + x] = color; }
        internal static string Format(float v) => v < 0 ? "—" : v >= 100 ? v.ToString("0") : v.ToString("0.##");
        public void Dispose()
        {
            if (_texture != null) UnityEngine.Object.Destroy(_texture);
            _texture = null; _pixels = null; _version = -1;
        }
    }
}
