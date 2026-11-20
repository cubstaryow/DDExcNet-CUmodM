using System;
using System.Collections.Generic;
using DDEN.Shared;
using HarmonyLib;
using UnityEngine;

namespace DDEN.MPfix
{
    /// <summary>
    /// 物品掉落物理保险（**只处理“自由物品”**）。
    ///
    /// 联机里丢东西偶发“穿墙往下掉”，根因：
    ///  ① Together 的 <c>HeldQuiet</c> 在 Awake 时把物品所有 Collider2D 关掉并记下“当时状态”，
    ///     Release/OnDestroy 时写回；若挂上那一刻本来就是关的（物品在背包里），写回后仍然没有碰撞，
    ///     而 <c>HeldSeat</c> 已经把 <c>rb.simulated</c> 打开 → 无碰撞 + 有物理 = 穿墙下落；
    ///  ② 丢出/生成点与墙块重叠（贴墙丢、客机位置同步插进墙里）；
    ///  ③ 默认离散碰撞检测 + 继承玩家速度，薄墙会被隧穿。
    ///
    /// 早期版本对所有物品无条件 `rb.simulated = true` + 打开碰撞体，副作用是
    /// **拿在手里 / 穿在身上 / 装在背包里的物品也被打开物理**，表现为“东西自己往下掉”。
    /// 现在收紧为三条铁律：
    ///  1. 只处理 <see cref="IsFree"/> 为真的物品（transform 无父级、也不在任何 Container 里）；
    ///  2. 只“打开”不“改写”：collider 关着的才打开，rb 没模拟的才打开；
    ///  3. 脱困动作（上移）有 1 秒冷却，且必须先判定“脚下没有地板支撑”才动，绝不动正常躺在地上的物品。
    /// </summary>
    internal static class ItemPhysicsGuard
    {
        private static float _lastLogAt = -99f;
        private static readonly Dictionary<int, float> NudgeAt = new Dictionary<int, float>();
        private static readonly Dictionary<int, int> NudgeCount = new Dictionary<int, int>();
        private static readonly HashSet<int> GiveUp = new HashSet<int>();
        private static int _nudgesThisSession;
        private static bool _disabledByNoise;

        // ===== 丢出物品 =====

        [HarmonyPatch(typeof(Body), "DropItem", new[] { typeof(Item) })]
        internal static class Body_DropItem_GuardPatch
        {
            private static void Postfix(Item item) => Guard(item, "DropItem");
        }

        [HarmonyPatch(typeof(Body), "DropWearable")]
        internal static class Body_DropWearable_GuardPatch
        {
            private static void Postfix(Item item) => Guard(item, "DropWearable");
        }

        // 说明：**不再**挂钩 Container.UnloadItem / Utils.Create。
        // 这两个时机在一局里会被调用成百上千次（拾取、拖拽、装包、水晶生成、合成产出…），
        // 任何一次把物品的物理打开，都可能让“本该静止/在包里的东西”自己动起来 —— 那正是
        // 用户报的“主机物品一直掉落”。物理保险只保留在**真正的丢出动作**上（下面两个）。

        // ===== Together.HeldQuiet.Restore 之后：只补碰撞体，不动 rb、不移位 =====

