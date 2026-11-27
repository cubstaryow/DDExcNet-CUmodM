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

        [HarmonyPatch(typeof(Item), "get_fullName"), HarmonyPrefix]
        private static void BeltChargeName(Item __instance)
        {
            if (__instance.id == Registration.BeltId) GravityBelt.For(__instance).RefreshEncumbrance();
        }

        [HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.ItemHoverDescription)), HarmonyPostfix]
        private static void ItemEnergyHint(Item item, ref (string, string) __result)
        {
            if (item == null || !item.Stats.rec.recognizable) return;
            string hint;
            bool loaded;
            if (item.id == Registration.BeltId)
            {
                var state = GravityBelt.For(item).State;
                loaded = state.Cell.Loaded;
                hint = loaded
                    ? string.Format(Registration.Text("beltSocketLoaded"), state.Remaining, state.ChargeFraction * 100)
                    : Registration.Text("beltSocketEmpty");
            }
            else if (Registration.IsGun(item.id))
            {
                var gun = DragonGun.For(item);
                hint = gun.CellHint();
                loaded = gun.IsChargeGun ? gun.Ammo.Loaded : gun.State.Loaded;
            }
            else if (item.id == Registration.CellId)
            {
                // A loose cell's native condition is its charge; the gun's condition is weapon wear.
                float charge = Mathf.Clamp01(item.condition);
                hint = string.Format(Registration.Text("cellEnergy"), charge * EnergyState.Capacity, charge * 100);
                loaded = true;
            }
            else return;
            __result.Item2 = "<color=#5eff00><sprite index=" + (loaded ? "15" : "16") + " tint=1>" + hint + "</color>\n\n" + __result.Item2;
        }

        [HarmonyPatch(typeof(Body), nameof(Body.Jump)), HarmonyPrefix]
        private static void BeforeJump(float ___jumpCooldown, out float __state) => __state = ___jumpCooldown;

        [HarmonyPatch(typeof(Body), nameof(Body.Jump)), HarmonyPostfix]
        private static void AfterJump(Body __instance, float ___jumpCooldown, float __state, ref bool ___firstWallJump)
        {
            if (___jumpCooldown <= __state || !GravityBelt.Local(__instance)) return;
            var belt = GravityBelt.Worn(__instance);
            if (belt == null) return;
            // Climbing claws re-arm consecutive wall jumps. This passive ability needs no cell.
            ___firstWallJump = true;
            if (__instance.rb.velocity.y > 0) belt.Boost(__instance);
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

    // Keep CCL's ordinary draw intact unless this matching category wins the explicit rare-item roll.
    [HarmonyPatch]
    internal static class LootPoolPatches
    {
        private static IDictionary<DropPool, List<string>> _sourcePools;
        private static MethodBase TargetMethod()
        {
            var registry = AccessTools.TypeByName("CUCoreLib.Registries.DropPoolRegistry");
            _sourcePools = (IDictionary<DropPool, List<string>>)AccessTools.Field(registry, "ExplicitPools").GetValue(null);
            return AccessTools.Method(registry, "TryGetRandomItemId");
        }
        private static bool Prefix(DropPool source, string fallbackCategory, ref string itemId, ref bool __result, out bool __state)
        {
            __state = false;
            if (Mp.IsRunning && !Mp.IsServer || !LootRules.Eligible((int)source, fallbackCategory)) return true;
            List<string> category = null;
            ItemLootPool.pool?.TryGetValue(fallbackCategory, out category);
            _sourcePools.TryGetValue(source, out var explicitItems);
            int ordinaryEntries = (category?.Count ?? 0) + (explicitItems?.Count ?? 0);
            // One virtual CCL pool entry, alongside its category and source entries. Resolve it here
            // so no placeholder item ID can escape into spawning, trading, saves or network packets.
            if (Random.Range(0, ordinaryEntries + 1) != ordinaryEntries) return true;
            string rare = ArsenalLootPool.TryPick((int)source, fallbackCategory, Random.value, Random.value);
            if (rare == null) return true;
            itemId = rare; __result = true; __state = true;
            return false;
        }
        private static void Postfix(DropPool source, string fallbackCategory, ref string itemId, ref bool __result, bool __state)
        {
            if (__state || !__result || string.IsNullOrWhiteSpace(itemId) || Mp.IsRunning && !Mp.IsServer
                || !LootRules.Eligible((int)source, fallbackCategory)) return;
            string rare = LootRules.Select((int)source, fallbackCategory, Random.value);
            if (rare != null) itemId = rare;
        }
    }
}
