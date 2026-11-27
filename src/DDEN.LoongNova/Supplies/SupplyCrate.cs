using System;
using System.Collections.Generic;
using System.Linq;
using CUCoreLib.Helpers;
using DDEN.Shared;
using HarmonyLib;
using UnityEngine;

namespace DDEN.LoongNova
{
    public sealed class SupplyCrate : MonoBehaviour
    {
        private readonly SupplyCrateOpening _opening = new SupplyCrateOpening();
        internal static void Open(Body body, Item item)
        {
            // Native Body.UseItem validates/relays the action in MP. Clients predict no spawns or consumption.
            if (Mp.IsRunning && !Mp.IsServer || body == null || !body.conscious || item == null
                || item.id != SupplyCrateRules.SealedId || item.condition <= 0) return;
            var crate = item.GetComponent<SupplyCrate>() ?? item.gameObject.AddComponent<SupplyCrate>();
            bool favourite = item.favourited;
            IReadOnlyList<string> contents;
            try
            {
                contents = SupplyCrateRules.Contents(SupplyCrateRules.PickVariant(UnityEngine.Random.Range(0, SupplyCrateRules.TotalVariantWeight)),
                    SupplyLootCatalog.Build(), count => UnityEngine.Random.Range(0, count), () => UnityEngine.Random.value);
            }
            catch (Exception failure)
            {
                LoongNovaPlugin.Log.Warn("Supply crate contents: " + failure.GetBaseException().Message);
                if (PlayerCamera.main != null && PlayerCamera.main.body == body) PlayerCamera.main.DoAlert(Registration.Text("supplyFailed"));
                return;
            }
            bool allowRevival = !contents.Contains(CannonAmmoState.RevivalId);
            bool ok = crate._opening.TryOpen(contents,
                () => Create(SupplyCrateRules.OpenId, body), id => Create(id, body), (box, part) => Store(box, part, allowRevival),
                made => UnityEngine.Object.Destroy(made.gameObject),
                () => UnityEngine.Object.Destroy(item.gameObject), out var opened, out var error);
            if (!ok)
            {
                if (error != null)
                {
                    LoongNovaPlugin.Log.Warn("Supply crate opening: " + error.GetBaseException().Message);
                    if (PlayerCamera.main != null && PlayerCamera.main.body == body)
                        PlayerCamera.main.DoAlert(Registration.Text("supplyFailed"));
                }
                return;
            }
            opened.favourited = favourite;
            if (opened.GetComponent<FreshItemDrop>() == null) opened.gameObject.AddComponent<FreshItemDrop>();
            try { body.AutoPickUpItem(opened); }
            catch (Exception failure) { LoongNovaPlugin.Log.Warn("Supply crate pickup: " + failure.GetBaseException().Message); }
        }

        private static Item Create(string id, Body body)
        {
            var made = CustomInstantiate.InstantiateReturn(id, body.transform.position, Quaternion.identity, 1);
            var item = made != null ? made.GetComponent<Item>() : null;
            if (item == null && made != null) UnityEngine.Object.Destroy(made);
            return item;
        }

        private static bool Store(Item box, Item item, bool allowRevival)
        {
            var container = box.GetComponent<Container>();
            if (container == null || !container.CanHoldItem(item)) return false;
            // Preserve normal crate-spawn loadouts; these guards already prevent duplicate grants.
            if (item.id == Registration.ChargeGunId) CannonSpawnAmmo.Give(item, allowRevival);
            else if (item.id == MedicalCatalog.InjectorId) MedicalInjector.For(item).GrantSpawnDose();
            if (item.id == "riflemagazine" || item.id == "boxof12gauge" || item.id == "smallmagazine")
            {
                var ammo = item.GetComponent<AmmoScript>();
                if (ammo == null || ammo.maxRounds <= 0) return false;
                ammo.rounds = ammo.maxRounds;
            }
            container.LoadItem(item);
            return item.transform.parent == box.transform;
        }
    }

    [HarmonyPatch(typeof(Item), nameof(Item.GetItem))]
    internal static class SupplyCratePatches
    {
        private static void Postfix(string id, ref ItemInfo __result)
        {
            if (id == SupplyCrateRules.SealedId && __result != null) __result.useAction = SupplyCrate.Open;
        }
    }
}
