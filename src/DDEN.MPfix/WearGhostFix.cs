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
    /// 穿戴兜底（联机）：
    /// 客机穿上一件物品，主机那边 <c>ServerReceiver__ItemWearing</c> 没认下来（TryGetItem / 距离 / 归属判定失败），
    /// 结果主机眼里这件物品还在地上甚至掉下去，客机身上却有 —— 就是“装备反复掉下又回来”。
    ///
    /// 本模块**不重建物品**：客机穿上 1.5s 后通过 CUCoreLib 频道把 {id, 位置} 发给主机，
    /// 主机按 id + 位置找到那件物品（自己那边的实体），补登记网络身份，然后
    /// <c>Together.ItemSync.Body_ForceWearWearable</c> 让它正确穿到该玩家身上。
    /// 双方看到的都是同一件物品，不掉落、不复制。
    ///
    /// 只有在主机确实找不到这件物品时，才（默认关闭的）旧策略可选用“掉地 + 重建”。
    /// 安全策略：全程 try/catch；异常/通道不可用即本会话禁用并 Warn；每局有次数上限。
    /// </summary>
    internal static class WearGhostFix
    {
        private const string FixChannel = "ddn.mp.wearfix";

        private struct Pending
        {
            public Item Item;
            public Body Body;
            public string Id;
            public Vector2 Pos;
            public float Due;
        }

        private static readonly List<Pending> Queue = new List<Pending>();
        private static int _adopted;
        private static bool _disabled;
        private static float _lastLogAt = -99f;

        internal static bool Disabled => _disabled;

        // ===== 客机：记录每次穿戴 =====

        [HarmonyPatch(typeof(Body), "WearWearable")]
        internal static class Body_WearWearable_RecordPatch
        {
            private static void Postfix(Body __instance, Item item)
            {
                if (_disabled) return;
                if (!MPfixPlugin.CfgWearFix.Value) return;
                if (item == null || __instance == null) return;

                try
                {
                    if (!Mp.IsRunning || !Mp.IsClient) return;
                    if (Game.LocalBody != __instance) return;
                    if (!item.Stats.wearable) return;

                    Queue.Add(new Pending
                    {
                        Item = item,
                        Body = __instance,
                        Id = item.id,
                        Pos = item.transform.position,
                        Due = Time.unscaledTime + Mathf.Max(0.3f, MPfixPlugin.CfgWearGrace.Value)
                    });
                }
                catch (Exception ex)
                {
                    Disable("记录穿戴失败", ex);
                }
            }
        }

        // ===== 主机：认出并穿上（带 sender 的注册，失败则退回无 sender 版本）=====

        internal static void RegisterHostHandlers()
        {
            bool ok = TryRegisterSenderAware();
            if (!ok)
            {
                MPfixPlugin.Log.Warn("[穿戴兜底] 无法注册带发送者的频道处理器，退回无 sender 版本（只能修主机自己的穿戴）。");
                Mp.RegisterServerHandler(FixChannel, token => HandleFix(0u, token));
            }

            // 可选旧策略（默认关闭时才用）：主机按 id 重建一件物品
            Mp.RegisterServerHandler("ddn.mp.wearrespawn", token =>
            {
                try
                {
                    if (!Mp.IsServer || token == null) return new JObject { ["ok"] = false };
                    string id = token["id"]?.ToString() ?? string.Empty;
                    float x = token["x"]?.Value<float>() ?? 0f;
                    float y = token["y"]?.Value<float>() ?? 0f;

                    GameObject go = Utils.Create(id, new Vector2(x, y), 0f);
                    if (go == null) return new JObject { ["ok"] = false };
                    Utils_Create_ModItemPatch.RegisterOnHost(go);
                    return new JObject { ["ok"] = true };
                }
                catch (Exception ex)
                {
                    Log.Debug("wearrespawn 失败：" + ex.Message);
                    return new JObject { ["ok"] = false };
                }
            });
        }

        private static bool TryRegisterSenderAware()
        {
            try
            {
                Type bridge = AccessTools.TypeByName("CUCoreLib.Networking.MultiplayerBridge");
                if (bridge == null) return false;

                MethodInfo reg = null;
                foreach (MethodInfo m in bridge.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (m.Name != "RegisterServerHandler") continue;
                    ParameterInfo[] ps = m.GetParameters();
                    if (ps.Length == 2 && ps[0].ParameterType == typeof(string) && ps[1].ParameterType != typeof(Func<JToken, JToken>))
                    {
                        reg = m;
                        break;
                    }
                }
                if (reg == null) return false;

                ParameterInfo handlerParam = reg.GetParameters()[1];
                MethodInfo target = typeof(WearGhostFix).GetMethod(nameof(HandleFixEntry),
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (target == null) return false;

                Delegate del = Delegate.CreateDelegate(handlerParam.ParameterType, target);
                reg.Invoke(null, new object[] { FixChannel, del });
                MPfixPlugin.Log.Info("[穿戴兜底] 已注册带发送者的 wearfix 频道（主机可识别是谁在穿）。");
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("sender-aware 注册失败：" + ex.Message);
                return false;
            }
        }

        /// <summary>必须与 Func&lt;uint, JToken, JToken&gt; 的签名一致。</summary>
        private static JToken HandleFixEntry(uint senderClientId, JToken token)
        {
            return HandleFix(senderClientId, token);
        }

        private static JToken HandleFix(uint senderClientId, JToken token)
        {
            try
            {
                if (!Mp.IsServer || token == null) return Fail();

                string id = token["id"]?.ToString() ?? string.Empty;
                float x = token["x"]?.Value<float>() ?? 0f;
                float y = token["y"]?.Value<float>() ?? 0f;
                if (string.IsNullOrEmpty(id)) return Fail();

                Body body = BodyForClient(senderClientId) ?? Game.LocalBody;
                if (body == null) return Fail();

                // 已经穿上了就别重复折腾
                Item already = body.GetWearable(id);
                if (already != null) return new JObject { ["ok"] = true, ["same"] = true };

                Item target = FindCandidate(id, new Vector2(x, y), body);
                if (target == null) return Fail();

                Utils_Create_ModItemPatch.RegisterOnHost(target.gameObject);

                bool worn = ForceWear(body, target);
                if (worn && Time.unscaledTime - _lastLogAt > 1f)
                {
                    _lastLogAt = Time.unscaledTime;
                    MPfixPlugin.Log.Info($"[穿戴兜底] 主机已认出并让 '{id}' 正确穿到玩家身上（不重建物品）");
                }
                return new JObject { ["ok"] = worn };
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Warn("[穿戴兜底] 主机处理失败，已放过（不影响游戏）：" + ex.Message);
                return Fail();
            }
        }

        private static JObject Fail() => new JObject { ["ok"] = false };

        private static Body BodyForClient(uint clientId)
        {
            try
            {
                Type scav = AccessTools.TypeByName("Together.ScavPlayer");
                if (scav == null) return null;
                MethodInfo get = AccessTools.Method(scav, "GetBodyFromClientId");
                if (get == null) return null;

                Type idType = get.GetParameters()[0].ParameterType;
                object knet = Activator.CreateInstance(idType, (ushort)clientId);
                return get.Invoke(null, new[] { knet }) as Body;
            }
            catch (Exception ex)
            {
                Log.Debug("解析发送者 Body 失败：" + ex.Message);
                return null;
            }
        }

        private static Item FindCandidate(string id, Vector2 pos, Body owner)
        {
            try
            {
                Item best = null;
                float bestDist = float.MaxValue;

                foreach (Item item in UnityEngine.Object.FindObjectsOfType<Item>(true))
                {
                    if (item == null || item.id != id) continue;
                    if (!item.Stats.wearable) continue;

                    // 只认“自由物品”（在地上、不在任何人身上/包里），避免把别人的装备抢过来
                    Transform parent = item.transform.parent;
                    if (parent != null) continue;
                    if (item.TryGetParentContainer(out var container) && container != null) continue;

                    float d = Vector2.Distance(item.transform.position, pos);
                    if (d > 4f) continue;      // 4m 内才算“刚买到/刚掉在脚边的那件”
                    if (d < bestDist) { bestDist = d; best = item; }
                }
                return best;
            }
            catch (Exception ex)
            {
                Log.Debug("查找物品失败：" + ex.Message);
                return null;
            }
        }

        private static bool ForceWear(Body body, Item item)
        {
            try
            {
                Type itemSync = AccessTools.TypeByName("Together.ItemSync");
                MethodInfo method = itemSync == null ? null : AccessTools.Method(itemSync, "Body_ForceWearWearable");
                if (method == null)
                {
                    MPfixPlugin.Log.Warn("[穿戴兜底] 找不到 ItemSync.Body_ForceWearWearable，跳过（不影响游戏）。");
                    return false;
                }

                method.Invoke(null, new object[] { body, item });

                // 用游戏自己的判定验证是否真的穿上了
                return body.GetWearable(item.id) == item;
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Warn("[穿戴兜底] 强制穿戴失败（不影响游戏）：" + ex.Message);
                return false;
            }
        }

        // ===== 客机：到点请求主机 =====

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
                    if (p.Body.GetWearable(p.Id) != p.Item) continue;   // 已经不在身上了

                    Request(p);
                }
            }
            catch (Exception ex)
            {
                Disable("Tick 失败", ex);
            }
        }

        private static void Request(Pending p)
        {
            if (_adopted >= MPfixPlugin.CfgWearFixMax.Value)
            {
                if (Time.unscaledTime - _lastLogAt > 5f)
                {
                    _lastLogAt = Time.unscaledTime;
                    MPfixPlugin.Log.Warn($"[穿戴兜底] 已达本会话上限（{MPfixPlugin.CfgWearFixMax.Value} 次），暂停。");
                }
                _disabled = true;
                return;
            }

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
                    if (resp != null && resp.Value<bool?>("ok") == true)
                    {
                        _adopted++;
                        return;      // 主机已认出并穿上：本地什么都不用做
                    }

                    if (!MPfixPlugin.CfgWearRebuild.Value)
                    {
                        MPfixPlugin.Log.Warn($"[穿戴兜底] 主机找不到 '{p.Id}'，按配置不重建（保持现状）。");
                        return;
                    }

                    Rebuild(p, payload);
                }
                catch (Exception ex)
                {
                    Disable("请求回调失败", ex);
                }
            });
        }

        /// <summary>可选旧策略（默认关闭）：主机确实没有这件物品时才用，掉地 + 请求重建。</summary>
        private static void Rebuild(Pending p, JObject payload)
        {
            Item item = p.Item;
            Body body = p.Body;
            if (item == null || body == null) return;
            if (body.GetWearable(p.Id) != item) return;

            try { body.DropWearable(item); }
            catch (Exception ex) { Log.Debug("DropWearable 失败：" + ex.Message); }

            Mp.RequestServer("ddn.mp.wearrespawn", payload, r2 =>
            {
                try
                {
                    if (r2 == null || r2.Value<bool?>("ok") != true) return;
                    TryRetireGhost(item, p.Pos, p.Id);
                    _adopted++;
                    MPfixPlugin.Log.Info($"[穿戴兜底] 主机没有 '{p.Id}'，已按配置重建（第 {_adopted} 次）");
                }
                catch (Exception ex)
                {
                    Disable("重建回调失败", ex);
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

        private static void Disable(string why, Exception ex)
        {
            if (_disabled) return;
            _disabled = true;
            Queue.Clear();
            MPfixPlugin.Log.Warn($"[穿戴兜底] 已回退（{why}，本会话不再介入）：{ex?.Message}");
        }
    }
}
