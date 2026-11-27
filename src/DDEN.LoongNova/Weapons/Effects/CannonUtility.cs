using System;
using System.Collections.Generic;
using DDEN.Shared;
using UnityEngine;

namespace DDEN.LoongNova
{
    internal static class CannonUtility
    {
        private static WorldGeneration _world;
        private static ushort[,] _blocks;
        private static byte[,] _fluid;
        private static object _connection;
        private static readonly Queue<IEnumerator<GridCell>> Drills = new Queue<IEnumerator<GridCell>>();
        private static readonly Queue<ConnectedFluid> Drains = new Queue<ConnectedFluid>();

        private static void Context()
        {
            var world = WorldGeneration.world;
            var fluid = FluidManager.main != null ? FluidManager.main.fluid : null;
            var blocks = Reflect.GetInstance(world, "worldBlocks") as ushort[,];
            if (_world == world && ReferenceEquals(_blocks, blocks)
                && ReferenceEquals(_fluid, fluid) && ReferenceEquals(_connection, NetworkAccess.Connection)) return;
            Clear(); _world = world; _blocks = blocks; _fluid = fluid;
            _connection = NetworkAccess.Connection;
        }

        internal static Vector2 Fire(Body owner, Vector2 origin, Vector2 direction, CannonRound profile)
        {
            if (Mp.IsRunning && (!Mp.IsServer || !Mp.CoreReady)) throw new InvalidOperationException("Utility ammunition requires authority");
            Context();
            if (_world == null || _blocks == null || !_world.worldExists) return origin;
            if (profile.Effect == CannonEffect.Breach) return CannonBreach.Fire(owner, origin, direction, profile);
            if (profile.Effect == CannonEffect.Drill)
            {
                var plan = new List<GridCell>();
                Vector2 end = origin;
                foreach (var cell in UtilityGeometry.Drill(_blocks, origin.x + _world.halfWidth, origin.y + _world.halfHeight,
                    direction.x, direction.y, profile.Range, profile.Size, center =>
                    {
                        Vector2 point = new Vector2(center.X - _world.halfWidth + 0.5f, center.Y - _world.halfHeight + 0.5f);
                        float distance = Mathf.Clamp(Vector2.Dot(point - origin, direction) + 1, 0, profile.Range);
                        end = origin + direction * distance;
                    })) plan.Add(cell);
                if (plan.Count > 0) Drills.Enqueue(plan.GetEnumerator());
                return end;
            }
            foreach (var cell in UtilityGeometry.Ray(origin.x + _world.halfWidth, origin.y + _world.halfHeight,
                direction.x, direction.y, profile.Range, _blocks.GetLength(0), _blocks.GetLength(1)))
            {
                Vector2 point = new Vector2(cell.X - _world.halfWidth + 0.5f, cell.Y - _world.halfHeight + 0.5f);
                // Fluids have no physics collider. Stop at the first liquid before solid ground.
                if (profile.Effect == CannonEffect.Drain && _fluid != null && cell.X < _fluid.GetLength(0)
                    && cell.Y < _fluid.GetLength(1) && _fluid[cell.X, cell.Y] != 0)
                {
                    Drains.Enqueue(new ConnectedFluid(_fluid, cell.X, cell.Y)); return point;
                }
                if (_blocks[cell.X, cell.Y] == 0) continue;
                return point;
            }
            return origin + direction * profile.Range;
        }

        internal static void Tick()
        {
            Context();
            CannonBreach.TickDoors();
            if (_world == null || !_world.worldExists || _world.generatingWorld || _blocks == null
                || Mp.IsRunning && (!Mp.IsServer || !Mp.CoreReady)) { Clear(); return; }
            if (Drills.Count > 0)
            {
                // Flush the native 96-entry queue around each <=64-cell batch, including other world edits.
                UtilityNativeSync.FlushBlocks();
                var chunks = new HashSet<Vector2Int>();
                int writes = 0;
                while (Drills.Count > 0 && writes < 64)
                {
                    var drill = Drills.Peek();
                    if (!drill.MoveNext()) { drill.Dispose(); Drills.Dequeue(); continue; }
                    var cell = drill.Current;
                    if (_blocks[cell.X, cell.Y] == 14) { drill.Dispose(); Drills.Dequeue(); continue; }
                    if (_blocks[cell.X, cell.Y] == 0) continue;
                    _world.SetBlockNoUpdate(new Vector2Int(cell.X, cell.Y), 0);
                    chunks.Add(new Vector2Int(cell.X / WorldGeneration.CHUNKSIZE, cell.Y / WorldGeneration.CHUNKSIZE)); writes++;
                }
                UtilityNativeSync.FlushBlocks();
                foreach (var chunk in chunks) _world.UpdateChunk(chunk);
            }
            if (Drains.Count > 0 && Drains.Peek().Step(8192))
            {
                // Commit together: simulation cannot refill earlier batches while the region is being erased.
                var region = Drains.Dequeue();
                foreach (var cell in region.Cells) _fluid[cell.X, cell.Y] = 0;
                UtilityNativeSync.FluidChanged(region.Cells);
            }
            UtilityNativeSync.Tick();
        }

        internal static void Clear()
        {
            foreach (var drill in Drills) drill.Dispose();
            Drills.Clear(); Drains.Clear(); UtilityNativeSync.Clear();
        }
    }
}
