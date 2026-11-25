using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Together
{
    public static class Trace
    {
        public static readonly List<string> Calls = new List<string>();
        public static bool Throw, Skip;
        public static readonly Exception Failure = new InvalidOperationException("original fixture exception");
    }
    public static class ChunkSleep
    {
        private static readonly List<object> Entries = new List<object>();
        [MethodImpl(MethodImplOptions.NoInlining)] public static void Tick()
        { Trace.Calls.Add("ChunkSleep"); if (Trace.Throw) throw Trace.Failure; }
    }
    public static class BagSleep
    {
        private static readonly List<object> Rows = new List<object>();
        [MethodImpl(MethodImplOptions.NoInlining)] public static void Tick() => Trace.Calls.Add("BagSleep");
        [MethodImpl(MethodImplOptions.NoInlining)] public static void WriteHost() => Trace.Calls.Add("WriteHost");
    }
    public static class HeaterMultiplayerRewrite { [MethodImpl(MethodImplOptions.NoInlining)] public static void Tick() => Trace.Calls.Add("Heater"); }
    public static class FallCatch { [MethodImpl(MethodImplOptions.NoInlining)] public static void Tick() => Trace.Calls.Add("FallCatch"); }
    public static class BodyViewParticles { [MethodImpl(MethodImplOptions.NoInlining)] public static void Tick() => Trace.Calls.Add("Particles"); }
    public static class WallholeZones
    {
        private static readonly List<object> Holes = new List<object>();
        [MethodImpl(MethodImplOptions.NoInlining)] public static void Tick() => Trace.Calls.Add("Wallholes");
    }
}
namespace KrokV5Optimization
{
    public static class SafetyNet { [MethodImpl(MethodImplOptions.NoInlining)] public static void Tick() => Together.Trace.Calls.Add("Safety"); }
    public static class SlowStamp
    {
        private static readonly List<object> Jobs = new List<object>();
        [MethodImpl(MethodImplOptions.NoInlining)] public static void Tick() => Together.Trace.Calls.Add("Stamp");
    }
    public static class BagPlace
    {
        private static readonly List<object> Waiting = new List<object>(), Announcing = new List<object>();
        [MethodImpl(MethodImplOptions.NoInlining)] public static void Tick() => Together.Trace.Calls.Add("BagPlace");
    }
    public static class FixInvSync
    {
        private static readonly Dictionary<int,object> Pending = new Dictionary<int,object>(), Exhausted = new Dictionary<int,object>();
        [MethodImpl(MethodImplOptions.NoInlining)] public static void Tick() { Together.Trace.Calls.Add("Inventory"); Sweep(); }
        [MethodImpl(MethodImplOptions.NoInlining)] private static void Sweep() => Together.Trace.Calls.Add("Sweep");
    }
    public static class OptimizerMain
    {
        [MethodImpl(MethodImplOptions.NoInlining)] public static void Tick()
        { SafetyNet.Tick(); SlowStamp.Tick(); Together.BodyViewParticles.Tick(); Together.WallholeZones.Tick(); BagPlace.Tick(); FixInvSync.Tick(); }
    }
}

namespace Together
{
    public static class Net { [MethodImpl(MethodImplOptions.NoInlining)] public static void DoUpdate() {} }
    public class TransportSteamworks
    {
        [MethodImpl(MethodImplOptions.NoInlining)] public void Update() { _Update_PollMessages(); }
        [MethodImpl(MethodImplOptions.NoInlining)] public void _Update_PollMessages() {}
    }
    public interface NetTransportBase { void Update(); }
    public class TransportLiteNetLib : NetTransportBase
    { [MethodImpl(MethodImplOptions.NoInlining)] void NetTransportBase.Update() {} }
    public class SyncInfo
    {
        [MethodImpl(MethodImplOptions.NoInlining)] public void DestroyTrackedGameObject() {}
        [MethodImpl(MethodImplOptions.NoInlining)] public void Update() { WriteObjectIntoPacket(); }
        [MethodImpl(MethodImplOptions.NoInlining)] public void WriteObjectIntoPacket() {}
        [MethodImpl(MethodImplOptions.NoInlining)] public void ApplyPacketToObject() {}
    }
    public class Plugin
    {
        [MethodImpl(MethodImplOptions.NoInlining)] public void Update() {}
        [MethodImpl(MethodImplOptions.NoInlining)] public void FixedUpdate() {}
        [MethodImpl(MethodImplOptions.NoInlining)] public void LateUpdate() {}
    }
    public class ServerMain
    {
        [MethodImpl(MethodImplOptions.NoInlining)] public void Update() {}
        [MethodImpl(MethodImplOptions.NoInlining)] public void LateUpdate() {}
    }
}

namespace Together
{
    public class SyncInfoGameObjectTracker
    {
        [MethodImpl(MethodImplOptions.NoInlining)] public void FixedUpdate() { TickSync(); }
        [MethodImpl(MethodImplOptions.NoInlining)] public void TickSync() {}
        [MethodImpl(MethodImplOptions.NoInlining)] public static void FlushFrameSamples() {}
    }
}
