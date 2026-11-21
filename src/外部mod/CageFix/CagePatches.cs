using System;
using System.Collections.Generic;
using HarmonyLib;
using Newtonsoft.Json.Linq;

namespace CageFix
{
    internal static class CagePatches
    {
        internal static void FilterHeatSources(RecipeItem __instance, List<Item> items, ref List<Item> exclusions)
        {
            try
            {
                if (!Plugin.Active || !Plugin.Heat.Value || items == null || !CageRules.HeatRequest(__instance.specific, __instance.isLiquid, __instance.quality?.id)) return;
                List<Item> filtered = null;
                foreach (var item in items)
                {
                    if (item == null || item.id != CageRules.ItemId || CageRuntime.IsOn(item) || exclusions?.Contains(item) == true) continue;
                    if (filtered == null) filtered = exclusions == null ? new List<Item>() : new List<Item>(exclusions);
                    if (!filtered.Contains(item)) filtered.Add(item);
                }
                if (filtered != null) exclusions = filtered; // Never change a shared ingredient pool or caller exclusions.
            }
            catch (Exception ex) { Plugin.Error(ex, "筛选关闭的热源"); }
        }
        internal static void FilterHeatType(RecipeItem __instance, Item item, ref bool __result)
        {
            try
            {
                if (__result && Plugin.Active && Plugin.Heat.Value && item != null && item.id == CageRules.ItemId
                    && CageRules.HeatRequest(__instance.specific, __instance.isLiquid, __instance.quality?.id) && !CageRuntime.IsOn(item)) __result = false;
            }
            catch (Exception ex) { Plugin.Error(ex, "核对热源用途"); }
        }
        internal static void InstallPersistence(Harmony harmony)
        {
            var provider = CageNetwork.FindType("CUCoreLib", "BuiltInItemRuntimeSaveProvider");
            var capture = AccessTools.Method(provider, "Capture");
            var restore = AccessTools.Method(provider, "Restore");
            if (capture == null || restore == null) throw new MissingMemberException("CUCoreLib 物品存档接口");
            harmony.Patch(capture, postfix: new HarmonyMethod(typeof(CagePatches), nameof(CaptureState)));
            harmony.Patch(restore, postfix: new HarmonyMethod(typeof(CagePatches), nameof(RestoreState)));
        }
        private static void CaptureState(Item item, ref JToken __result)
        {
            try
            {
                if (Plugin.Active && Plugin.Remember.Value && item != null && item.id == CageRules.ItemId)
                    __result = CageRules.Save(__result, CageRuntime.IsOn(item));
            }
            catch (Exception ex) { Plugin.Error(ex, "记录加热笼开关"); }
        }
        private static void RestoreState(Item item, JToken payload, object context)
        {
            try
            {
                if (!Plugin.Active || !Plugin.Remember.Value || item == null || item.id != CageRules.ItemId) return;
                bool? on = CageRules.Load(payload);
                var state = CageRuntime.Track(item);
                if (!on.HasValue || state == null) return;
                bool desired = on.Value;
                CageRuntime.Set(state, desired);
                state.DeferredRestore = desired;
                int revision = state.DeferredRevision = state.Revision;
                var defer = context?.GetType().GetMethod("Defer", new[] { typeof(Action) });
                defer?.Invoke(context, new object[] { (Action)(() =>
                {
                    try { if (Plugin.Active && Plugin.Remember.Value && state != null && state.Revision == revision) { state.DeferredRestore = null; CageRuntime.Set(state, desired); } }
                    catch (Exception ex) { Plugin.Error(ex, "完成加热笼状态恢复"); }
                }) });
            }
            catch (Exception ex) { Plugin.Error(ex, "恢复加热笼开关"); }
        }
    }
}
