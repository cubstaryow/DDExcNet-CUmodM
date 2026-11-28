using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace DDEN.LoongNova
{
    [HarmonyPatch]
    internal static class TacticalGogglesPatches
    {
        private static int _frame = -1;
        private static PlayerCamera _camera;
        private static bool _active;
        private static Renderer _mask;
        private static bool _maskEnabled;
        private static Material _screen;
        private static float _night, _brightness, _brightMult;
        internal static bool Active(PlayerCamera camera)
        {
            if (camera == null || camera != PlayerCamera.main || camera.body == null || !camera.body.conscious
                || WorldGeneration.world == null || !WorldGeneration.world.worldExists || WorldGeneration.world.generatingWorld) return false;
            if (_frame == Time.frameCount && _camera == camera) return _active;
            _frame = Time.frameCount; _camera = camera;
            var head = camera.body.LimbByName("Head");
            var item = head != null && !head.dismembered ? camera.body.GetWearableBySlotID("eyes", head) : null;
            return _active = item != null && item.id == Registration.TacticalGogglesId && item.condition > 0;
        }
        [HarmonyPatch(typeof(PlayerCamera), "HandleScreenShaders"), HarmonyPostfix, HarmonyPriority(Priority.Last)]
        private static void NightVision(PlayerCamera __instance)
        {
            if (!Active(__instance) || __instance.mainScreenPass == null) return;
            _screen = __instance.mainScreenPass;
            _night = _screen.GetFloat("_NightVisAmount"); _brightness = _screen.GetFloat("_Brightness"); _brightMult = _screen.GetFloat("_BrightMult");
            // Native dark-vision compensation only; never raise scene brightness or the bloom multiplier.
            _screen.SetFloat("_NightVisAmount", Mathf.Max(_night, 0.04f));
        }
        internal static bool ShouldDrawMask(bool enabled, PlayerCamera camera) => enabled && !(WorldGeneration.unchipped && Active(camera));
        [HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.HandleLineOfSight)), HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> SkipMaskCalculation(IEnumerable<CodeInstruction> instructions)
        {
            var getter = AccessTools.PropertyGetter(typeof(WorldGeneration), "lineOfSightEnabled");
            var field = AccessTools.Field(typeof(WorldGeneration), "lineOfSightEnabled");
            var filter = AccessTools.Method(typeof(TacticalGogglesPatches), nameof(ShouldDrawMask));
            foreach (var instruction in instructions)
            {
                yield return instruction;
                if (getter != null && instruction.Calls(getter) || field != null && instruction.opcode == OpCodes.Ldsfld && Equals(instruction.operand, field))
                { yield return new CodeInstruction(OpCodes.Ldarg_0); yield return new CodeInstruction(OpCodes.Call, filter); }
            }
        }
        [HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.HandleLineOfSight)), HarmonyPostfix]
        private static void Mask(PlayerCamera __instance)
        {
            if (__instance != PlayerCamera.main) return;
            if (!WorldGeneration.unchipped || !Active(__instance)) { RestoreMask(); return; }
            var renderer = __instance.LOSHole != null ? __instance.LOSHole.GetComponent<Renderer>() : null;
            if (_mask != renderer) { RestoreMask(); _mask = renderer; if (_mask != null) _maskEnabled = _mask.enabled; }
            if (_mask != null) _mask.enabled = false;
        }
        [HarmonyPatch(typeof(BuildingEntity), "Start"), HarmonyPostfix]
        private static void BuildingStarted(BuildingEntity __instance) => TacticalGogglesOverlay.Observe(__instance);
        [HarmonyPatch(typeof(TraderScript), "Start"), HarmonyPostfix]
        private static void TraderStarted(TraderScript __instance) => TacticalGogglesOverlay.Observe(__instance.GetComponentInParent<BuildingEntity>());
        [HarmonyPatch(typeof(PlayerCamera),nameof(PlayerCamera.WarnSoundCannon)),HarmonyPostfix]
        private static void SoundAlarm(PlayerCamera __instance,Vector2 pos)
        {if(__instance==PlayerCamera.main && Active(__instance))TacticalGogglesOverlay.SoundWarning(pos);}
        private static void RestoreMask() { if (_mask != null) _mask.enabled = _maskEnabled; _mask = null; }
        internal static void Reset()
        {
            RestoreMask();
            if (_screen != null) { _screen.SetFloat("_NightVisAmount", _night); _screen.SetFloat("_Brightness", _brightness); _screen.SetFloat("_BrightMult", _brightMult); }
            _screen = null; _frame = -1; _camera = null; _active = false;
        }
    }
}
