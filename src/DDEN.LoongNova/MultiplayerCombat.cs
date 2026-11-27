using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DDEN.Shared;
using HarmonyLib;
using UnityEngine;

namespace DDEN.LoongNova
{
    internal static class MultiplayerCombat
    {
        private static Type _combat;
        private static MethodInfo _apply;
        private static Type _trackerType;
        private static PropertyInfo _body, _netBody;
        private static FieldInfo[] _context;
        private static FieldInfo _limbs, _buildings;
        private static bool _guards;
        internal static bool Ready => _guards && _apply != null;

        internal static void InstallGuards()
        {
            if (_guards) return;
            _combat = Reflect.Type("Together.TurretScript_Shoot_MultiplayerPatch");
            _apply = AccessTools.Method(_combat, "ApplyShootDamages");
            var validation = AccessTools.Method(Reflect.Type("Together.GunScript_Fire_MultiplayerPatch"), "ServerValidateShootHit");
            if (_apply == null || validation == null) throw new MissingMethodException("Together gun compatibility");
            var native = Reflect.Type("Together.GunScript_Fire_MultiplayerPatch");
            var prefix = AccessTools.Method(native, "Prefix");
            var postfix = AccessTools.Method(native, "Postfix");
            var combine = AccessTools.Method(Reflect.Type("Together.Body_CombineItems_MultiplayerPatch"), "Body_CombineItems");
            var eject = AccessTools.Method(Reflect.Type("Together.PlayerCamera_GunEjectMag_MultiplayerPatch"), "Postfix");
            var rack = AccessTools.Method(Reflect.Type("Together.PlayerCamera_GunRack_MultiplayerPatch"), "Postfix");
            var use = Reflect.Type("Together.Body_UseItem_MultiplayerPatch");
            _trackerType = Reflect.Type("Together.TrackerGunScript");
            if (prefix == null || postfix == null || combine == null || eject == null || rack == null || _trackerType == null)
                throw new MissingMethodException("Together gun compatibility");
            _body = AccessTools.Property(_trackerType, "body");
            _netBody = AccessTools.Property(_trackerType, "pbody");
            if (_body == null || _netBody == null) throw new MissingMemberException("Together gun ownership");
            string[] names = { "LastActiveShooter", "LastActiveShooterGun", "LastActiveShooterIsSuicide", "HitOnlyStatic", "SuppressBodyDamage", "last_fire_info" };
            _context = new FieldInfo[names.Length];
            for (int i = 0; i < names.Length; i++)
                _context[i] = AccessTools.Field(_combat, names[i]) ?? throw new MissingFieldException(names[i]);
            _limbs = AccessTools.Field(_combat, "JustHitLimbs") ?? throw new MissingFieldException("JustHitLimbs");
            _buildings = AccessTools.Field(_combat, "JustHitBuildings") ?? throw new MissingFieldException("JustHitBuildings");
            var harmony = new Harmony(LoongNovaPlugin.Guid + ".mp");
            try
            {
                harmony.Patch(validation, prefix: new HarmonyMethod(typeof(MultiplayerCombat), nameof(BlockNativeShot)));
                harmony.Patch(prefix, prefix: new HarmonyMethod(typeof(MultiplayerCombat), nameof(SkipNativeFire)));
                harmony.Patch(postfix, prefix: new HarmonyMethod(typeof(MultiplayerCombat), nameof(SkipNativeFire)));
                harmony.Patch(combine, prefix: new HarmonyMethod(typeof(MultiplayerCombat), nameof(SkipNativeCombine)));
                harmony.Patch(eject, prefix: new HarmonyMethod(typeof(MultiplayerCombat), nameof(SkipNativeCamera)));
                harmony.Patch(rack, prefix: new HarmonyMethod(typeof(MultiplayerCombat), nameof(SkipNativeCamera)));
                harmony.Patch(AccessTools.Method(use, "Prefix"), prefix: new HarmonyMethod(typeof(MultiplayerCombat), nameof(SkipNativeInjectorPrefix)));
                harmony.Patch(AccessTools.Method(use, "Postfix"), prefix: new HarmonyMethod(typeof(MultiplayerCombat), nameof(SkipNativeInjectorUse)));
                harmony.Patch(AccessTools.Method(Reflect.Type("Together.DrillPod_OnUse_MultiplayerPatch"), "OnUseRewritten"),
                    prefix: new HarmonyMethod(typeof(DrillPodSource), nameof(DrillPodSource.Multiplayer)));
                _guards = true;
            }
            catch { harmony.UnpatchSelf(); throw; }
        }

