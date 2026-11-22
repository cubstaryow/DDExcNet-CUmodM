using System;
using System.Reflection;
using DDEN.Shared;
using HarmonyLib;
using UnityEngine;

namespace DDEN.MPfix
{
    /// <summary>
    /// BuildingEntity melee: basic mode repairs only remote host damage fields, never knockback.
    /// Aggressive mode consumes validated client calculation from the original native attack packet.
    /// The original MP method still owns hit effects, health changes, animal reactions and drops.
    /// </summary>
    internal static class BuildingDamageRelax
    {
        private const string TypeName = "Together.Body_Attack_MultiplayerPatch";
        private const string MethodName = "DamageBuilding";

        private static bool _warned;

        internal static void Install(Harmony harmony)
        {
            Type t = AccessTools.TypeByName(TypeName);
            MethodInfo m = t == null
                ? null
                : AccessTools.Method(t, MethodName,
                    new[] { typeof(Body), typeof(AttackInfo), typeof(BuildingEntity), typeof(float), typeof(bool), typeof(Vector2) });

            if (m == null)
            {
                if (!_warned)
                {
                    _warned = true;
                    MPfixPlugin.Log.Warn($"[打单位] 找不到 {TypeName}.{MethodName}（Together 版本变了？），这条修复不启用。");
                }
                return;
            }

            harmony.Patch(m, prefix: new HarmonyMethod(typeof(BuildingDamageRelax), nameof(BeforeDamageBuilding)));
            MPfixPlugin.Log.Info("已启用补丁：主机打单位伤害重算（只补世界伤害倍率）");
        }

        /// <summary>参数名必须与 Together 的方法签名一致，Harmony 按名字注入。</summary>
        private static void BeforeDamageBuilding(Body attacker, ref AttackInfo atk, ref float armstrength)
        {
            try
            {
                UnitDamageRepair.Repair(1, attacker, ref atk, ref armstrength);
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Debug("[打单位] 补乘失败：" + ex.Message);
            }
        }
    }
}
