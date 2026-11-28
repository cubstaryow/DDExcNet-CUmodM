using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using CUCoreLib.Data;
using CUCoreLib.Registries;

namespace DsToolExtension
{
    internal static class DsLootRules
    {
        internal const double DrillChance = 0.20;
        internal const double PlasmaChance = 0.20;
        internal const double LegPackChance = 0.15;
        internal const double AutoBeltChance = 0.15;
        internal const double AwardChance = 0.25;

        internal static bool IsTrader(int source) => source == 16 || source == 32 || source == 64 || source == 112;
        internal static bool IsContainer(int source) => source == 8 || source == 128 || source == 256;
        internal static bool Eligible(int source, string category)
            => (IsTrader(source) || IsContainer(source))
               && (category == "tool" || category == "container");
    }

    /// <summary>保底池：虚拟占位命中后按权重从四件物品中选一个（同 DDEN.LoongNova 的 ArsenalLootPool）。</summary>
    internal static class ArsenalLootPool
    {
        internal readonly struct Entry
        {
            internal readonly string Id;
            internal readonly double Weight;
            internal Entry(string id, double weight) { Id = id; Weight = weight; }
        }

        internal static IEnumerable<Entry> Entries(int source, string category)
        {
            if (!DsLootRules.Eligible(source, category)) yield break;
            if (category == "tool")
            {
                yield return new Entry(Plugin.DrillItemId, DsLootRules.DrillChance);
                yield return new Entry(Plugin.PlasmaItemId, DsLootRules.PlasmaChance);
            }
            else if (category == "container")
            {
                yield return new Entry(Plugin.LegPackItemId, DsLootRules.LegPackChance);
                yield return new Entry(Plugin.AutoBeltItemId, DsLootRules.AutoBeltChance);
            }
        }

        internal static string TryPick(int source, string category, double awardRoll, double weightRoll)
        {
            if (double.IsNaN(awardRoll) || awardRoll < 0 || awardRoll >= DsLootRules.AwardChance) return null;
            return Pick(source, category, weightRoll);
        }

        internal static string Pick(int source, string category, double roll)
        {
            if (double.IsNaN(roll) || roll < 0 || roll > 1) return null;
            double total = 0;
            foreach (Entry entry in Entries(source, category)) total += entry.Weight;
            if (total <= 0) return null;
            double cursor = roll * total;
            string last = null;
            foreach (Entry entry in Entries(source, category))
            {
                last = entry.Id;
                if (cursor < entry.Weight) return entry.Id;
                cursor -= entry.Weight;
            }
            return last;
        }
    }

    [HarmonyPatch]
    internal static class LootPoolPatches
    {
        private static IDictionary<DropPool, List<string>> _sourcePools;

        private static MethodBase TargetMethod()
        {
            Type registry = AccessTools.TypeByName("CUCoreLib.Registries.DropPoolRegistry");
            _sourcePools = (IDictionary<DropPool, List<string>>)AccessTools.Field(registry, "ExplicitPools")?.GetValue(null);
            return AccessTools.Method(registry, "TryGetRandomItemId");
        }

        private static bool Prefix(DropPool source, string fallbackCategory, ref string itemId, ref bool __result, out bool __state)
        {
            __state = false;
            if (Plugin.Enabled != null && !Plugin.Enabled.Value) return true;
            if (!DsLootRules.Eligible((int)source, fallbackCategory)) return true;

            List<string> category = null;
            ItemLootPool.pool?.TryGetValue(fallbackCategory, out category);
            List<string> explicitItems = null;
            _sourcePools?.TryGetValue(source, out explicitItems);
            int ordinary = (category?.Count ?? 0) + (explicitItems?.Count ?? 0);
            if (UnityEngine.Random.Range(0, ordinary + 1) != ordinary) return true;

            string rare = ArsenalLootPool.TryPick((int)source, fallbackCategory, UnityEngine.Random.value, UnityEngine.Random.value);
            if (rare == null) return true;
            itemId = rare;
            __result = true;
            __state = true;
            return false;
        }

        private static void Postfix(DropPool source, string fallbackCategory, ref string itemId, ref bool __result, bool __state)
        {
            if (__state || !__result || string.IsNullOrWhiteSpace(itemId)) return;
            if (Plugin.Enabled != null && !Plugin.Enabled.Value) return;
            if (!DsLootRules.Eligible((int)source, fallbackCategory)) return;

            double roll = UnityEngine.Random.value;
            if (roll < DsLootRules.DrillChance) { itemId = Plugin.DrillItemId; return; }
            roll -= DsLootRules.DrillChance;
            if (roll < DsLootRules.PlasmaChance) { itemId = Plugin.PlasmaItemId; return; }
            roll -= DsLootRules.PlasmaChance;
            if (roll < DsLootRules.LegPackChance) { itemId = Plugin.LegPackItemId; return; }
            roll -= DsLootRules.LegPackChance;
            if (roll < DsLootRules.AutoBeltChance) itemId = Plugin.AutoBeltItemId;
        }
    }
}