        private static bool SkipNativeInjectorPrefix(Item item, ref bool __result)
        {
            if (item == null || item.id != MedicalCatalog.InjectorId) return true;
            __result = true; // Allow Body.UseItem to invoke our delegate without a second native MP announcement.
            return false;
        }
        private static bool SkipNativeInjectorUse(Item item) => item == null || item.id != MedicalCatalog.InjectorId;
        private static bool SkipNativeFire(GunScript __0) => !Registration.IsGun(__0.GetComponent<Item>()?.id);
        private static bool SkipNativeCamera(PlayerCamera __0) => __0.body == null || !Registration.IsGun(__0.body.GetItem(__0.body.handSlot)?.id);

        private static bool SkipNativeCombine(Body __0, Item __1, Item __2, ref bool __result)
        {
            if (__1?.id == Registration.BeltId || __2?.id == Registration.BeltId || __1?.id == MedicalCatalog.InjectorId || __2?.id == MedicalCatalog.InjectorId)
            {
                // Belt operations always use the authenticated CCL channel, including tool-assisted ejection.
                __result = false;
                return false;
            }
            if (!Registration.IsGun(__1?.id) && !Registration.IsGun(__2?.id)) return true;
            var item = __1 != null && Registration.IsGun(__1.id) ? __1 : __2;
            var cell = item == __1 ? __2 : __1;
            if (Mp.CoreReady && Mp.IsRunning && Mp.IsServer && cell != null && (item.id == Registration.ChargeGunId ? CannonAmmoState.IsRound(cell.id) : cell.id == Registration.CellId))
                LoongNovaPlugin.Service.Execute("load", DragonGun.For(item), cell, __0);
            __result = false;
            return false;
        }

        // This weapon uses authenticated CCL requests. Reject the stock gun protocol for its ID.
        private static bool BlockNativeShot(object __0, ref bool __result)
        {
            var component = __0 as Component;
            if (component == null || !Registration.IsGun(component.GetComponent<Item>()?.id)) return true;
            __result = false;
            return false;
        }

        internal static void Fire(DragonGun gun, Body body, FireInfo fire)
        {
            if (!Ready || !Mp.CoreReady || !Mp.IsRunning || !Mp.IsServer) throw new InvalidOperationException("Multiplayer combat unavailable");
            if (gun.Tracker == null) gun.Tracker = gun.GetComponent(_trackerType) ?? gun.gameObject.AddComponent(_trackerType);
            float priorStructure = gun.Gun.structureDamage, priorCreature = gun.Gun.animalDamage;
            var tracker = gun.Tracker;
            var netBody = NetworkAccess.NetBody(body);
            _body.SetValue(tracker, body);
            _netBody.SetValue(tracker, netBody);
            object[] prior = new object[_context.Length];
            for (int i = 0; i < _context.Length; i++) prior[i] = _context[i].GetValue(null);
            var limbs = (IList)_limbs.GetValue(null);
            var buildings = (IList)_buildings.GetValue(null);
            object[] oldLimbs = new object[limbs.Count], oldBuildings = new object[buildings.Count];
            limbs.CopyTo(oldLimbs, 0); buildings.CopyTo(oldBuildings, 0);
            try
            {
                _context[0].SetValue(null, netBody);
                _context[1].SetValue(null, tracker);
                _context[2].SetValue(null, false);
                _context[3].SetValue(null, false);
                _context[4].SetValue(null, false);
                // Together's PvP path reads TrackerGunScript.og.animalDamage, not FireInfo.animalDamage.
                gun.Gun.structureDamage = fire.structureDamage; gun.Gun.animalDamage = fire.animalDamage;
                TurretScript.Shoot(fire);
                // A pulse can cross several colliders belonging to one body or building.
                var uniqueLimbs = new List<Limb>();
                var bodies = new HashSet<Body>();
                foreach (Limb limb in limbs) if (limb != null && bodies.Add(limb.body)) uniqueLimbs.Add(limb);
                var uniqueBuildings = (IList)Activator.CreateInstance(buildings.GetType());
                var seenBuildings = new HashSet<object>();
                foreach (object building in buildings) if (seenBuildings.Add(building)) uniqueBuildings.Add(building);
                _apply.Invoke(null, new object[] { tracker, uniqueLimbs, uniqueBuildings });
            }
            finally
            {
                gun.Gun.structureDamage = priorStructure; gun.Gun.animalDamage = priorCreature;
                for (int i = 0; i < _context.Length; i++) _context[i].SetValue(null, prior[i]);
                limbs.Clear(); foreach (object limb in oldLimbs) limbs.Add(limb);
                buildings.Clear(); foreach (object building in oldBuildings) buildings.Add(building);
            }
        }
    }
}
