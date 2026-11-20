using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DDEN.Shared;
using HarmonyLib;
using UnityEngine;

namespace DDEN.MPfix
{
    /// <summary>
    /// 修「一台客机掉线后，另一台客机帧数暴跌」（v2）。
    ///
    /// 实测现场（NetPerfTracker 抓的）：
    ///   物品 296 件，其中 **无主 107、无主且物理开着 82**；活跃刚体 336、碰撞体 676；
    ///   物理 2.55 步/帧（正常 0.83）；网络对象 374 条里 **78 条是已销毁的死条目**；帧数 19。
    /// 现象还会伴随「物品在原地闪烁」——那是**同步每帧把它们写回去**的迹象。
    ///
    /// v1（只冻结）为什么没用：我们 `rb.simulated = false` 之后，只要还有别的东西在写它的位置/状态，
    /// 物理就被重新打开、位置被来回改 → 闪烁 + 冻结失效。
    ///
    /// v2 改成两段式，用 Together **自己的**客户端清理接口（不自己删、不自己挪）：
    ///   ① 先冻结（保守，可逆）；
    ///   ② 同一件物品**第二次**又变回"活着"的状态 → 认定它是主机已不认的幽灵，
    ///      调 `NetObjectRegistry.Client_DeleteUnregisteredSyncableObject(go)` 交给 Together 删
    ///      （这个接口自己会再判一次 `ObjectShouldNotBeSynced`，不该删的它拒绝，我们就退回冻结）。
    ///   ③ 顺手清掉 `SyncRegistry` 里 key 已销毁的死条目（那些纯粹是垃圾，且会让每帧遍历变贵）。
    ///
    /// 只在客机跑；本地玩家 2.5 米内不碰；主机认得的（`IsRegistered`）一律不碰，只计数并打日志 ——
    /// 那种情况真要彻底解决得在主机侧，客户端怎么改都会被同步写回来。
    /// </summary>
    internal static class GhostItemCleaner
    {
        private const float Interval = 3f;
        private const float GraceSeconds = 3f;
        private const float NearPlayerSkip = 2.5f;
        private const int PerPassBudget = 40;
        private const float PruneSeconds = 60f;

        private static readonly Dictionary<int, float> Seen = new Dictionary<int, float>();
        private static readonly Dictionary<int, int> Strikes = new Dictionary<int, int>();
        private static readonly Dictionary<int, bool> Kinematic = new Dictionary<int, bool>();
        private static readonly Dictionary<int, Vector2> FirstPos = new Dictionary<int, Vector2>();
        private static float _next;
        private static int _frozen, _deleted, _kinematic, _registeredSkipped, _session;
        private static float _lastLogAt = -99f;
        private static bool _disabled;

        private static MethodInfo _isRegistered;
        private static MethodInfo _deleteUnregistered;
        private static MethodInfo _safeDestroy;
        private static bool _lookedUp;

        private static float _startAt;
        private static float _pollNext;
        private static int _lastPlayers = -1;
        private static float _watchUntil;
        private static float _nextSafety;
        private static float _nextPrune;

        private static IDictionary _playerDict;
        private static bool _playerLookedUp;

