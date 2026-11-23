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
            DdenUI.Fill(area, DdenUI.Bg);
            DdenUI.Fill(area, new Color(color.r, color.g, color.b, 0.14f));
            DdenUI.Frame(area, new Color(color.r, color.g, color.b, 0.6f));
            DdenUI.Fill(new Rect(area.x, area.y, 4, area.height), color);
            string title = kind == NoticeKind.Success ? UnitListPlugin.T("生成成功", "ITEM CREATED")
                : kind == NoticeKind.Failure ? UnitListPlugin.T("生成失败 / 未完成", "CREATION FAILED / INCOMPLETE") : UnitListPlugin.T("等待主机确认", "WAITING FOR HOST");
            var heading = new GUIStyle(DdenUI.Row) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            heading.normal.textColor = color;
            DdenUI.Label(new Rect(area.x + 14, area.y + 5, area.width - 26, 22), title, heading);
            var body = new GUIStyle(DdenUI.Sub) { wordWrap = true, clipping = TextClipping.Clip };
            DdenUI.Label(new Rect(area.x + 14, area.y + 29, area.width - 26, area.height - 34), message, body);
        }
    }
}
