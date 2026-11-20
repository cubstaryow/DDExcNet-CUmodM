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
                if (PerfRescue.IsFrozen(item)) return;      // 卡顿应急冻住的对象：别再给它恢复碰撞
                if (!NearPlayer(item)) return;              // 只看身边的，远处不恢复碰撞

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

        /// <summary>
        /// 只看玩家身边的东西（用户口径 2026-11）：远处物品的碰撞恢复对玩家毫无意义，
        /// 却要给它们做 GetComponentsInChildren + 物理查询，纯属额外开销。没有本地身体时一律不处理。
        /// </summary>
        private static bool NearPlayer(Item item)
        {
            try
            {
                Body local = Game.LocalBody;
                if (local == null || item == null || item.transform == null) return false;
                float r = Mathf.Max(1f, MPfixPlugin.CfgGuardRadius.Value);
                Vector2 a = local.transform.position;
                Vector2 b = item.transform.position;
                float dx = a.x - b.x, dy = a.y - b.y;
                return dx * dx + dy * dy <= r * r;
            }
            catch
            {
                return false;
            }
        }

        internal static void Guard(Item item, string why)
        {
            if (item == null) return;
            try
            {
                if (!MPfixPlugin.CfgPhysicsGuard.Value) return;
                if (!IsFree(item)) return;      // ★ 关键：绝不给手持/穿戴/在包里的物品打开物理
                // 玩家亲手丢出来的东西**一律恢复碰撞**（不看距离）——这是"丢下去要正常落地"的保证，
                // 距离限制只用在被动的 HeldQuiet.Restore 那条路上（见 AfterHeldQuietRestore）。

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
            // 说明：这里**只做诊断**，不移动物品。
            // 早期版本会把卡住的物品往上挪 0.5~3m；一旦它随后被重新挂回手里/身上，
            // 位移会残留在局部坐标里 → “还穿在身上，但浮在角色上方”。
            // 现在的策略：穿墙只靠“补开碰撞体 + 恢复 HeldQuiet 状态”，位置交给物理引擎自己收敛。
            try
            {
                int mask = LayerMask.GetMask("Ground");
                if (mask == 0) return;

                Vector2 pos = item.transform.position;
                if (Physics2D.Raycast(pos, Vector2.down, 0.12f, mask).collider != null) return;   // 正常落地
                if (Physics2D.OverlapCircle(pos, 0.12f, mask) == null) return;                     // 在空中，正常

                if (Time.unscaledTime - _lastLogAt < 3f) return;
                _lastLogAt = Time.unscaledTime;
                MPfixPlugin.Log.Warn($"[物品物理] {item.id} 在 {why} 后压在实体里（不自动挪动，交给物理收敛）" +
                                     (hadDisabledCollider ? "；碰撞体已补开" : ""));
            }
            catch (Exception ex)
            {
                Log.Debug("Unstick 诊断失败：" + ex.Message);
            }
        }
    }
}
