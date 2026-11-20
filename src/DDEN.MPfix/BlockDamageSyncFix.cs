using System;
using System.Collections.Generic;
using System.Reflection;
using DDEN.Shared;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DDEN.MPfix
{
    /// <summary>
    /// 修「客机挖块不同步」：血量跳 + 客机挖掉后主机判定没挖掉、chunk 同步把方块推回来（“挖了又长回来”）。
    ///
    /// 病根（Together 5.5.4，两处）：
    ///  ① 主机的权威判定太紧：<c>BlockDamageAuthority.ChooseBlockDamage(hostOwn, clientReported, clientId)</c>
    ///     （126046）只在 <c>clientReported &lt;= hostOwn * 3</c> 时才用客机的数字，否则整段丢掉。
    ///     主机那份 <c>hostOwn = atk.structuralDamage * CalculateAttackPower(...)</c> 是用**主机那份工具**重算的，
    ///     模组工具 / 品质不同 → hostOwn 偏小 → 客机永远挖不动，客机本地却已经把方块打到 0。
    ///  ② 中继会把客机显示的裂纹“掰”回主机数值：<c>ClientReceiver__PlayerDamageBlockRelay</c>（43901）
    ///     直接拿 <c>主机累计 - 本地累计</c> 做差值（可能是负数）喂给 <c>DamageBlock</c> —— 血量/裂纹会往回跳。
    ///
    /// 本模块分两步，先治根再兜底：
    ///  A. <see cref="BlockDamageRelax"/>：主机侧把 ① 的比例从 3 倍放宽到可配置（默认 6 倍，硬上限 2000 不变）。
    ///     方块仍然是**正常被打碎**的：掉落、音效、SetBlock 广播、硬度判定全都走原版流程。
    ///  B. 本类：客机本地真把方块打到 0 时，上报 <c>{x, y, blockId, total}</c>；
    ///     主机**先校验**（同一格、同一方块、发送者 12 格内、自己账上这块已经掉过半血），
    ///     再“收敛”到客机的累计值（<c>delta = total - 主机累计</c>，只加不减），而不是无脑打满。
    ///     校验不过就什么都不做并留痕 —— 一镐敲穿钢墙这条路是不通的。
    ///
    /// 同坐标 5 秒去重、每局 3000 次上限、全程 try/catch。
    /// </summary>
    internal static class BlockDamageSyncFix
    {
        private const string Channel = "ddn.mp.blockdmg";
        private const float DedupeSeconds = 5f;
        private const int MaxPerSession = 3000;
        private const float MaxReportDistance = 12f;     // 主机侧距离校验（原版攻击校验是 10.4m）

        private static readonly Dictionary<Vector2Int, float> Sent = new Dictionary<Vector2Int, float>();
        private static int _count;

        // 一次 DamageBlock 调用内的上下文（Unity 主线程单线程，静态字段足够）
        private static ushort _curBlock;
        private static Vector2Int _curPos;
        private static bool _curRelayed;

        private static FieldInfo _relayedField;
        private static bool _relayedLookedUp;

        private static float _lastHostLogAt = -99f;

        // ===== 客机：本地把方块打到 0 时上报 =====

        [HarmonyPatch(typeof(WorldGeneration), "DamageBlock", new[]
        {
            typeof(Vector2Int), typeof(float), typeof(bool), typeof(bool), typeof(bool)
        })]
        internal static class DamageBlock_Patch
        {
            private static void Prefix(WorldGeneration __instance, Vector2Int pos)
            {
                try
                {
                    _curPos = pos;
                    _curBlock = 0;
                    _curRelayed = false;
                    if (__instance == null) return;
                    _curBlock = __instance.GetBlock(pos);
                    _curRelayed = IsRelayedDamage();
                }
                catch
                {
                    _curBlock = 0;
                }
            }

            private static void Postfix(WorldGeneration __instance, Vector2Int pos)
            {
                try
                {
                    if (__instance == null) return;
                    if (_curRelayed) return;            // 主机中继过来的伤害：不再回声
                    if (_curBlock == 0) return;         // 打之前就是空的
                    if (__instance.GetBlock(pos) != 0) return;   // 还没碎
                    if (!MPfixPlugin.CfgBlockBreakSync.Value) return;
                    if (!Mp.IsRunning || !Mp.IsClient) return;

                    BlockInfo info = __instance.GetBlockInfo(_curBlock);
                    float health = info != null && info.health > 0f ? info.health : 100f;
                    Report(pos, _curBlock, health);
                }
                catch (Exception ex)
                {
                    MPfixPlugin.Log.Debug("挖块上报失败：" + ex.Message);
                }
            }
        }

        private static void Report(Vector2Int pos, ushort block, float total)
        {
            if (_count >= MaxPerSession) return;

            float now = Time.unscaledTime;
            float last;
            if (Sent.TryGetValue(pos, out last) && now - last < DedupeSeconds) return;
            if (Sent.Count > 1024) Sent.Clear();
            Sent[pos] = now;

            _count++;
            Mp.SendToServer(Channel, new JObject
            {
                ["x"] = pos.x,
                ["y"] = pos.y,
                ["block"] = (int)block,
                ["total"] = total
            });
        }

        /// <summary>主机正在应用“中继伤害”时为 true —— 那一路不需要再上报（会回声）。</summary>
        private static bool IsRelayedDamage()
        {
            try
            {
                if (!_relayedLookedUp)
                {
                    _relayedLookedUp = true;
                    Type t = AccessTools.TypeByName("Together.BlockDamageAuthority");
                    _relayedField = t == null
                        ? null
                        : t.GetField("ApplyingRelayedDamage",
                            BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                }
                if (_relayedField == null) return false;
                object v = _relayedField.GetValue(null);
                return v is bool b && b;
            }
            catch
            {
                return false;
            }
        }

        // ===== 主机：校验后收敛到客机的累计值 =====

        internal static void RegisterHost()
        {
            if (!MpChannels.TryRegisterSender(Channel, Handle))
            {
                MPfixPlugin.Log.Warn("[挖块] 无法注册带发送者的 blockdmg 频道，主机侧不会收敛挖块伤害。");
            }
        }

        private static JToken Handle(uint sender, JToken token)
        {
            try
            {
                if (!Mp.IsServer || token == null) return Fail("not-host");
                if (!MPfixPlugin.CfgBlockBreakSync.Value) return Fail("disabled");

                int x = token["x"]?.Value<int>() ?? int.MinValue;
                int y = token["y"]?.Value<int>() ?? int.MinValue;
                int block = token["block"]?.Value<int>() ?? 0;
                float total = token["total"]?.Value<float>() ?? 0f;
                if (x == int.MinValue || y == int.MinValue) return Fail("bad-pos");

                WorldGeneration world = WorldGeneration.world;
                if (world == null) return Fail("no-world");

                var pos = new Vector2Int(x, y);
                int current = world.GetBlock(pos);
                if (current == 0) return Ok("already");
                if (block <= 0 || current != block) return Fail("block-changed");

                Body body = MpChannels.BodyForClient(sender);
                if (body == null) return Fail("no-body");

                float dist = Vector2.Distance(body.transform.position, world.BlockToWorldPos(pos));
                if (dist > MaxReportDistance) return Fail($"far({dist:0.0}m)");

                BlockInfo info = world.GetBlockInfo((ushort)current);
                float health = info != null && info.health > 0f ? info.health : 100f;

                BlockDamage bd = world.GetBlockDamage(pos);
                float already = bd != null ? bd.damage : 0f;

                // ★ 可信度门槛：主机自己的账上这块必须已经掉过半血。
                //   客机说“我挖穿了”不算数；主机说“它确实快碎了”才算。
                float minFraction = Mathf.Clamp(MPfixPlugin.CfgBlockBreakMinFraction.Value, 0f, 0.95f);
                if (already < health * minFraction)
                {
                    HostLog($"[挖块] 拒绝收敛：{pos} 客机称已碎，但主机账上只掉了 " +
                            $"{already:0.#}/{health:0.#}（<{minFraction:P0}）。多数情况下放宽比例后不该走到这一步。");
                    return Fail("not-credible");
                }

                float target = Mathf.Clamp(total, already, health);
                float delta = target - already;
                if (delta <= 0f) return Ok("no-delta");

                // 走原版流程：掉落 / 音效 / SetBlock 广播都是原版的
                world.DamageBlock(pos, delta, true, false, false);
                return Ok(world.GetBlock(pos) == 0 ? "broke" : "damaged");
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Debug("[挖块] 主机处理失败：" + ex.Message);
                return Fail("exception");
            }
        }

        private static JObject Ok(string why) => new JObject { ["ok"] = true, ["why"] = why };

        private static JObject Fail(string why) => new JObject { ["ok"] = false, ["why"] = why };

        private static void HostLog(string message)
        {
            if (Time.unscaledTime - _lastHostLogAt < 5f) return;
            _lastHostLogAt = Time.unscaledTime;
            MPfixPlugin.Log.Info(message);
        }
    }

    /// <summary>
    /// 主机侧修挖块伤害，两个病根一起治：
    ///
    /// **① 主机那份伤害天生少了世界伤害倍率（这是“血量掉一截又回升”的真凶）。**
    ///   原版 <c>Body.Attack</c> 第一件事就是
    ///   <c>atk.damage/atk.structuralDamage *= WorldGeneration.GetRunSettingFloat("attackdamage")</c>
    ///   （反编译 Assembly-CSharp 3546-3547）。客机那次 Attack 是**真跑完整流程**的，所以客机上报的
    ///   <c>clientReported</c> 含倍率；而主机在 <c>ServerReceiver_PlayerBodyAttack</c> 里先把
    ///   <c>ForceOnlyRetrieveAttackInfo</c> 置真再走 <c>BetterUseItem</c>，Together 的 <c>Body.Attack</c> prefix
    ///   抓到 <c>LastAttackInfo</c> 就直接 return false —— **原方法体没执行，3546/3547 那两句乘法在主机从没跑过**。
    ///   于是 <c>ChooseBlockDamage(hostOwn=…)</c> 的 hostOwn 少一个 attackdamage。
    ///   表现：客机裂纹先掉“含倍率的一份”，主机回一个不含倍率的小值，中继再把客机累计掰回主机的数 → 掉一截又回升；
    ///   倍率 >3 时客机数字每次都被拒 → 永远挖不动 → chunk 同步把原 tile 推回（“挖了又长回来”）。
    ///   修法：在这里把 hostOwn **乘回 attackdamage**（读不到就按 1，绝不猜）。
    ///
    /// **② 原版只接受 <c>clientReported &lt;= hostOwn * 3</c>**，模组工具/品质不一致时仍可能被拒。
    ///   为此比例上限可配（默认 6，可配 1~20；有效值下限 3 = 原版）。
    ///
    /// 本前缀**全程接管**（不再 return true 交回原版），否则“被拒绝”那条路仍会用没乘倍率的 hostOwn 落地，
    /// 血量照样往回跳。硬上限 2000 / NaN / Inf / &gt;0 全部保留原版语义。
    /// </summary>
    internal static class BlockDamageRelax
    {
        private const float VanillaRatio = 3f;
        private const float MaxBlockDamage = 2000f;

        private static float _lastSalvageLogAt = -99f;
        private static float _lastMultLogAt = -99f;
        private static int _salvaged;

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
                MPfixPlugin.Log.Info($"已启用补丁：主机挖块伤害（补乘世界伤害倍率 attackdamage；" +
                                     $"比例上限 {VanillaRatio:0.#}× → {Mathf.Max(VanillaRatio, MPfixPlugin.CfgBlockDamageRatio.Value):0.#}×）");
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Warn("挖块伤害修正补丁安装失败（不影响游戏）：" + ex.Message);
            }
        }

        /// <summary>世界设置里的攻击伤害倍率（原版 Body.Attack:3547 用的就是它）。读不到 / 非法值按 1。</summary>
        private static float AttackDamageMultiplier()
        {
            try
            {
                float v = WorldGeneration.GetRunSettingFloat("attackdamage");
                if (float.IsNaN(v) || float.IsInfinity(v) || v <= 0f) return 1f;
                return Mathf.Clamp(v, 0.05f, 50f);
            }
            catch
            {
                return 1f;      // 主菜单/世界未就绪时 runSettings 可能没有这个键
            }
        }

        private static bool Prefix(float hostOwnDamage, float clientReportedDamage, ref float __result)
        {
            try
            {
                float ratio = Mathf.Max(VanillaRatio, MPfixPlugin.CfgBlockDamageRatio.Value);
                float mult = AttackDamageMultiplier();
                float host = hostOwnDamage * mult;

                bool clientOk = !float.IsNaN(clientReportedDamage) && !float.IsInfinity(clientReportedDamage)
                                && clientReportedDamage > 0f
                                && clientReportedDamage <= MaxBlockDamage
                                && clientReportedDamage <= host * ratio;

                __result = clientOk ? clientReportedDamage : host;

                if (mult > 1.0001f && Time.unscaledTime - _lastMultLogAt > 60f)
                {
                    _lastMultLogAt = Time.unscaledTime;
                    MPfixPlugin.Log.Info($"[挖块] 世界伤害倍率 attackdamage={mult:0.##}：主机侧自己那份伤害已按它补乘" +
                                         "（原版漏乘 → 每次挖掘掉血再回升 / 挖了又长回来）。");
                }

                if (clientOk && hostOwnDamage > 0f && clientReportedDamage > hostOwnDamage * VanillaRatio)
                {
                    _salvaged++;
                    if (Time.unscaledTime - _lastSalvageLogAt > 10f)
                    {
                        _lastSalvageLogAt = Time.unscaledTime;
                        MPfixPlugin.Log.Info($"[挖块] 采纳客机伤害：客机 {clientReportedDamage:0.#} / " +
                                             $"主机(已补倍率) {host:0.#} = {clientReportedDamage / host:0.0}×" +
                                             $"（累计救回 {_salvaged} 次）");
                    }
                }
                else if (!clientOk && hostOwnDamage > 0f && Time.unscaledTime - _lastSalvageLogAt > 10f)
                {
                    _lastSalvageLogAt = Time.unscaledTime;
                    MPfixPlugin.Log.Info($"[挖块] 客机数字仍不可信，用主机自己的：客机 {clientReportedDamage:0.#} / " +
                                         $"主机(已补倍率) {host:0.#} = {(host > 0f ? clientReportedDamage / host : 0f):0.0}× " +
                                         $"(上限 {ratio:0.#}×、硬上限 {MaxBlockDamage:0})");
                }

                return false;
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Debug("挖块伤害修正失败：" + ex.Message);
                return true;
            }
        }
    }
}
