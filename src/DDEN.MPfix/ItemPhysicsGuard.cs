using System;
using DDEN.Shared;
using HarmonyLib;
using UnityEngine;

namespace DDEN.MPfix
{
    /// <summary>
    /// 物品物理保险（只处理**玩家亲手丢出**的物品）。
    ///
    /// 病根（Together 的 `HeldQuiet`）：物品被拿在手里/穿上时，`HeldQuiet` 会把所有 `Collider2D` 关掉
    /// 并记下"当时的状态"；`Restore()` 再把记下的状态写回去。若挂上那一刻碰撞体本来就是关的
    /// （在背包里 / 模组物品后加的碰撞体不在名单里），写回后仍然是关的，而 `HeldSeat` 已经把
    /// `rb.simulated = true` → **无碰撞 + 有物理 = 穿墙往下掉**。
    ///
    /// 修法（2026-11 收紧后的最终形态）：
    ///   **只在 `Body.DropItem` / `Body.DropWearable`（真正的丢出动作）之后**，把被关掉的碰撞体打开。
    ///   · **不再挂 `HeldQuiet.Restore`**（用户口径：被动恢复范围那套去掉，只处理玩家丢弃的物品）；
    ///   · 不碰 `rb.simulated`、不设 `CollisionDetectionMode.Continuous`、不移动物品
    ///     （这几样是"物品抽搐/飞天"的来源：物品正压在实体里时打开物理会被解算器弹飞）；
    ///   · 与实体重叠时本次跳过，不做持续恢复。
    ///   位置交给物理引擎，物品从不被挪动。
    /// </summary>
    internal static class ItemPhysicsGuard
    {
        private static LogChannel Log => MPfixPlugin.Log;

        [HarmonyPatch(typeof(Body), nameof(Body.DropItem), new[] { typeof(int) })]
        internal static class Body_DropItem_GuardPatch
        {
            /// <summary>`Body.DropItem(int)` 是 void（没有返回值），所以在调用前记下那一格里的物品。</summary>
            private static void Prefix(Body __instance, int slot, out Item __state)
            {
                __state = null;
                if (!MPfixPlugin.Active || !MPfixPlugin.CfgPhysicsGuard.Value) return;
                try { __state = __instance != null ? __instance.GetItem(slot) : null; }
                catch { __state = null; }
            }

            private static void Postfix(Item __state)
            {
                Guard(__state, "DropItem");
            }
        }

        [HarmonyPatch(typeof(Body), nameof(Body.DropWearable), new[] { typeof(Item) })]
        internal static class Body_DropWearable_GuardPatch
        {
            private static void Postfix(Item item) => Guard(item, "DropWearable");
        }

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

        /// <summary>物品当前是否和实体（非地面、非自身）重叠 —— 重叠时打开碰撞体必然被弹飞，所以跳过。</summary>
        private static readonly Collider2D[] OverlapBuffer = new Collider2D[32];
        private static bool OverlappingSolid(Item item)
        {
            try
            {
                Vector2 pos = item.transform.position;
                int count = Physics2D.OverlapCircleNonAlloc(pos, 0.12f, OverlapBuffer);
                if (count == OverlapBuffer.Length) return true;
                int ground = LayerMask.NameToLayer("Ground");
                for (int i = 0; i < count; i++)
                {
                    Collider2D hit = OverlapBuffer[i];
                    OverlapBuffer[i] = null;
                    if (hit == null || hit.isTrigger || hit.transform == null || hit.gameObject.layer == ground) continue;
                    if (hit.transform == item.transform || hit.transform.IsChildOf(item.transform)) continue;
                    return true;
                }
                return false;
            }
            catch
            {
                return true;    // 判不了就不动
            }
            finally { Array.Clear(OverlapBuffer, 0, OverlapBuffer.Length); }
        }

        internal static void Guard(Item item, string why)
        {
            if (item == null) return;
            try
            {
                if (!MPfixPlugin.Active) return;
                if (!MPfixPlugin.CfgPhysicsGuard.Value) return;
                if (!IsFree(item)) return;              // 绝不给手持/穿戴/在包里的物品打开物理
                if (OverlappingSolid(item)) return;     // 压在实体里：打开碰撞会被弹飞

                Collider2D[] cols = item.GetComponentsInChildren<Collider2D>(true);
                bool turnedOn = false;
                for (int i = 0; i < cols.Length; i++)
                {
                    if (cols[i] != null && !cols[i].enabled)
                    {
                        cols[i].enabled = true;
                        turnedOn = true;
                    }
                }

                if (turnedOn)
                {
                    Log.Debug($"[物品物理] {item.id} 在 {why} 后补开碰撞体（不移动、不改刚体）");
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"物品物理保险失败（{why}）：{ex.Message}");
            }
        }
    }
}
