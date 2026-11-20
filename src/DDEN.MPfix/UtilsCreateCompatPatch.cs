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
    /// 修法：给 <c>Utils.Create</c> 加前缀 —— Resources 里找不到、但 CUCoreLib 能解析出预制体时，
    /// 用 <c>CustomInstantiate.InstantiateReturn</c> 生成。这样所有走 Utils.Create 的路径
    /// （联机合成、掉落、世界生成）对模组物品都正常。
    /// </summary>
    [HarmonyPatch]
    internal static class Utils_Create_ModItemPatch
    {
        private static readonly HashSet<string> VanillaIds = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, float> WarnedAt = new Dictionary<string, float>(StringComparer.Ordinal);
        private static float _lastLogAt = -99f;

        private static System.Reflection.MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Utils), "Create",
                new[] { typeof(string), typeof(Vector2), typeof(float) });
        }

        private static bool Prefix(string id, Vector2 pos, float rot, ref GameObject __result)
        {
            try
            {
                // 商人购买窗口内记一下生成过什么 id，供「道生一 一生二」复制（不影响正常流程）
                DoublePurchase.NoteId(id);

                if (!MPfixPlugin.CfgFixModItemCraftSpawn.Value) return true;
                if (string.IsNullOrEmpty(id)) return true;
                if (VanillaIds.Contains(id)) return true;

                // ★ 客机不本地生成：物品生成一律以主机为权威。
                //   商人购买 / 合成 / 水晶掉落这些流程在客机上也会跑一遍“本地预测”，
                //   一旦我们让客机的 Utils.Create 成功造出模组物品，客机自己那份 + 主机同步来的那份
                //   就变成两份（“客机买商人东西买两份”）。客机保持原版行为（造不出来 → 交给主机同步）。
                if (Mp.IsRunning && !Mp.IsServer)
                {
                    return true;
                }

                // 与原版一致：用非泛型 Resources.Load（泛型 GameObject 可能漏掉非 GameObject 资源）
                UnityEngine.Object raw = Resources.Load(id);
                if (raw != null)
                {
                    if (VanillaIds.Count < 4096) VanillaIds.Add(id);
                    return true;
                }

                Quaternion quat = Quaternion.Euler(0f, 0f, rot);
                string reason;
                GameObject made = Resolve(id, pos, quat, out reason);

                if (made == null)
                {
                    // 失败只记一条可诊断的日志（同一个 id 30 秒内不重复刷），并交回原逻辑保持原版表现
                    WarnOnce(id, $"[CraftFix] 生成失败 '{id}'：不在 Resources，模组侧也解析不出来（{reason}）。" +
                                  "该配方在本局仍会报“无法生成物品”。");
                    return true;
                }

                Item item = made.GetComponent<Item>();
                if (item == null)
                {
                    // Resolve 已经做过校验，正常不会走到这里；万一走到，也只记录、不销毁任何对象。
                    WarnOnce(id, $"[CraftFix] 生成失败 '{id}'：拿到的对象没有 Item 组件（{reason}），已跳过兜底。");
                    return true;
                }

                __result = made;
                RegisterOnHost(made);   // 强制登记（没有开关：不登记联机就不认这件物品）

                if (Time.unscaledTime - _lastLogAt > 0.5f)
                {
                    _lastLogAt = Time.unscaledTime;
                    MPfixPlugin.Log.Info($"[CraftFix] 物品 '{id}' 不在 Resources，已用模组资源生成（{reason}）");
                }
                return false;
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Debug("Utils.Create 前缀异常：" + ex.Message);
                return true;
            }
        }

        private static bool _duplicating;

        /// <summary>
        /// 「道生一 一生二」的第二条（用户口径 2026-11）：**合成等其它“造东西”的行为有 20% 概率多出一件**。
        /// 商人购买不走这里（购买那条由 <see cref="DoublePurchase"/> 保证“必定两个”，用时间窗区分）。
        /// 只在主机做，且带递归保护（多出来的那件自己不会再触发一次）。
        /// </summary>
        private static void Postfix(string id, Vector2 pos, float rot, GameObject __result)
        {
            try
            {
                if (__result == null) return;
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
        /// 三级兜底：CUCoreLib 模板 →预制体直接实例化 →**克隆场上/背包里同 id 的现成物品**。
        /// 最后一级专治“修理类配方”：玩家手里必然有一件同 id 的破损物品（它就是材料），
        /// 拿它的外壳克隆一份，比任何注册表都可靠。
        /// </summary>
        /// <summary>
        /// 只走 CUCoreLib 的两条正路，**不克隆、不销毁**任何物品：
        ///  ① 先 `ResolvePrefab` 拿到预制体并确认它带 Item 组件，再 `Instantiate`（不产生“造出来又不能用的残次品”）；
        ///  ② 退一步直接用 CCL 的 `InstantiateReturn`（它内部会建模板 + 实例化）。
        /// 两条都不行 → 放弃兜底，交回原版逻辑（该配方在这局仍会失败，但与我们无关，也不会留下副作用）。
        /// </summary>
        private static GameObject Resolve(string id, Vector2 pos, Quaternion quat, out string reason)
        {
            var notes = new List<string>();
            Vector3 at = new Vector3(pos.x, pos.y, 0f);

            // ① 预制体路径：先验证，再实例化
            try
            {
                GameObject prefab = CUCoreLib.Helpers.CustomInstantiate.ResolvePrefab(id);
                if (prefab != null)
                {
                    if (prefab.GetComponent<Item>() == null)
                    {
                        notes.Add("ResolvePrefab 有对象但没有 Item 组件");
                    }
                    else
                    {
                        GameObject made = UnityEngine.Object.Instantiate(prefab, at, quat);
                        if (made != null)
                        {
                            reason = "ResolvePrefab+Instantiate";
                            return made;
                        }
                        notes.Add("Instantiate(prefab)=null");
                    }
                }
                else
                {
                    notes.Add("ResolvePrefab=null");
                }
            }
            catch (Exception ex)
            {
                notes.Add("ResolvePrefab 异常：" + ex.Message);
            }

            // ② CCL 自带模板构建
            try
            {
                GameObject made = CUCoreLib.Helpers.CustomInstantiate.InstantiateReturn(id, pos, quat);
                if (made != null)
                {
                    if (made.GetComponent<Item>() != null)
                    {
                        reason = "CustomInstantiate";
                        return made;
                    }
                    // 造出来但不是物品：不销毁（避免任何“删东西”的行为），只记录并放弃兜底。
                    notes.Add("CustomInstantiate 产出的对象没有 Item 组件（已忽略，未做销毁）");
                }
                else
                {
                    notes.Add("CustomInstantiate=null");
                }
            }
            catch (Exception ex)
            {
                notes.Add("CustomInstantiate 异常：" + ex.Message);
            }

            reason = string.Join("；", notes) + "；注册表查询：" + RegistryInfo(id);
            return null;
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
        /// 为什么必须幂等（2026-11 修「客机买一件变两份」）：
        /// `NetObjectRegistry.Server_EnsureItemIsNetworkRegistered`（反编译 140755）只有在
        /// **`SyncRegistry` 里已有该对象**时才提前返回；否则它会 `ServerEntityManager.AddEntity` 建**新实体**，
        /// 而把这个 `go` 写回 `SyncRegistry` 的那一步**只在“对象是水晶”的分支里**做。
        /// 也就是说：普通物品第一次调用建了实体却没进表 → 下一次调用又建一个。
        /// 而 `TraderCommit.RunBuy`（142460）在我们之后还会调一次 `TryGetSyncInfoOrRegister`，
        /// 于是同一件物品出现两个实体 → 客机收到两份。
        /// 所以这里补上 Together 漏掉的"写回表"，让第二次调用命中去重。
        /// </summary>
        internal static void RegisterOnHost(GameObject go)
        {
            try
            {
                if (go == null || !Mp.IsRunning || !Mp.IsServer) return;

                Type regType = CUCoreLib.Networking.MultiplayerApi.ResolveKrokMpType("NetObjectRegistry");
                if (regType == null) return;

                IDictionary registry = SyncRegistry(regType);
                if (registry == null) return;

                // ① 表里已经有这个对象 → 已登记，直接返回（这是"重复实体"的根因所在）
                try
                {
                    if (registry.Contains(go)) return;
                }
                catch { /* ignored */ }

                // ② 对象身上已有现成的 SyncInfo → 只把它挂回表，不新建实体
                object existing = TrackerSyncInfo(go);
                if (existing != null)
                {
                    Adopt(registry, go, existing);
                    return;
                }

                // ③ 真正登记一次，并把结果写回 SyncRegistry（Together 只给水晶做这一步）
                System.Reflection.MethodInfo mi = CUCoreLib.Networking.MultiplayerApi.FindKrokMpMethod(
                    regType, "Server_EnsureItemIsNetworkRegistered", new[] { typeof(GameObject) });
                if (mi == null) return;

                object made = mi.Invoke(null, new object[] { go });
                if (made != null) Adopt(registry, go, made);
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Debug("物品网络登记失败：" + ex.Message);
            }
        }

        private static IDictionary _syncRegistry;
        private static Type _syncRegistryOwner;

        private static IDictionary SyncRegistry(Type regType)
        {
            if (_syncRegistry != null && _syncRegistryOwner == regType) return _syncRegistry;
            try
            {
                FieldInfo f = regType.GetField("SyncRegistry",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                _syncRegistry = f == null ? null : f.GetValue(null) as IDictionary;
                _syncRegistryOwner = regType;
            }
            catch { _syncRegistry = null; }
            return _syncRegistry;
        }

        /// <summary>对象身上的 `SyncInfoGameObjectTracker.syncInfo`（没有就返回 null）。</summary>
        private static object TrackerSyncInfo(GameObject go)
        {
            try
            {
                Type trackerType = CUCoreLib.Networking.MultiplayerApi.ResolveKrokMpType("SyncInfoGameObjectTracker");
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

                Type regType = CUCoreLib.Networking.MultiplayerApi.ResolveKrokMpType("NetObjectRegistry");
                if (regType == null) return;

                System.Reflection.MethodInfo mi = CUCoreLib.Networking.MultiplayerApi.FindKrokMpMethod(
                    regType, "SafeNewGO", new[] { typeof(GameObject) });
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
