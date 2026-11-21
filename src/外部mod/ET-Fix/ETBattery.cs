using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HeatingCageFix
{
    /// <summary>
    /// 原 ET-NoBatteryFix 的修复逻辑，并入 CageFix：
    ///  - 增强钛砍刀/钛镐没有电池时也能攻击（不改 condition，仍显示 0%）；
    ///  - 装上电池后重新点亮工具灯。
    /// </summary>
    internal static class ETBattery
    {
        private static readonly HashSet<string> TargetIds = new HashSet<string>();
        private static readonly HashSet<string> Reported = new HashSet<string>();

        internal static void Install(Harmony harmony)
        {
            try
            {
                RebuildTargets();
                harmony.PatchAll(typeof(ETBattery));
                if (Plugin.Instance != null) Plugin.Instance.Config.SettingChanged += (_, __) => RebuildTargets();
            }
            catch (Exception ex)
            {
                Plugin.Error(ex, "无电池修复安装失败");
            }
        }

        private static void RebuildTargets()
        {
            TargetIds.Clear();
            TargetIds.Add("enhancedmachete");
            TargetIds.Add("enhancedpickaxe");
            var extra = Plugin.ExtraIds;
            if (extra != null && !string.IsNullOrWhiteSpace(extra.Value))
            {
                foreach (var part in extra.Value.Split(','))
                {
                    var id = part.Trim();
                    if (id.Length > 0) TargetIds.Add(id);
                }
            }
        }

        private static bool Active => Plugin.NoBatteryEnabled == null || Plugin.NoBatteryEnabled.Value;

        [HarmonyPatch(typeof(Body), "UseItemInHand")]
        [HarmonyPriority(800)]
        [HarmonyPrefix]
        private static bool UseItemInHand_Prefix(Body __instance)
        {
            if (!Active) return true;
            if (__instance == null || !__instance.conscious) return true;
            int slot = __instance.handSlot;
            if (!__instance.HoldingItem(slot)) return true;
            Item item = __instance.GetItem(slot);
            if (item == null || !TargetIds.Contains(item.id)) return true;
            ItemInfo stats = item.Stats;
            if (stats == null) return true;
            if (Reported.Add(item.id))
                Plugin.LogInfo($"[ET-Fix] [无电池] {item.id} condition={item.condition:0.###} usable={stats.usable} usableLMB={stats.usableWithLMB} useAction={(object)stats.useAction != null}");
            if ((object)stats.useAction != null && stats.usableWithLMB)
            {
                stats.useAction(__instance, item);
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(BatteryItem), "LoadBattery")]
        [HarmonyPostfix]
        private static void LoadBattery_Postfix(BatteryItem __instance)
        {
            if (!Active) return;
            if (Plugin.ReenableLight != null && !Plugin.ReenableLight.Value) return;
            if (__instance == null) return;
            Item item = __instance.GetComponent<Item>();
            if (item == null || !TargetIds.Contains(item.id)) return;
            if (!__instance.hasBattery) return;
            MonoBehaviour[] comps = item.GetComponents<MonoBehaviour>();
            for (int i = 0; i < comps.Length; i++)
            {
                MonoBehaviour c = comps[i];
                if (c == null || c.GetType().Name != "CustomLightControl") continue;
                MethodInfo m = c.GetType().GetMethod("EnableLight", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (m != null)
                {
                    m.Invoke(c, null);
                    Plugin.LogInfo($"[ET-Fix] [无电池] {item.id}: light re-enabled after battery load.");
                }
                break;
            }
        }
    }
}
