using System.Collections.Generic;
using UnityEngine;

namespace DDEN.Shared
{
    public static partial class DdenUI
    {
        private static GUIStyle _scrollTrack, _scrollThumb, _valueTrack, _valueThumb;
        private sealed class ScrollState
        {
            public ScrollState() { }
            public Vector2 Last, Pending;
            public bool HasPending;
            public float DragOffset;
        }
        private struct ScrollFrame
        {
            public Rect Area;
            public Rect VisibleArea, VisibleViewport;
            public Vector2 Position;
            public ScrollLayout Layout;
            public ScrollState State;
        }
        private static readonly Stack<ScrollFrame> ScrollFrames = new Stack<ScrollFrame>();
        private static bool ScrollPointerVisible() => ScrollFrames.Count == 0 || ScrollFrames.Peek().VisibleViewport.Contains(GUIUtility.GUIToScreenPoint(Event.current.mousePosition));
        private static Rect ScreenRect(Rect rect)
        {
            var a = GUIUtility.GUIToScreenPoint(new Vector2(rect.xMin, rect.yMin));
            var b = GUIUtility.GUIToScreenPoint(new Vector2(rect.xMax, rect.yMax));
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }
        private static Rect Intersect(Rect a, Rect b) => Rect.MinMaxRect(Mathf.Max(a.xMin, b.xMin), Mathf.Max(a.yMin, b.yMin), Mathf.Min(a.xMax, b.xMax), Mathf.Min(a.yMax, b.yMax));
        private static Texture2D ScrollTexture(Color color)
        {
            var texture = new Texture2D(1, 1) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixel(0, 0, color); texture.Apply(); return texture;
        }
        private static void EnsureScrollStyles()
        {
            if (_scrollTrack != null) return;
            _scrollTrack = new GUIStyle { margin = new RectOffset(), padding = new RectOffset(2, 2, 2, 2) };
            _scrollTrack.normal.background = ScrollTexture(Bg);
            _scrollTrack.hover.background = _scrollTrack.active.background = _scrollTrack.normal.background;
            _scrollThumb = new GUIStyle { margin = new RectOffset(), padding = new RectOffset() };
            _scrollThumb.normal.background = ScrollTexture(new Color(0.25f, 0.41f, 0.51f));
            _scrollThumb.hover.background = ScrollTexture(Accent);
            _scrollThumb.active.background = ScrollTexture(new Color(0.55f, 0.84f, 1f));
            _valueTrack = new GUIStyle(_scrollTrack) { padding = new RectOffset(0, 0, 5, 5) };
            _valueThumb = new GUIStyle(_scrollThumb) { fixedWidth = 10f, fixedHeight = 16f };
        }
        public static Vector2 BeginScrollView(Rect area, Vector2 position, Rect content)
        {
            var layout = new ScrollLayout(area.width, area.height, content.width, content.height);
            bool pointerVisible = ScrollPointerVisible();
            int viewId = GUIUtility.GetControlID(0x44445356, FocusType.Passive);
            int verticalId = GUIUtility.GetControlID(0x44445642, FocusType.Passive);
            int horizontalId = GUIUtility.GetControlID(0x44444842, FocusType.Passive);
            var state = (ScrollState)GUIUtility.GetStateObject(typeof(ScrollState), viewId);
            if (state.HasPending)
            {
                // An explicit caller reset wins over a wheel update queued by EndScrollView.
                if (position == state.Last) position = state.Pending;
                state.HasPending = false;
            }
            position.x = Mathf.Clamp(position.x, 0, layout.MaxX); position.y = Mathf.Clamp(position.y, 0, layout.MaxY);
            if (layout.Vertical)
            {
                var track = new Rect(area.xMax - ScrollLayout.Bar, area.y, ScrollLayout.Bar, layout.Height);
                position.y = ScrollBar(track, position.y, layout.Height, content.height, verticalId, state, false, pointerVisible);
            }
            if (layout.Horizontal)
            {
                var track = new Rect(area.x, area.yMax - ScrollLayout.Bar, layout.Width, ScrollLayout.Bar);
                position.x = ScrollBar(track, position.x, layout.Width, content.width, horizontalId, state, true, pointerVisible);
            }
            if ((!layout.Vertical && GUIUtility.hotControl == verticalId) || (!layout.Horizontal && GUIUtility.hotControl == horizontalId)) GUIUtility.hotControl = 0;
            state.Last = position;
            var visibleArea = ScreenRect(area);
            var visibleViewport = ScreenRect(new Rect(area.x, area.y, layout.Width, layout.Height));
            if (ScrollFrames.Count > 0)
            {
                visibleArea = Intersect(visibleArea, ScrollFrames.Peek().VisibleViewport);
                visibleViewport = Intersect(visibleViewport, ScrollFrames.Peek().VisibleViewport);
            }
            ScrollFrames.Push(new ScrollFrame { Area = area, VisibleArea = visibleArea, VisibleViewport = visibleViewport, Position = position, Layout = layout, State = state });
            GUI.BeginGroup(new Rect(area.x, area.y, layout.Width, layout.Height));
            GUI.BeginGroup(new Rect(-position.x - content.x, -position.y - content.y, Mathf.Max(1, content.width), Mathf.Max(1, content.height)));
            return position;
        }
        public static void EndScrollView()
        {
            GUI.EndGroup(); GUI.EndGroup();
            var frame = ScrollFrames.Pop();
            var e = Event.current;
            // Children get the wheel first; at their boundary it can bubble to a parent.
            if (!GUI.enabled || e.type != EventType.ScrollWheel || !frame.Area.Contains(e.mousePosition) || !frame.VisibleArea.Contains(GUIUtility.GUIToScreenPoint(e.mousePosition))) return;
            var next = frame.Position;
            if (e.shift || frame.Layout.MaxY == 0) next.x = Mathf.Clamp(next.x + (e.delta.x + e.delta.y) * 24, 0, frame.Layout.MaxX);
            else
            {
                next.x = Mathf.Clamp(next.x + e.delta.x * 24, 0, frame.Layout.MaxX);
                next.y = Mathf.Clamp(next.y + e.delta.y * 24, 0, frame.Layout.MaxY);
            }
            if (next != frame.Position) { frame.State.Pending = next; frame.State.HasPending = true; e.Use(); }
        }
        private static float ScrollBar(Rect track, float value, float viewport, float content, int id, ScrollState state, bool horizontal, bool pointerVisible)
        {
            var e = Event.current;
            float start = (horizontal ? track.x : track.y) + 2;
            var axis = new ScrollAxis((horizontal ? track.width : track.height) - 4, viewport, content);
            float mouse = horizontal ? e.mousePosition.x : e.mousePosition.y;
            float offset = axis.Offset(value);
            var thumb = horizontal ? new Rect(start + offset, track.y + 2, axis.Thumb, track.height - 4)
                : new Rect(track.x + 2, start + offset, track.width - 4, axis.Thumb);
            if (!GUI.enabled && GUIUtility.hotControl == id) GUIUtility.hotControl = 0;
            if (GUI.enabled && pointerVisible && e.type == EventType.MouseDown && e.button == 0 && track.Contains(e.mousePosition))
            {
                if (thumb.Contains(e.mousePosition)) { GUIUtility.hotControl = id; state.DragOffset = mouse - start - offset; }
                else value = Mathf.Clamp(value + (mouse < start + offset ? -viewport : viewport), 0, axis.Maximum);
                e.Use();
            }
            else if (GUIUtility.hotControl == id && e.type == EventType.MouseDrag)
            { value = axis.Value(mouse - start - state.DragOffset); e.Use(); }
            else if (GUIUtility.hotControl == id && e.type == EventType.MouseUp)
            { GUIUtility.hotControl = 0; e.Use(); }
            if (horizontal) thumb.x = start + axis.Offset(value); else thumb.y = start + axis.Offset(value);
            if (horizontal)
            {
                // A thin flat rail and square thumb, matching manager-hosted legacy scroll views.
                Fill(new Rect(track.x, track.center.y - 2, track.width, 4), PanelAlt);
            }
            else { Fill(track, Bg); Frame(track, AccentDim); }
            Fill(thumb, GUIUtility.hotControl == id ? new Color(.55f, .84f, 1) : pointerVisible && thumb.Contains(e.mousePosition) ? Accent : new Color(.25f, .41f, .51f));
            if (horizontal && thumb.width >= 24)
            {
                Fill(new Rect(thumb.center.x - 2, thumb.y + 2, 1, Mathf.Max(1, thumb.height - 4)), Bg);
                Fill(new Rect(thumb.center.x + 1, thumb.y + 2, 1, Mathf.Max(1, thumb.height - 4)), Bg);
            }
            return value;
        }
        public static float HorizontalSlider(Rect area, float value, float minimum, float maximum)
        {
            EnsureScrollStyles();
            return GUI.HorizontalSlider(area, value, minimum, maximum, _valueTrack, _valueThumb);
        }
    }
}
