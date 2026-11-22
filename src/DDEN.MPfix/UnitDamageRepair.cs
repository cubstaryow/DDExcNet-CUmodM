using System;
using System.Reflection;
using DDEN.Shared;
using HarmonyLib;
using UnityEngine;

namespace DDEN.MPfix
{
    /// <summary>
    /// 主机替客机结算**近战伤害**时补齐世界伤害倍率（基础模式，只此一件事）。
    ///
    /// 原版 `Body.Attack` 会把 `atk.damage/atk.structuralDamage *= attackdamage`；主机在
    /// `ServerReceiver_PlayerBodyAttack` 里先置 `ForceOnlyRetrieveAttackInfo=true`，Together 的
    /// `Body.Attack` 前缀抓完 `LastAttackInfo` 就 return false —— 3546/3547 在主机从未执行。
    /// 这里复制一份 AttackInfo，**只补伤害字段**，不动击退力量：世界倍率不应该放大击退。
    ///
    /// 2026-11 起不再有「激进上报」分支：原 `AttackReport` 随攻击包附加 24 字节上报的整条链路已剔除
    /// （备份见 `decomp/backup-aggressive-wear/`）。Together 仍只通过反射访问，不做程序集引用。
    /// </summary>
    internal static class UnitDamageRepair
    {
        private static Type _attackType;
        private static MethodInfo _memberwise;

        /// <summary>branch 1 = 建筑/单位（SyncBuildingDamage），2 = 角色与肢体（SyncBodyDamage）。</summary>
        internal static bool Enabled(int branch) => branch == 1
            ? MPfixPlugin.CfgSyncBuildingDamage.Value : MPfixPlugin.CfgSyncBodyDamage.Value;

        internal static void Install(Harmony harmony)
        {
            try
            {
                _attackType = AccessTools.TypeByName("Together.Body_Attack_MultiplayerPatch");
                if (_attackType == null) throw new MissingMemberException("Body_Attack_MultiplayerPatch");
                MethodInfo bodyDamage = AccessTools.Method(_attackType, "DoPVPMeleeDamage");
                if (bodyDamage == null) throw new MissingMethodException("DoPVPMeleeDamage");

                harmony.Patch(bodyDamage, prefix: new HarmonyMethod(typeof(UnitDamageRepair), nameof(BeforeBodyDamage)));
                MPfixPlugin.ClearPatchError("身体/肢体伤害重算");
                MPfixPlugin.Log.Info("已启用补丁：身体/肢体近战伤害重算（只补世界伤害倍率）");
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Warn("身体/肢体伤害补丁安装失败：" + ex.Message);
                MPfixPlugin.NotePatchError("身体/肢体伤害重算", ex.Message);
            }
        }

        internal static void Repair(int branch, Body attacker, ref AttackInfo atk, ref float power)
        {
            if (!MPfixPlugin.Active || !Enabled(branch)) return;
            if (!Mp.IsRunning || !Mp.IsServer) return;
            if (attacker == null || attacker == Game.LocalBody || atk == null) return;

            float multiplier = BlockDamageRelax.AttackDamageMultiplier();
            if (Math.Abs(multiplier - 1f) < 0.0001f) return;

            AttackInfo copy = Copy(atk);
            copy.damage *= multiplier;
            copy.structuralDamage *= multiplier;
            atk = copy;
        }

        private static AttackInfo Copy(AttackInfo atk)
        {
            _memberwise ??= AccessTools.Method(typeof(object), "MemberwiseClone");
            return (AttackInfo)_memberwise.Invoke(atk, null);
        }

        private static void BeforeBodyDamage(Body attacker, ref AttackInfo atk, ref float armstrength)
        {
            try { Repair(2, attacker, ref atk, ref armstrength); }
            catch (Exception ex) { MPfixPlugin.Log.Debug("身体伤害重算失败：" + ex.Message); }
        }
    }
}
