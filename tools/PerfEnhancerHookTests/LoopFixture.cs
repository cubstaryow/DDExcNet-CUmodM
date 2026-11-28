// Owned PlayerLoop graph fixture. No Unity DLLs/native engine are loaded.
using System;
namespace UnityEngine
{ public static class Time { public static int frameCount; public static double realtimeSinceStartupAsDouble; } }
namespace UnityEngine.LowLevel
{
    public struct PlayerLoopSystem
    {
        public delegate void UpdateFunction();
        public Type type;
        public PlayerLoopSystem[] subSystemList;
        public UpdateFunction updateDelegate;
    }
    public static class PlayerLoop
    {
        public static PlayerLoopSystem Current;
        public static PlayerLoopSystem GetCurrentPlayerLoop() => Current;
        public static void SetPlayerLoop(PlayerLoopSystem loop) { Current = loop; }
    }
}
namespace UnityEngine.PlayerLoop
{
    public struct Update { public struct ScriptRunBehaviourUpdate { } }
    public struct FixedUpdate { public struct ScriptRunBehaviourFixedUpdate { } public struct Physics2DFixedUpdate { } }
    public struct PreLateUpdate { public struct ScriptRunBehaviourLateUpdate { } }
}
namespace DDEN.Shared
{ public static class Mp { public static bool IsRunning, IsServer; } }
