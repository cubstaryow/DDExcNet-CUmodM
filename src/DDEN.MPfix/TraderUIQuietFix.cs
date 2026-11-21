using System;
using System.Collections.Generic;
using System.Reflection;
using DDEN.Shared;
using HarmonyLib;
using UnityEngine;

namespace DDEN.MPfix
{
    /// <summary>
    /// 消掉 Together 商人界面的假警报：`[traderui] refresh … id=xxx prefab=null`。
    ///
    /// 来源（反编译 143661 `Together.TraderPinLog.LogRefreshFault`）：它在每次商人界面刷新后做一遍自检，
    /// 对货架上的每一件物品执行 `Resources.Load(item.id)`，取不到就 `log.error(... prefab=null)` 并 break。
    /// **模组物品本来就不在 Resources 里**（它们由 CUCoreLib 的 AssetLoader / CustomInstantiate 解析），
    /// 所以只要货架上有模组物品，每次刷新都会刷一条 ERROR —— 既不是故障，也不影响图标与购买
    /// （界面取图标走的是 `Item.GetItem(id)` 那条路，跟这个自检无关）。
    ///
    /// 修法：给这个方法加前缀 —— **当本次"取不到"的 id 全部都能被 CUCoreLib 解析时，跳过这段自检**；
    /// 只要有一个 id 连 CUCoreLib 也解析不出来（真故障：货架与主机不同步等），就照常让原版打印，
    /// 不掩盖真正的问题。
    /// </summary>
    internal static class TraderUIQuietFix
    {
        private const string TypeName = "Together.TraderPinLog";
        private const string MethodName = "LogRefreshFault";

        private static readonly HashSet<string> ModResolved = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> VanillaResolved = new HashSet<string>(StringComparer.Ordinal);
        private static float _lastLogAt = -99f;

        internal static void Install(Harmony harmony)
        {
            Type t = AccessTools.TypeByName(TypeName);
            MethodInfo m = t == null ? null : AccessTools.Method(t, MethodName, new[] { typeof(PlayerCamera) });
            if (m == null)
            {
                MPfixPlugin.Log.Warn($"[商人界面] 找不到 {TypeName}.{MethodName}（Together 版本变了？），这条不起作用（只会多刷日志，不影响功能）。");
                return;
            }

            harmony.Patch(m, prefix: new HarmonyMethod(typeof(TraderUIQuietFix), nameof(BeforeLogRefreshFault)));
            MPfixPlugin.Log.Info("已启用补丁：商人界面自检不再对模组物品报“prefab=null”");
        }

        /// <summary>返回 false = 跳过原版自检（本次“故障”只是模组物品不在 Resources 里）。</summary>
        private static bool BeforeLogRefreshFault(PlayerCamera cam)
        {
            try
            {
                if (!MPfixPlugin.Active) return true;
                if (!MPfixPlugin.CfgQuietTraderUIModItems.Value) return true;
                if (cam == null || cam.currentTrader == null) return true;

                List<TraderItem> items = cam.currentTrader.items;
                if (items == null || items.Count == 0) return true;

                bool sawModOnly = false;
                for (int i = 0; i < items.Count; i++)
                {
                    TraderItem it = items[i];
                    if (it == null) return true;                       // 真异常，交给原版
                    if (string.IsNullOrEmpty(it.id)) return true;      // 同上

                    if (VanillaResolved.Contains(it.id)) continue;
                    if (Resources.Load(it.id) != null)
                    {
                        if (VanillaResolved.Count < 4096) VanillaResolved.Add(it.id);
                        continue;
                    }

                    if (ModResolved.Contains(it.id) || ResolvableByMod(it.id))
                    {
                        sawModOnly = true;
                        continue;
                    }

                    return true;                                       // 连 CUCoreLib 也解析不出来 → 保留原版诊断
                }

                if (sawModOnly && Time.unscaledTime - _lastLogAt > 60f)
                {
                    _lastLogAt = Time.unscaledTime;
                    MPfixPlugin.Log.Debug("[商人界面] 本次自检的“prefab=null”都来自模组物品（不在 Resources），已静音。");
                }
                return !sawModOnly;
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Debug("[商人界面] 自检判定失败：" + ex.Message);
                return true;
            }
        }

        /// <summary>该 id 能否由 CUCoreLib 的资产解析器找到预制体（模组物品走这条路）。</summary>
        private static bool ResolvableByMod(string id)
        {
            try
            {
                GameObject prefab = ModItemFactory.ResolvePrefab(id);
                if (prefab == null) return false;
                if (ModResolved.Count < 4096) ModResolved.Add(id);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
