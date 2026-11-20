using System;
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
                if (!MPfixPlugin.CfgFixModItemCraftSpawn.Value) return true;
                if (string.IsNullOrEmpty(id)) return true;
                if (VanillaIds.Contains(id)) return true;

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
                    WarnOnce(id, $"[CraftFix] 生成失败 '{id}'：拿到的对象 {made.name} 没有 Item 组件（{reason}）。");
                    UnityEngine.Object.Destroy(made);
                    return true;
                }

                __result = made;
                RegisterOnHost(made);

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

        /// <summary>
        /// 三级兜底：CUCoreLib 模板 →预制体直接实例化 →**克隆场上/背包里同 id 的现成物品**。
        /// 最后一级专治“修理类配方”：玩家手里必然有一件同 id 的破损物品（它就是材料），
        /// 拿它的外壳克隆一份，比任何注册表都可靠。
        /// </summary>
        private static GameObject Resolve(string id, Vector2 pos, Quaternion quat, out string reason)
        {
            var notes = new List<string>();

            try
            {
                GameObject made = CUCoreLib.Helpers.CustomInstantiate.InstantiateReturn(id, pos, quat);
                if (made != null)
                {
                    reason = "CustomInstantiate";
                    return made;
                }
                notes.Add("CustomInstantiate=null");
            }
            catch (Exception ex)
            {
                notes.Add("CustomInstantiate 异常：" + ex.Message);
            }

            try
            {
                GameObject prefab = CUCoreLib.Helpers.CustomInstantiate.ResolvePrefab(id);
                if (prefab != null)
                {
                    GameObject made = UnityEngine.Object.Instantiate(prefab, new Vector3(pos.x, pos.y, 0f), quat);
                    if (made != null)
                    {
                        reason = "ResolvePrefab+Instantiate";
                        return made;
                    }
                    notes.Add("Instantiate(prefab)=null");
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

            try
            {
                GameObject clone = CloneExisting<Item>(id, pos, quat, out string cloneNote);
                if (clone != null)
                {
                    reason = "克隆现存同 id 物品";
                    return clone;
                }
                notes.Add(cloneNote);
            }
            catch (Exception ex)
            {
                notes.Add("克隆异常：" + ex.Message);
            }

            reason = string.Join("；", notes) + "；注册表查询：" + RegistryInfo(id);
            return null;
        }

        private static GameObject CloneExisting<T>(string id, Vector2 pos, Quaternion quat, out string note) where T : Component
        {
            note = "场上没有同 id 物品可克隆";
            T[] all;
            try { all = UnityEngine.Object.FindObjectsOfType<T>(true); }
            catch (Exception ex) { note = "FindObjectsOfType 失败：" + ex.Message; return null; }

            foreach (T comp in all)
            {
                if (comp == null) continue;
                Item src = comp as Item;
                if (src == null || src.id != id) continue;

                GameObject clone = UnityEngine.Object.Instantiate(src.gameObject, new Vector3(pos.x, pos.y, 0f), quat);
                if (clone == null) continue;

                // 克隆出来的外壳要清干净：子物品不能跟着复制（否则等于凭空多出一堆东西），
                // 缩放/父级也归位（原件可能正被 HeldSeat 按肢体缩放过）。
                StripChildItems(clone);
                clone.transform.localScale = Vector3.one;
                note = "由现存实例克隆";
                return clone;
            }
            return null;
        }

        private static void StripChildItems(GameObject root)
        {
            try
            {
                var kids = new List<Transform>();
                foreach (Transform t in root.transform)
                {
                    if (t != null && t.GetComponent<Item>() != null) kids.Add(t);
                }
                for (int i = 0; i < kids.Count; i++)
                {
                    if (kids[i] != null) UnityEngine.Object.Destroy(kids[i].gameObject);
                }
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Debug("清理克隆子物品失败：" + ex.Message);
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
        /// 主机侧把模组物品登记进联机的网络对象表。
        /// 不登记的话：客机拿起/穿上后，主机在 <c>ServerReceiver__ItemWearing</c> 里 TryGetItem 找不到它
        /// → “Server declines itemwearing” → 主机看到物品掉在地上、过一会又被客机拽回身上（就是那个闪烁）。
        /// </summary>
        internal static void RegisterOnHost(GameObject go)
        {
            try
            {
                if (go == null || !Mp.IsRunning || !Mp.IsServer) return;

                Type regType = CUCoreLib.Networking.MultiplayerApi.ResolveKrokMpType("NetObjectRegistry");
                if (regType == null) return;

                System.Reflection.MethodInfo mi = CUCoreLib.Networking.MultiplayerApi.FindKrokMpMethod(
                    regType, "Server_EnsureItemIsNetworkRegistered", new[] { typeof(GameObject) });
                if (mi == null) return;

                mi.Invoke(null, new object[] { go });
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Debug("物品网络登记失败：" + ex.Message);
            }
        }
    }
}
