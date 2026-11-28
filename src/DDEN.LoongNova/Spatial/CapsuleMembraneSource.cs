using System;
using CUCoreLib.Helpers;
using DDEN.Shared;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DDEN.LoongNova
{
    [HarmonyPatch(typeof(Openable), nameof(Openable.OnUse))]
    internal static class CapsuleMembraneSource
    {
        private static readonly PendingActions Requests = new PendingActions();
        private static bool Eligible(Openable capsule) => capsule != null && capsule.isKeypad
            && capsule.GetComponent<BuildingEntity>()?.id == "dropcapsule"
            && capsule.GetComponent<BuildingEntity>().health >= 0.5f;
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static bool Use(Openable __instance)
        {
            var body = PlayerCamera.main != null ? PlayerCamera.main.body : null;
            if (!Eligible(__instance) || PodSalvage.HeldWrench(body) == null) return true;
            if (!Mp.IsRunning || Mp.IsServer) Execute(body, __instance.GetComponent<BuildingEntity>());
            else
            {
                Requests.Expire(Time.unscaledTime, out _);
                if (Requests.Count != 0 || !LoongNovaPlugin.Service.Ready) return false;
                int id = PodSalvage.Id(__instance.gameObject);
                if (id == 0) return false;
                string request = Guid.NewGuid().ToString("N");
                var connection = NetworkAccess.Connection;
                Requests.Add(request, "capsule", Time.unscaledTime);
                bool sent = Mp.RequestServer(WeaponService.Channel, new JObject
                { ["request"] = request, ["operation"] = "spatial-extract", ["building"] = id }, reply =>
                {
                    if (!Requests.Finish(request) || !ReferenceEquals(connection, NetworkAccess.Connection)) return;
                    if (reply?.Value<bool>("ok") != true) PlayerCamera.main?.DoAlert(Registration.Text("spatialExchangeFailed"));
                });
                if (!sent) Requests.Finish(request);
            }
            return false;
        }
        internal static JObject Execute(Body body, BuildingEntity building)
        {
            if (Mp.IsRunning && !Mp.IsServer || body == null || !body.conscious || building == null
                || !Eligible(building.GetComponent<Openable>())
                || Mp.IsRunning && Vector2.Distance(body.transform.position, building.transform.position) > 20) return new JObject { ["ok"] = false };
            var wrench = PodSalvage.HeldWrench(body);
            if (wrench == null || !DragonizationRules.TrySpend(wrench.condition, 100, out float after)) return new JObject { ["ok"] = false };
            var claim = PodSalvage.Begin(building.gameObject);
            if (claim == null) return new JObject { ["ok"] = false };
            GameObject created = null;
            try
            {
                created = CustomInstantiate.InstantiateReturn(SpatialCatalog.MembraneId, building.transform.position, Quaternion.identity, 1);
                var membrane = created != null ? created.GetComponent<Item>() : null;
                if (membrane == null || !PodSalvage.Consume(building.gameObject, claim))
                { if (created != null) UnityEngine.Object.Destroy(created); return new JObject { ["ok"] = false }; }
                wrench.condition = after;
                if (membrane.GetComponent<FreshItemDrop>() == null) membrane.gameObject.AddComponent<FreshItemDrop>();
                try { body.AutoPickUpItem(membrane); } catch (Exception error) { LoongNovaPlugin.Log.Warn("Membrane pickup: " + error.Message); }
                return new JObject { ["ok"] = true };
            }
            catch (Exception error)
            {
                if (created != null) UnityEngine.Object.Destroy(created);
                LoongNovaPlugin.Log.Warn("Capsule exchange: " + error.GetBaseException().Message);
                return new JObject { ["ok"] = false };
            }
            finally { if (claim != null) claim.Busy = false; }
        }
    }
}
