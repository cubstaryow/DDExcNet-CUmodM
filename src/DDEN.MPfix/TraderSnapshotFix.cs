using System;
using System.Reflection;
using System.Collections.Generic;
using DDEN.Shared;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DDEN.MPfix
{
    /// <summary>
    /// 修「客机打开商人时商品表和主机不一致 → 花了钱不给东西」。
    ///
    /// 病根：商人库存由主机维护，客机看到的是主机某次 <c>TraderCommit.Send</c> 的快照。
    /// 一旦客机在快照之后被别的东西改了本地列表（买卖、刷新、重连、其它模组），
    /// 客机下单的 `{index, itemId, value}` 在主机侧就校验不过：
    /// <code>
    /// TraderCommit.HandleBuy:  job.index >= trader.items.Count
    ///                       || trader.items[index].id != job.itemId
    ///                       || value 不符        → 直接 Publish 走人，不发货
    /// </code>
    /// 结果就是“扣了钱、没拿到东西”。
    ///
    /// 修法：客机**每次打开交易菜单**都向主机要一次全量快照。
    /// 走 Together 自己的 <c>TraderSync.SendTraderState(clientId)</c>（它内部对每个范围内商人调 PublishTo，
    /// 发包内容包括完整 items 列表），所以不需要我们自己序列化任何东西。
    /// </summary>
    internal static class TraderSnapshotFix
    {
        private const string Channel = "ddn.mp.tradersnap";
        private static float _lastAskAt = -99f;
        private static readonly Dictionary<uint, float> LastSent = new Dictionary<uint, float>();

        internal static void Reset() { _lastAskAt = -99f; LastSent.Clear(); }

        [HarmonyPatch(typeof(PlayerCamera), "ToggleTradeMenu")]
        internal static class ToggleTradeMenu_Postfix
        {
            private static void Postfix(PlayerCamera __instance)
            {
                try
                {
                    if (!MPfixPlugin.Active) return;
                    if (!MPfixPlugin.CfgTraderSnapshot.Value) return;
                    if (!Mp.IsRunning || !Mp.IsClient) return;
                    if (__instance == null || __instance.tradeMenu == null || !__instance.tradeMenu.activeSelf
                        || __instance.currentTrader == null) return;

                    float now = Time.unscaledTime;
                    if (now - _lastAskAt < 0.5f) return;   // 连点开合菜单不刷包
                    _lastAskAt = now;

                    AskHostForSnapshot();
                }
                catch (Exception ex)
                {
                    MPfixPlugin.Log.Debug("打开商人时请求快照失败：" + ex.Message);
                }
            }
        }

        /// <summary>客机：向主机索取一次商人快照（含商品表）。</summary>
        internal static void AskHostForSnapshot()
        {
            if (!MPfixPlugin.Active || !MPfixPlugin.CfgTraderSnapshot.Value || !Mp.IsRunning || !Mp.IsClient) return;
            Mp.RequestServer(Channel, new JObject { ["ask"] = "traders" }, resp =>
            {
                if (resp == null) return;
                if (resp.Value<bool?>("ok") == true) MPfixPlugin.Log.Debug("[商人] 已请主机重发商品表");
            });
        }

        /// <summary>主机：收到请求后把范围内所有商人的完整快照发给这个客机。</summary>
        internal static void RegisterHost()
        {
            bool ok = MpChannels.TryRegisterSender(Channel, (sender, token) =>
            {
                try
                {
                    if (!MPfixPlugin.Active || !MPfixPlugin.CfgTraderSnapshot.Value || !Mp.IsRunning || !Mp.IsServer)
                        return new JObject { ["ok"] = false };
                    if (MpChannels.BodyForClient(sender) == null) return new JObject { ["ok"] = false };
                    float now = Time.unscaledTime;
                    if (LastSent.TryGetValue(sender, out float last) && now - last < 0.5f)
                        return new JObject { ["ok"] = false };
                    LastSent[sender] = now;

                    Type traderSync = AccessTools.TypeByName("Together.TraderSync");
                    MethodInfo send = traderSync == null ? null : AccessTools.Method(traderSync, "SendTraderState");
                    object knet = MpChannels.MakeKnet(sender);

                    if (send == null || knet == null)
                    {
                        MPfixPlugin.Log.Warn("[商人] 找不到 TraderSync.SendTraderState（Together 版本变了？），本次不补发。");
                        return new JObject { ["ok"] = false };
                    }

                    send.Invoke(null, new[] { knet });
                    return new JObject { ["ok"] = true };
                }
                catch (Exception ex)
                {
                    MPfixPlugin.Log.Debug("[商人] 补发快照失败：" + ex.Message);
                    return new JObject { ["ok"] = false };
                }
            });

            if (!ok)
            {
                MPfixPlugin.Log.Warn("[商人] 无法注册带发送者的 tradersnap 频道，打开商人时不会自动补发商品表。");
            }
        }
    }
}
