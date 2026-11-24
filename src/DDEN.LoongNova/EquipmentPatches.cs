using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CUCoreLib.Data;
using DDEN.Shared;
using HarmonyLib;
using UnityEngine;

namespace DDEN.LoongNova
{
    [HarmonyPatch]
    internal static class EquipmentPatches
    {
        [HarmonyPatch(typeof(Container), nameof(Container.GetEncumberance)), HarmonyPrefix]
        private static void BeltEncumbrance(Container __instance)
        {
            // Derive from the item's existing energy state on demand; no polling or extra MP messages.
            if (__instance.TryGetComponent<GravityBelt>(out var belt)) belt.RefreshEncumbrance(__instance);
        }

        [HarmonyPatch(typeof(Body), nameof(Body.Jump)), HarmonyPrefix]
        private static void BeforeJump(float ___jumpCooldown, out float __state) => __state = ___jumpCooldown;

        [HarmonyPatch(typeof(Body), nameof(Body.Jump)), HarmonyPostfix]
        private static void AfterJump(Body __instance, float ___jumpCooldown, float __state)
        {
            if (___jumpCooldown > __state && __instance.rb.velocity.y > 0 && GravityBelt.Local(__instance))
                GravityBelt.Worn(__instance)?.Boost(__instance);
        }

        [HarmonyPatch(typeof(Body), "OnCollisionEnter2D"), HarmonyPrefix]
        private static bool BodyLanding(Body __instance, Collision2D collision) =>
            !GravityBelt.Local(__instance) || GravityBelt.Worn(__instance)?.ProtectLanding(__instance, collision) != true;

        [HarmonyPatch(typeof(Limb), "OnCollisionEnter2D"), HarmonyPrefix]
        private static bool LimbLanding(Limb __instance, Collision2D col) =>
            !GravityBelt.Local(__instance.body) || GravityBelt.Worn(__instance.body)?.ProtectLanding(__instance.body, col) != true;

        [HarmonyPatch(typeof(Recipe), "get_fullName"), HarmonyPostfix]
        private static void FullRecipeName(Recipe __instance, ref string __result)
        {
            if (Refill(__instance)) __result = (__instance.hasMadeBefore ? "" : "<sprite index=25>") + Registration.Text("refillName");
        }
        [HarmonyPatch(typeof(Recipe), "get_simpleName"), HarmonyPostfix]
        private static void SimpleRecipeName(Recipe __instance, ref string __result)
        {
            if (Refill(__instance)) __result = Registration.Text("refillName");
        }
        private static bool Refill(Recipe recipe) => recipe.result.id == Registration.CellId
            && recipe.items != null && recipe.items.Any(item => item.specificId == Registration.CellId && item.noMaxCondition);
    }

    // Match the vanilla wrench's weight inside tool draws only, without widening other loot sources.
    [HarmonyPatch]
    internal static class MerchantPatches
    {
        private static readonly System.Type Registry = AccessTools.TypeByName("CUCoreLib.Registries.DropPoolRegistry");
        private static readonly FieldInfo Pools = AccessTools.Field(Registry, "ExplicitPools");
        private static MethodBase TargetMethod() => AccessTools.Method(Registry, "TryGetRandomItemId");
        private static void Postfix(DropPool source, string fallbackCategory, ref string itemId, ref bool __result)
        {
            if (fallbackCategory != "tool" || source != DropPool.Trader1 && source != DropPool.Trader2 && source != DropPool.Trader3
                || Mp.IsRunning && !Mp.IsServer || ItemLootPool.pool == null
                || !ItemLootPool.pool.TryGetValue("tool", out var tools)) return;
            var pools = Pools?.GetValue(null) as Dictionary<DropPool, List<string>>;
            List<string> extras = null;
            pools?.TryGetValue(source, out extras);
            int weight = tools.Count(id => id == "wrench") + (extras?.Count(id => id == "wrench") ?? 0);
            if (weight <= 0) return;
            int count = tools.Count + (extras?.Count ?? 0);
            int choice = Random.Range(0, count + weight * 2);
            if (choice < count) return;
            itemId = choice < count + weight ? Registration.DragonId : Registration.WrenchId;
            __result = true;
        }
    }
}
