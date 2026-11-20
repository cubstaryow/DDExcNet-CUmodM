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
    /// 穿戴兜底（联机）：客机穿上了，主机没认下来。
    ///
    /// 病根（Together 5.5.4 <c>ServerReceiver__ItemWearing</c> 的四道门）：
    ///  ① 物品必须在 <c>NetObjectRegistry</c> 登记表里（<c>TryGetItem</c>）—— 模组物品最容易栽在这；
    ///  ② <c>Stats.wearable</c>；
    ///  ③ **物品与玩家在主机的权威坐标上相距 ≤ 14m**（客机看到的是自己那件，主机那件可能还在柜台/几米外）；
    ///  ④ <c>CheckIfBodyCanStealThisItem</c>。
    /// 任一条不过 → decline：主机眼里它仍是自由物品（有物理就往下掉），
    /// 而客机每帧把“我身上这件”的位置推给主机 → 那件自由物品就**跟着客机跑**。
    ///
    /// 修法：客机穿上后 1.5s 起，最多问主机 3 次（间隔递增）；
    /// 主机按**自己**的身体位置找 id + wearable + 没被别人占用的那件（14m 内，取最近），
    /// 补登记网络身份 → <c>Together.ItemSync.Body_ForceWearWearable</c> → 校验 →
    /// 再 <c>NetObjectRegistry.SafeNewGO</c> 把新状态推给所有客机（Together 自己在 <c>136245</c> 也是这么做的，
    /// 少了这一下主机的修正到不了客机，症状就还是“物品跟着人跑”）。
    ///
    /// 不重建、不复制、不掉落；解析不到发送者就放弃（**绝不猜人**）。
    /// </summary>
    internal static class WearGhostFix
    {
        private const string FixChannel = "ddn.mp.wearfix";

        /// <summary>搜索半径与 Together 的穿戴距离门对齐（14m）。</summary>
        private const float SearchRadius = 14f;

        private const int MaxAttempts = 3;
        private const float RetrySpacing = 1.5f;
        /// <summary>穿好之后等多久再去问主机（秒）。用户口径 2026-11：内置 1.5，不做成配置。</summary>
        private const float InitialDelay = 1.5f;

        /// <summary>报过 wearfix 之后，同 id 的物品在这段时间里不许被别的修复（孤儿物品）动。</summary>
        private const float ClaimSeconds = 12f;

        private struct Pending
        {
            public Item Item;
            public Body Body;
            public string Id;
            public Vector2 Pos;
            public float Due;
            public int Attempts;
        }

        private static readonly List<Pending> Queue = new List<Pending>();
        private static readonly Dictionary<string, float> Claimed = new Dictionary<string, float>(StringComparer.Ordinal);

        private static int _adopted;
        private static bool _disabled;
        private static float _lastLogAt = -99f;

        private static MethodInfo _forceWear;
        private static bool _forceWearLookedUp;

        internal static bool Disabled => _disabled;

        /// <summary>某个 id 刚被穿戴兜底接管过（孤儿物品修复要避开，别两套修复互相拆台）。</summary>
        internal static bool RecentlyRequested(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            float at;
            if (!Claimed.TryGetValue(id, out at)) return false;
            if (Time.unscaledTime - at <= ClaimSeconds) return true;
            Claimed.Remove(id);
            return false;
        }

        private static void Claim(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (Claimed.Count > 256) Claimed.Clear();
            Claimed[id] = Time.unscaledTime;
        }

        // ===== 客机：记录每次穿戴 =====

        [HarmonyPatch(typeof(Body), "WearWearable")]
        internal static class Body_WearWearable_RecordPatch
        {
            private static void Postfix(Body __instance, Item item)
            {
                try
                {
                    if (_disabled) return;
                    if (!MPfixPlugin.CfgWearFix.Value) return;
                    if (item == null || __instance == null) return;
                    if (!Mp.IsRunning || !Mp.IsClient) return;
                    if (Game.LocalBody != __instance) return;
                    if (item.Stats == null || !item.Stats.wearable) return;

                    Queue.Add(new Pending
                    {
                        Item = item,
                        Body = __instance,
                        Id = item.id,
                        Pos = item.transform.position,
                        Due = Time.unscaledTime + InitialDelay,   // 固定 1.5 秒（用户口径：不再做成配置）
                        Attempts = 0
                    });
                }
                catch (Exception ex)
                {
                    // 记录失败只影响这一件，绝不因此关掉整个兜底（旧版的坑：一个异常 = 本会话停手）
                    Log.Debug("记录穿戴失败（已放过）：" + ex.Message);
                }
            }
        }

        // ===== 主机：认出并穿上 =====

        internal static void RegisterHostHandlers()
        {
            if (!MpChannels.TryRegisterSender(FixChannel, HandleFix))
            {
                _disabled = true;
                MPfixPlugin.Log.Warn("[穿戴兜底] 无法注册带发送者的 wearfix 频道（CUCoreLib 版本变了？），" +
                                     "本会话不做穿戴兜底 —— 宁可不修，也不去猜是谁在穿。");
            }

            // （旧策略已删除：不再有“主机按 id 重建一件物品”这条通道 —— 它会多出一件，风险大于收益）
        }

        private static JToken HandleFix(uint senderClientId, JToken token)
        {
            try
            {
                if (!Mp.IsServer || token == null) return Fail("not-host");
                if (!MPfixPlugin.CfgWearFix.Value) return Fail("disabled");

                string id = token["id"]?.ToString() ?? string.Empty;
                float x = token["x"]?.Value<float>() ?? 0f;
                float y = token["y"]?.Value<float>() ?? 0f;
                if (string.IsNullOrEmpty(id)) return Fail("bad-id");

                // ★ 绝不回落成 Game.LocalBody：解析不到发送者就不要动，否则会把客机的物品穿到主机自己身上
                Body body = MpChannels.BodyForClient(senderClientId);
                if (body == null)
                {
                    LogOnce(true, $"[穿戴兜底] 解析不到发送者（clientId={senderClientId}）的身体，已放弃这次兜底。");
                    return Fail("no-body");
                }

                // 已经穿上了就别重复折腾
                Item already = body.GetWearable(id);
                if (already != null) return new JObject { ["ok"] = true, ["same"] = true };

                string why;
                Item target = FindCandidate(id, body, new Vector2(x, y), out why);
                if (target == null)
                {
                    LogOnce(true, $"[穿戴兜底] 主机没找到可穿的那件 '{id}'：{why}");
                    return Fail("no-item");
                }

                Utils_Create_ModItemPatch.RegisterOnHost(target.gameObject);

                bool worn = ForceWear(body, target);
                if (worn)
                {
                    // Together 在 ServerReceiver__ItemWearing 里穿完一定紧跟这一句（136245）：
                    // 把物品的新状态推给所有客机。少了它，主机的修正到不了客机。
                    Utils_Create_ModItemPatch.SyncNow(target.gameObject);
                    _adopted++;
                    Claim(id);
                    LogOnce(false, $"[穿戴兜底] 主机已认出并让 '{id}' 正确穿到玩家身上（不重建物品，累计 {_adopted} 次）");
                }

                return new JObject { ["ok"] = worn, ["why"] = worn ? "worn" : why };
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Warn("[穿戴兜底] 主机处理失败，已放过（不影响游戏）：" + ex.Message);
                return Fail("exception");
            }
        }

        private static JObject Fail(string why) => new JObject { ["ok"] = false, ["why"] = why };

        /// <summary>
        /// 在主机这一侧找“该穿上却还没穿上”的那件：
        /// 同 id + wearable + **不属于别人**（不在别的身体身上/包里）+ 距该玩家 ≤14m（与 Together 距离门一致），取最近。
        /// </summary>
        private static Item FindCandidate(string id, Body body, Vector2 reported, out string why)
        {
            why = "-";
            try
            {
                Vector2 at = body.transform.position;
                Item best = null;
                float bestDist = float.MaxValue;

                int total = 0, usable = 0;
                float nearest = float.MaxValue;
                string nearestInfo = "无";

                foreach (Item item in UnityEngine.Object.FindObjectsOfType<Item>(true))
                {
                    if (item == null || item.id != id) continue;
                    total++;

                    if (item.Stats == null || !item.Stats.wearable) continue;

                    Body owner = OwnerBody(item);
                    if (owner != null && owner != body) continue;      // 别人身上/包里那件：不抢
                    usable++;

                    float d = Vector2.Distance(item.transform.position, at);
                    if (d < nearest)
                    {
                        nearest = d;
                        Transform parent = item.transform.parent;
                        nearestInfo = $"d={d:0.0}m parent={(parent != null ? parent.name : "null")}";
                    }

                    if (d > SearchRadius) continue;                   // 太远：不是“刚穿上丢了”的那件
                    if (d < bestDist) { bestDist = d; best = item; }
                }

                if (best == null)
                {
                    why = $"同 id {total} 件 / 可穿 {usable} 件 / 最近 {nearestInfo} / 上报位置 ({reported.x:0.0},{reported.y:0.0})";
                }
                return best;
            }
            catch (Exception ex)
            {
                why = "查找异常：" + ex.Message;
                return null;
            }
        }

        /// <summary>物品挂在谁身上：父链里最近的 Body（没有就是自由物品，返回 null）。</summary>
        private static Body OwnerBody(Item item)
        {
            try
            {
                Transform t = item.transform;
                if (t == null || t.parent == null) return null;
                return t.parent.GetComponentInParent<Body>();
            }
            catch
            {
                return null;
            }
        }

        private static bool ForceWear(Body body, Item item)
        {
            try
            {
                if (!_forceWearLookedUp)
                {
                    _forceWearLookedUp = true;
                    Type itemSync = AccessTools.TypeByName("Together.ItemSync");
                    _forceWear = itemSync == null ? null : AccessTools.Method(itemSync, "Body_ForceWearWearable");
                    if (_forceWear == null)
                    {
                        MPfixPlugin.Log.Warn("[穿戴兜底] 找不到 ItemSync.Body_ForceWearWearable（Together 版本变了？），" +
                                             "穿戴兜底本会话停手。");
                        _disabled = true;
                    }
                }
                if (_forceWear == null) return false;

                _forceWear.Invoke(null, new object[] { body, item });

                // 用游戏自己的判定验证是否真的穿上了
                return body.GetWearable(item.id) == item;
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Warn("[穿戴兜底] 强制穿戴失败（不影响游戏）：" + ex.Message);
                return false;
            }
        }

        // ===== 客机：到点请求主机（失败可重试）=====

        internal static void Tick()
        {
            if (_disabled || Queue.Count == 0) return;
            if (!MPfixPlugin.CfgWearFix.Value) { Queue.Clear(); return; }

            try
            {
                if (!Mp.IsRunning || !Mp.IsClient) { Queue.Clear(); return; }

                float now = Time.unscaledTime;
                for (int i = Queue.Count - 1; i >= 0; i--)
                {
                    Pending p = Queue[i];
                    if (now < p.Due) continue;
                    Queue.RemoveAt(i);

                    if (p.Item == null || p.Body == null) continue;
                    if (p.Body.GetWearable(p.Id) != p.Item) continue;   // 已经不在身上了：要么成了，要么换了件

                    Request(p);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Tick 失败（跳过这一轮）：" + ex.Message);
            }
        }

        private static void Request(Pending p)
        {
            if (_adopted >= MPfixPlugin.CfgWearFixMax.Value)
            {
                LogOnce(true, $"[穿戴兜底] 已达本会话上限（{MPfixPlugin.CfgWearFixMax.Value} 次），暂停。");
                _disabled = true;
                return;
            }

            Claim(p.Id);        // 立刻登记“这件归穿戴兜底管”，孤儿物品修复别插手

            var payload = new JObject
            {
                ["id"] = p.Id,
                ["x"] = p.Pos.x,
                ["y"] = p.Pos.y
            };

            Mp.RequestServer(FixChannel, payload, resp =>
            {
                try
                {
                    if (resp != null && resp.Value<bool?>("ok") == true) return;    // 主机已认出并穿上

                    string why = resp?["why"]?.ToString() ?? "no-response";

                    if (p.Attempts + 1 < MaxAttempts)
                    {
                        p.Attempts++;
                        p.Due = Time.unscaledTime + RetrySpacing;
                        Queue.Add(p);
                        Log.Debug($"[穿戴兜底] '{p.Id}' 主机暂未接受（{why}），第 {p.Attempts}/{MaxAttempts - 1} 次重试。");
                        return;
                    }

                    MPfixPlugin.Log.Warn($"[穿戴兜底] '{p.Id}' 试了 {MaxAttempts} 次主机都没接受（{why}）。" +
                                         "保持现状（不做重建/掉落，避免多出一件）。");
                }
                catch (Exception ex)
                {
                    Log.Debug("请求回调失败：" + ex.Message);
                }
            });
        }


        /// <summary>重建时才会用到：先把内容物取出来，空壳才销毁。</summary>
        private static bool TryRetireGhost(Item ghost, Vector2 pos, string id)
        {
            if (ghost == null) return true;
            try
            {
                var children = new List<Transform>();
                foreach (Transform child in ghost.transform)
                {
                    if (child != null && child.GetComponent<Item>() != null) children.Add(child);
                }

                for (int i = 0; i < children.Count; i++)
                {
                    Transform child = children[i];
                    if (child == null) continue;
                    child.SetParent(null, true);
                    child.position = new Vector3(
                        pos.x + UnityEngine.Random.Range(-0.5f, 0.5f),
                        pos.y + 0.6f + i * 0.15f,
                        0f);
                    var childItem = child.GetComponent<Item>();
                    if (childItem != null) ItemPhysicsGuard.Guard(childItem, "ghost-detach");
                }
                if (children.Count > 0)
                {
                    MPfixPlugin.Log.Info($"[穿戴兜底] 幽灵 '{id}' 里有 {children.Count} 件子物品，已全部取出落地。");
                }

                if (ghost.TryGetComponent<WaterContainerItem>(out var water) && water != null
                    && water.stack != null && water.stack.Count > 0)
                {
                    MPfixPlugin.Log.Warn($"[穿戴兜底] 幽灵 '{id}' 含液体，保留不删除。");
                    return false;
                }

                int remaining = 0;
                foreach (Transform child in ghost.transform)
                {
                    if (child != null && child.GetComponent<Item>() != null) remaining++;
                }
                if (remaining > 0)
                {
                    MPfixPlugin.Log.Warn($"[穿戴兜底] 幽灵 '{id}' 仍有 {remaining} 件子物品未取出，保留不删除。");
                    return false;
                }

                UnityEngine.Object.Destroy(ghost.gameObject);
                return true;
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Warn($"[穿戴兜底] 清理幽灵 '{id}' 失败，已保留：{ex.Message}");
                return false;
            }
        }

        /// <summary>warn=true 走 Warn（默认 8 秒一条），否则 Info；避免一局刷屏。</summary>
        private static void LogOnce(bool warn, string message)
        {
            if (Time.unscaledTime - _lastLogAt < 1f) return;
            _lastLogAt = Time.unscaledTime;
            if (warn) MPfixPlugin.Log.Warn(message);
            else MPfixPlugin.Log.Info(message);
        }
    }
}