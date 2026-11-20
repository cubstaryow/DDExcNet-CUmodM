using System;
using System.Collections.Generic;
using DDEN.Shared;
using HarmonyLib;
using UnityEngine;

namespace DDEN.MPfix
{
    /// <summary>
    /// 物品掉落物理保险：
    /// 联机里丢东西偶发“穿墙往下掉”，根因有三类，这里统一兜住：
    ///  ① 丢出时物品还带着容器/背包里被关掉的碰撞体（原版 DropItem 只把 rigidbody 打开，不会重新开 collider）；
    ///  ② 生成/丢出点与墙块重叠（玩家贴着墙、或客机位置同步过来时插进墙里）→ 物理解算会把它挤穿；
    ///  ③ 默认离散碰撞检测 + 继承玩家速度，薄墙会被隧穿。
    ///
    /// 做法（只在这几个时机动手，且全程 try/catch）：
    ///  - 打开物品自身与子物体的所有 Collider2D；
    ///  - rigidbody 设为 Continuous（防隧穿）；
    ///  - 如果当前位置压在 Ground 层碰撞体里，逐格往上挪到不重叠为止（最多 12 次，每次 0.4m）。
    /// 不做任何删除/位移之外的改动；异常只记 Debug，不影响游戏。
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

        /// <summary>容器把物品丢出来（背包、地面容器）时也检查一次。</summary>
        [HarmonyPatch(typeof(Container), "UnloadItem")]
        internal static class Container_UnloadItem_GuardPatch
        {
            private static void Postfix(Item item)
            {
                if (item != null) Guard(item, "Container.UnloadItem");
            }
        }

        // ===== 新生成的物品（含联机重建、水晶掉落、合成产出）=====

        [HarmonyPatch(typeof(Utils), "Create", new[] { typeof(string), typeof(Vector2), typeof(float) })]
        internal static class Utils_Create_GuardPatch
        {
            private static void Postfix(ref GameObject __result)
            {
                if (__result == null) return;
                try
                {
                    Item item = __result.GetComponent<Item>();
                    if (item != null) Guard(item, "Utils.Create");
                }
                catch (Exception ex)
                {
                    Log.Debug("Create 后物理检查失败：" + ex.Message);
                }
            }
        }

        // ===== 修复 Together 的 HeldQuiet（丢东西穿墙的真凶）=====

        /// <summary>
        /// Together 的 <c>HeldQuiet</c> 在 Awake 时把物品所有 Collider2D 关掉并记下“当时的状态”，
        /// Release/OnDestroy 时再按记下的状态写回。问题是：如果挂上那一刻碰撞体本来就是关的
        /// （物品在背包里、或模组物品后加的碰撞体根本不在 _cols 里），写回之后仍然是关的，
        /// 而 rigidbody 已经被 HeldSeat 打开了 → 物品带着“无碰撞 + 有物理”掉下去，就是穿墙。
        ///
        /// 所以这里给它的 Restore 挂一个 postfix：恢复完立刻把该物品的碰撞体确实打开。
        /// </summary>
        internal static void AfterHeldQuietRestore(Component __instance)
        {
            try
            {
                if (__instance == null) return;
                Item item = __instance.GetComponent<Item>();
                if (item != null) Guard(item, "HeldQuiet.Restore");
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

        internal static void Guard(Item item, string why)
        {
            if (item == null) return;
            try
            {
                if (!MPfixPlugin.CfgPhysicsGuard.Value) return;

                Rigidbody2D rb = item.rb;
                if (rb != null)
                {
                    rb.simulated = true;
                    rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                }

                Collider2D[] cols = item.GetComponentsInChildren<Collider2D>(true);
                for (int i = 0; i < cols.Length; i++)
                {
                    if (cols[i] != null && !cols[i].enabled) cols[i].enabled = true;
                }

                Unstick(item, why);
            }
            catch (Exception ex)
            {
                Log.Debug($"物品物理保险失败（{why}）：{ex.Message}");
            }
        }

        private static void Unstick(Item item, string why)
        {
            int groundMask = LayerMask.GetMask("Ground");
            if (groundMask == 0) return;

            Vector2 pos = item.transform.position;
            Collider2D hit = Physics2D.OverlapCircle(pos, 0.2f, groundMask);
            if (hit == null) return;

            const int maxSteps = 12;
            const float step = 0.4f;
            for (int i = 0; i < maxSteps; i++)
            {
                pos += Vector2.up * step;
                item.transform.position = pos;
                if (Physics2D.OverlapCircle(pos, 0.2f, groundMask) == null)
                {
                    if (Time.unscaledTime - _lastLogAt > 2f)
                    {
                        _lastLogAt = Time.unscaledTime;
                        MPfixPlugin.Log.Info($"[物品物理] {item.id} 在 {why} 时压在墙块里，已上移 {(i + 1) * step:0.0}m 脱困");
                    }
                    return;
                }
            }

            // 12 次还没脱困：多半真的在实体里，保持原位不再硬推（避免把人/物品推飞）
            MPfixPlugin.Log.Warn($"[物品物理] {item.id} 脱困失败（{why}），保持原位不动");
        }
    }
}
