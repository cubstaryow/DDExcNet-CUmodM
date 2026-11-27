using System;
using System.Reflection;
using CUCoreLib.Helpers;
using DDEN.Shared;
using UnityEngine;

namespace DDEN.LoongNova
{
    internal static class RevivalEffects
    {
        internal const float LimbHealth = 25, BrainHealth = 80, BloodField = 40;

        internal static bool IsPlayer(Body body)
        {
            if (body == null) return false;
            if (!Mp.IsRunning) return PlayerCamera.main != null && PlayerCamera.main.body == body;
            var netBody = NetworkAccess.NetBody(body);
            return Reflect.GetInstance(netBody, "IsPlayer") is bool isPlayer && isPlayer
                && ReferenceEquals(Reflect.GetInstance(Reflect.GetInstance(netBody, "plr"), "body"), body);
        }

        internal static bool TryRevive(Body body)
        {
            if (Mp.IsRunning && (!Mp.IsServer || !Mp.CoreReady)) throw new InvalidOperationException("Revival requires authority");
            if (body == null || body.alive) return false;
            if (!IsPlayer(body)) return false;
            object netBody = Mp.IsRunning ? NetworkAccess.NetBody(body) : null;

            // Use native reattachment so the existing MP patch also restores collisions and hinges.
            foreach (var limb in body.limbs)
                if (limb.dismembered && !limb.isHead && !limb.isVital && !limb.isAbdomen) limb.Reattach();
            foreach (var limb in body.limbs)
            {
                limb.skinHealth = limb.muscleHealth = LimbHealth;
                limb.bleedAmount = limb.infectionAmount = limb.pain = 0;
                limb.infected = false;
                if (limb.isHead || limb.isVital)
                {
                    limb.MendBone(); limb.UnDislocate();
                }
                limb.GetStatus<MedicalTreatment>().Remaining = 0;
            }
            body.GetStatus<RegenerationComa>().Remaining = 0;
            body.brainHealth = BrainHealth;
            // Native display: 0.5 + bloodVolume / 200. Field 40 represents 70%, not 40%.
            body.bloodVolume = BloodField;
            body.bloodOxygen = 85; body.bloodPressure = 90; body.heartRate = 70;
            body.respiratoryRate = 100; body.breathing = true;
            body.bloodVesselSize = 1; body.bloodViscosity = body.heartRatePressureOffset = 0;
            body.fibrillationProgress = 0; body.fibrillationForced = false;
            body.strokeAmount = 0; body.hasPulmonaryEmbolism = false;
            body.internalBleeding = body.hemothorax = body.shock = body.painShock = body.traumaAmount = 0;
            body.averagePain = body.totalBleedSpeed = 0;
            body.temperature = 37; body.septicShock = Mathf.Min(body.septicShock, 20);
            body.venomCurrent = body.venomTotal = 0;
            body.sicknessAmount = Mathf.Min(body.sicknessAmount, 25);
            body.hunger = Mathf.Max(body.hunger, 25); body.thirst = Mathf.Clamp(body.thirst, 40, 120);
            body.energy = Mathf.Max(body.energy, 25); body.stamina = 25;
            body.consciousness = 40; body.sleeping = false;
            // Remaining sedatives/opioids must not immediately undo the restored respiration.
            if (body.TryGetComponent<Painkillers>(out var painkillers)) UnityEngine.Object.Destroy(painkillers);
            if (body.TryGetComponent<SleepingPills>(out var sedatives)) UnityEngine.Object.Destroy(sedatives);
            if (Mp.IsRunning) NotifyMultiplayer(netBody);
            MedicalEffects.PushHealth(body);
            return true;
        }

        private static void NotifyMultiplayer(object netBody)
        {
            // Reuse Together's revive marker and health packet. Its alive transition handles spectator/control recovery.
            var eventType = Reflect.Type("Together.BodyDamageEvent");
            var kind = Enum.Parse(eventType.GetNestedType("DamageEventType"), "Revive");
            var marker = Activator.CreateInstance(eventType, new[] { kind, netBody });
            netBody.GetType().GetMethod("OnDamageEvent").Invoke(netBody, new[] { marker });
            Reflect.Type("Together.SharedMain").GetMethod("ForceUpdatePlayerLists", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
        }
    }
}
