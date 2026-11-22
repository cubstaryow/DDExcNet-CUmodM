using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DDEN.MPfix
{
    /// <summary>
    /// 主机替客机结算挖方块伤害时的**倍率补齐**（最小侵入）。
    ///
    /// 病根：原版 `Body.Attack` 开头会把 `atk.damage/atk.structuralDamage *= GetRunSettingFloat("attackdamage")`；
    /// 主机在 `ServerReceiver_PlayerBodyAttack` 里先置 `ForceOnlyRetrieveAttackInfo=true` 再走
    /// `BetterUseItem`，Together 的 `Body.Attack` 前缀抓到 `LastAttackInfo` 就 return false ——
    /// 于是主机那份伤害少了 attackdamage，`ChooseBlockDamage(hostOwn=…)` 偏小、客机上报被拒，表现为“掉一截又回升”。
    ///
    /// 修法：前缀里把参数乘回 attackdamage，然后 `return true` 交回 Together 原码；
    /// 有限性、2000 上限、3× 比例、被拒绝时退回 hostOwn 全部由 MP 自己处理。
    /// </summary>
    internal static class BlockDamageRelax
    {
        private static float _lastMultLogAt = -99f;

        // attackdamage 缓存（世界实例 id + 1 秒 TTL）
        private const float MultCacheSeconds = 1f;
        private static bool _multCached;
        private static float _multValue = 1f;
        private static float _multAt = -99f;
        private static int _multWorldId;

        internal static void ResetCache() => _multCached = false;

        internal static void Install(Harmony harmony)
        {
            try
            {
                Type t = AccessTools.TypeByName("Together.BlockDamageAuthority");
                MethodInfo m = t == null ? null : AccessTools.Method(t, "ChooseBlockDamage");
                if (m == null)
                {
                    MPfixPlugin.Log.Debug("未找到 Together.BlockDamageAuthority.ChooseBlockDamage，挖块伤害修正跳过。");
                    return;
                }

                harmony.Patch(m, prefix: new HarmonyMethod(typeof(BlockDamageRelax), nameof(Prefix)));
                MPfixPlugin.Log.Info("已启用补丁：主机挖块伤害补乘世界倍率 attackdamage（判定仍走 MP 原逻辑）");
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Warn("挖块伤害修正补丁安装失败（不影响游戏）：" + ex.Message);
            }
        }

        /// <summary>世界设置里的攻击伤害倍率（原版 Body.Attack:3547 用的就是它）。读不到 / 非法值按 1。</summary>
        internal static float AttackDamageMultiplier()
        {
            try
            {
                WorldGeneration world = WorldGeneration.world;
                int worldId = world == null ? 0 : world.GetInstanceID();
                float now = Time.unscaledTime;

                if (_multCached && worldId == _multWorldId && now - _multAt < MultCacheSeconds)
                {
                    return _multValue;
                }

                float v = WorldGeneration.GetRunSettingFloat("attackdamage");
                if (float.IsNaN(v) || float.IsInfinity(v) || v <= 0f) v = 1f;

                _multValue = v;
                _multWorldId = worldId;
                _multAt = now;
                _multCached = true;
                return v;
            }
            catch
            {
                return 1f;      // 主菜单/世界未就绪时 runSettings 可能没有这个键
            }
        }

        /// <summary>
        /// 只修正倍率遗漏：乘回 attackdamage 后交回 Together 原判定。
        /// 工具与力量状态的主客差异仍可能让上报被拒；不能承诺单靠倍率修正就消除所有回弹。
        /// </summary>
        private static bool Prefix(ref float hostOwnDamage)
        {
            try
            {
                if (!MPfixPlugin.Active) return true;                     // 闸门：完全等于原版 MP
                if (!MPfixPlugin.CfgBlockBreakSync.Value) return true;

                float mult = AttackDamageMultiplier();
                if (Math.Abs(mult - 1f) > 0.0001f)
                {
                    hostOwnDamage *= mult;

                    if (Time.unscaledTime - _lastMultLogAt > 60f)
                    {
                        _lastMultLogAt = Time.unscaledTime;
                        MPfixPlugin.Log.Info($"[挖块] 世界伤害倍率 attackdamage={mult:0.##}：主机侧那份伤害已补乘" +
                                             "（原版在主机漏乘 → 血量掉一截又回升）；判定仍走 MP 原逻辑。");
                    }
                }

                return true;    // 交给 Together.BlockDamageAuthority.ChooseBlockDamage 原码
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Debug("挖块伤害修正失败：" + ex.Message);
                return true;
            }
        }
    }
}
