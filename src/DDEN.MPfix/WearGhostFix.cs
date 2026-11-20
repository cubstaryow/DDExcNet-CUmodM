using System;
using System.Collections.Generic;
using System.Linq;
using DDEN.Shared;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DDEN.MPfix
{
    /// <summary>
    /// 穿戴兜底：客机穿上一件“主机不认识”的物品（水晶层本地生成的幽灵物品最典型）时，
    /// 主机会在 <c>ServerReceiver__ItemWearing</c> 里静默拒绝，于是客机本地一直穿着、
    /// 主机那边物品还在地上 → 主机看到装备反复“掉下去又回来”。
    ///
    /// 本模块只做一件事，且只做一次：客机穿上物品 1.5s 后问主机“你那边这个位置有这件 id 的物品吗？”
    ///  - 有 → 主机认识它，什么都不做（正常情况）。
    ///  - 没有 → 判定为幽灵：**取消穿戴让它掉在地上**，并请求主机按 id 在同样位置重建一个真物品，
    ///           然后销毁本地幽灵，避免重复。结果是最多闪一下。
    ///
    /// 安全策略：全程 try/catch；任何异常或通道不可用 → 立刻把本模块在本会话禁用（回退到原版行为），
    /// 并写 Warn 日志；还有每会话次数上限，超了就停手。
    /// </summary>
    internal static class WearGhostFix
    {
        private const string CheckChannel = "ddn.mp.wearcheck";
        private const string RespawnChannel = "ddn.mp.wearrespawn";

        private struct Pending
        {
            public Item Item;
            public Body Body;
            public string Id;
            public Vector2 Pos;
            public float Due;
        }

        private static readonly List<Pending> Queue = new List<Pending>();
        private static int _recovered;
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

        // ===== 主机：两个处理器 =====

        internal static void RegisterHostHandlers()
        {
            Mp.RegisterServerHandler(CheckChannel, token =>
            {
                try
                {
                    if (!Mp.IsServer || token == null) return new JObject { ["found"] = true };
                    string id = token["id"]?.ToString() ?? string.Empty;
                    float x = token["x"]?.Value<float>() ?? 0f;
                    float y = token["y"]?.Value<float>() ?? 0f;
                    return new JObject { ["found"] = HostHasItemNear(id, new Vector2(x, y)) };
                }
                catch (Exception ex)
                {
                    Log.Debug("wearcheck 失败：" + ex.Message);
                    return new JObject { ["found"] = true };   // 出错就当成“认识”，绝不误删玩家物品
                }
            });

            Mp.RegisterServerHandler(RespawnChannel, token =>
            {
                try
                {
                    if (!Mp.IsServer || token == null) return new JObject { ["ok"] = false };
                    string id = token["id"]?.ToString() ?? string.Empty;
                    float x = token["x"]?.Value<float>() ?? 0f;
                    float y = token["y"]?.Value<float>() ?? 0f;

                    GameObject go = Utils.Create(id, new Vector2(x, y), 0f);
                    if (go == null)
                    {
                        MPfixPlugin.Log.Warn($"[穿戴兜底] 主机重建失败：'{id}' 生成不出来（会保持原样，不删客机物品）");
                        return new JObject { ["ok"] = false };
                    }

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

        private static bool HostHasItemNear(string id, Vector2 pos)
        {
            if (string.IsNullOrEmpty(id)) return true;
            try
            {
                foreach (Item item in UnityEngine.Object.FindObjectsOfType<Item>())
                {
                    if (item == null || item.id != id) continue;
                    if (Vector2.Distance(item.transform.position, pos) <= 6f) return true;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("HostHasItemNear 失败：" + ex.Message);
                return true;   // 不确定就当成有
            }
            return false;
        }

        // ===== 客机：到点检查并兜底 =====

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
                    // 已经不在身上了（玩家自己脱了 / 丢了 / 被换下）→ 不管
                    if (p.Body.GetWearable(p.Id) != p.Item) continue;

                    Check(p);
                }
            }
            catch (Exception ex)
            {
                Disable("Tick 失败", ex);
            }
        }

        private static void Check(Pending p)
        {
            if (_recovered >= MPfixPlugin.CfgWearFixMax.Value)
            {
                if (Time.unscaledTime - _lastLogAt > 5f)
                {
                    _lastLogAt = Time.unscaledTime;
                    MPfixPlugin.Log.Warn($"[穿戴兜底] 已达本会话上限（{MPfixPlugin.CfgWearFixMax.Value} 次），暂停兜底。");
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

            Mp.RequestServer(CheckChannel, payload, resp =>
            {
                try
                {
                    if (resp == null) return;
                    if (resp.Value<bool?>("found") != false) return;   // 主机认识 → 正常
                    Recover(p, payload);
                }
                catch (Exception ex)
                {
                    Disable("检查回调失败", ex);
                }
            });
        }

        private static void Recover(Pending p, JObject payload)
        {
            Item item = p.Item;
            Body body = p.Body;
            if (item == null || body == null) return;

            // 1) 取消穿戴 → 掉在地上（游戏自带 API，会恢复物理与贴图）
            try { body.DropWearable(item); }
            catch (Exception ex) { Log.Debug("DropWearable 失败：" + ex.Message); }
            try
            {
                if (item.transform.parent != null) item.transform.SetParent(null, true);
            }
            catch { /* ignored */ }

            // 2) 请主机重建同 id 物品（主机侧走 Utils.Create 兜底 + 登记）
            Mp.RequestServer(RespawnChannel, payload, r2 =>
            {
                try
                {
                    if (r2 == null || r2.Value<bool?>("ok") != true) return;   // 失败就保持掉在地上，不删本地物品

                    // 3) 主机已重建 → 先保住内容物，再销毁空壳幽灵
                    bool removed = TryRetireGhost(item, p.Pos, p.Id);
                    _recovered++;

                    if (Time.unscaledTime - _lastLogAt > 1f)
                    {
                        _lastLogAt = Time.unscaledTime;
                        MPfixPlugin.Log.Info($"[穿戴兜底] 主机不认识 '{p.Id}'：已取消穿戴并请求主机重建" +
                                             $"（第 {_recovered} 次，最多闪一次；幽灵{(removed ? "已清理" : "保留")}）");
                    }
                }
                catch (Exception ex)
                {
                    Disable("重建回调失败", ex);
                }
            });
        }

        /// <summary>
        /// 清理本地幽灵前先把“内容物”全拿出来：
        ///  - 子物品（背包里装的东西）逐个脱离并落在附近，绝不跟着一起销毁；
        ///  - 含液体的容器（WaterContainerItem.stack 有内容）**不销毁**，宁可留一个本地幽灵也不丢液体；
        ///  - 只有确认空壳了才 Destroy，且销毁前再点一遍子物体。
        /// 返回是否真的清理掉了。
        /// </summary>
        private static bool TryRetireGhost(Item ghost, Vector2 pos, string id)
        {
            if (ghost == null) return true;

            try
            {
                // 1) 子物品先脱离（Container 里的东西）
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
                    MPfixPlugin.Log.Info($"[穿戴兜底] 幽灵 '{id}' 里有 {children.Count} 件子物品，已全部取出落地，未随幽灵删除。");
                }

                // 2) 液体内容无法搬运 → 保留幽灵，宁可不清理
                if (ghost.TryGetComponent<WaterContainerItem>(out var water) && water != null
                    && water.stack != null && water.stack.Count > 0)
                {
                    MPfixPlugin.Log.Warn($"[穿戴兜底] 幽灵 '{id}' 含液体内容，已保留不删除（避免丢失液体）。");
                    return false;
                }

                // 3) 再确认一次没有残留 Item 子物体
                int remaining = 0;
                foreach (Transform child in ghost.transform)
                {
                    if (child != null && child.GetComponent<Item>() != null) remaining++;
                }
                if (remaining > 0)
                {
                    MPfixPlugin.Log.Warn($"[穿戴兜底] 幽灵 '{id}' 仍有 {remaining} 件子物品未能取出，保留不删除。");
                    return false;
                }

                UnityEngine.Object.Destroy(ghost.gameObject);
                return true;
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Warn($"[穿戴兜底] 清理幽灵 '{id}' 失败，已保留（不影响游戏）：{ex.Message}");
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