        internal static void Tick()
        {
            if (_disabled) return;
            if (!MPfixPlugin.CfgCleanGhostItems.Value) return;
            if (!Mp.IsRunning || !Mp.IsClient) return;
            if (!Game.InWorld) return;

            if (_startAt <= 0f) _startAt = Time.realtimeSinceStartup;
            if (Time.realtimeSinceStartup - _startAt < 3f) return;      // 跳过开局（冷启动最贵）

            float now = Time.unscaledTime;

            // 便宜地盯着玩家数：只在"有人离开"的 20 秒窗口里密集扫描，其余每 30 秒兜一次底
            if (now >= _pollNext)
            {
                _pollNext = now + 0.5f;
                int pc = PlayersCount();
                if (pc >= 0)
                {
                    if (_lastPlayers >= 0 && pc < _lastPlayers)
                    {
                        _watchUntil = now + 20f;
                        MPfixPlugin.Log.Info($"[幽灵物品] 有玩家离开（{_lastPlayers}→{pc}），接下来 20 秒重点清理遗留物品。");
                    }
                    _lastPlayers = pc;
                }
            }

            if (now >= _nextPrune)
            {
                _nextPrune = now + PruneSeconds;
                PruneDeadRegistry();
            }

            if (now < _next) return;
            _next = now + Interval;

            bool inWatch = now < _watchUntil;
            bool safety = now >= _nextSafety;
            if (!inWatch && !safety) return;
            if (safety) _nextSafety = now + 30f;

            int cap = Mathf.Max(0, MPfixPlugin.CfgCleanGhostMax.Value);
            if (_session >= cap)
            {
                if (!_disabled)
                {
                    _disabled = true;
                    MPfixPlugin.Log.Info($"[幽灵物品] 本局处理次数已达上限（{cap}），停手。");
                }
                return;
            }

            if (!ResolveApis()) return;

            int frozenNow = 0, deletedNow = 0, regNow = 0, kineNow = 0;
            try
            {
                Body local = Game.LocalBody;
                Vector2 at = local != null ? (Vector2)local.transform.position : Vector2.zero;

                RestoreHeldOnes(out int restored);

                foreach (Item item in UnityEngine.Object.FindObjectsOfType<Item>(true))
                {
                    if (item == null) continue;
                    Transform t = item.transform;
                    if (t == null || t.parent != null) continue;          // 只处理无主物品
                    Rigidbody2D rb = item.rb;
                    if (rb == null) continue;

                    int key = item.GetInstanceID();

                    // 已经是"运动学冻结"状态：只要它还是无主就继续按住，别再重复计数
                    if (Kinematic.ContainsKey(key))
                    {
                        if (rb.simulated) rb.velocity = Vector2.zero;
                        Seen.Remove(key);
                        continue;
                    }

                    if (!rb.simulated || rb.IsSleeping())
                    {
                        // 已经停下/已经静止：不占成本，清掉记录
                        Seen.Remove(key);
                        Strikes.Remove(key);
                        continue;
                    }

                    float since;
                    if (!Seen.TryGetValue(key, out since))
                    {
                        Seen[key] = now;
                        FirstPos[key] = t.position;
                        continue;
                    }
                    if (now - since < GraceSeconds) continue;

                    if (local != null && Vector2.Distance(t.position, at) < NearPlayerSkip) continue;

                    Vector2 start;
                    if (!FirstPos.TryGetValue(key, out start)) { FirstPos[key] = t.position; continue; }
                    float dropped = start.y - t.position.y;
                    bool falling = dropped >= 1.5f || (now - since >= 8f && rb.velocity.y < -0.5f);

                    int strikes;
                    Strikes.TryGetValue(key, out strikes);
                    strikes++;
                    Strikes[key] = strikes;

                    bool registered = Registered(item.gameObject);
                    if (registered) regNow++;

                    if (strikes <= 1 && !falling)
                    {
                        // 第一段：只停物理（可逆），谁的物品都不删
                        Freeze(rb, item);
                        frozenNow++;
                    }
                    else if (CanDestroy(item) && DestroyGhost(item.gameObject))
                    {
                        // 确认是「无主 + 一直下坠」的遗留物 → 走 MP 自己的销毁通道（它会先卸掉容器内容物）
                        deletedNow++;
                        Seen.Remove(key);
                        Strikes.Remove(key);
                        FirstPos.Remove(key);
                        _session++;
                    }
                    else
                    {
                        // 容器里还有东西（或销毁失败）：不删，改成运动学冻结，至少不再吃物理
                        KinematicFreeze(rb, item);
                        Seen.Remove(key);
                        FirstPos.Remove(key);
                        kineNow++;
                    }

                    if (frozenNow + deletedNow + kineNow >= PerPassBudget || _session >= cap) break;
                }

                if (Seen.Count > 2048) Seen.Clear();
                if (Strikes.Count > 2048) Strikes.Clear();
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Debug("[幽灵物品] 巡检失败：" + ex.Message);
            }

            _frozen += frozenNow;
            _deleted += deletedNow;
            _kinematic += kineNow;
            _registeredSkipped = regNow;

            if ((frozenNow > 0 || deletedNow > 0 || regNow > 0 || kineNow > 0) && now - _lastLogAt > 10f)
            {
                _lastLogAt = now;
                MPfixPlugin.Log.Info($"[幽灵物品] 本次：冻结 {frozenNow} 件、删除 {deletedNow} 件、运动学冻结 {kineNow} 件" +
                                     (regNow > 0 ? $"（其中主机仍认得 {regNow} 件，按运动学处理）" : "") +
                                     $"；本局累计 冻结 {_frozen} / 删除 {_deleted} / 运动学 {_kinematic}。");
            }
        }

