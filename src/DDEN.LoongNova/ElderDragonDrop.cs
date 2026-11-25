using System;
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
        }

        [HarmonyPatch(typeof(BuildingEntity), nameof(BuildingEntity.CheckDamage)), HarmonyPostfix]
        private static void AfterDeath(bool ___didDestroyRoutine, DeathAttempt __state)
        {
            if (__state.Gate == null || !___didDestroyRoutine || Mp.IsRunning && (!Mp.IsServer || !Mp.CoreReady)
                || !__state.Gate.TryRoll(UnityEngine.Random.value)) return;
            try
            {
                // The normal CCL factory/Item.Start path registers the single host-created drop for multiplayer.
                var drop = CustomInstantiate.InstantiateReturn(Registration.DragonId, __state.Position, Quaternion.identity, 1);
                if (drop != null && drop.GetComponent<FreshItemDrop>() == null) drop.AddComponent<FreshItemDrop>();
            }
            catch (Exception error) { LoongNovaPlugin.Log.Warn("LoongNova elder drop: " + error.GetBaseException().Message); }
        }
    }
}
