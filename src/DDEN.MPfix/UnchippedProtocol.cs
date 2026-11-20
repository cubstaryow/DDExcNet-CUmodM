using System;
using DDEN.Shared;

namespace DDEN.MPfix
{
    /// <summary>
    /// “无芯片模式”联机同步组件。
    ///
    /// 现状问题：Together(CasualtiesMP) 只在 <c>UnchippedIsIndividual=false</c> 时才把主机裁决往客机推，
    /// 且只覆盖“生成世界那一刻”和 body 级 sync var；客机本地 <see cref="WorldGeneration.unchippedMode"/>
    /// 一旦和主机不一致，所有走 <c>WorldGeneration.unchipped</c> 的判定（深度表 / 视野遮罩 / 伤口界面 /
    /// 心搏骤停 / 辐照 / 枪膛可视）都会退回“有芯片”，于是表现就是“主机无芯片、客机仍有芯片”。
    ///
    /// 本组件的做法：
    /// 1) 主机侧 capture 一条 "unchipped" 状态进 CUCoreLib 的联机快照（晚进主机也能拿到）；
    /// 2) 主机状态变化时用 CUCoreLib Bridge 主动 push 给所有客机；
    /// 3) 客机连上时主动 RequestServer 兜底要一次真值；
    /// 4) 客机拿到真值后强制写本地 world.unchippedMode，并镜像 Together 的 RunUnchippedLock，
    ///    可选地把 UnchippedIsIndividual 改成 false，让 Together 自己的判定也一致；
    /// 5) 每帧级 watchdog：任何人（控制台 / 规则菜单 / 其它模组）把客机状态改回去，都会在 0.5s 内被纠正。
    /// </summary>
    public static class UnchippedProtocol
    {
        public const string ModuleKey = "cumod.unchipped";
        public const string RequestChannel = "cumod.unchipped.req";
        public const string PushChannel = "cumod.unchipped.push";
        public const string StateField = "unchipped";
    }
}
