using static DDEN.NetPerfTracker.ModuleText;
using System;
using System.Globalization;

namespace DDEN.NetPerfTracker
{
    internal static class ProbeReport
    {
        private static string N(float value) => value < 0 || float.IsNaN(value) || float.IsInfinity(value)
            ? L("不可用") : value.ToString("0.##", CultureInfo.InvariantCulture);
        private static string I(int value) => value < 0 ? L("不可用") : value.ToString(CultureInfo.InvariantCulture);
        internal static string Format(PerfSample sample)
        {
            var p = sample.Probe;
            if (!p.Enabled) return L("Together探针已关闭；开启后记录报错样本、同步状态与线程CPU，不改变联机行为。");
            string role = p.Running ? p.Server ? L("主机") : L("客机") : L("未联机");
            string result = LF($"Together反馈 · {role} · 本窗口错误 {p.Logs.Errors} / 警告 {p.Logs.Warnings} / baseline日志 {p.Logs.BaselineLogs} / 省略细节 {p.Logs.DetailSkipped}");
            result += "\n" + LF($"探针累计错误 {p.TotalLogs.Errors} / 警告 {p.TotalLogs.Warnings} / baseline日志 {p.TotalLogs.BaselineLogs}（日志条数，非去重故障数）");
            result += "\n" + LF($"Unity异常日志：本窗口 {p.Logs.UnityErrors} / 累计 {p.TotalLogs.UnityErrors}；包含其它模组，堆栈提及Together／LES／CPU优化时也计入上方相关错误数，不能据此认定责任。");
            result += "\n" + LF($"LES tick {I(p.Tick)}，增量 {I(p.TickDelta)}；待处理请求 {I(p.Queue)}，变化实体 {I(p.ChangedEntities)}，区块队列 {I(p.QueuedChunks)}");
            result += "\n" + LF($"LES连接 {I(p.LesPlayers)}，已检查 {p.InspectedPeers}；待baseline {I(p.PendingBaseline)}，最长确认间隔 {N(p.AckAgeSeconds)}秒");
            if (!p.CompletePeers && p.Server) result += L("（连接状态可能不完整）");
            result += "\n" + LF($"主线程CPU {N(p.MainCpuPct)}%（单线程口径）；近10秒1%低帧估算 {N(p.LowOnePercent)}fps / {p.TailFrames}帧");
            result += "\n" + LF($"Job等待均值/峰值 {N(p.JobWaitMs)}/{N(p.JobWaitPeakMs)}ms；GC标记均值/峰值 {N(p.GcMs)}/{N(p.GcPeakMs)}ms");
            result += "\n" + LF($"原生阶段均值：2D固定更新 {N(sample.Stages.Physics2DLoop.Frames > 0 ? sample.Stages.Physics2DLoop.Mean : -1)}ms；脚本固定更新 {N(sample.Stages.ScriptFixed.Frames > 0 ? sample.Stages.ScriptFixed.Mean : -1)}ms；脚本更新 {N(sample.Stages.ScriptUpdate.Frames > 0 ? sample.Stages.ScriptUpdate.Mean : -1)}ms；脚本后更新 {N(sample.Stages.ScriptLate.Frames > 0 ? sample.Stages.ScriptLate.Mean : -1)}ms。");
            result += "\n" + L("这些阶段可能互相包含；2D固定更新不等于精确物理模拟耗时，缺失计时不按零处理。");
            if ((p.Logs.Errors > 0 || p.Logs.UnityErrors > 0) && sample.LongFrames > 0)
                result += "\n" + L("本窗口同时观察到报错和长帧，仅表示区间相关，不能确定报错先后或认定因果。");
            if (p.NoTickSeconds >= 2 && p.Logs.Errors > 0)
                result += "\n" + LF($"同步tick连续约{p.NoTickSeconds:0.0}秒未观察到推进，并伴随报错；请结合堆栈排查，不等同死锁证明。");
            if (p.Status == "partial" || p.Running && p.Server && !p.StateAvailable)
                result += "\n" + L("部分Together内部字段不可用，已保留其余读数；缺失不按零处理。");
            return result + "\n" + L("低帧为分桶估算，至少120帧；Job/GC计时可能缺失或延迟。没有baseline日志不代表没有重同步。主线程CPU不能区分等待与调度延迟。");
        }
        private static string Plain(string text) => (text ?? "").Replace('<', '[').Replace('>', ']');
        internal static string Message(ProbeMessage message) => LF($"日志样本 {message.T:0.000}s · {Plain(message.Source)}")
            + "\n" + Plain(message.Text);
    }
}