        /// <summary>容器里还有内容物就不删（避免牵连真实物品），交给运动学冻结处理。</summary>
        private static bool CanDestroy(Item item)
        {
            try
            {
                return item.GetComponentsInChildren<Item>(true).Length <= 1;
            }
            catch { return false; }
        }

        /// <summary>走 Together 自己的销毁通道：先卸容器内容物 → 摘注册表 → Destroy。</summary>
        private static bool DestroyGhost(GameObject go)
        {
            try
            {
                _safeDestroy.Invoke(null, new object[] { go });
                return true;
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Debug("[幽灵物品] 销毁失败：" + ex.Message);
                return false;
            }
        }

        private static void Freeze(Rigidbody2D rb, Item item)
        {
            try
            {
                rb.velocity = Vector2.zero;
                rb.angularVelocity = 0f;
                rb.simulated = false;
                Collider2D[] cols = item.GetComponentsInChildren<Collider2D>(true);
                for (int i = 0; i < cols.Length; i++)
                {
                    if (cols[i] != null) cols[i].enabled = false;
                }
            }
            catch { /* ignored */ }
        }

        /// <summary>
        /// 运动学冻结：不参与重力/求解，但仍是"活着的刚体"，被同步写回位置也不会有物理开销。
        /// 这是对"主机还认得、只关 simulated 又会被写回来"的那批物品的兜底手段。
        /// </summary>
        private static void KinematicFreeze(Rigidbody2D rb, Item item)
        {
            try
            {
                rb.velocity = Vector2.zero;
                rb.angularVelocity = 0f;
                if (!rb.simulated) rb.simulated = true;      // 保持 true，避免和同步逻辑互相打架
                if (rb.bodyType != RigidbodyType2D.Kinematic) rb.bodyType = RigidbodyType2D.Kinematic;
                if (rb.gravityScale != 0f) rb.gravityScale = 0f;

                Collider2D[] cols = item.GetComponentsInChildren<Collider2D>(true);
                for (int i = 0; i < cols.Length; i++)
                {
                    if (cols[i] != null) cols[i].enabled = false;
                }

                Kinematic[item.GetInstanceID()] = true;
            }
            catch { /* ignored */ }
        }

        /// <summary>被玩家捡起/穿上（有了父级）就把刚体类型还回去，别影响正常玩法。</summary>
        private static void RestoreHeldOnes(out int restored)
        {
            restored = 0;
            if (Kinematic.Count == 0) return;
            try
            {
                var done = new List<int>(8);
                foreach (var pair in Kinematic)
                {
                    Transform tr = null;
                    foreach (Item it in UnityEngine.Object.FindObjectsOfType<Item>(true))
                    {
                        if (it == null || it.GetInstanceID() != pair.Key) continue;
                        tr = it.transform;
                        if (tr != null && tr.parent != null)
                        {
                            Rigidbody2D rb = it.rb;
                            if (rb != null)
                            {
                                if (rb.bodyType != RigidbodyType2D.Dynamic) rb.bodyType = RigidbodyType2D.Dynamic;
                                if (rb.gravityScale == 0f) rb.gravityScale = 1f;
                                restored++;
                            }
                        }
                        done.Add(pair.Key);
                        break;
                    }
                    if (tr == null) done.Add(pair.Key);      // 对象没了，清记录
                }
                for (int i = 0; i < done.Count; i++) Kinematic.Remove(done[i]);
            }
            catch { /* ignored */ }
        }

