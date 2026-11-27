using DDEN.Shared;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace DDEN.LoongNova
{
    [HarmonyPatch]
    internal static class WeaponPatches
    {
        [HarmonyPatch(typeof(TurretScript), nameof(TurretScript.Shoot)), HarmonyTranspiler, HarmonyPriority(Priority.Last)]
        private static IEnumerable<CodeInstruction> CaptureEndpoint(IEnumerable<CodeInstruction> instructions)
        {
            var position = AccessTools.Method(typeof(LineRenderer), nameof(LineRenderer.SetPosition), new[] { typeof(int), typeof(Vector3) });
            var wrapper = AccessTools.Method(typeof(PulseVisual), nameof(PulseVisual.SetPosition));
            int replaced = 0;
            foreach (var instruction in instructions)
            {
                if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt)
                    && instruction.operand is MethodInfo call && call == position)
                {
                    var argument = new CodeInstruction(instruction) { opcode = OpCodes.Ldarg_0, operand = null };
                    yield return argument;
                    yield return new CodeInstruction(OpCodes.Call, wrapper);
                    replaced++;
                }
                else yield return instruction;
            }
            if (replaced != 2) throw new InvalidOperationException("Unexpected TurretScript line rendering layout");
        }

        [HarmonyPatch(typeof(Item), nameof(Item.GetItem)), HarmonyPostfix]
        private static void BatteryStats(string id, ref ItemInfo __result)
        {
            // Delegates are local behavior and cannot be reconstructed from CCL's JSON content snapshot.
            if (id == Registration.WrenchId && __result != null) __result.useAction = Registration.UseWrench;
            if (id == Registration.PickaxeId && __result != null) __result.useAction = Registration.UsePickaxe;
            if (id != Registration.CellId || Registration.CellStats == null) return;
            __result = Registration.CellStats;
            // Keep direct dictionary consumers consistent with Item.Stats after CCL reinjection.
            if (Item.GlobalItems != null) Item.GlobalItems[id] = __result;
        }

        [HarmonyPatch(typeof(Item), "Start"), HarmonyPostfix]
        private static void EnsureGunComponent(Item __instance)
        {
            if (Registration.IsGun(__instance.id))
            {
                DragonGun.For(__instance).Normalize();
                if (__instance.id == Registration.ChargeGunId && __instance.GetComponent<CannonChargeAudio>() == null)
                    __instance.gameObject.AddComponent<CannonChargeAudio>();
            }
            else if (__instance.id == Registration.BeltId) GravityBelt.For(__instance).RefreshEncumbrance();
        }

        [HarmonyPatch(typeof(BatteryItem), nameof(BatteryItem.LoadBattery)), HarmonyPrefix]
        private static bool PortableCharger(Item battery) => battery == null || battery.id != Registration.CellId;

        [HarmonyPatch(typeof(Body), nameof(Body.CanCombine)), HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static bool CanLoad(Item it1, Item it2, ref bool __result)
        {
            if (it1?.id == Registration.BeltId || it2?.id == Registration.BeltId)
            {
                var belt = it1?.id == Registration.BeltId ? it1 : it2;
                var other = belt == it1 ? it2 : it1;
                bool loaded = GravityBelt.For(belt).State.Cell.Loaded;
                __result = other != null && (other.id == Registration.CellId ? !loaded : loaded && IsTool(other));
                return false;
            }
            if (!Registration.IsGun(it1?.id) && !Registration.IsGun(it2?.id)) return true;
            var gun = it1 != null && Registration.IsGun(it1.id) ? it1 : it2;
            var cell = gun == it1 ? it2 : it1;
            var controller = DragonGun.For(gun);
            __result = cell != null && (gun.id == Registration.ChargeGunId
                ? CannonAmmoState.IsRound(cell.id) && controller.Ammo.CanLoad && !controller.State.Loaded && cell.condition > 0
                : cell.id == Registration.CellId && !controller.State.Loaded);
            return false;
        }

        [HarmonyPatch(typeof(Body), nameof(Body.CombineItems)), HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static bool Load(Body __instance, Item it1, Item it2)
        {
            if (it1?.id == Registration.BeltId || it2?.id == Registration.BeltId)
            {
                var belt = it1?.id == Registration.BeltId ? it1 : it2;
                var other = belt == it1 ? it2 : it1;
                if (other?.id == Registration.CellId) GravityBelt.For(belt).Request("load", __instance, other);
                else if (IsTool(other)) GravityBelt.For(belt).Request("eject", __instance);
                return false;
            }
            if (!Registration.IsGun(it1?.id) && !Registration.IsGun(it2?.id)) return true;
            var gun = it1 != null && Registration.IsGun(it1.id) ? it1 : it2;
            var cell = gun == it1 ? it2 : it1;
            if (cell != null && (gun.id == Registration.ChargeGunId ? CannonAmmoState.IsRound(cell.id) : cell.id == Registration.CellId)) DragonGun.For(gun).Request("load", __instance, cell);
            return false;
        }

        internal static bool IsTool(Item item) => item != null && item.Stats.HasTag("tool");

        [HarmonyPatch(typeof(GunScript), "Update"), HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static bool GunUpdate(GunScript __instance)
        {
            var item = __instance.GetComponent<Item>();
            if (item == null || !Registration.IsGun(item.id)) return true;
            var state = DragonGun.For(item);
            // Network content snapshots can replace CCL's useAction delegate with its default.
            if (item.id == Registration.GunId && __instance.triggerPressed && PlayerCamera.main != null)
                state.Request("shoot", PlayerCamera.main.body);
            __instance.triggerPressed = false;
            state.Normalize();
            return false;
        }

        [HarmonyPatch(typeof(GunScript), nameof(GunScript.UnloadMag)), HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static bool Eject(GunScript __instance)
        {
            var item = __instance.GetComponent<Item>();
            if (item == null || !Registration.IsGun(item.id)) return true;
            if (PlayerCamera.main != null) DragonGun.For(item).Request("eject", PlayerCamera.main.body);
            return false;
        }

        [HarmonyPatch(typeof(GunScript), nameof(GunScript.TryRack)), HarmonyPrefix]
        private static bool Rack(GunScript __instance)
        {
            var item = __instance.GetComponent<Item>();
            if (item != null && item.id == Registration.ChargeGunId && PlayerCamera.main != null)
                DragonGun.For(item).Request("rack", PlayerCamera.main.body);
            return !Registration.IsGun(item?.id);
        }

        [HarmonyPatch(typeof(GunScript), nameof(GunScript.LoadMag)), HarmonyPrefix]
        private static bool Ammunition(GunScript __instance) => !Registration.IsGun(__instance.GetComponent<Item>()?.id);

        [HarmonyPatch(typeof(GunScript), nameof(GunScript.Fire)), HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static bool NativeFire(GunScript __instance) => !Registration.IsGun(__instance.GetComponent<Item>()?.id);

    }
}
