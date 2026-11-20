using System;
using System.Collections.Generic;
using DDEN.Shared;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DDEN.MPfix
{
    /// <summary>
    /// 修「客机挖掉方块后又重新长出来」。
    ///
    /// 病根（Together 5.5.4）：
    ///  - 客机挖块是**本地先乐观结算**：<c>WorldGeneration.DamageBlock</c> 本地把方块打成 0 并显示消失；
    ///  - 真正的“落地”要等主机同意：客机把这一挥累计的伤害随攻击包发给主机，
    ///    主机用 <c>BlockDamageAuthority.ChooseBlockDamage(hostOwn, clientReported, clientId)</c> 取一个值，
    ///    只有通过校验（≤ host 自身 3 倍、≤2000 等）才用客机的数字，否则**退回主机自己的小伤害**；
    ///  - 只要主机没把这块打碎（伤害不够 / GetBlock 判定为 0 / 坐标差一点），
    ///    之后 chunk 同步把原始 tile 重新推给客机 → 客机看到“挖掉了又出现”。
    ///
    /// 修法：客机本地**真的把方块打成 0** 的那一刻，单独发一条 `ddn.mp.blockbreak {x,y}` 给主机，
    /// 主机按自己的方块表算“还差多少血”，然后用**一次足量 DamageBlock** 把它打碎
    /// （走原版流程，所以掉落/音效/广播都正常）。之后主机的 SetBlock 广播会把所有客机对齐。
    ///
    /// 安全：只在客机、只在“本地确实变成 0”时发；同坐标 5 秒内去重；每局上限 3000 次；
    /// 主机侧只看自己的方块表，已经是空的就什么都不做；全程 try/catch。
    /// </summary>
    internal static class BlockBreakFix
    {
        private const string Channel = "ddn.mp.blockbreak";
        private const float DedupeSeconds = 5f;
        private const int MaxPerSession = 3000;

        private static readonly Dictionary<Vector2Int, float> Sent = new Dictionary<Vector2Int, float>();
        private static int _count;

        [HarmonyPatch(typeof(WorldGeneration), "DamageBlock", new[]
        {
            typeof(Vector2Int), typeof(float), typeof(bool), typeof(bool), typeof(bool)
        })]
        internal static class DamageBlock_Postfix
        {
            private static void Postfix(WorldGeneration __instance, Vector2Int pos)
            {
                try
                {
                    if (!MPfixPlugin.CfgBlockBreakSync.Value) return;
                    if (!Mp.IsRunning || !Mp.IsClient) return;
                    if (__instance == null) return;
                    if (__instance.GetBlock(pos) != 0) return;      // 还没碎

                    Report(pos);
                }
                catch (Exception ex)
                {
                    MPfixPlugin.Log.Debug("方破碎上报失败：" + ex.Message);
                }
            }
        }

        private static void Report(Vector2Int pos)
        {
            if (_count >= MaxPerSession) return;

            float now = Time.unscaledTime;
            float last;
            if (Sent.TryGetValue(pos, out last) && now - last < DedupeSeconds) return;
            if (Sent.Count > 1024) Sent.Clear();
            Sent[pos] = now;

            _count++;
            Mp.SendToServer(Channel, new JObject { ["x"] = pos.x, ["y"] = pos.y });
        }

        /// <summary>主机：把这个坐标的方块按“剩余血量”一次打碎（走原版 DamageBlock，掉落正常）。</summary>
        internal static void RegisterHost()
        {
            try
            {
                Mp.RegisterServerHandler(Channel, token =>
                {
                    try
                    {
                        if (!Mp.IsServer || token == null) return new JObject { ["ok"] = false };

                        int x = token["x"]?.Value<int>() ?? int.MinValue;
                        int y = token["y"]?.Value<int>() ?? int.MinValue;
                        if (x == int.MinValue || y == int.MinValue) return new JObject { ["ok"] = false };

                        WorldGeneration world = WorldGeneration.world;
                        if (world == null) return new JObject { ["ok"] = false };

                        var pos = new Vector2Int(x, y);
                        int block = world.GetBlock(pos);
                        if (block == 0) return new JObject { ["ok"] = true, ["already"] = true };

                        BlockInfo info = world.GetBlockInfo((ushort)block);
                        BlockDamage bd = world.GetBlockDamage(pos);
                        float already = bd != null ? bd.damage : 0f;
                        float need = Mathf.Max(1f, (info != null ? info.health : 100f) - already);

                        world.DamageBlock(pos, need, true, false, false);
                        return new JObject { ["ok"] = true };
                    }
                    catch (Exception ex)
                    {
                        MPfixPlugin.Log.Debug("[方破碎] 主机处理失败：" + ex.Message);
                        return new JObject { ["ok"] = false };
                    }
                });
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Warn("[方破碎] 注册失败（不影响游戏）：" + ex.Message);
            }
        }
    }
}
