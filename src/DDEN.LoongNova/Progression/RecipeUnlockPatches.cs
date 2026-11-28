using System;
using System.Collections.Generic;
using System.Reflection;
using DDEN.Shared;
using HarmonyLib;

namespace DDEN.LoongNova
{
    [HarmonyPatch]
    internal static class RecipeUnlockPatches
    {
        [ThreadStatic] private static Body _craftBody;
        [ThreadStatic] private static bool _insideCommit;
        private struct Scope { internal Body Body; internal bool Active, Entered; }
        private static float _retry;
        internal static bool Installed { get; private set; }

        [HarmonyPatch(typeof(Recipe), "get_visible"), HarmonyPostfix]
        private static void Visible(Recipe __instance, ref bool __result)
        {
            if (__result)
                __result = RecipeUnlocks.Allows(__instance, PlayerCamera.main != null ? PlayerCamera.main.body : null);
        }
        [HarmonyPatch(typeof(Recipe), nameof(Recipe.GetItemsForRecipe)), HarmonyPrefix]
        private static bool Ingredients(Recipe __instance, ref List<Item> __result)
        {
            if (RecipeUnlocks.Allows(__instance, PlayerCamera.main != null ? PlayerCamera.main.body : null)) return true;
            __result = null; return false;
        }
        [HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.TryCraft)), HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static bool LocalCraft(PlayerCamera __instance)
        {
            int index = __instance.selectedRecipe;
            if (Recipes.recipes == null || index < 0 || index >= Recipes.recipes.Count
                || RecipeUnlocks.Allows(Recipes.recipes[index], __instance.body)) return true;
            __instance.DoAlert(Registration.Text(RecipeUnlocks.IsSmelting(Recipes.recipes[index])?"unlockNeedsSmelter":RecipeUnlocks.LockHint(Recipes.recipes[index].result?.id, __instance.body)));
            return false;
        }
        [HarmonyPatch(typeof(Body), nameof(Body.GetAllItemsThorough)), HarmonyPostfix]
        private static void Inventory(Body __instance, List<Item> __result) => RecipeUnlocks.Observe(__instance, __result);
        [HarmonyPatch(typeof(Body), nameof(Body.PickUpItem)), HarmonyPostfix]
        private static void PickedUp(Body __instance, Item item)
        {
            if (item != null && RecipeUnlocks.IsPlayer(__instance) && __instance.HoldingItem(item)) RecipeUnlocks.Acquired(__instance, item);
        }
        [HarmonyPatch(typeof(Container), nameof(Container.LoadItem)), HarmonyPostfix]
        private static void Loaded(Container __instance, Item __0)
        {
            if (__0 == null || __0.transform.parent != __instance.transform) return;
            var body = __instance.GetComponentInParent<Body>();
            if (RecipeUnlocks.IsPlayer(body)) RecipeUnlocks.Acquired(body, __0);
        }
        internal static void InstallMultiplayer()
        {
            if (Installed || UnityEngine.Time.unscaledTime < _retry) return;
            _retry = UnityEngine.Time.unscaledTime + 5;
            var harmony = new Harmony(LoongNovaPlugin.Guid + ".recipes");
            try
            {
                var commit = AccessTools.Method(Reflect.Type("Together.CraftCommit"), "Commit");
                var match = AccessTools.Method(Reflect.Type("Together.PlayerCamera_TryCraft_MultiplayerPatch"), "CheckIfTheseItemsAreEnoughForTheRecipe");
                if (commit == null || match == null || commit.GetParameters().Length != 5
                    || commit.GetParameters()[3].ParameterType != typeof(Recipe)
                    || match.ReturnType != typeof(List<Item>)) throw new MissingMethodException("MP recipe commit/matcher unavailable");
                harmony.Patch(commit, prefix: Method(nameof(BeginCommit)), finalizer: Method(nameof(EndCommit)));
                harmony.Patch(match, prefix: Method(nameof(ServerIngredients)));
                Installed = true;
            }
            catch (Exception error)
            {
                harmony.UnpatchSelf();
                LoongNovaPlugin.Log.Warn("Recipe unlock MP guard: " + error.GetBaseException().Message);
            }
        }
        private static HarmonyMethod Method(string name) => new HarmonyMethod(typeof(RecipeUnlockPatches).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static));
        private static void BeginCommit(object __0, out Scope __state)
        { __state = new Scope { Body = _craftBody, Active = _insideCommit, Entered = true }; _craftBody = Reflect.GetInstance(__0, "body") as Body; _insideCommit = true; }
        private static void EndCommit(Scope __state) { if (__state.Entered) { _craftBody = __state.Body; _insideCommit = __state.Active; } }
        private static bool ServerIngredients(Recipe __0, ref List<Item> __result)
        {
            if (!_insideCommit || RecipeUnlocks.Allows(__0, _craftBody)) return true;
            // Native Commit produces its normal Missing receipt before any material is lifted/spent.
            __result = null; return false;
        }
        internal static void Stop()
        { new Harmony(LoongNovaPlugin.Guid + ".recipes").UnpatchSelf(); Installed = false; _craftBody = null; _insideCommit = false; }
    }
}
