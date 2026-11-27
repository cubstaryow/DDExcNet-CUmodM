using System.Reflection;
using DDEN.Shared;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DDEN.LoongNova
{
    internal static class CannonSpawnAmmo
    {
        internal static void Give(Item item, bool allowRevival = true)
        {
            if (item == null || item.id != Registration.ChargeGunId || Mp.IsRunning && !Mp.IsServer) return;
            var gun = DragonGun.For(item);
            if (gun.State.Loaded || !gun.Ammo.TryGrantSpawnRound(CannonSpawnWeights.Pick(Random.value, allowRevival))) return;
            gun.Known = true; gun.Normalize();
            if (Mp.IsRunning && Mp.CoreReady && NetworkAccess.Id(item) != 0)
                Mp.Broadcast(WeaponService.VisualChannel, new JObject { ["gun"] = NetworkAccess.Id(item), ["state"] = gun.CaptureState() });
        }
    }
    [HarmonyPatch(typeof(Body), nameof(Body.AutoPickUpItem))]
    internal static class CannonPurchaseAmmoPatch
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static void BeforePickup(Item item)
        {
            if (item == null || item.id != Registration.ChargeGunId && item.id != MedicalCatalog.InjectorId) return;
            var bought = item.GetComponent<BoughtItem>();
            // Native, CCL singleplayer and Together host purchases all add this marker before pickup.
            if (bought != null && bought.time >= 299)
            {
                if (item.id == Registration.ChargeGunId) CannonSpawnAmmo.Give(item);
                else MedicalInjector.For(item).GrantSpawnDose();
            }
        }
    }
    [HarmonyPatch]
    internal static class CannonDropAmmoPatch
    {
        // The drop factory covers generated crates/capsules, not arbitrary inventory storage or crafting.
        private static MethodBase TargetMethod() => AccessTools.Method(
            AccessTools.TypeByName("CUCoreLib.Registries.BuildingEntityRegistry"), "SpawnDrop");
        private static void Postfix(string itemId, GameObject __result)
        {
            if (__result == null) return;
            var item = __result.GetComponent<Item>();
            if (itemId == Registration.ChargeGunId) CannonSpawnAmmo.Give(item);
            else if (itemId == MedicalCatalog.InjectorId && item != null) MedicalInjector.For(item).GrantSpawnDose();
        }
    }
}
