using System;
using System.Collections.Generic;
using CUCoreLib.Helpers;
using DDEN.Shared;
using HarmonyLib;
using UnityEngine;

namespace DDEN.LoongNova
{
    [HarmonyPatch(typeof(DrillPod), nameof(DrillPod.OnUse))]
    internal static class DrillPodSource
    {
        private static Item HeldWrench(Body body)
        {
            var item = body != null ? body.GetItem(body.handSlot) : null;
            return item != null && item.id == Registration.WrenchId ? item : null;
        }
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        internal static bool Use(DrillPod __instance)
        {
            if (Mp.IsRunning) return true; // The native MP interaction authenticates the user and target.
            var body = PlayerCamera.main != null ? PlayerCamera.main.body : null;
            var wrench = HeldWrench(body);
            if (wrench == null) return true;
            Give(body, __instance, wrench); return false;
        }
        internal static bool Multiplayer(Body user, DrillPod drel)
        {
            var wrench = HeldWrench(user);
            if (wrench == null) return true;
            if (Mp.IsRunning && Mp.IsServer) Give(user, drel, wrench);
            return false;
        }
        private static void Give(Body body, DrillPod pod, Item wrench)
        {
            if (body == null || pod == null || !body.conscious || Mp.IsRunning && !Mp.IsServer) return;
            var cases = new List<Item>(); var seen = new HashSet<Item>();
            foreach (var item in body.GetAllItemsThorough())
                if (item != null && item.id == CannonAmmoState.CarrierId && !item.favourited && seen.Add(item)) cases.Add(item);
            var result = DrillExchange.Prepare(wrench.condition, cases.Count, () =>
            {
                var created = CustomInstantiate.InstantiateReturn(CannonAmmoState.DrillId, pod.transform.position, Quaternion.identity, 1);
                var round = created != null ? created.GetComponent<Item>() : null;
                if (round == null && created != null) UnityEngine.Object.Destroy(created);
                return round;
            }, item => UnityEngine.Object.Destroy(item.gameObject), out float remaining, out var rounds);
            if (result != DrillExchangeResult.Success)
            {
                Alert(body, Registration.Text(result == DrillExchangeResult.MissingCases ? "drillMissingCases"
                    : result == DrillExchangeResult.InsufficientQuality ? "drillMissingQuality" : "drillExchangeFailed"));
                return;
            }
            wrench.condition = remaining; // SyncInfo's native item state carries authoritative condition to peers.
            for (int i = 0; i < DrillExchange.Rounds; i++) UnityEngine.Object.Destroy(cases[i].gameObject);
            foreach (var round in rounds)
            {
                if (round.GetComponent<FreshItemDrop>() == null) round.gameObject.AddComponent<FreshItemDrop>();
                try { body.AutoPickUpItem(round); }
                catch (Exception error) { LoongNovaPlugin.Log.Warn("Drill pod ammunition pickup: " + error.Message); }
            }
            Alert(body, Registration.Text("drillExchangeDone"));
        }
        private static void Alert(Body body, string message)
        {
            if (!Mp.IsRunning || PlayerCamera.main != null && PlayerCamera.main.body == body)
            { PlayerCamera.main?.DoAlert(message); return; }
            var player = Reflect.GetInstance(NetworkAccess.NetBody(body), "plr");
            player?.GetType().GetMethod("Server_DoAlertSingle", new[] { typeof(string).MakeByRefType(), typeof(bool) })
                ?.Invoke(player, new object[] { message, true });
        }
    }
}
