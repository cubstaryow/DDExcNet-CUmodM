using System;
using UnityEngine;

namespace DDEN.Shared
{
    /// <summary>
    /// 环境版本闸门（2026-11 用户口径）：DDEN 的修复是针对**特定游戏 + 联机框架版本**逐行核对出来的，
    /// 版本对不上时不能装作没事继续跑 —— 那会变成"看起来修好了，其实在跟别的代码对打"。
    ///
    /// 规则：
    ///   · `MpVersion` 与预期联机版本一致 → 放行，GameVersion只作展示参考；
    ///   · 不一致时，必须由用户在管理器「总览」里显式打开「忽略核对环境版本」才放行；
    ///   · 没放行时，依赖版本核对的模块（目前是 MPfix）主开关视为关闭，并在主界面挂一条醒目提示。
    /// </summary>
    public static class DdenGate
    {
        /// <summary>本版 DDEN 核对过的联机框架版本（Together / CasualtiesMP）。</summary>
        public const string ExpectedMp = "5.5.5";

        /// <summary>本版 DDEN 核对过的游戏版本（只用于展示；判定以联机框架版本为准）。</summary>
        public const string ExpectedGame = "1.0.2";

        /// <summary>用户在管理器里打开「忽略核对环境版本」后置真（由管理器写入，模块只读）。</summary>
        public static bool IgnoreEnv;

        public static string GameVersion { get; private set; } = "未知";
        public static string MpVersion { get; private set; } = "未知";

        private static bool _probed;

        // ===== 参与版本核对的模块（用户口径 2026-11：没人声明就不在主界面挂提示）=====

        private static readonly System.Collections.Generic.HashSet<string> Participants =
            new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

        /// <summary>模块声明“本模块的修复依赖环境版本核对”。声明过的模块决定主界面是否挂提示条。</summary>
        public static void RegisterParticipant(string moduleId)
        {
            if (string.IsNullOrEmpty(moduleId)) return;
            Participants.Add(moduleId);
        }

        /// <summary>是否至少有一个模块声明参与版本核对。</summary>
        public static bool HasParticipants => Participants.Count > 0;

        public static int ParticipantCount => Participants.Count;

        /// <summary>采样一次环境版本（可重复调用，只在第一次真正去读）。</summary>
        public static void Probe(bool force = false)
        {
            if (_probed && !force) return;
            _probed = true;

            try { GameVersion = string.IsNullOrEmpty(Application.version) ? "未知" : Application.version; }
            catch { GameVersion = "未知"; }

            try
            {
                Version v = Compat.PluginVersion("CasualtiesMP");
                MpVersion = v == null ? "未安装联机组件" : v.ToString();
            }
            catch { MpVersion = "未知"; }
        }

        /// <summary>当前仅核对联机组件版本；游戏版本用于参考展示。</summary>
        public static bool VersionOk
        {
            get
            {
                Probe();
                return MpVersion == ExpectedMp;
            }
        }

        /// <summary>被闸门拦住：版本不一致，且用户没有打开「忽略核对环境版本」。</summary>
        public static bool Blocked => Compat.PluginLoaded("CasualtiesMP") && !IgnoreEnv && !VersionOk;

        /// <summary>给玩家看的一句话说明（不含任何内部术语以外的实现细节）。</summary>
        public static string Reason =>
            $"当前游戏 {GameVersion} · 联机组件 {MpVersion}；本版是针对联机组件 {ExpectedMp} 做的修复。";

        /// <summary>主界面 / 面板里那条醒目提示的正文。</summary>
        public const string NoticeText = "DDEN未校对此环境版本兼容性，需要打开面板处理！";
    }
}