        /// <summary>交给 Together 自己删（它会再判一次 ObjectShouldNotBeSynced）。</summary>
        private static bool Delete(GameObject go)
        {
            try
            {
                object r = _deleteUnregistered.Invoke(null, new object[] { go });
                return r is bool b && b;
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Debug("[幽灵物品] 删除失败：" + ex.Message);
                return false;
            }
        }

        /// <summary>干掉 SyncRegistry 里 key 已销毁的死条目（垃圾条目会让每帧遍历变贵）。</summary>
        private static void PruneDeadRegistry()
        {
            try
            {
                IDictionary reg = ReflectDict("Together.NetObjectRegistry", "SyncRegistry");
                if (reg == null) return;
                var dead = new List<object>(16);
                foreach (DictionaryEntry e in reg)
                {
                    var go = e.Key as UnityEngine.Object;
                    if (go == null) dead.Add(e.Key);
                    if (dead.Count >= 200) break;
                }
                for (int i = 0; i < dead.Count; i++)
                {
                    try { reg.Remove(dead[i]); } catch { /* ignored */ }
                }
                if (dead.Count > 0) MPfixPlugin.Log.Info($"[幽灵物品] 清掉 {dead.Count} 条已销毁的网络登记条目。");
            }
            catch { /* ignored */ }
        }

        private static bool ResolveApis()
        {
            if (_lookedUp) return _isRegistered != null && _safeDestroy != null;
            _lookedUp = true;
            try
            {
                Type t = AccessTools.TypeByName("Together.NetObjectRegistry");
                if (t == null)
                {
                    _disabled = true;
                    MPfixPlugin.Log.Warn("[幽灵物品] 找不到网络注册表（Together 版本变了？），这条修复不启用。");
                    return false;
                }
                _isRegistered = AccessTools.Method(t, "IsRegistered", new[] { typeof(GameObject) });
                _deleteUnregistered = AccessTools.Method(t, "Client_DeleteUnregisteredSyncableObject", new[] { typeof(GameObject) });
                _safeDestroy = AccessTools.Method(t, "SafeDestroyObject", new[] { typeof(GameObject) });
                if (_isRegistered == null || _safeDestroy == null)
                {
                    _disabled = true;
                    MPfixPlugin.Log.Warn("[幽灵物品] 注册表接口不完整，这条修复不启用（宁可不修，也不盲删）。");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _disabled = true;
                MPfixPlugin.Log.Warn("[幽灵物品] 解析注册表接口失败，这条修复不启用：" + ex.Message);
                return false;
            }
            return true;
        }

        private static bool Registered(GameObject go)
        {
            try
            {
                object r = _isRegistered.Invoke(null, new object[] { go });
                return r is bool b && b;
            }
            catch { return true; }      // 查不了就当"已登记"，宁可不碰
        }

        private static int PlayersCount()
        {
            if (!_playerLookedUp)
            {
                _playerLookedUp = true;
                try
                {
                    Type t = AccessTools.TypeByName("Together.ScavPlayer");
                    FieldInfo f = t == null ? null : t.GetField("ClientIdToPlayerDict",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    _playerDict = f == null ? null : f.GetValue(null) as IDictionary;
                }
                catch { /* ignored */ }
            }
            return _playerDict == null ? -1 : _playerDict.Count;
        }

        private static readonly Dictionary<string, IDictionary> DictCache = new Dictionary<string, IDictionary>(StringComparer.Ordinal);
        private static readonly HashSet<string> DictFailed = new HashSet<string>(StringComparer.Ordinal);

        private static IDictionary ReflectDict(string typeName, string fieldName)
        {
            string key = typeName + "." + fieldName;
            IDictionary cached;
            if (DictCache.TryGetValue(key, out cached) && cached != null) return cached;
            if (DictFailed.Contains(key)) return null;
            try
            {
                Type t = Reflect.Type(typeName);
                if (t == null) { DictFailed.Add(key); return null; }
                FieldInfo f = t.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (f == null) { DictFailed.Add(key); return null; }
                var dict = f.GetValue(null) as IDictionary;
                if (dict == null) { DictFailed.Add(key); return null; }
                DictCache[key] = dict;
                return dict;
            }
            catch { DictFailed.Add(key); return null; }
        }
    }
}
