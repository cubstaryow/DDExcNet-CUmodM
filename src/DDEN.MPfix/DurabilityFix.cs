using System;
using System.Collections.Generic;
using System.Reflection;
using DDEN.Shared;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DDEN.MPfix
{
    /// <summary>
    /// 修「客机挖东西时工具耐久闪烁」（挖一下掉了，紧接着耐久又跳回去）。
    ///
    /// 病根：耐久扣减写在**物品的 useAction 里**（例：镐子
    /// <c>useAction = (body,item) =&gt; { if (body.Attack(...)) item.condition -= 0.0018f; }</c>），
    /// 而联机里攻击是**主机重跑一遍**（<c>Body_Attack_MultiplayerPatch.hostRerunAttacker</c>）来结算伤害的，
    /// 主机那条路径**不会**调用 useAction，所以主机那份工具耐久一直停在旧值。
    /// 客机本地扣了 → 主机的物品状态同步过来把值写回去 → 看起来“挖掉了又回满”。
    ///
    /// 修法：客机在 <c>Body.UseItemInHand</c> / <c>Body.UseItem</c> 前后量一下手里那件物品的 condition，
    /// 只要真的掉了（0 &lt; delta ≤ 1），就把这个消耗量报给主机，主机对自己的同一件物品扣掉同样的量。
    /// 这样两边最终一致，不再被同步覆盖。
    ///
    /// 安全：只对客机生效；delta 有上下限；每秒发包有节流；找不到对应物品就安静放弃；
    /// 任何异常只记 Debug，绝不影响正常游戏。
    /// </summary>
    internal static class DurabilityFix
    {
        private const string Channel = "ddn.mp.tooldura";
        private const float MaxDelta = 1f;          // 单次最多认 1.0 的耐久消耗
        private const float MinDelta = 0.00001f;
        private const float MinInterval = 0.05f;    // 发包节流

        private static float _lastSendAt = -99f;
        private static int _lost;

        /// <summary>主机侧最近一次“重跑使用动作”造成的耐久变化，用来避免和客机上报重复扣。</summary>
        private static readonly Dictionary<int, float[]> HostApplied = new Dictionary<int, float[]>(); // [delta, at]

        [HarmonyPatch(typeof(Body), "UseItemInHand")]
        internal static class UseItemInHand_Patch
        {
            private static void Prefix(Body __instance, out float __state) => __state = Before(__instance);
            private static void Postfix(Body __instance, float __state) => After(__instance, __state);
        }

        [HarmonyPatch(typeof(Body), "UseItem", new[] { typeof(Item) })]
        internal static class UseItem_Patch
        {
            private static void Prefix(Body __instance, Item item, out float __state)
            {
                // 只量本地玩家的使用动作；别人的（主机重跑等）不参与
                __state = (item != null && Active(__instance)) ? item.condition : -1f;
            }

            private static void Postfix(Item item, float __state) => AfterItem(item, __state);
        }

        private static float Before(Body body)
        {
            try
            {
                if (!Active(body)) return -1f;
                Item hand = body.GetItem(body.handSlot);
                return hand != null ? hand.condition : -1f;
            }
            catch { return -1f; }
        }

        private static void After(Body body, float before)
        {
            try
            {
                if (before < 0f || !Active(body)) return;
                Item hand = body.GetItem(body.handSlot);
                AfterItem(hand, before);
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Debug("耐久同步(手持)失败：" + ex.Message);
            }
        }

        private static void AfterItem(Item item, float before)
        {
            try
            {
                if (item == null || before < 0f) return;
                if (!MPfixPlugin.CfgToolDurability.Value) return;
                if (!Mp.IsRunning || !Mp.IsClient) return;

                float delta = before - item.condition;
                if (delta <= MinDelta || delta > MaxDelta) return;

                float now = Time.unscaledTime;
                if (now - _lastSendAt < MinInterval) return;
                _lastSendAt = now;

                Mp.RequestServer(Channel, new JObject
                {
                    ["id"] = item.id,
                    ["delta"] = delta
                }, resp =>
                {
                    if (resp != null && resp.Value<bool?>("ok") != true)
                    {
                        if (++_lost <= 3)
                        {
                            MPfixPlugin.Log.Debug($"[耐久] 主机没接受 '{item.id}' 的耐久同步（{delta:0.00000}）");
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Debug("耐久同步失败：" + ex.Message);
            }
        }

        private static bool Active(Body body)
        {
            if (!MPfixPlugin.CfgToolDurability.Value) return false;
            if (!Mp.IsRunning || !Mp.IsClient) return false;
            return body != null && Game.LocalBody == body;
        }

        /// <summary>主机：把客机报上来的耐久消耗扣到他对付的那件物品上。</summary>
        internal static void RegisterHost()
        {
            bool ok = MpChannels.TryRegisterSender(Channel, (sender, token) =>
            {
                try
                {
                    if (!Mp.IsServer || token == null) return new JObject { ["ok"] = false };

                    string id = token["id"]?.ToString() ?? "";
                    float delta = token["delta"]?.Value<float>() ?? 0f;
                    if (string.IsNullOrEmpty(id) || delta <= MinDelta || delta > MaxDelta) return new JObject { ["ok"] = false };

                    Body body = MpChannels.BodyForClient(sender);
                    if (body == null) return new JObject { ["ok"] = false };

                    Item target = FindTool(body, id);
                    if (target == null) return new JObject { ["ok"] = false };

                    // 主机的 Together 重跑使用动作时可能已经自己扣过（ItemSync.BetterUseItem → useAction），
                    // 那就不要再扣第二次，否则耐久掉得比单机快一倍。
                    if (HostAlreadyApplied(target, delta)) return new JObject { ["ok"] = true, ["skip"] = true };

                    target.condition = Mathf.Max(0f, target.condition - delta);
                    return new JObject { ["ok"] = true };
                }
                catch (Exception ex)
                {
                    MPfixPlugin.Log.Debug("[耐久] 主机处理失败：" + ex.Message);
                    return new JObject { ["ok"] = false };
                }
            });

            if (!ok)
            {
                MPfixPlugin.Log.Warn("[耐久] 无法注册带发送者的 tooldura 频道，工具耐久不会与主机对齐。");
            }
        }

        private static Item FindTool(Body body, string id)
        {
            try
            {
                Item hand = body.GetItem(body.handSlot);
                if (hand != null && hand.id == id) return hand;

                List<Item> all = body.GetAllItemsThorough();
                if (all == null) return null;
                for (int i = 0; i < all.Count; i++)
                {
                    Item it = all[i];
                    if (it != null && it.id == id && it.condition > 0f) return it;
                }
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Debug("查找工具失败：" + ex.Message);
            }
            return null;
        }

        // ===== 主机侧：观察 Together 重跑使用动作是否已经扣过耐久，避免重复扣 =====

        internal static void InstallHostWatcher(Harmony harmony)
        {
            try
            {
                Type itemSync = AccessTools.TypeByName("Together.ItemSync");
                MethodInfo better = itemSync == null ? null : AccessTools.Method(itemSync, "BetterUseItem");
                if (better == null)
                {
                    MPfixPlugin.Log.Debug("未找到 Together.ItemSync.BetterUseItem，耐久去重逻辑跳过（可能双扣一次，仅影响精度）。");
                    return;
                }

                harmony.Patch(better,
                    prefix: new HarmonyMethod(typeof(DurabilityFix), nameof(BeforeHostUse)),
                    postfix: new HarmonyMethod(typeof(DurabilityFix), nameof(AfterHostUse)));
                MPfixPlugin.Log.Info("已启用补丁：主机重跑使用动作的耐久变化观测（避免重复扣耐久）");
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Warn("耐久去重补丁安装失败（不影响游戏）：" + ex.Message);
            }
        }

        private static void BeforeHostUse(Item item, out float __state)
        {
            __state = item != null ? item.condition : -1f;
        }

        private static void AfterHostUse(Item item, float __state)
        {
            try
            {
                if (item == null || __state < 0f) return;
                float delta = __state - item.condition;
                if (delta <= MinDelta) return;

                if (HostApplied.Count > 512) HostApplied.Clear();
                HostApplied[item.GetInstanceID()] = new[] { delta, Time.unscaledTime };
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Debug("观测主机耐久变化失败：" + ex.Message);
            }
        }

        /// <summary>主机是否刚自己扣过（扣的量与客机报的接近）。</summary>
        private static bool HostAlreadyApplied(Item item, float delta)
        {
            float[] rec;
            if (!HostApplied.TryGetValue(item.GetInstanceID(), out rec) || rec == null) return false;
            if (Time.unscaledTime - rec[1] > 1.5f) return false;
            return rec[0] >= delta * 0.5f;      // 主机扣得不比客机少 → 视为已扣过
        }
    }
}
