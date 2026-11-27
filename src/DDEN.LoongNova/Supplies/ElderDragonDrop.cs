using System;
using System.Collections;
using System.Runtime.CompilerServices;
using CUCoreLib.Helpers;
using DDEN.Shared;
using HarmonyLib;
using UnityEngine;

namespace DDEN.LoongNova
{
    [HarmonyPatch]
    internal static class ElderDragonDrop
    {
        private static readonly ConditionalWeakTable<BuildingEntity, BossDropGate> Gates =
            new ConditionalWeakTable<BuildingEntity, BossDropGate>();
        private struct DeathAttempt
        {
            internal BossDropGate Gate;
            internal Vector3 Position;
            internal int Players;
        }

        [HarmonyPatch(typeof(BuildingEntity), nameof(BuildingEntity.CheckDamage)), HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static void BeforeDeath(BuildingEntity __instance, bool ___didDestroyRoutine, out DeathAttempt __state)
        {
            __state = default;
            if (__instance == null || ___didDestroyRoutine || __instance.indestructible || !(__instance.health < 0.5f)
                || Mp.IsRunning && (!Mp.IsServer || !Mp.CoreReady)
                || __instance.GetComponent<ElderThornbackBehaviour>() == null) return;
            __state.Gate = Gates.GetValue(__instance, _ => new BossDropGate());
            __state.Position = __instance.transform.position;
            __state.Players = SupplyCrateRules.PlayerCount(Mp.IsRunning,
                Mp.IsRunning ? Reflect.GetStatic(Reflect.Type("Together.ScavPlayer"), "AllPlayers") as IEnumerable : null);
        }

        [HarmonyPatch(typeof(BuildingEntity), nameof(BuildingEntity.CheckDamage)), HarmonyPostfix]
        private static void AfterDeath(bool ___didDestroyRoutine, DeathAttempt __state)
        {
            if (__state.Gate == null || !___didDestroyRoutine || Mp.IsRunning && (!Mp.IsServer || !Mp.CoreReady)
                || !__state.Gate.TryClaim()) return;
            int count = SupplyCrateRules.DropCount(__state.Players, UnityEngine.Random.Range(0, 3));
            for (int i = 0; i < count; i++)
                Spawn(SupplyCrateRules.SealedId, __state.Position + (Vector3)(UnityEngine.Random.insideUnitCircle * 1.5f));
            if (UnityEngine.Random.value < LootRules.ElderChance) Spawn(Registration.DragonId, __state.Position);
        }

        private static void Spawn(string id, Vector3 position)
        {
            try
            {
                // Each host-created drop uses CCL's ordinary spawn/Item.Start synchronization path.
                var drop = CustomInstantiate.InstantiateReturn(id, position, Quaternion.identity, 1);
                if (drop == null) throw new InvalidOperationException("Elder reward creation failed: " + id);
                if (drop != null && drop.GetComponent<FreshItemDrop>() == null) drop.AddComponent<FreshItemDrop>();
            }
            catch (Exception error) { LoongNovaPlugin.Log.Warn("LoongNova elder drop: " + error.GetBaseException().Message); }
        }
    }
}
