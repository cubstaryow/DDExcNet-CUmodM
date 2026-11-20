using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DDEN.Shared;
using HarmonyLib;
using UnityEngine;

namespace DDEN.MPfix
{
    /// <summary>
    /// 修「一台客机掉线后，另一台客机帧数暴跌」。
    ///
    /// 实测现场（DDEN.Perf 抓到的）：客机掉线后，**剩下那台客机**上出现
    ///   物品 367 件，其中 **无主 181 件、无主且物理开着 181 件**；活跃刚体 349、碰撞体 731；
    ///   物理 **3.71 步/帧**（正常 0.83），帧数 13。
    /// 也就是：离场玩家身上的物品在这一侧被「摘下来」变成了无主物品，但 `rb.simulated` 仍然是 true
    /// （那是 Together HeldSeat 给"被拿着/被穿着"的状态），于是它们**一起持续下坠**，
    /// 每帧都要参与物理求解 —— 这就是"掉一个客机，另一台巨卡、主机没事、必须换层或全员重连才好"。
    /// 主机没事是因为主机侧是权威、离场时物品被收进存档并销毁了。
    ///
    /// 修法（只做"止血"，不删任何东西）：
    ///  在客机上定期找出「无主 + 物理开着 + 正在动（没睡着）+ 主机不认（网络注册表里没有）」的物品，
    ///  把它们**原地停下**：`rb.simulated = false` + 速度归零 + 关碰撞体。位置一点不动，物品也不删，
    ///  换层/重连本来就会把它们清掉。
    ///
    /// 保守规则（缺一不可）：
    ///  ① 只在客机、只在世界里；② 必须连续观察 ≥3 秒（不是刚被丢下的那一下）；
    ///  ③ 必须 `!rb.IsSleeping()`（已经静止落地的不碰）；④ 必须"主机不认"（注册表里查不到）；
    ///  ⑤ 本地玩家 2.5 米内不碰（脚边的东西留给玩家）；⑥ 每轮最多 40 件、每局上限可配。
    /// 注册表接口拿不到时**整体不启用**（宁可不修，也不误伤真物品）。
    /// </summary>
    internal static class StaleItemFreezer
    {
        private const float Interval = 3f;
        private const float GraceSeconds = 3f;
        private const float NearPlayerSkip = 2.5f;
        private const int PerPassBudget = 40;

        private static readonly Dictionary<int, float> Seen = new Dictionary<int, float>();
        private static float _next;
        private static int _session;
        private static float _lastLogAt = -99f;
        private static bool _disabled;

        private static MethodInfo _isRegistered;
        private static bool _lookedUp;

        // 「有人掉线」窗口：只在玩家数变少的 20 秒内密集扫描，其余时间每 30 秒兜一次底，
        // 避免我们自己每 3 秒就 FindObjectsOfType<Item>(true)（物品一多，这个扫描本身就贵）。
        private static float _startAt;
        private static float _pollNext;
        private static int _lastPlayers = -1;
        private static float _watchUntil;
        private static float _nextSafety;

        private static IDictionary _playerDict;
        private static bool _playerLookedUp;

