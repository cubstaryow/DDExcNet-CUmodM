using System;
using System.Reflection;
using DDEN.Shared;
using HarmonyLib;
using UnityEngine;

namespace DDEN.MPfix
{
    /// <summary>
    /// 修「打单位（储物箱、植物、动物）血量会回弹/重置」—— 与挖块同一个根因，但走的是另一条路径。
    ///
    /// 病根（反编译 mp554）：
    ///   `ServerReceiver_PlayerBodyAttack` 在主机代理客户端攻击时先置 `ForceOnlyRetrieveAttackInfo = true`，
    ///   再走 `plr.body.Attack(...)` / `ItemSync.BetterUseItem(...)`。Together 的 `Body.Attack` 前缀抓到
    ///   `LastAttackInfo` 就 `return false` —— **原版 `Body.Attack` 开头那句
    ///   `atk.damage / atk.structuralDamage *= GetRunSettingFloat("attackdamage")`（Assembly-CSharp 3546-3547）在主机从没执行过**。
    ///   而 `CalculateAttackPower` 里也没有这个倍率（只算 armPowerMult/bonusStrength/STR/爪）。
    ///   于是主机算出来的伤害：
    ///       `DamageBuilding(...)` → `num = (animal ? atk.damage : atk.structuralDamage) * armstrength * …`
    ///   比客户端本地预测那份**小了一个 attackdamage**。客户端先把血打下去一大截，主机按小值结算并回传权威血量
    ///   → 玩家看到的就是「血量掉一下又弹回去 / 重置」。
    ///
    /// 修法：给 `Together.Body_Attack_MultiplayerPatch.DamageBuilding` 加前缀，
    ///   **只在“主机 + 攻击者不是主机本地身体”时**把 `armstrength` 乘回 `attackdamage`
    ///   （主机自己的攻击本来就在 `Body.Attack` 里乘过；客机侧也乘过，所以这两侧都不动）。
    ///   与挖块那句 `BlockDamageRelax` 用的是同一个读数（读不到/非法值按 1，绝不猜）。
    /// </summary>
    internal static class BuildingDamageRelax
    {
        private const string TypeName = "Together.Body_Attack_MultiplayerPatch";
        private const string MethodName = "DamageBuilding";

        private static float _lastLogAt = -99f;
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
            MPfixPlugin.Log.Info("已启用补丁：主机打单位伤害（补乘世界伤害倍率 attackdamage）");
        }

        /// <summary>参数名必须与 Together 的方法签名一致，Harmony 按名字注入。</summary>
        private static void BeforeDamageBuilding(Body attacker, ref float armstrength)
        {
            try
            {
                if (!MPfixPlugin.Active) return;
                if (!MPfixPlugin.CfgSyncBuildingDamage.Value) return;
                if (!Mp.IsRunning || !Mp.IsServer) return;        // 只在主机结算
                if (attacker == null) return;

                Body local = Game.LocalBody;
                if (local != null && attacker == local) return;    // 主机自己：Body.Attack 已经乘过

                float mult = BlockDamageRelax.AttackDamageMultiplier();
                if (mult <= 1.0001f) return;

                armstrength *= mult;

                if (Time.unscaledTime - _lastLogAt > 30f)
                {
                    _lastLogAt = Time.unscaledTime;
                    MPfixPlugin.Log.Info($"[打单位] 主机代理客机攻击：伤害已按世界倍率 attackdamage={mult:0.##} 补乘" +
                                         "（否则客机打完血量会回弹）。");
                }
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Debug("[打单位] 补乘失败：" + ex.Message);
            }
        }
    }
}
