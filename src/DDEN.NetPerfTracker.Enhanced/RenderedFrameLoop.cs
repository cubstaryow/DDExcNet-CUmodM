using System;
using System.Collections.Generic;
using System.Diagnostics;
using DDEN.Shared;
using UnityEngine;
using UnityEngine.LowLevel;

namespace DDEN.NetPerfTracker.Enhanced
{
    // Timing bookends only: preserve every original node/delegate and their ordering.
    // Cleanup edits the CURRENT loop and removes only our private marker types.
    internal sealed class RenderedFrameLoop : IDisposable
    {
        private sealed class BeginMarker { }
        private sealed class EndMarker { }
        private sealed class StageBegin { }
        private sealed class StageEnd { }
        private static readonly Type[] Stages = {
            typeof(UnityEngine.PlayerLoop.Update.ScriptRunBehaviourUpdate), typeof(UnityEngine.PlayerLoop.FixedUpdate.ScriptRunBehaviourFixedUpdate),
            typeof(UnityEngine.PlayerLoop.PreLateUpdate.ScriptRunBehaviourLateUpdate), typeof(UnityEngine.PlayerLoop.FixedUpdate.Physics2DFixedUpdate) };
        private readonly TimingLedger _ledger;
        private readonly long[] _stageStarts = new long[4], _stageTicks = new long[4];
        private readonly int[] _stageCalls = new int[4];
        private readonly bool[] _found = new bool[4];
        private bool _open, _installed, _manualExport, _warmingFrame;
        private long _begin;
        private int _gc0, _gc1, _gc2;
        private double _warmUntil;
        internal bool Installed => _installed;
        internal RenderedFrameLoop(TimingLedger ledger) { _ledger = ledger; }
        internal void Install()
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop(); Remove(ref loop); Array.Clear(_found, 0, _found.Length); Decorate(ref loop);
            var list = new List<PlayerLoopSystem>(loop.subSystemList ?? Array.Empty<PlayerLoopSystem>());
            list.Insert(0, new PlayerLoopSystem { type = typeof(BeginMarker), updateDelegate = Begin });
            list.Add(new PlayerLoopSystem { type = typeof(EndMarker), updateDelegate = End }); loop.subSystemList = list.ToArray();
            _warmUntil = Time.realtimeSinceStartupAsDouble + 2;
            PlayerLoop.SetPlayerLoop(loop); _installed = true;
        }
        private void Decorate(ref PlayerLoopSystem parent)
        {
            if (parent.subSystemList == null) return;
            var output = new List<PlayerLoopSystem>();
            foreach (var original in parent.subSystemList)
            {
                var child = original; Decorate(ref child); int index = Array.IndexOf(Stages, child.type);
                if (index < 0) { output.Add(child); continue; }
                if (_found[index]) throw new NotSupportedException("Duplicate timing stage: " + child.type);
                _found[index] = true; int slot = index;
                output.Add(new PlayerLoopSystem { type = typeof(StageBegin), updateDelegate = () => StartStage(slot) }); output.Add(child);
                output.Add(new PlayerLoopSystem { type = typeof(StageEnd), updateDelegate = () => EndStage(slot) });
            }
            parent.subSystemList = output.ToArray();
        }
        private void Begin()
        {
            if (!_ledger.Active) { _open = false; return; }
            _manualExport = false; _warmingFrame = Time.realtimeSinceStartupAsDouble < _warmUntil;
            _ledger.BeginRenderedFrame(); Array.Clear(_stageTicks, 0, 4); Array.Clear(_stageCalls, 0, 4);
            _gc0 = GC.CollectionCount(0); _gc1 = GC.CollectionCount(1); _gc2 = GC.CollectionCount(2);
            _begin = Stopwatch.GetTimestamp(); _open = true;
        }
        private void StartStage(int index) { if (_open) { _stageStarts[index] = Stopwatch.GetTimestamp(); _stageCalls[index]++; } }
        private void EndStage(int index) { if (_open) _stageTicks[index] += Stopwatch.GetTimestamp() - _stageStarts[index]; }
        private double Ms(int index) => _found[index] ? _stageTicks[index] * (1000.0 / Stopwatch.Frequency) : -1;
        private void End()
        {
            if (!_open || !_ledger.Active) return;
            long ended = Stopwatch.GetTimestamp(); _open = false;
            int role = !Mp.IsRunning ? 0 : Mp.IsServer ? 1 : 2;
            _ledger.CompleteRenderedFrame(new FrameMeta { Frame = Time.frameCount, T = Time.realtimeSinceStartupAsDouble,
                LoopMs = (ended - _begin) * (1000.0 / Stopwatch.Frequency), Warmup = _warmingFrame,
                ManualExport = _manualExport,
                Role = role, FixedSteps = _found[1] ? _stageCalls[1] : -1,
                GC0 = GC.CollectionCount(0) - _gc0, GC1 = GC.CollectionCount(1) - _gc1, GC2 = GC.CollectionCount(2) - _gc2,
                ScriptUpdateMs = Ms(0), ScriptFixedMs = Ms(1), ScriptLateMs = Ms(2), PhysicsMs = Ms(3) });
        }
        internal void MarkManualExport() { if (_open) _manualExport = true; }
        internal bool BoundaryPresent()
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop(); int begin = 0, end = 0;
            foreach (var child in loop.subSystemList ?? Array.Empty<PlayerLoopSystem>())
            { if (child.type == typeof(BeginMarker)) begin++; if (child.type == typeof(EndMarker)) end++; }
            var children = loop.subSystemList;
            return begin == 1 && end == 1 && children.Length >= 2 && children[0].type == typeof(BeginMarker) && children[children.Length - 1].type == typeof(EndMarker);
        }
        private static bool Owned(Type type) => type == typeof(BeginMarker) || type == typeof(EndMarker) || type == typeof(StageBegin) || type == typeof(StageEnd);
        private static void Remove(ref PlayerLoopSystem parent)
        {
            if (parent.subSystemList == null) return;
            var next = new List<PlayerLoopSystem>();
            foreach (var original in parent.subSystemList) { if (Owned(original.type)) continue; var child = original; Remove(ref child); next.Add(child); }
            parent.subSystemList = next.ToArray();
        }
        public void Dispose()
        {
            _open = false;
            if (!_installed) return;
            var loop = PlayerLoop.GetCurrentPlayerLoop(); Remove(ref loop); PlayerLoop.SetPlayerLoop(loop); _installed = false;
        }
    }
}
