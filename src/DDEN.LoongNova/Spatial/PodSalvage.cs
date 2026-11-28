using System;
using System.Reflection;
using DDEN.Shared;
using UnityEngine;

namespace DDEN.LoongNova
{
    // A per-object guard remains until Unity's deferred destruction completes.
    public sealed class PodSalvageClaim : MonoBehaviour { internal bool Busy, Consumed; }

    internal static class PodSalvage
    {
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private static MethodInfo _byObject, _byId;
        private static Type _syncType, _idType;
        private static object Sync(GameObject target)
        {
            _syncType = _syncType ?? Reflect.Type("Together.SyncInfo");
            if (_syncType == null) return null;
            _byObject = _byObject ?? Reflect.Type("Together.NetObjectRegistry")?.GetMethod("TryGetSyncInfo", Flags, null,
                new[] { typeof(GameObject), _syncType.MakeByRefType() }, null);
            var args = new object[] { target, null };
            return _byObject?.Invoke(null, args) is bool found && found ? args[1] : null;
        }
        internal static int Id(GameObject target)
        {
            var sync = target != null ? Sync(target) : null;
            var id = Reflect.GetInstance(sync, "syncId");
            return Reflect.GetInstance(id, "id") is ushort value ? value : 0;
        }
        internal static BuildingEntity Building(int id)
        {
            if (!Mp.CoreReady || !Mp.IsServer || id <= 0 || id > ushort.MaxValue) return null;
            _syncType = _syncType ?? Reflect.Type("Together.SyncInfo"); _idType = _idType ?? Reflect.Type("Together.knetid");
            if (_syncType == null || _idType == null) return null;
            _byId = _byId ?? Reflect.Type("Together.NetObjectRegistry")?.GetMethod("TryGetSyncInfo", Flags, null,
                new[] { _idType, _syncType.MakeByRefType() }, null);
            var args = new object[] { Activator.CreateInstance(_idType, (ushort)id), null };
            return _byId?.Invoke(null, args) is bool found && found ? Reflect.GetInstance(args[1], "building") as BuildingEntity : null;
        }
        internal static PodSalvageClaim Begin(GameObject target)
        {
            if (target == null || Mp.IsRunning && !Mp.IsServer) return null;
            var claim = target.GetComponent<PodSalvageClaim>() ?? target.AddComponent<PodSalvageClaim>();
            if (claim.Busy || claim.Consumed) return null;
            if (Mp.IsRunning && Sync(target)?.GetType().GetMethod("Destroy", Type.EmptyTypes) == null) return null;
            claim.Busy = true; return claim;
        }
        internal static bool Consume(GameObject target, PodSalvageClaim claim)
        {
            if (target == null || claim == null || !claim.Busy || claim.Consumed) return false;
            var building = target.GetComponent<BuildingEntity>();
            if (building == null) return false;
            object sync = Mp.IsRunning ? Sync(target) : null;
            var destroy = sync?.GetType().GetMethod("Destroy", Type.EmptyTypes);
            if (Mp.IsRunning && destroy == null) return false;
            var drops = building.itemsDropOnDestroy; var always = building.alwaysDrop;
            int guaranteed = building.guaranteedDropAmount; float chance = building.dropChanceMultiplier;
            try
            {
                claim.Consumed = true;
                building.itemsDropOnDestroy = Array.Empty<ItemDrop>(); building.alwaysDrop = Array.Empty<ItemDrop>();
                building.guaranteedDropAmount = 0; building.dropChanceMultiplier = 0;
                // Do not set health to zero: that invokes native loot and death behavior.
                if (destroy != null) destroy.Invoke(sync, null); else UnityEngine.Object.Destroy(target);
                return true;
            }
            catch (Exception error)
            {
                if (building != null) { building.itemsDropOnDestroy = drops; building.alwaysDrop = always; building.guaranteedDropAmount = guaranteed; building.dropChanceMultiplier = chance; }
                if (claim != null) claim.Consumed = false;
                LoongNovaPlugin.Log.Warn("Pod removal: " + error.GetBaseException().Message); return false;
            }
        }
        internal static Item HeldWrench(Body body)
        {
            var item = body != null ? body.GetItem(body.handSlot) : null;
            return item != null && item.id == Registration.WrenchId ? item : null;
        }
    }
}
