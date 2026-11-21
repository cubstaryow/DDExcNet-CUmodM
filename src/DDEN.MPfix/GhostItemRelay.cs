using System;
using System.Collections.Generic;
using System.Reflection;
using CUCoreLib.Networking;
using DDEN.Shared;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DDEN.MPfix
{
    /// <summary>
    /// 「主机点名 → 客机执行」的遗留物品清理（替代靠猜的启发式）。
    ///
    /// 为什么需要它：Together 在客机退出时走 `PlayerSavedState`：
    ///   物品 `SetParent` 到 `SAVESTATE_&lt;netBody&gt;` → `NetObjectRegistry.DetachSyncKeepMesh(go)` → 容器 `SetActive(false)`。
    /// 而 `DetachSyncKeepMesh` 是 **`if (!Net.IsServer) return;`** —— 只改主机自己的注册表，**不给客机发任何消息**。
    /// 于是客机本地那几份副本没人管：`HeldSeat.Detach/Release` 会把它 `SetParent(null)`、
    /// 恢复成 Dynamic + `simulated = true`，碰撞体却是"被抓时"关着的状态 → 一路下坠、永远 sleep 不了，
    /// 每帧都在吃物理（现场实测 82 件 / 336 刚体 / 2.55 步每帧）。
    ///
    /// 这里补上 MP 漏掉的那一步：主机在 `DetachSyncKeepMesh` 之后
    /// **把（物品 id + 坐标）攒成一批广播给所有客机**（CUCoreLib `Broadcast`），
    /// 客机收到后按 id + 坐标精确命中本地那份无主副本，调 `NetObjectRegistry.SafeDestroyObject` 删掉。
    /// 全程不用"是否在下坠""是不是还登记着"这类会误判的猜测。
    /// </summary>
    internal static class GhostItemRelay
    {
        private const string Channel = "ddn.mp.ghostitems";
        private const float FlushDelay = 1.2f;
        private const int MaxPending = 512;
        private const float MatchRadius = 1.5f;
        private const float NearLocalSkip = 1.0f;

        private struct Entry
        {
            public string Id;
            public Vector2 Pos;
        }

        private static readonly List<Entry> Pending = new List<Entry>(64);
        private static float _flushAt = -1f;
        private static bool _installed;
        private static MethodInfo _safeDestroy;

        private static int _sent, _destroyed;

        internal static void Install(Harmony harmony)
        {
            if (_installed) return;
            _installed = true;

            // 客机侧：接收主机点名
            try
            {
                MultiplayerBridge.RegisterClientHandler(Channel, OnClientReceive);
                MPfixPlugin.Log.Info($"[遗留物品] 已注册客机处理器 '{Channel}'。");
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Warn("[遗留物品] 注册客机处理器失败：" + ex.Message);
            }

            // 主机侧：MP 每把一件物品收进存档容器，就记一笔
            try
            {
                Type t = AccessTools.TypeByName("Together.NetObjectRegistry");
                MethodInfo m = t == null ? null : AccessTools.Method(t, "DetachSyncKeepMesh", new[] { typeof(GameObject) });
                if (m == null)
                {
                    MPfixPlugin.Log.Warn("[遗留物品] 找不到 DetachSyncKeepMesh（Together 版本变了？），主机点名不启用。");
                    return;
                }
                harmony.Patch(m, postfix: new HarmonyMethod(typeof(GhostItemRelay), nameof(AfterDetach)));
                MPfixPlugin.Log.Info("[遗留物品] 已挂主机侧点名（DetachSyncKeepMesh → 广播客机）。");
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Warn("[遗留物品] 挂主机侧点名失败：" + ex.Message);
            }
        }

        /// <summary>主机：物品被收进存档容器（停止同步）之后调用。</summary>
        private static void AfterDetach(GameObject go)
        {
            try
            {
                if (!MPfixPlugin.Active) return;
                if (!MPfixPlugin.CfgRelayGhostItems.Value) return;
                if (go == null) return;
                Item item = go.GetComponent<Item>();
                if (item == null) return;

                if (Pending.Count >= MaxPending) return;
                Pending.Add(new Entry { Id = item.id, Pos = go.transform.position });
                _flushAt = Time.unscaledTime + FlushDelay;
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Debug("[遗留物品] 记录被收进存档的物品失败：" + ex.Message);
            }
        }

        /// <summary>主机：攒够一批就广播（退出时一次收进去几十件，不需要一件一条消息）。</summary>
        internal static void Tick()
        {
            if (_flushAt < 0f || Time.unscaledTime < _flushAt) return;
            _flushAt = -1f;
            if (Pending.Count == 0) return;

            JArray arr = new JArray();
            for (int i = 0; i < Pending.Count; i++)
            {
                arr.Add(new JObject
                {
                    ["id"] = Pending[i].Id,
                    ["x"] = Math.Round(Pending[i].Pos.x, 2),
                    ["y"] = Math.Round(Pending[i].Pos.y, 2)
                });
            }
            int n = Pending.Count;
            Pending.Clear();

            try
            {
                bool ok = MultiplayerBridge.Broadcast(Channel, new JObject { ["items"] = arr }, false, true);
                if (ok) _sent += n;
                MPfixPlugin.Log.Info($"[遗留物品] 主机点名 {n} 件已收进存档的物品 → 广播{(ok ? "成功" : "失败（没有客机在线？）")}；累计 {_sent} 件。");
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Debug("[遗留物品] 广播失败：" + ex.Message);
            }
        }

        /// <summary>客机：按 id + 坐标精确删掉本地那份无主副本（走 Together 自己的销毁通道）。</summary>
        private static void OnClientReceive(JToken token)
        {
            try
            {
                if (!MPfixPlugin.Active) return;
                if (!MPfixPlugin.CfgRelayGhostItems.Value) return;
                if (!Mp.IsRunning || !Mp.IsClient) return;
                JArray arr = token?["items"] as JArray;
                if (arr == null || arr.Count == 0) return;

                if (_safeDestroy == null)
                {
                    Type t = AccessTools.TypeByName("Together.NetObjectRegistry");
                    _safeDestroy = t == null ? null : AccessTools.Method(t, "SafeDestroyObject", new[] { typeof(GameObject) });
                }
                if (_safeDestroy == null)
                {
                    MPfixPlugin.Log.Warn("[遗留物品] 找不到 SafeDestroyObject，客机侧清理不生效。");
                    return;
                }

                Body local = Game.LocalBody;
                Vector2 at = local != null ? (Vector2)local.transform.position : Vector2.zero;

                Item[] all = UnityEngine.Object.FindObjectsOfType<Item>(true);
                int killed = 0, skipped = 0;

                for (int i = 0; i < arr.Count; i++)
                {
                    JToken e = arr[i];
                    string id = e?["id"]?.ToString();
                    if (string.IsNullOrEmpty(id)) continue;
                    float x = e["x"]?.Value<float>() ?? 0f;
                    float y = e["y"]?.Value<float>() ?? 0f;
                    Vector2 pos = new Vector2(x, y);

                    Item hit = null;
                    float best = MatchRadius;
                    for (int k = 0; k < all.Length; k++)
                    {
                        Item it = all[k];
                        if (it == null) continue;
                        if (it.id != id) continue;
                        Transform tr = it.transform;
                        if (tr == null || tr.parent != null) continue;       // 只处理无主副本
                        float d = Vector2.Distance((Vector2)tr.position, pos);
                        if (d <= best) { best = d; hit = it; }
                    }

                    if (hit == null) continue;
                    if (local != null && Vector2.Distance((Vector2)hit.transform.position, at) < NearLocalSkip)
                    {
                        skipped++;
                        continue;
                    }

                    try
                    {
                        _safeDestroy.Invoke(null, new object[] { hit.gameObject });
                        killed++;
                    }
                    catch (Exception ex)
                    {
                        MPfixPlugin.Log.Debug("[遗留物品] 删除副本失败：" + ex.Message);
                    }
                }

                _destroyed += killed;
                if (killed > 0 || skipped > 0)
                {
                    MPfixPlugin.Log.Info($"[遗留物品] 客机侧按主机点名清理：删除 {killed} 件" +
                                         (skipped > 0 ? $"、跳过 {skipped} 件（本地玩家就在旁边）" : "") +
                                         $"；本局累计 {_destroyed} 件。");
                }
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Debug("[遗留物品] 处理主机点名失败：" + ex.Message);
            }
        }
    }
}
