using System;
using System.Collections.Generic;
using System.Reflection;
using DDEN.Shared;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DDEN.MPfix
{
    /// <summary>Reliable break reports with cheap hit capture, bounded work per frame and world-local queues.</summary>
    internal static class BlockDamageSyncFix
    {
        private const string Channel = "ddn.mp.blockdmg";
        private const float BatchInterval = 0.35f;
        private const int BatchMax = 64;
        private const int HostMaxPerFrame = 16;
        private const double HostBudgetMs = 1.5;
        private const float DedupeSeconds = 5f;
        private const float MaxReportDistance = 12f;

        private struct Report
        {
            internal Vector2Int Pos;
            internal ushort Block;
            internal float Total;
            internal BlockDamage Record;
        }
        private struct Stamp { internal Vector2Int Pos; internal float At; }
        internal struct Capture { internal ushort Block; }
        private static readonly Queue<Report> Pending = new Queue<Report>(BatchMax);
        private static readonly FrameWorkQueue<Report> HostPending = new FrameWorkQueue<Report>();
        private static readonly Action<Report> Apply = ApplyOne;
        private static readonly Dictionary<Vector2Int, float> Sent = new Dictionary<Vector2Int, float>();
        private static readonly Queue<Stamp> Expiry = new Queue<Stamp>();
        private static float _nextFlush = -1f;
        private static WorldGeneration _world;
        private static bool _running;
        private static FieldInfo _relayedField;
        private static bool _relayedLookedUp;

        internal static int PendingCount => Pending.Count + HostPending.Count;
        private static bool Enabled => MPfixPlugin.Active && MPfixPlugin.CfgBlockBreakSync.Value
            && MPfixPlugin.CfgAggressiveConverge.Value;

        [HarmonyPatch(typeof(WorldGeneration), "GenerateWorld")]
        internal static class WorldReset_Patch
        {
            private static void Prefix()
            {
                Reset();
                WearGhostFix.Reset();
                TraderSnapshotFix.Reset();
            }
        }

        internal static void Reset()
        {
            Pending.Clear(); HostPending.Clear(); Sent.Clear(); Expiry.Clear();
            _nextFlush = -1f;
            _world = WorldGeneration.world;
            _running = Mp.IsRunning;
            BlockDamageRelax.ResetCache();
        }

        private static void CheckWorld()
        {
            bool running = Mp.IsRunning;
            if (_world != WorldGeneration.world || _running != running) Reset();
        }

        [HarmonyPatch(typeof(WorldGeneration), "DamageBlock", new[]
        { typeof(Vector2Int), typeof(float), typeof(bool), typeof(bool), typeof(bool) })]
        internal static class DamageBlock_Patch
        {
            private static void Prefix(WorldGeneration __instance, Vector2Int pos, out Capture __state)
            {
                __state = default;
                try
                {
                    // Disabled mode and host/relay damage do no world reads or per-hit reflection.
                    if (!Enabled || !Mp.IsClient || __instance == null || IsRelayedDamage()) return;
                    CheckWorld();
                    __state.Block = __instance.GetBlock(pos);
                }
                catch (Exception ex) { MPfixPlugin.Log.Debug("记录碎块失败：" + ex.Message); }
            }

            private static void Postfix(WorldGeneration __instance, Vector2Int pos, Capture __state)
            {
                try
                {
                    if (__state.Block == 0 || __instance == null || !Enabled || !Mp.IsClient) return;
                    if (__instance.GetBlock(pos) != 0) return;
                    float now = Time.unscaledTime;
                    Prune(now);
                    if (Sent.TryGetValue(pos, out float last) && now - last < DedupeSeconds) return;
                    Sent[pos] = now;
                    Expiry.Enqueue(new Stamp { Pos = pos, At = now });
                    BlockInfo info = __instance.GetBlockInfo(__state.Block);
                    Pending.Enqueue(new Report { Pos = pos, Block = __state.Block,
                        Total = info != null && info.health > 0f ? info.health : 100f });
                    if (_nextFlush < 0f) _nextFlush = now + BatchInterval;
                    // Never serialize or send inside a hit, even when a batch fills up.
                    if (Pending.Count >= BatchMax) _nextFlush = now;
                }
                catch (Exception ex) { MPfixPlugin.Log.Debug("排队上报碎块失败：" + ex.Message); }
            }
        }

        private static void Prune(float now)
        {
            while (Expiry.Count > 0 && now - Expiry.Peek().At >= DedupeSeconds)
            {
                Stamp stamp = Expiry.Dequeue();
                if (Sent.TryGetValue(stamp.Pos, out float at) && at == stamp.At) Sent.Remove(stamp.Pos);
            }
        }

        internal static void Tick()
        {
            CheckWorld();
            if (!Enabled || !Mp.IsRunning || !Game.InWorld)
            { if (PendingCount > 0 || Sent.Count > 0) Reset(); return; }
            if (Mp.IsClient)
            {
                Prune(Time.unscaledTime);
                if (Pending.Count > 0 && Time.unscaledTime >= _nextFlush) Flush();
            }
            else if (Mp.IsServer)
            {
                HostPending.Drain(Apply, HostMaxPerFrame, HostBudgetMs);
            }
        }

        private static void Flush()
        {
            JArray items = new JArray();
            foreach (Report entry in Pending)
            {
                items.Add(new JObject { ["x"] = entry.Pos.x, ["y"] = entry.Pos.y,
                    ["block"] = (int)entry.Block, ["total"] = entry.Total });
                if (items.Count >= BatchMax) break;
            }
            bool sent = Mp.SendToServer(Channel, items.Count == 1 ? (object)items[0]
                : new JObject { ["items"] = items });
            if (sent)
            {
                for (int i = 0; i < items.Count; i++) Pending.Dequeue();
                _nextFlush = Pending.Count > 0 ? Time.unscaledTime : -1f;
            }
            else _nextFlush = Time.unscaledTime + BatchInterval; // Keep unsent entries for retry.
        }

        private static bool IsRelayedDamage()
        {
            if (!_relayedLookedUp)
            {
                _relayedLookedUp = true;
                Type t = AccessTools.TypeByName("Together.BlockDamageAuthority");
                _relayedField = t == null ? null : AccessTools.Field(t, "ApplyingRelayedDamage");
            }
            // If the relay marker is unavailable, do not create echo reports.
            return _relayedField == null || (bool)_relayedField.GetValue(null);
        }

        internal static void RegisterHost()
        {
            if (!MpChannels.TryRegisterSender(Channel, Handle))
                MPfixPlugin.Log.Warn("[挖块] 主机上报频道注册失败，激进碎块兜底不启用。");
        }

        private static JToken Handle(uint sender, JToken token)
        {
            try
            {
                if (!Enabled || !Mp.IsServer || !Game.InWorld || token == null) return null;
                CheckWorld();
                JArray items = token["items"] as JArray;
                if (items != null && items.Count > BatchMax) return null;
                // Resolve player once per packet, not once per block.
                Body body = MpChannels.BodyForClient(sender);
                if (body == null) return null;
                Vector2 origin = body.transform.position;
                if (items == null) EnqueueOne(origin, token);
                else for (int i = 0; i < items.Count; i++) EnqueueOne(origin, items[i]);
            }
            catch (Exception ex) { MPfixPlugin.Log.Debug("接收碎块上报失败：" + ex.Message); }
            // This is a fire-and-forget event: allocating a response is unnecessary.
            return null;
        }

        private static void EnqueueOne(Vector2 origin, JToken token)
        {
            if (!(token is JObject)) return;
            int x = token.Value<int?>("x") ?? int.MinValue;
            int y = token.Value<int?>("y") ?? int.MinValue;
            int block = token.Value<int?>("block") ?? 0;
            float total = token.Value<float?>("total") ?? 0f;
            if (x == int.MinValue || y == int.MinValue || block <= 0 || block > ushort.MaxValue
                || float.IsNaN(total) || float.IsInfinity(total) || total <= 0f) return;
            var pos = new Vector2Int(x, y);
            if (_world.GetBlock(pos) != block) return; // Includes blocks already broken by native MP.
            // Validate distance on receipt, not later after the player has moved away from the queue.
            if (((Vector2)_world.BlockToWorldPos(pos) - origin).sqrMagnitude > MaxReportDistance * MaxReportDistance) return;
            BlockDamage record = _world.GetBlockDamage(pos);
            if (record == null) return; // Native attack must have registered damage first.
            HostPending.Enqueue(new Report { Pos = pos, Block = (ushort)block, Total = total, Record = record });
        }

        private static void ApplyOne(Report entry)
        {
            try
            {
                if (!Enabled || _world == null || _world != WorldGeneration.world || !Game.InWorld) return;
                if (_world.GetBlock(entry.Pos) != entry.Block) return;
                BlockDamage record = _world.GetBlockDamage(entry.Pos);
                // A replaced/reset block at the same coordinate must never inherit an old report.
                if (record == null || !ReferenceEquals(record, entry.Record)) return;
                BlockInfo info = _world.GetBlockInfo(entry.Block);
                float health = info != null && info.health > 0f ? info.health : 100f;
                float already = record.damage;
                if (already < health * Mathf.Clamp(MPfixPlugin.CfgBlockBreakMinFraction.Value, 0f, 0.95f)) return;
                float delta = Mathf.Clamp(entry.Total, already, health) - already;
                if (delta > 0f) _world.DamageBlock(entry.Pos, delta, true, false, false);
            }
            catch (Exception ex) { MPfixPlugin.Log.Debug("补齐碎块伤害失败：" + ex.Message); }
        }
    }
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
        /// 然后把判定**原封不动交回 Together** —— 上报有限性检查、2000 上报上限、`clientReported ≤ hostOwn×3` 比例、
        /// 被拒绝时退回 hostOwn，全都是 MP 自己的代码，这里一行都不重写。
        ///
        /// 此处只修正倍率遗漏。工具和力量状态的主客差异仍可能使上报被拒；
        /// 不能承诺单靠倍率修正就消除所有回弹。
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
