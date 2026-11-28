using System;
using System.Reflection;
using DDEN.Shared;
using HarmonyLib;
namespace DDEN.LoongNova
{
    internal static class SpatialDecayGuard
    {
        private static Harmony _harmony;
        private static bool _attempted;
        internal static bool Installed => _harmony != null;
        internal static void TryInstall()
        {
            if (Installed || _attempted) return;
            var target = AccessTools.Method(Reflect.Type("Together.Item_HandleDecay_MultiplayerPatch"), "Prefix");
            if (target == null) return;
            _attempted = true;
            try { Install(target); }
            catch (Exception error) { LoongNovaPlugin.Log.Warn("Spatial decay entry guard unavailable; retaining decay-only fallback: " + error.GetBaseException().Message); }
        }
        internal static void Install(MethodInfo target)
        {
            if (Installed || target == null || target.ReturnType != typeof(bool)) return;
            var parameters = target.GetParameters();
            if (parameters.Length != 1 || parameters[0].ParameterType != typeof(Item)) return;
            var harmony = new Harmony("com.dden.loongnova.spatial-decay");
            try { harmony.Patch(target, prefix: new HarmonyMethod(typeof(SpatialDecayGuard), nameof(Before))); }
            catch { harmony.UnpatchSelf(); throw; }
            _harmony = harmony;
        }
        private static bool Before(Item __0, ref bool __result)
        { if (!SpatialPatches.Frozen(__0)) return true; __result = false; return false; }
        internal static void Stop() { _harmony?.UnpatchSelf(); _harmony = null; _attempted = false; }
    }
}
