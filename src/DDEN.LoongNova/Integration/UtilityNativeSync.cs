using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DDEN.Shared;

namespace DDEN.LoongNova
{
    // Keep Together optional. Terrain, drops, destruction and fluids use its existing protocols.
    internal static class UtilityNativeSync
    {
        private static readonly Queue<GridCell> FluidChunks = new Queue<GridCell>();
        private static readonly HashSet<int> Queued = new HashSet<int>();
        private static MethodInfo _flush, _sendFluid;
        private static Type _coordinate;
        internal static void FlushBlocks()
        {
            if (!Mp.IsRunning || !Mp.IsServer) return;
            if (_flush == null) _flush = Reflect.Type("Together.WorldGeneration_SetBlock_MultiplayerPatch")?.GetMethod("FlushChangedBlocks");
            if (_flush == null) throw new MissingMethodException("Together block update flush");
            _flush.Invoke(null, null);
        }
        internal static void FluidChanged(IEnumerable<GridCell> cells)
        {
            if (!Mp.IsRunning || !Mp.IsServer) return;
            // Installed Together packets cover 32x32 cells (FLUIDCHUNKSIZE is not the wire stride).
            foreach (var cell in cells)
            {
                int x = cell.X / 32, y = cell.Y / 32;
                if (Queued.Add(x + y * 65536)) FluidChunks.Enqueue(new GridCell(x, y));
            }
        }
        internal static void Tick()
        {
            if (!Mp.IsRunning || !Mp.IsServer || FluidChunks.Count == 0) return;
            _coordinate = _coordinate ?? Reflect.Type("Together.Vector2UInt8");
            _sendFluid = _sendFluid ?? Reflect.Type("Together.WorldChunkSync")?.GetMethod("Server_SendFluidChunk");
            if (_coordinate == null || _sendFluid == null) throw new MissingMethodException("Together fluid chunk sync");
            var players = Reflect.GetStatic(Reflect.Type("Together.ScavPlayer"), "AllPlayers") as IEnumerable;
            if (players == null) return;
            for (int i = 0; i < 4 && FluidChunks.Count > 0; i++)
            {
                var cell = FluidChunks.Dequeue(); Queued.Remove(cell.X + cell.Y * 65536);
                var coordinate = Activator.CreateInstance(_coordinate, cell.X, cell.Y);
                foreach (var player in players)
                {
                    if (player == null || Reflect.GetInstance(player, "IsLocal") is bool local && local) continue;
                    _sendFluid.Invoke(null, new[] { coordinate, player, (object)true });
                }
            }
        }
        internal static void Clear() { FluidChunks.Clear(); Queued.Clear(); }
    }
}
