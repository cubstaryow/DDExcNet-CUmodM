using System;
using System.Collections.Generic;
using DDEN.Shared;
using HarmonyLib;
using UnityEngine;

namespace DDExcNet.CUmodM
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
        private static readonly HashSet<string> MissingIds = new HashSet<string>(StringComparer.Ordinal);
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
                if (!HubPlugin.CfgFixModItemCraftSpawn.Value) return true;
                if (string.IsNullOrEmpty(id)) return true;
                if (VanillaIds.Contains(id)) return true;

                // 原版资源在就直接走原逻辑（只多一次 Resources 查询，Unity 内部有缓存）
                GameObject vanilla = Resources.Load<GameObject>(id);
                if (vanilla != null)
                {
                    if (VanillaIds.Count < 4096) VanillaIds.Add(id);
                    return true;
                }

                if (MissingIds.Contains(id)) return true;

                GameObject made = null;
                try
                {
                    made = CUCoreLib.Helpers.CustomInstantiate.InstantiateReturn(
                        id, pos, Quaternion.Euler(0f, 0f, rot));
                }
                catch (Exception ex)
                {
                    Log.Debug($"CustomInstantiate('{id}') 失败：{ex.Message}");
                }

                if (made == null)
                {
                    if (MissingIds.Count < 4096) MissingIds.Add(id);
                    return true;   // 交给原逻辑（保持原版失败表现，不吞掉）
                }

                __result = made;
                if (Time.unscaledTime - _lastLogAt > 0.5f)
                {
                    _lastLogAt = Time.unscaledTime;
                    HubPlugin.Log.Info($"[CraftFix] 物品 '{id}' 不在 Resources，已用模组资源生成（修复联机合成失败）");
                }
                return false;
            }
            catch (Exception ex)
            {
                HubPlugin.Log.Debug("Utils.Create 前缀异常：" + ex.Message);
                return true;
            }
        }
    }
}
