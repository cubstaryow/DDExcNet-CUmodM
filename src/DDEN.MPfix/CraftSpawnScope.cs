using System;
using System.Reflection;
using DDEN.Shared;
using HarmonyLib;

namespace DDEN.MPfix
{
    /// <summary>Bounded to the native server craft transaction; finalizers restore nested scopes on every exit.</summary>
    internal static class CraftSpawnScope
    {
        [ThreadStatic] private static string _id;
        internal static bool ActiveFor(string id) => _id != null && string.Equals(_id, id, StringComparison.Ordinal);
        internal static bool Install(Harmony harmony)
        {
            try
            {
                var target = AccessTools.Method(Reflect.Type("Together.PlayerCamera_TryCraft_MultiplayerPatch"), "RecipeResult_SpawnResult");
                if (target == null) return false;
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(CraftSpawnScope), nameof(Before)),
                    finalizer: new HarmonyMethod(typeof(CraftSpawnScope), nameof(After)));
                return true;
            }
            catch (Exception ex) { MPfixPlugin.Log.Exception(ex, "安装合成失败作用域"); return false; }
        }
        private static void Before(RecipeResult rres, out string __state)
        {
            __state = _id;
            _id = MPfixPlugin.Active && MPfixPlugin.CfgFixModItemCraftSpawn.Value && Mp.IsRunning && Mp.IsServer
                && rres != null && !rres.isLiquid ? rres.id : null;
        }
        private static Exception After(Exception __exception, string __state)
        { _id = __state; return __exception; }
    }

    /// <summary>Late-bound: a soft dependency must not prevent the plugin loading without CCL.</summary>
    internal static class ModItemFactory
    {
        private static MethodInfo _resolve, _create;
        internal static UnityEngine.GameObject ResolvePrefab(string id)
        {
            if (_resolve == null) _resolve = AccessTools.Method(Reflect.Type("CUCoreLib.Helpers.CustomInstantiate"), "ResolvePrefab", new[] { typeof(string) });
            return _resolve?.Invoke(null, new object[] { id }) as UnityEngine.GameObject;
        }
        internal static UnityEngine.GameObject Create(string id, UnityEngine.Vector2 at, UnityEngine.Quaternion rotation)
        {
            if (_create == null) _create = AccessTools.Method(Reflect.Type("CUCoreLib.Helpers.CustomInstantiate"), "InstantiateReturn",
                new[] { typeof(string), typeof(UnityEngine.Vector3), typeof(UnityEngine.Quaternion), typeof(float?) });
            return _create?.Invoke(null, new object[] { id, (UnityEngine.Vector3)at, rotation, null }) as UnityEngine.GameObject;
        }
    }
}
