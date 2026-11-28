using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DDEN.LoongNova
{
    [HarmonyPatch]
    internal static class SpatialPatches
    {
        [ThreadStatic] private static bool _matching;
        internal static bool Frozen(Item item)
        {
            var parent = item != null ? item.transform.parent : null;
            while (parent != null)
            {
                var device = parent.GetComponentInParent<MicroSpaceCrate>(true);
                if (device == null) return false;
                if (device.Item != null && device.Item.id == SpatialCatalog.CrateId && device.State.Powered) return true;
                parent = device.transform.parent;
            }
            return false;
        }
        [HarmonyPatch(typeof(Item), nameof(Item.HandleDecay)), HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static bool Decay(Item __instance, out float __state)
        {
            __state = DDEN.Shared.Mp.IsRunning && SpatialDecayGuard.Installed ? float.NaN : Frozen(__instance) ? __instance.condition : float.NaN;
            return float.IsNaN(__state);
        }
        // HarmonyX may still run MP's side-effecting prefix after our skip. Restore only this
        // passive decay invocation; attacks, crafting wear and container durability are unaffected.
        [HarmonyPatch(typeof(Item), nameof(Item.HandleDecay)), HarmonyPostfix, HarmonyPriority(Priority.Last)]
        private static void Preserve(Item __instance, float __state)
        { if (__instance != null && !float.IsNaN(__state)) __instance.condition = __state; }

        [HarmonyPatch(typeof(Item), nameof(Item.TimeUntilDecayed)), HarmonyPrefix]
        private static bool DecayEstimate(Item __instance, ref TimeSpan __result)
        {
            if (!Frozen(__instance)) return true;
            __result = TimeSpan.Zero; return false;
        }
        [HarmonyPatch(typeof(Item), "Start"), HarmonyPostfix]
        private static void Started(Item __instance)
        { if (SpatialCatalog.IsDevice(__instance.id)) MicroSpaceCrate.For(__instance).Refresh(); }

        [HarmonyPatch(typeof(Item), nameof(Item.GetItem)), HarmonyPostfix]
        private static void ItemInfo(string id, ref ItemInfo __result)
        { if (SpatialCatalog.IsDevice(id) && __result != null) __result.useAction = MicroSpaceCrate.Unload; }

        [HarmonyPatch(typeof(Container), nameof(Container.GetEncumberance)), HarmonyPrefix]
        private static void Encumbrance(Container __instance)
        { if (__instance.TryGetComponent<MicroSpaceCrate>(out var box)) { box.Settle(); box.Refresh(__instance); } }

        [HarmonyPatch(typeof(Container), nameof(Container.LoadItem)), HarmonyPrefix]
        private static void ContentsChanging(Container __instance)
        { if (__instance.TryGetComponent<MicroSpaceCrate>(out var box)) box.Settle(); }
        [HarmonyPatch(typeof(Container), nameof(Container.UnloadItem)), HarmonyPrefix]
        private static void ContentsLeaving(Container __instance) => ContentsChanging(__instance);

        [HarmonyPatch(typeof(PlayerCamera), "TryPerformInventoryAction"), HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static bool Inventory(PlayerCamera __instance, RaycastResult hit, List<RaycastResult> uiCasts, ref bool __result)
        {
            var dragged = __instance.dragItem;
            if (__instance.body == null || dragged == null || hit.gameObject == null
                || !hit.gameObject.TryGetComponent<InvButton>(out var button) || !button.Overlaps(uiCasts)) return true;
            var item = button.GetItem(); if (item == null || !SpatialCatalog.IsDevice(item.id)) return true;
            var box = MicroSpaceCrate.For(item);
            if (dragged.id == Registration.CellId)
            {
                if (box.State.Cell.Loaded) return true; // Additional cells belong in storage, where the module can recharge them.
                box.Request("load", __instance.body, dragged);
            }
            else if (box.State.Cell.Loaded && dragged.Stats.HasTag("tool"))
                box.Request("eject", __instance.body);
            else return true;
            __result = true; return false; // Never also put the cell or tool into the ordinary storage contents.
        }
        [HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.ItemHoverDescription)), HarmonyPostfix]
        private static void Hint(Item item, ref (string, string) __result)
        {
            if (item == null || !SpatialCatalog.IsDevice(item.id) || !item.Stats.rec.recognizable) return;
            var box = MicroSpaceCrate.For(item); box.Settle();
            var body = PlayerCamera.main != null ? PlayerCamera.main.body : null;
            if (DDEN.Shared.Mp.IsRunning && !DDEN.Shared.Mp.IsServer && body != null) box.Request("read", body);
            string hint = box.State.Cell.Loaded ? string.Format(Registration.Text("beltSocketLoaded"), box.State.Energy, box.State.Energy / 10)
                : Registration.Text("beltSocketEmpty");
            __result.Item2 = "<color=#5eff00><sprite index=" + (box.State.Cell.Loaded ? "15" : "16") + " tint=1>" + hint + "</color>\n\n" + __result.Item2;
        }
        [HarmonyPatch(typeof(Container), nameof(Container.CanHoldItem)), HarmonyPrefix]
        private static bool BatteriesOnly(Container __instance, Item item, ref bool __result)
        {
            if (__instance.GetComponent<Item>()?.id != SpatialCatalog.ChargerId) return true;
            __result = item != null && item.Stats is BatteryInfo && item.totalWeight <= __instance.maxWeightPerItem
                && __instance.GetHoldingWeight() + item.totalWeight <= __instance.maxWeight;
            return false;
        }
        [HarmonyPatch(typeof(RecipeItem), nameof(RecipeItem.GetMatchingItem)), HarmonyPrefix]
        private static bool EmptyCrate(RecipeItem __instance, List<Item> items, List<Item> exclusions, ref Item __result)
        {
            if (_matching || !__instance.specific || __instance.specificId != SupplyCrateRules.OpenId || __instance.ignoredId != SpatialCatalog.CrateId) return true;
            var eligible = items.FindAll(item => item != null && item.TryGetComponent<Container>(out var container) && container.itemCount == 0);
            _matching = true;
            try { __result = __instance.GetMatchingItem(eligible, exclusions); }
            finally { _matching = false; }
            return false;
        }
    }
}
