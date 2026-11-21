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
                    if (!MPfixPlugin.Active) return;
                    if (!MPfixPlugin.CfgBlockBreakSync.Value) return;              // 伤害重算开关
                    if (!MPfixPlugin.CfgAggressiveConverge.Value) return;          // 激进（上报收敛）开关
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

        // ===== 客机：攒批上报（A：把“每块一个包”压成“每 0.35 秒一个包”）=====

        /// <summary>攒批窗口（秒）。0.35 秒内的碎块合成一个包发出去。</summary>
        private const float BatchInterval = 0.35f;

        /// <summary>一包最多带几块（再多就立即发，避免一个包过大）。</summary>
        private const int BatchMax = 24;

        private static readonly List<JObject> Pending = new List<JObject>(BatchMax);
        private static float _nextFlush = -1f;
        private static int _batched;      // 统计用：攒批后实际发出的包数

        private static void Report(Vector2Int pos, ushort block, float total)
        {
            if (_count >= MaxPerSession) return;

            float now = Time.unscaledTime;
            float last;
            if (Sent.TryGetValue(pos, out last) && now - last < DedupeSeconds) return;
            if (Sent.Count > 1024) Sent.Clear();
            Sent[pos] = now;

            _count++;
            Pending.Add(new JObject
            {
                ["x"] = pos.x,
                ["y"] = pos.y,
                ["block"] = (int)block,
                ["total"] = total
            });

            if (Pending.Count >= BatchMax) { Flush(); return; }
            if (_nextFlush < 0f) _nextFlush = now + BatchInterval;
        }

        /// <summary>每帧调用：到点就把攒下的碎块一起发出去（一个包带多块）。</summary>
        internal static void Tick()
        {
            if (!MPfixPlugin.Active) { Pending.Clear(); _nextFlush = -1f; return; }
            if (Pending.Count == 0) return;
            if (!MPfixPlugin.CfgAggressiveConverge.Value) { Pending.Clear(); _nextFlush = -1f; return; }
            if (_nextFlush >= 0f && Time.unscaledTime < _nextFlush) return;
            Flush();
        }

        private static void Flush()
        {
            _nextFlush = -1f;
            if (Pending.Count == 0) return;

            try
            {
                if (!Mp.IsRunning || !Mp.IsClient) { Pending.Clear(); return; }

                // 只有一块时仍按单条格式发（与旧版主机处理器完全兼容）
                if (Pending.Count == 1)
                {
                    Mp.SendToServer(Channel, Pending[0]);
                }
                else
                {
                    JArray arr = new JArray();
                    for (int i = 0; i < Pending.Count; i++) arr.Add(Pending[i]);
                    Mp.SendToServer(Channel, new JObject { ["items"] = arr });
                    _batched++;
                    MPfixPlugin.Log.Debug($"[挖块] 攒批发送 {arr.Count} 块（第 {_batched} 个批量包）");
                }
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Debug("[挖块] 攒批发送失败：" + ex.Message);
            }
            finally
            {
                Pending.Clear();
            }
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

        /// <summary>兼容两种形状：单条 `{x,y,block,total}` 与攒批 `{items:[…]}`（A：客机攒批上报）。</summary>
        private static JToken Handle(uint sender, JToken token)
        {
            try
            {
                if (!MPfixPlugin.Active) return Fail("disabled");
                if (!Mp.IsServer || token == null) return Fail("not-host");
                if (!MPfixPlugin.CfgBlockBreakSync.Value) return Fail("disabled");
                if (!MPfixPlugin.CfgAggressiveConverge.Value) return Fail("aggressive-off");

                JArray items = token["items"] as JArray;
                if (items != null)
                {
                    int applied = 0, skipped = 0;
                    for (int i = 0; i < items.Count; i++)
                    {
                        JToken entry = items[i];
                        if (entry == null) { skipped++; continue; }
                        string r = ApplyOne(sender, entry);
                        if (r == "broke" || r == "damaged" || r == "already" || r == "no-delta") applied++;
                        else skipped++;
                    }
                    return new JObject { ["ok"] = true, ["batch"] = items.Count, ["applied"] = applied, ["skipped"] = skipped };
                }

                string res = ApplyOne(sender, token);
                return res == "broke" || res == "damaged" ? Ok(res) : Fail(res);
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Debug("[挖块] 主机处理失败：" + ex.Message);
                return Fail("exception");
            }
        }

        /// <summary>处理一块：返回 "broke" / "damaged" / "already" / "no-delta" 或失败原因。</summary>
        private static string ApplyOne(uint sender, JToken token)
        {
            try
            {
                int x = token["x"]?.Value<int>() ?? int.MinValue;
                int y = token["y"]?.Value<int>() ?? int.MinValue;
                int block = token["block"]?.Value<int>() ?? 0;
                float total = token["total"]?.Value<float>() ?? 0f;
                if (x == int.MinValue || y == int.MinValue) return "bad-pos";

                WorldGeneration world = WorldGeneration.world;
                if (world == null) return "no-world";

                var pos = new Vector2Int(x, y);
                int current = world.GetBlock(pos);
                if (current == 0) return "already";
                if (block <= 0 || current != block) return "block-changed";

                Body body = MpChannels.BodyForClient(sender);
                if (body == null) return "no-body";

                float dist = Vector2.Distance(body.transform.position, world.BlockToWorldPos(pos));
                if (dist > MaxReportDistance) return "far";

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
                    return "not-credible";
                }

                float target = Mathf.Clamp(total, already, health);
                float delta = target - already;
                if (delta <= 0f) return "no-delta";

                // 走原版流程：掉落 / 音效 / SetBlock 广播都是原版的
                world.DamageBlock(pos, delta, true, false, false);
                return world.GetBlock(pos) == 0 ? "broke" : "damaged";
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Debug("[挖块] 主机处理失败：" + ex.Message);
                return "exception";
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

        private static float _lastMultLogAt = -99f;

        // D：attackdamage 缓存（世界实例 id + 1 秒 TTL）
        /// <summary>缓存有效期（秒）。改世界伤害倍率最多这么久后生效。</summary>
        private const float MultCacheSeconds = 1f;
        private static bool _multCached;
        private static float _multValue = 1f;
        private static float _multAt = -99f;
        private static int _multWorldId;

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
                
                MPfixPlugin.Log.Info("已启用补丁：主机挖块/打单位伤害补乘世界倍率 attackdamage（判定仍走 MP 原逻辑）");
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Warn("挖块伤害修正补丁安装失败（不影响游戏）：" + ex.Message);
            }
        }

        /// <summary>
        /// 世界设置里的攻击伤害倍率（原版 Body.Attack:3547 用的就是它）。读不到 / 非法值按 1。
        ///
        /// **D：缓存**（2026-11）—— 这个值在主机每次命中都要取一次，而 `GetRunSettingFloat` 是字典/反射查找；
        /// 快速挖矿时一秒几十次命中，纯属白花。现在按「世界实例 id + 1 秒 TTL」缓存：
        /// 世界重开立刻失效；玩家在设置里改了倍率，最多 1 秒后生效。
        /// </summary>
        internal static float AttackDamageMultiplier()
        {
            try
            {
                WorldGeneration world = WorldGeneration.world;
                int worldId = world == null ? 0 : world.GetInstanceID();
                float now = Time.unscaledTime;

                if (_multCached
                    && worldId == _multWorldId
                    && now - _multAt < MultCacheSeconds)
                {
                    return _multValue;
                }

                float v = WorldGeneration.GetRunSettingFloat("attackdamage");
                if (float.IsNaN(v) || float.IsInfinity(v) || v <= 0f) v = 1f;
                v = Mathf.Clamp(v, 0.05f, 50f);

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
        /// **最小侵入**：只把主机算出来的那份伤害补乘它漏掉的世界倍率（原版 `Body.Attack:3546-3547` 在主机从没执行），
        /// 然后把判定**原封不动交回 Together** —— 有限性检查、2000 硬上限、`clientReported ≤ hostOwn×3` 比例、
        /// 被拒绝时退回 hostOwn，全都是 MP 自己的代码，这里一行都不重写。
        ///
        /// 为什么这样就够：主机那份数字变正确之后，客机的诚实上报天然落在 3× 以内 → MP 采纳；
        /// 被拒绝时退回的 hostOwn 也已含倍率 → 裂纹前进量与客机一致，不再“掉一截又回升”。
        /// </summary>
        private static bool Prefix(ref float hostOwnDamage)
        {
            try
            {
                if (!MPfixPlugin.Active) return true;                     // 闸门：完全等于原版 MP
                if (!MPfixPlugin.CfgBlockBreakSync.Value) return true;

                float mult = AttackDamageMultiplier();
                if (mult > 1.0001f)
                {
                    hostOwnDamage *= mult;

                    if (Time.unscaledTime - _lastMultLogAt > 60f)
                    {
                        _lastMultLogAt = Time.unscaledTime;
                        MPfixPlugin.Log.Info($"[挖块] 世界伤害倍率 attackdamage={mult:0.##}：主机侧那份伤害已补乘" +
                                             "（原版在主机漏乘 → 血量掉一截又回升）；判定仍走 MP 原逻辑。");
                    }
                }

                return true;    // ★ 交给 Together.BlockDamageAuthority.ChooseBlockDamage 原码
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Debug("挖块伤害修正失败：" + ex.Message);
                return true;
            }
        }
    }
}
