using System;
using System.Collections;
using System.Reflection;
using System.Collections.Generic;
using DDEN.Shared;
using HarmonyLib;
using UnityEngine;

namespace DDEN.MPfix
{
    /// <summary>
    /// 兼容修复：联机合成“无法合成：无法生成物品”。
    ///
    /// 病根（反编译核对）：
    ///   1) Together 在主机侧重新实现了一份合成生成：
    ///      <c>PlayerCamera_TryCraft_MultiplayerPatch.RecipeResult_SpawnResult</c>
    ///      → <c>Utils.Create(rres.id, pos, rot)</c> → <c>Instantiate(Resources.Load(id))</c>。
    ///   2) 模组物品（EnhancedTools / 通过 CUCoreLib 注册的物品等）不在 Unity Resources 里，
    ///      而是运行时从 AssetBundle/文件加载的 → <c>Resources.Load(id)</c> 返回 null
    ///      → 生成失败 → <c>CraftReason.SpawnFailed</c> → 客户端提示 "Can't craft: the result could not be made"。
    ///   3) 单机不走这条路（单机是 <c>RecipeResult.SpawnResult</c>，CUCoreLib 已经 prefix 掉了并换成
    ///      <c>CustomInstantiate</c>），所以只有联机才会踩。
    ///   4) Together 会把这次失败按 attempt 缓存成 receipt，同一会话里重试仍然是失败，
    ///      直到换层/重连换了 session —— 这就是“过一层又能合成”的观感。
    ///
    /// Let CCL and the original function finish first. Only a missing result within the
    /// native server craft transaction may be repaired. Entertainment remains independent.
    /// </summary>
    [HarmonyPatch]
    internal static class Utils_Create_ModItemPatch
    {
        private static readonly Dictionary<string, float> WarnedAt = new Dictionary<string, float>(StringComparer.Ordinal);
        private static float _lastLogAt = -99f;

