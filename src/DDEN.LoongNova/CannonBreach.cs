using System;
using System.Collections.Generic;
using CUCoreLib.Helpers;
using DDEN.Shared;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DDEN.LoongNova
{
    internal static class CannonBreach
    {
        private static readonly List<Vector2> OpenAreas = new List<Vector2>();
        private static readonly HashSet<Vector2Int> AreaCells = new HashSet<Vector2Int>();
        private static readonly Queue<Vector2> PendingDoors = new Queue<Vector2>();
        private static object _doorBlocks;
        private static object _doorConnection;
        internal static Vector2 Fire(Body owner, Vector2 origin, Vector2 direction, CannonRound profile)
        {
            if (Mp.IsRunning && (!Mp.IsServer || !Mp.CoreReady)) throw new InvalidOperationException("Breach requires authority");
            var hits = Physics2D.RaycastAll(origin, direction, profile.Range, LayerMask.GetMask("Ground", "Body", "Limb", "Descriptor"));
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                if (hit.collider == null) continue;
                if (Utils.GetBody(hit.collider.gameObject, out var body) && body == owner) continue;
                var building = hit.collider.GetComponentInParent<BuildingEntity>();
                if (hit.collider.gameObject.layer != 6 && body == null && (building == null || building.cantHit)) continue;
                Apply(hit.point, profile.Size); return hit.point;
            }
            return origin + direction * profile.Range;
        }
        internal static void Apply(Vector2 point, int size)
        {
            if (Mp.IsRunning && (!Mp.IsServer || !Mp.CoreReady)) throw new InvalidOperationException("Breach requires authority");
            var world = WorldGeneration.world;
            var center = world.WorldToBlockPos(point);
            var targets = new HashSet<GameObject>();
            // Include inactive traps and trigger-only facilities; colliders alone miss these.
            foreach (var script in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>(true))
            {
                if (script == null || !script.gameObject.scene.IsValid() || !script.gameObject.scene.isLoaded) continue;
                var cell = world.WorldToBlockPos(script.transform.position);
                if (!UtilityGeometry.InSquare(cell.x, cell.y, center.x, center.y, size)) continue;
                if (script.GetComponentInParent<Body>() != null || script.GetComponentInParent<Limb>() != null) continue;
                var building = script.GetComponentInParent<BuildingEntity>();
                if (building != null && building.animal && !BreachTargets.IsTarget(script.GetType().Name, building.id, true)) continue;
                var item = script.GetComponentInParent<Item>();
                if (item != null && item.transform.parent != null) continue; // Carried equipment is not a facility.
                if (script is CosmeticDoor door) { Open(door); continue; }
                if (building != null && building.id == "reinforceddoor") { building.Backgroundify(); continue; }
                if (BreachTargets.IsTarget(script.GetType().Name, building != null ? building.id : null, building != null && building.animal)) targets.Add(building != null ? building.gameObject : script.gameObject);
            }
            foreach (var target in targets) Salvage(target);
            DoorContext(); if (AreaCells.Add(center)) OpenAreas.Add(point);
            if (Mp.IsRunning) Mp.Broadcast(WeaponService.VisualChannel,
                new JObject { ["breachDoors"] = true, ["areas"] = new JArray(new JObject { ["x"] = point.x, ["y"] = point.y }) });
        }
        private static void DoorContext()
        {
            object blocks = Reflect.GetInstance(WorldGeneration.world, "worldBlocks");
            if (!ReferenceEquals(blocks, _doorBlocks) || !ReferenceEquals(NetworkAccess.Connection, _doorConnection))
            { OpenAreas.Clear(); AreaCells.Clear(); _doorBlocks = blocks; _doorConnection = NetworkAccess.Connection; }
        }
        internal static JObject CaptureDoors()
        {
            DoorContext();
            var areas = new JArray();
            foreach (var point in OpenAreas) areas.Add(new JObject { ["x"] = point.x, ["y"] = point.y });
            return new JObject { ["areas"] = areas };
        }
        internal static void ReceiveDoors(JToken packet)
        {
            if (!Mp.IsRunning || Mp.IsServer || !(packet?["areas"] is JArray areas)) return;
            foreach (var area in areas)
            {
                float x = area.Value<float>("x"), y = area.Value<float>("y");
                if (!float.IsNaN(x) && !float.IsInfinity(x) && !float.IsNaN(y) && !float.IsInfinity(y)) PendingDoors.Enqueue(new Vector2(x, y));
            }
        }
        internal static void TickDoors()
        {
            var world = WorldGeneration.world;
            if (!Mp.IsRunning || Mp.IsServer) { PendingDoors.Clear(); return; }
            if (world == null || !world.worldExists || world.generatingWorld) return;
            for (int i = 0; i < 8 && PendingDoors.Count > 0; i++)
            {
                var center = world.WorldToBlockPos(PendingDoors.Dequeue());
                foreach (var door in UnityEngine.Object.FindObjectsOfType<CosmeticDoor>(true))
                {
                    var cell = world.WorldToBlockPos(door.transform.position);
                    if (door.gameObject.scene.IsValid() && door.gameObject.scene.isLoaded
                        && UtilityGeometry.InSquare(cell.x, cell.y, center.x, center.y, 25)) Open(door);
                }
            }
        }
        internal static void ClearDoors() { OpenAreas.Clear(); AreaCells.Clear(); PendingDoors.Clear(); _doorBlocks = null; _doorConnection = null; }
        private static void Open(CosmeticDoor door)
        {
            door.forceDisable = false;
            Reflect.TrySetInstance(door, "open", true); Reflect.TrySetInstance(door, "side", 1f);
            foreach (var collider in door.GetComponents<Collider2D>()) collider.enabled = false;
            var building = door.GetComponent<BuildingEntity>();
            if (building != null) building.Backgroundify();
        }
        private static void Salvage(GameObject target)
        {
            if (target == null) return;
            // Do not kill the building: Mine.OnDestroy explodes only when its health is zero.
            // Retaining health also keeps remote destruction safe when native SyncInfo applies its last state.
            foreach (var mine in target.GetComponentsInChildren<MineScript>(true)) Reflect.TrySetInstance(mine, "exploded", true);
            foreach (var script in target.GetComponentsInChildren<MonoBehaviour>(true))
                if (script != null && BreachTargets.IsScript(script.GetType().Name)) { script.enabled = false; script.CancelInvoke(); script.StopAllCoroutines(); }
            foreach (var collider in target.GetComponentsInChildren<Collider2D>(true)) collider.enabled = false;
            foreach (var animator in target.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
            var building = target.GetComponent<BuildingEntity>();
            bool dropped = false;
            if (building != null)
            {
                dropped |= Drops(target.transform.position, building.itemsDropOnDestroy);
                dropped |= Drops(target.transform.position, building.alwaysDrop);
            }
            if (!dropped) Drop(target.transform.position, "scrapmetal", 1, 1);
            if (Mp.IsRunning && Mp.IsServer)
            {
                // Explicit native destruction distinguishes disarming from Together unloading a far-away mesh.
                var trackerType = Reflect.Type("Together.SyncInfoGameObjectTracker");
                var tracker = trackerType != null ? target.GetComponent(trackerType) : null;
                var sync = Reflect.GetInstance(tracker, "syncInfo");
                var syncType = Reflect.Type("Together.SyncInfo");
                var lookup = syncType != null ? Reflect.Type("Together.NetObjectRegistry")?.GetMethod("TryGetSyncInfo",
                    new[] { typeof(GameObject), syncType.MakeByRefType() }) : null;
                var args = new object[] { target, null };
                if (lookup != null && lookup.Invoke(null, args) is bool found && found) sync = args[1];
                var destroy = sync?.GetType().GetMethod("Destroy", Type.EmptyTypes);
                if (destroy != null) { destroy.Invoke(sync, null); return; }
            }
            UnityEngine.Object.Destroy(target);
        }
        private static bool Drops(Vector3 position, ItemDrop[] entries)
        {
            bool dropped = false;
            if (entries != null) foreach (var entry in entries)
                if (entry != null && !string.IsNullOrWhiteSpace(entry.id)) dropped |= Drop(position, entry.id, entry.conditionMin, entry.conditionMax);
            return dropped;
        }
        private static bool Drop(Vector3 position, string id, float min, float max)
        {
            var created = CustomInstantiate.InstantiateReturn(id, position, Quaternion.identity, UnityEngine.Random.Range(min, max));
            if (created == null) return false;
            if (created.GetComponent<FreshItemDrop>() == null) created.AddComponent<FreshItemDrop>();
            return true;
        }
    }
}