        internal static void AfterHeldQuietRestore(Component __instance)
        {
            try
            {
                if (__instance == null) return;
                Item item = __instance.GetComponent<Item>();
                if (item == null) return;
                if (!MPfixPlugin.CfgPhysicsGuard.Value) return;
                if (!IsFree(item)) return;

                Collider2D[] cols = item.GetComponentsInChildren<Collider2D>(true);
                for (int i = 0; i < cols.Length; i++)
                {
                    if (cols[i] != null && !cols[i].enabled) cols[i].enabled = true;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("HeldQuiet 修复失败：" + ex.Message);
            }
        }

        internal static void InstallHeldQuietRepair(Harmony harmony)
        {
            try
            {
                Type type = AccessTools.TypeByName("Together.HeldQuiet");
                if (type == null)
                {
                    Log.Debug("未找到 Together.HeldQuiet（版本可能已改），跳过该修复。");
                    return;
                }
                System.Reflection.MethodBase target = AccessTools.Method(type, "Restore");
                if (target == null)
                {
                    Log.Debug("未找到 HeldQuiet.Restore，跳过。");
                    return;
                }
                harmony.Patch(target, postfix: new HarmonyMethod(typeof(ItemPhysicsGuard), nameof(AfterHeldQuietRestore)));
                MPfixPlugin.Log.Info("已启用补丁：HeldQuiet 释放后恢复物品碰撞（修“丢东西穿墙”）");
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Warn("HeldQuiet 补丁安装失败（不影响游戏）：" + ex.Message);
            }
        }

        // ===== 实现 =====

        /// <summary>只对“自由物品”做物理保险：拿在手里 / 穿在身上 / 装在容器里的，一律不碰。</summary>
        private static bool IsFree(Item item)
        {
            try
            {
                if (item == null) return false;
                if (item.transform == null) return false;
                if (item.transform.parent != null) return false;      // 挂在手 / 肢体 / 容器下

                Container container;
                if (item.TryGetParentContainer(out container) && container != null) return false;

                return true;
            }
            catch
            {
                return false;    // 判不了就当“不自由”，宁可不处理
            }
        }

        internal static void Guard(Item item, string why)
        {
            if (item == null) return;
            try
            {
                if (!MPfixPlugin.CfgPhysicsGuard.Value) return;
                if (!IsFree(item)) return;      // ★ 关键：绝不给手持/穿戴/在包里的物品打开物理

                Rigidbody2D rb = item.rb;
                if (rb != null)
                {
                    if (!rb.simulated) rb.simulated = true;                       // 只打开
                    if (rb.collisionDetectionMode != CollisionDetectionMode2D.Continuous)
                        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                }

                bool turnedOn = false;
                Collider2D[] cols = item.GetComponentsInChildren<Collider2D>(true);
                for (int i = 0; i < cols.Length; i++)
                {
                    if (cols[i] != null && !cols[i].enabled)
                    {
                        cols[i].enabled = true;
                        turnedOn = true;
                    }
                }

                Unstick(item, why, turnedOn);
            }
            catch (Exception ex)
            {
                Log.Debug($"物品物理保险失败（{why}）：{ex.Message}");
            }
        }

        /// <summary>
        /// 只有“脚下没有地板 + 身体压在实体里”才向上脱困，且同一物品 1 秒内只动一次。
        /// 正常躺在地上的物品（向下 0.12m 能探到 Ground）一律不动。
        /// </summary>
        private static void Unstick(Item item, string why, bool hadDisabledCollider)
        {
            int mask = LayerMask.GetMask("Ground");
            if (mask == 0) return;

            Vector2 pos = item.transform.position;

            // ① 脚下有地板 → 正常躺着/正落下中，不干预
            if (Physics2D.Raycast(pos, Vector2.down, 0.12f, mask).collider != null) return;

            // ② 没有地板支撑，且当前位置压在实体里 → 判定为插进墙/地板
            if (Physics2D.OverlapCircle(pos, 0.12f, mask) == null) return;

            int key = item.GetInstanceID();
            if (GiveUp.Contains(key)) return;                     // 这件物品已经放弃处理

            float now = Time.unscaledTime;
            float last;
            if (NudgeAt.TryGetValue(key, out last) && now - last < 1f) return;   // 冷却，避免来回跳
            if (NudgeAt.Count > 2048) { NudgeAt.Clear(); NudgeCount.Clear(); }
            NudgeAt[key] = now;

            int count;
            NudgeCount.TryGetValue(key, out count);
            count++;
            NudgeCount[key] = count;
            if (count >= 3)
            {
                GiveUp.Add(key);
                MPfixPlugin.Log.Warn($"[物品物理] {item.id} 连续被挪动 {count} 次，判定位置异常，已放弃干预（保持原样）");
                return;
            }

            _nudgesThisSession++;
            if (_nudgesThisSession > 200 && !_disabledByNoise)
            {
                _disabledByNoise = true;
                MPfixPlugin.Log.Warn("[物品物理] 本次会话挪动次数过多（>200），已整体停用物理保险，避免影响游戏。");
                return;
            }
            if (_disabledByNoise) return;

            float startY = pos.y;
            bool escaped = false;
            for (int i = 0; i < 6; i++)
            {
                pos += Vector2.up * 0.5f;
                item.transform.position = pos;

                if (Physics2D.Raycast(pos, Vector2.down, 0.12f, mask).collider != null) { escaped = true; break; }  // 已经踩到地板
                if (Physics2D.OverlapCircle(pos, 0.12f, mask) == null) { escaped = true; break; }                   // 已经脱出实体
            }

            if (!escaped)
            {
                // 6 步还出不来：多半整块区域都是实体，**退回原位**并长时间不再尝试，
                // 否则会变成“每秒往上跳一下”——那才是“物品一直掉落/乱飞”的来源。
                item.transform.position = new Vector2(pos.x, startY);
                NudgeAt[key] = now + 60f;
                MPfixPlugin.Log.Warn($"[物品物理] {item.id} 在 {why} 处陷得比较深，已保留原位不再挪动（避免反复位移）");
                return;
            }

            float moved = pos.y - startY;
            if (moved > 0.01f && Time.unscaledTime - _lastLogAt > 2f)
            {
                _lastLogAt = Time.unscaledTime;
                MPfixPlugin.Log.Info($"[物品物理] {item.id} 在 {why} 时卡在实体里，已上移 {moved:0.0}m 脱困" +
                                     (hadDisabledCollider ? "（同时补开了碰撞体）" : ""));
            }
        }
    }
}
