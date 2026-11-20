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