        internal static void Tick()
        {
            if (_disabled) return;
            if (!MPfixPlugin.CfgFreezeGhostItems.Value) return;
            if (!Mp.IsRunning || !Mp.IsClient) return;
            if (!Game.InWorld) return;

            if (_startAt <= 0f) _startAt = Time.realtimeSinceStartup;
            if (Time.realtimeSinceStartup - _startAt < 3f) return;      // 跳过开局那几秒（冷启动/JIT 最贵）

            float now = Time.unscaledTime;

            // 便宜地盯玩家数（反射读一个字典的 Count，几乎零成本）
            if (now >= _pollNext)
            {
                _pollNext = now + 0.5f;
                int pc = PlayersCount();
                if (pc >= 0)
                {
                    if (_lastPlayers >= 0 && pc < _lastPlayers)
                    {
                        _watchUntil = now + 20f;
                        MPfixPlugin.Log.Info($"[遗留物品] 有玩家离开（{_lastPlayers}→{pc}），接下来 20 秒重点排查遗留物品。");
                    }
                    _lastPlayers = pc;
                }
            }

            if (now < _next) return;
            _next = now + Interval;

            bool inWatch = now < _watchUntil;
            bool safety = now >= _nextSafety;
            if (!inWatch && !safety) return;          // 平时不动，不做全场景扫描
            if (safety) _nextSafety = now + 30f;

            int cap = Mathf.Max(0, MPfixPlugin.CfgFreezeGhostMax.Value);
            if (_session >= cap)
            {
                if (!_disabled)
                {
                    _disabled = true;
                    MPfixPlugin.Log.Info($"[遗留物品] 本局冻结次数已达上限（{cap}），停手。");
                }
                return;
            }

            MethodInfo isReg = ResolveIsRegistered();
            if (isReg == null) return;      // 拿不到接口就不动，绝盲冻

            int frozen = 0;
            try
            {
                Body local = Game.LocalBody;
                Vector2 at = local != null ? (Vector2)local.transform.position : Vector2.zero;

                foreach (Item item in UnityEngine.Object.FindObjectsOfType<Item>(true))
                {
                    if (item == null) continue;
                    Transform t = item.transform;
                    if (t == null || t.parent != null) continue;      // 不是无主物品
                    Rigidbody2D rb = item.rb;
                    if (rb == null || !rb.simulated) continue;        // 物理没开，没成本
                    if (rb.IsSleeping()) continue;                    // 已静止落地：正常物品

                    int key = item.GetInstanceID();
                    float since;
                    if (!Seen.TryGetValue(key, out since)) { Seen[key] = now; continue; }
                    if (now - since < GraceSeconds) continue;         // 只认"一直在动"的

                    if (local != null && Vector2.Distance(t.position, at) < NearPlayerSkip) continue;

                    if (Registered(isReg, item.gameObject)) { Seen.Remove(key); continue; }  // 主机认得的：正常物品

                    // 冻结：只停物理，不动位置、不删对象
                    rb.velocity = Vector2.zero;
                    rb.angularVelocity = 0f;
                    rb.simulated = false;

                    Collider2D[] cols = item.GetComponentsInChildren<Collider2D>(true);
                    for (int c = 0; c < cols.Length; c++)
                    {
                        if (cols[c] != null) cols[c].enabled = false;
                    }

                    Seen.Remove(key);
                    frozen++;
                    _session++;
                    if (frozen >= PerPassBudget || _session >= cap) break;
                }

                if (Seen.Count > 1024) Seen.Clear();
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Debug("[遗留物品] 巡检失败：" + ex.Message);
            }

            if (frozen > 0 && now - _lastLogAt > 10f)
            {
                _lastLogAt = now;
                MPfixPlugin.Log.Info($"[遗留物品] 已停下 {frozen} 件「无主 + 还在下坠 + 主机不认」的物品" +
                                     $"（本局累计 {_session}）。位置没动、物品没删，换层/重连会自然清掉。");
            }
        }

        /// <summary>
        /// 解析 Together.NetObjectRegistry.IsRegistered(GameObject)。拿不到返回 null（调用方据此直接放弃）。
        /// </summary>
        private static MethodInfo ResolveIsRegistered()
        {
            if (_lookedUp) return _isRegistered;
            _lookedUp = true;
            try
            {
                Type t = AccessTools.TypeByName("Together.NetObjectRegistry");
                _isRegistered = t == null ? null : AccessTools.Method(t, "IsRegistered", new[] { typeof(GameObject) });
                if (_isRegistered == null)
                {
                    _disabled = true;
                    MPfixPlugin.Log.Warn("[遗留物品] 找不到网络注册表接口（Together 版本变了？），这条修复不启用。");
                }
            }
            catch (Exception ex)
            {
                _disabled = true;
                MPfixPlugin.Log.Warn("[遗留物品] 解析注册表接口失败，这条修复不启用：" + ex.Message);
            }
            return _isRegistered;
        }

        private static bool Registered(MethodInfo isReg, GameObject go)
        {
            try
            {
                object r = isReg.Invoke(null, new object[] { go });
                return r is bool b && b;
            }
            catch
            {
                return true;      // 查不了就当"已登记"，宁可不冻
            }
        }

        /// <summary>当前联机里的玩家数（拿不到返回 -1）。用来判断"刚刚是不是有人掉了"。</summary>
        private static int PlayersCount()
        {
            if (!_playerLookedUp)
            {
                _playerLookedUp = true;
                try
                {
                    Type t = AccessTools.TypeByName("Together.ScavPlayer");
                    FieldInfo f = t == null ? null : t.GetField("ClientIdToPlayerDict",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    _playerDict = f == null ? null : f.GetValue(null) as IDictionary;
                    
                }
                catch {  }
            }
            return _playerDict == null ? -1 : _playerDict.Count;
        }
    }
}
