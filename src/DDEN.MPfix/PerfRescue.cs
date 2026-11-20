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
    /// 卡顿应急（只在**真的卡**的时候动手，平时完全不碰世界）。
    ///
    /// 用户口径（2026-11）：
    ///   · 不做任何"猜哪些物品该删"的启发式；
    ///   · 卡顿时只做两件低风险的事：① 清掉网络登记表里的**死条目**；② 把**吃物理最狠**的那些对象冻住；
    ///   · 冻结必须**绕开我们自己那条"恢复碰撞"的保险**（否则刚冻上又被 `ItemPhysicsGuard` 打开，
    ///     等于白冻 —— 这是前几版冻结无效的原因之一）。
    ///
    /// 判据只有"帧率"，与物品类型、是否在下坠、是谁的物品都无关：
    ///   连续 <see cref="TriggerSeconds"/> 秒低于 <see cref="TriggerFps"/> 才动手，最多每 5 秒一轮。
    /// </summary>
    internal static class PerfRescue
    {
        private const float CheckInterval = 1f;
        private const float ActionCooldown = 5f;
        private const int DeadPruneCap = 400;

        private static readonly HashSet<int> FrozenIds = new HashSet<int>();

        private static float _nextCheck;
        private static float _nextAction;
        private static float _frames, _time, _worst;
        private static float _slowFor;
        private static int _frozenTotal, _deadTotal, _rounds;

        /// <summary>被应急冻结的对象：ItemPhysicsGuard 必须跳过它们（不恢复碰撞、不打开物理）。</summary>
        internal static bool IsFrozen(Component c)
        {
            if (c == null || FrozenIds.Count == 0) return false;
            return FrozenIds.Contains(c.GetInstanceID());
        }

        internal static void Tick()
        {
            if (!MPfixPlugin.CfgRescueEnabled.Value) return;

            float dt = Time.unscaledDeltaTime;
            _frames++;
            _time += dt;
            if (dt > _worst) _worst = dt;

            float now = Time.unscaledTime;
            if (now < _nextCheck) return;
            _nextCheck = now + CheckInterval;

            float fps = _time > 0f ? _frames / _time : 0f;
            _frames = 0f;
            _time = 0f;
            _worst = 0f;

            if (fps > 0f && fps < MPfixPlugin.CfgRescueFps.Value) _slowFor += CheckInterval;
            else _slowFor = 0f;

            if (_slowFor < MPfixPlugin.CfgRescueSeconds.Value) return;
            if (now < _nextAction) return;
            _nextAction = now + ActionCooldown;
            _rounds++;

            int dead = PruneDeadEntries();
            int frozen = FreezeWorst();

            _deadTotal += dead;
            _frozenTotal += frozen;
            if (dead > 0 || frozen > 0)
            {
                MPfixPlugin.Log.Info($"[卡顿应急] 帧数 {fps:0}（持续 {_slowFor:0}s）：清掉死条目 {dead} 条、冻住吃物理的对象 {frozen} 个" +
                                     $"（本局累计 死条目 {_deadTotal} / 冻结 {_frozenTotal}，第 {_rounds} 轮）。");
            }
            _slowFor = 0f;
        }

        /// <summary>清 SyncRegistry 里 key 已销毁的条目（纯垃圾，还会让每帧遍历变贵）。</summary>
        private static int PruneDeadEntries()
        {
            try
            {
                IDictionary reg = Registry();
                if (reg == null) return 0;
                var dead = new List<object>(16);
                foreach (DictionaryEntry e in reg)
                {
                    var go = e.Key as UnityEngine.Object;
                    if (go == null) dead.Add(e.Key);
                    if (dead.Count >= DeadPruneCap) break;
                }
                int removed = 0;
                for (int i = 0; i < dead.Count; i++)
                {
                    try { reg.Remove(dead[i]); removed++; } catch { /* ignored */ }
                }
                return removed;
            }
            catch { return 0; }
        }

        /// <summary>
        /// 把"无主 + 物理开着 + 还在动"的对象冻成运动学：不再参与重力/求解，位置不动、对象不删。
        /// 只按帧率触发，不判断"该不该删"。
        /// </summary>
        private static int FreezeWorst()
        {
            int budget = Mathf.Max(1, MPfixPlugin.CfgRescuePerPass.Value);
            int frozen = 0;
            try
            {
                foreach (Item item in UnityEngine.Object.FindObjectsOfType<Item>(true))
                {
                    if (item == null) continue;
                    if (frozen >= budget) break;

                    Transform t = item.transform;
                    if (t == null || t.parent != null) continue;      // 只处理无主对象（被拿着的本来就不吃物理）

                    Rigidbody2D rb = item.rb;
                    if (rb == null || !rb.simulated) continue;
                    if (rb.bodyType != RigidbodyType2D.Dynamic) continue;
                    if (rb.IsSleeping()) continue;                    // 已经睡着的不花钱，不动它

                    int id = item.GetInstanceID();
                    if (FrozenIds.Contains(id)) continue;

                    rb.velocity = Vector2.zero;
                    rb.angularVelocity = 0f;
                    if (rb.gravityScale != 0f) rb.gravityScale = 0f;
                    rb.bodyType = RigidbodyType2D.Kinematic;          // 运动学：不参与求解，但位置照旧
                    rb.simulated = true;

                    Collider2D[] cols = item.GetComponentsInChildren<Collider2D>(true);
                    for (int i = 0; i < cols.Length; i++)
                    {
                        if (cols[i] != null) cols[i].enabled = false;
                    }

                    FrozenIds.Add(id);                                // 让 ItemPhysicsGuard 之后别再把碰撞打开
                    frozen++;
                }

                if (FrozenIds.Count > 4096) FrozenIds.Clear();
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Debug("[卡顿应急] 冻结失败：" + ex.Message);
            }
            return frozen;
        }

        private static IDictionary _registry;
        private static bool _registryLookedUp;

        private static IDictionary Registry()
        {
            if (_registryLookedUp) return _registry;
            _registryLookedUp = true;
            try
            {
                Type t = AccessTools.TypeByName("Together.NetObjectRegistry");
                FieldInfo f = t == null ? null : t.GetField("SyncRegistry",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                _registry = f == null ? null : f.GetValue(null) as IDictionary;
            }
            catch { _registry = null; }
            return _registry;
        }
    }
}
