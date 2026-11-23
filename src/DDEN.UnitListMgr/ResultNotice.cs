using DDEN.Shared;
using UnityEngine;

namespace DDEN.UnitListMgr
{
    internal enum NoticeKind { Waiting, Success, Failure }
    internal static class ResultNotice
    {
        internal static void Draw(Rect area, string message, NoticeKind kind, bool compact = false)
        {
            if (string.IsNullOrEmpty(message)) return;
            Color color = kind == NoticeKind.Success ? new Color(0.36f, 0.9f, 0.58f)
                : kind == NoticeKind.Failure ? new Color(1f, 0.38f, 0.4f) : DdenUI.Accent;
            DdenUI.Fill(area, new Color(0.05f, 0.07f, 0.09f, 0.82f));
            DdenUI.Frame(area, new Color(color.r, color.g, color.b, 0.5f));
            DdenUI.Fill(new Rect(area.x, area.y, 3, area.height), color);
            string title = kind == NoticeKind.Success ? UnitListPlugin.T("成功", "Success")
                : kind == NoticeKind.Failure ? UnitListPlugin.T("失败", "Failed") : UnitListPlugin.T("等待", "Waiting");
            var style = new GUIStyle(DdenUI.Row) { alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
            style.normal.textColor = color;
            string text = compact ? message : title + " · " + message;
            DdenUI.Label(new Rect(area.x + 12, area.y + 2, area.width - 24, area.height - 4), DdenUI.ElideToWidth(text, area.width - 24, style), style);
            if (!compact) DdenUI.Hover(area, message);
        }
    }
}