        private static System.Reflection.MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Utils), "Create",
                new[] { typeof(string), typeof(Vector2), typeof(float) });
        }

        // HarmonyX runs every prefix, including after another prefix returned false.
        // Never instantiate here: CCL may already have produced the authoritative result.
        private static void Prefix(string id) => DoublePurchase.NoteId(id);

        private static bool _duplicating;

        /// <summary>
        /// 「道生一 一生二」的第二条（用户口径 2026-11）：**合成等其它“造东西”的行为有 20% 概率多出一件**。
        /// 商人购买不走这里（购买那条由 <see cref="DoublePurchase"/> 保证“必定两个”，用时间窗区分）。
        /// 只在主机做，且带递归保护（多出来的那件自己不会再触发一次）。
        /// </summary>
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(string id, Vector2 pos, float rot, ref GameObject __result)
        {
            TryFailedSpawn(id, pos, rot, ref __result);
            ApplyEntertainment(id, pos, rot, __result);
        }

        // Resources.Instantiate(null) throws instead of returning null. Recover only this
        // precise failure, inside a server crafting call; keep all other exceptions intact.
        private static Exception Finalizer(string id, Vector2 pos, float rot, ref GameObject __result, bool __runOriginal, Exception __exception)
        {
            if (__exception is ArgumentException missing && missing.Message == "The Object you want to instantiate is null." && __runOriginal
                && __result == null && TryFailedSpawn(id, pos, rot, ref __result))
            {
                ApplyEntertainment(id, pos, rot, __result);
                return null;
            }
            return __exception;
        }

        private static void ApplyEntertainment(string id, Vector2 pos, float rot, GameObject __result)
        {
            try
            {
                if (!MPfixPlugin.Active) return;
                if (__result == null) return;
                if (__result.GetComponent<Item>() == null) return;
                if (!MPfixPlugin.CfgDoublePurchase.Value) return;
                if (!Mp.IsRunning || !Mp.IsServer) return;
                if (_duplicating) return;                       // 防递归：复制出来的那件不再复制
                if (DoublePurchase.SuppressCraftDuplicate) return;   // 「一生二」赠送那件不再触发 20%
                if (DoublePurchase.InPurchaseWindow) return;    // 购买流程由 DoublePurchase 负责“必定两个”
                if (UnityEngine.Random.value >= 0.2f) return;   // 20%
                if (string.IsNullOrEmpty(id)) return;

                _duplicating = true;
                try
                {
                    GameObject extra = Utils.Create(id, pos, rot);
                    if (extra == null) return;
                    if (extra.GetComponent<Item>() == null) return;

                    RegisterOnHost(extra);      // 走幂等登记，联机才认这件多出来的
                    SyncNow(extra);
                    MPfixPlugin.Log.Info($"[道生一 一生二] 额外生成一件 '{id}'（20% 概率触发，已登记并推送给客机）。");
                }
                finally
                {
                    _duplicating = false;
                }
            }
            catch (Exception ex)
            {
                _duplicating = false;
                MPfixPlugin.Log.Debug("「一生二」复制失败：" + ex.Message);
            }
        }

        /// <summary>
        /// Only a failed server crafting creation is eligible. Use CCL's complete
        /// construction/activation path once; never retry after a partially failed construction.
        /// </summary>
        private static bool TryFailedSpawn(string id, Vector2 pos, float rot, ref GameObject result)
        {
            if (result != null || !CraftSpawnScope.ActiveFor(id) || _duplicating || DoublePurchase.SuppressCraftDuplicate || !MPfixPlugin.Active
                || !MPfixPlugin.CfgFixModItemCraftSpawn.Value || !Mp.IsRunning || !Mp.IsServer
                || string.IsNullOrWhiteSpace(id)) return false;
            try
            {
                if (Resources.Load(id) != null) return false;
                GameObject prefab = ModItemFactory.ResolvePrefab(id);
                if (prefab == null || prefab.GetComponent<Item>() == null)
                {
                    WarnOnce(id, $"[CraftFix] 生成失败 '{id}'：没有可用的物品预制体；注册表查询：{RegistryInfo(id)}");
                    return false;
                }
                GameObject made = ModItemFactory.Create(id, pos, Quaternion.Euler(0f, 0f, rot));
                if (made == null)
                {
                    WarnOnce(id, $"[CraftFix] 生成失败 '{id}'：模组生成入口未返回对象；注册表查询：{RegistryInfo(id)}");
                    return false;
                }
                // Assign immediately: even a diagnostic/registration failure must not cause another spawn.
                result = made;
                RegisterOnHost(made);
                if (Time.unscaledTime - _lastLogAt > 0.5f)
                { _lastLogAt = Time.unscaledTime; MPfixPlugin.Log.Info($"[CraftFix] 合成生成失败后已补救 '{id}'。"); }
                return true;
            }
            catch (Exception ex)
            {
                WarnOnce(id, $"[CraftFix] 生成失败 '{id}'：{ex.Message}；注册表查询：{RegistryInfo(id)}");
                return result != null;
            }
        }

        /// <summary>日志里带上注册表信息，方便判断到底是“没注册”还是“注册了但造不出来”。</summary>
        private static string RegistryInfo(string id)
        {
            try
            {
                Type t = AccessTools.TypeByName("CUCoreLib.Registries.ItemRegistry");
                if (t == null) return "ItemRegistry 类型不存在";
                var prop = t.GetProperty("RegisteredItems", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (prop?.GetValue(null) is System.Collections.IDictionary dict)
                {
                    bool has = dict.Contains(id);
                    return has ? $"已注册（{dict.Count} 条）" : $"未注册（{dict.Count} 条）";
                }
                return "RegisteredItems 不可读";
            }
            catch (Exception ex)
            {
                return "注册表查询异常：" + ex.Message;
            }
        }

        private static void WarnOnce(string id, string message)
        {
            float now = Time.unscaledTime;
            float last;
            if (WarnedAt.TryGetValue(id, out last) && now - last < 30f) return;
            if (WarnedAt.Count > 512) WarnedAt.Clear();
            WarnedAt[id] = now;
            MPfixPlugin.Log.Warn(message);
        }
        /// <summary>
        /// 主机侧把模组物品登记进联机的网络对象表。**幂等**：同一件物品无论被我们调还是被
        /// Together 的 <c>TryGetSyncInfoOrRegister</c> 调，都只会产生一个实体。
        ///
        /// Defensive table/tracker reconciliation for recovered items and entertainment bonuses.
        /// Current MP also writes the table from SyncInfo.CreateObject. Object identity deduplication
        /// cannot prevent two different instances created by competing prefixes; that is fixed above.
        /// </summary>
        internal static void RegisterOnHost(GameObject go)
        {
            try
            {
                if (go == null || !Mp.IsRunning || !Mp.IsServer) return;

                Type regType = Reflect.Type("Together.NetObjectRegistry");
                if (regType == null) return;

                IDictionary registry = SyncRegistry(regType);
                if (registry == null) return;

                // ① A live binding for this exact object is already sufficient.
                try
                {
                    if (registry.Contains(go))
                    {
                        if (LiveSyncInfo(registry[go])) return;
                        registry.Remove(go);
                    }
                }
                catch { /* ignored */ }

                // ② 对象身上已有现成的 SyncInfo → 只把它挂回表，不新建实体
                object existing = TrackerSyncInfo(go);
                if (LiveSyncInfo(existing))
                {
                    Adopt(registry, go, existing);
                    return;
                }

                // ③ 真正登记一次，并把结果写回 SyncRegistry（Together 只给水晶做这一步）
                System.Reflection.MethodInfo mi = AccessTools.Method(regType, "Server_EnsureItemIsNetworkRegistered", new[] { typeof(GameObject) });
                if (mi == null) return;

                object made = mi.Invoke(null, new object[] { go });
                if (made != null) Adopt(registry, go, made);
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Debug("物品网络登记失败：" + ex.Message);
            }
        }

        private static FieldInfo _syncRegistryField;
        private static Type _syncRegistryOwner;
        private static PropertyInfo _isDestroyed;
        private static Type _syncInfoType;

        private static bool LiveSyncInfo(object info)
        {
            if (info == null) return false;
            Type type = info.GetType();
            if (_syncInfoType != type)
            { _syncInfoType = type; _isDestroyed = AccessTools.Property(type, "IsDestroyed"); }
            return _isDestroyed != null && _isDestroyed.GetValue(info) is bool destroyed && !destroyed;
        }

        private static IDictionary SyncRegistry(Type regType)
        {
            try
            {
                if (_syncRegistryOwner != regType)
                {
                    _syncRegistryField = regType.GetField("SyncRegistry", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    _syncRegistryOwner = regType;
                }
                return _syncRegistryField?.GetValue(null) as IDictionary;
            }
            catch { return null; }
        }

        /// <summary>对象身上的 `SyncInfoGameObjectTracker.syncInfo`（没有就返回 null）。</summary>
        private static object TrackerSyncInfo(GameObject go)
        {
            try
            {
                Type trackerType = Reflect.Type("Together.SyncInfoGameObjectTracker");
                if (trackerType == null) return null;
                Component tracker = go.GetComponent(trackerType);
                if (tracker == null) return null;
                FieldInfo f = trackerType.GetField("syncInfo", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                return f == null ? null : f.GetValue(tracker);
            }
            catch { return null; }
        }

        /// <summary>把 SyncInfo 写回 SyncRegistry，并补上它反向指向的 go（缺了这一步下次仍会重复建实体）。</summary>
        private static void Adopt(IDictionary registry, GameObject go, object syncInfo)
        {
            try
            {
                registry[go] = syncInfo;

                FieldInfo goField = syncInfo.GetType().GetField("go",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (goField != null && goField.GetValue(syncInfo) == null) goField.SetValue(syncInfo, go);

                // 有这一行说明"写回表"确实发生了 —— 之后 Together 的 TryGetSyncInfoOrRegister 会命中去重
                if (Time.unscaledTime - _lastAdoptLog > 0.5f)
                {
                    _lastAdoptLog = Time.unscaledTime;
                    MPfixPlugin.Log.Debug("[CraftFix] 已把登记结果写回网络对象表（避免重复实体 → 客机不再收到两份）");
                }
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Debug("登记后写回注册表失败：" + ex.Message);
            }
        }

        private static float _lastAdoptLog = -99f;

        /// <summary>
        /// 把一件已经在主机侧改过状态的物品推给所有客机：<c>NetObjectRegistry.SafeNewGO</c>。
        /// Together 自己在 <c>ServerReceiver__ItemWearing</c> 穿完物品后紧跟的就是这一句（反编译 136245）；
        /// 少了它，主机改对了、客机仍按旧状态渲染（典型表现：物品还贴在客机身边/仍在掉落）。
        /// </summary>
        internal static void SyncNow(GameObject go)
        {
            try
            {
                if (go == null || !Mp.IsRunning || !Mp.IsServer) return;

                Type regType = Reflect.Type("Together.NetObjectRegistry");
                if (regType == null) return;

                System.Reflection.MethodInfo mi = AccessTools.Method(regType, "SafeNewGO", new[] { typeof(GameObject) });
                if (mi == null) return;

                mi.Invoke(null, new object[] { go });
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Debug("物品状态推送失败：" + ex.Message);
            }
        }
    }
}
