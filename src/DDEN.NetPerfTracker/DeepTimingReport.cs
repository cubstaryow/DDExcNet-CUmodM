using System;
using System.Globalization;
using System.Text;
using DDEN.Diagnostics;
using static DDEN.NetPerfTracker.ModuleText;

namespace DDEN.NetPerfTracker
{
    internal static class DeepTimingReport
    {
        private static string DisplayName(string name)
        {
            switch (name)
            {
                case "Together.ChunkSleep.Tick": return L("区块休眠");
                case "Together.BagSleep.Tick": return L("背包休眠");
                case "Together.HeaterMultiplayerRewrite.Tick": return L("加热器同步");
                case "Together.BagSleep.WriteHost": return L("主机背包状态写入");
                case "Together.FallCatch.Tick": return L("坠落同步保护");
                case "KrokV5Optimization.SafetyNet.Tick": return L("网络安全检查");
                case "KrokV5Optimization.SlowStamp.Tick": return L("延迟任务");
                case "Together.BodyViewParticles.Tick": return L("身体粒子");
                case "Together.WallholeZones.Tick": return L("墙洞区域");
                case "KrokV5Optimization.BagPlace.Tick": return L("背包挂接");
                case "KrokV5Optimization.FixInvSync.Tick": return L("背包同步重试");
                case "KrokV5Optimization.OptimizerMain.Tick": return L("联机维护总计");
                case "KrokV5Optimization.FixInvSync.Sweep": return L("背包失效任务检查");
                case "Together.Net.DoUpdate": return L("网络更新总计");
                case "Together.TransportSteamworks.Update": return L("Steam传输更新");
                case "Together.TransportSteamworks._Update_PollMessages": return L("Steam消息接收");
                case "Together.TransportLiteNetLib.Together.NetTransportBase.Update": return L("局域网传输更新");
                case "LiteEntitySystem.EntityManager.Update": return L("实体同步更新");
                case "LiteEntitySystem.ServerEntityManager.OnLogicTick": return L("主机实体模拟");
                case "LiteEntitySystem.ClientEntityManager.Update": return L("客机实体同步");
                case "LiteEntitySystem.ClientEntityManager.OnLogicTick": return L("客机实体模拟");
                case "Together.SyncInfo.Update": return L("同步对象更新");
                case "Together.SyncInfo.WriteObjectIntoPacket": return L("同步对象写入");
                case "Together.SyncInfo.ApplyPacketToObject": return L("同步对象接收应用");
                case "Together.Plugin.Update": return L("联机每帧更新");
                case "Together.Plugin.FixedUpdate": return L("联机物理更新");
                case "Together.Plugin.LateUpdate": return L("联机帧末更新");
                case "Together.ServerMain.Update": return L("主机每帧更新");
                case "Together.ServerMain.LateUpdate": return L("主机帧末更新");
                case "Item.Update": return L("物品更新");
                case "Item.HandleDecay": return L("物品腐坏");
                case "Body.FixedUpdate": return L("身体物理更新");
                case "Body.Update": return L("身体更新");
                case "Body.HandlePhysics": return L("身体物理处理");
                case "CpuOptimizationAdv.CullWorld.Tick": return L("场景裁剪");
                case "Together.SyncInfoGameObjectTracker.FixedUpdate": return L("物体同步物理更新");
                case "Together.SyncInfoGameObjectTracker.TickSync": return L("物体同步采样");
                case "LiquidAffect.FixedUpdate": return L("液体影响");
                case "IKHandle.Update": return L("肢体姿态更新");
                case "IKHandle.UpdateIK": return L("肢体姿态计算");
                case "Item.Awake": return L("物品唤醒");
                case "Item.OnDestroy": return L("物品销毁");
                case "Together.SyncInfo.DestroyTrackedGameObject": return L("同步对象销毁");
                case "Wearable.Awake": return L("装备唤醒");
                case "Together.SyncInfoGameObjectTracker.FlushFrameSamples": return L("物体同步每帧汇总");
                case "Together.NetObjectRegistry.Update": return L("网络对象注册更新");
                case "Together.NetObjectRegistry._ObjectSyncUpdateLoopUniversal": return L("网络对象注册扫描");
                case "Together.LayerBuildingStream.HostRegister": return L("主机建筑注册扫描");
                case "Together.ChunkSleep.Entries": return L("区块休眠记录数");
                case "Together.BagSleep.Rows": return L("背包休眠记录数");
                case "KrokV5Optimization.SlowStamp.Jobs": return L("延迟任务数");
                case "Together.WallholeZones.Holes": return L("墙洞区域数");
                case "KrokV5Optimization.BagPlace.Waiting": return L("等待挂接背包数");
                case "KrokV5Optimization.BagPlace.Announcing": return L("等待广播背包数");
                case "KrokV5Optimization.FixInvSync.Pending": return L("背包同步待处理数");
                case "KrokV5Optimization.FixInvSync.Exhausted": return L("背包同步超时数");
                default: return L("其他计时项目");
            }
        }
        private static string N(double number) => number < 0 || double.IsNaN(number) || double.IsInfinity(number)
            ? L("不可用") : number.ToString("0.###", CultureInfo.InvariantCulture);
        internal static string Header(DeepWindow data)
        {
            if (data == null) return L("深层数据尚未采集。需安装增强模块，开启深层计时与Together探针，并保持性能采样运行。");
            string state = data.Status == "unpatch_failed" ? L("计时未能完全停止，请重启游戏")
                : data.Status == "partial" ? L("部分方法可用")
                : data.Active ? L("正在细分计时") : L("深层计时未运行");
            return LF($"深层计时 · {state} · 方法 {data.Patched}/{data.Expected} · 完整帧 {data.Frames} · 非主线程略过 {data.OffThread}")
                + "\n" + LF($"窗口为引擎时间 {data.Started:0.000}～{data.Ended:0.000}s；安装耗时 {data.SetupMs:0.##}ms。首次启用后先等待几秒再比较。")
                + "\n" + L("耗时包含等待，部分项目互相包含，请勿相加。启用深层计时会增加额外开销。");
        }
        internal static string Row(DeepRow row)
        {
            if (row == null) return L("不可用");
            string name = DisplayName(row.Name);
            if (!row.Available) return name + " · " + L("未启用或不可用");
            return name + (row.Inclusive ? " · " + L("含子项") : "") + "\n"
                + LF($"均值/帧 {N(row.PerFrameMs)}ms，峰值/帧 {N(row.PeakFrameMs)}ms；调用 {row.Calls} 次，异常结束 {row.Errors} 次；均值/次 {N(row.MeanCallMs)}ms，峰值/次 {N(row.PeakCallMs)}ms");
        }
        internal static string Counts(DeepWindow data)
        {
            if (data?.Counts == null || data.Counts.Length == 0) return "";
            var text = new StringBuilder(L("同步任务数量："));
            foreach (var count in data.Counts)
                if (count != null) text.Append('\n').Append(DisplayName(count.Name))
                    .Append(" = ").Append(count.Value < 0 ? L("不可用") : count.Value.ToString(CultureInfo.InvariantCulture));
            return text.ToString();
        }
    }
}
