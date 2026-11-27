using System;
using CUCoreLib.Data;
using CUCoreLib.Helpers;
using DDEN.Shared;
using HarmonyLib;
using UnityEngine;

namespace DDEN.LoongNova
{
    [StatusOptions(Key = "dden.loongnova.healing-field.v1", SaveEnabled = false, ClientAuthoritative = false)]
    public sealed class HealingFieldTreatment : BodyStatus
    {
        public float Remaining;
        internal float SyncIn;
    }
    internal static class HealingFieldEffects
    {
        internal static bool Apply(Body body, float seconds)
        {
            if (Mp.IsRunning && (!Mp.IsServer || !Mp.CoreReady)) throw new InvalidOperationException("Healing field requires authority");
            if (!RevivalEffects.IsPlayer(body) || !body.alive || float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= 0) return false;
            System.Collections.Generic.HashSet<Limb> missing = null;
            foreach (var limb in body.limbs)
                if (limb.dismembered) (missing ?? (missing = new System.Collections.Generic.HashSet<Limb>())).Add(limb);
            foreach (var limb in body.limbs)
                if (limb.dismembered && !limb.isHead && !limb.isVital && !limb.isAbdomen) limb.Reattach();
            foreach (var limb in body.limbs)
            {
                if (limb.dismembered || limb.prostheticInfo.hasProsthetic) continue;
                if (missing != null && missing.Contains(limb)) limb.muscleHealth = limb.skinHealth = MedicalRules.NewLimbHealth;
                limb.MendBone(); limb.UnDislocate();
                limb.muscleHealth = MedicalRules.HealHealth(limb.muscleHealth, 20 * seconds);
                limb.skinHealth = MedicalRules.HealHealth(limb.skinHealth, 20 * seconds);
                limb.bleedAmount = Mathf.Max(0, limb.bleedAmount - 30 * seconds);
                limb.infectionAmount = Mathf.Max(0, limb.infectionAmount - 40 * seconds);
                if (limb.infectionAmount == 0) limb.infected = false;
                limb.pain = Mathf.Max(0, limb.pain - 50 * seconds);
            }
            // Two raw blood units equal one percentage point of the displayed blood volume.
            if (body.bloodVolume < 100) body.bloodVolume = Mathf.Min(100, body.bloodVolume + 2 * seconds);
            if (body.bloodOxygen < 100) body.bloodOxygen = Mathf.Min(100, body.bloodOxygen + 2 * seconds);
            body.temperature = Mathf.MoveTowards(body.temperature, 37, seconds);
            body.internalBleeding = Mathf.Max(0, body.internalBleeding - 30 * seconds);
            body.hemothorax = Mathf.Max(0, body.hemothorax - 20 * seconds);
            body.septicShock = Mathf.Max(0, body.septicShock - 20 * seconds);
            MedicalEffects.RecoverBlood(body, seconds);
            return missing != null;
        }
    }
    [HarmonyPatch(typeof(Body), "Update")]
    internal static class HealingFieldTreatmentPatch
    {
        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        internal static void Treat(Body __instance)
        {
            var status = __instance.GetStatus<HealingFieldTreatment>();
            if (Mp.IsRunning && (!Mp.IsServer || !Mp.CoreReady)) return; // CCL status and native health snapshots come from the host.
            status.Remaining = __instance.alive && RevivalEffects.IsPlayer(__instance)
                ? HealingFields.RemainingAt(__instance.transform.position) : 0;
            if (status.Remaining <= 0) { status.SyncIn = 0; return; }
            bool reattached = HealingFieldEffects.Apply(__instance, Time.deltaTime);
            status.SyncIn -= Time.deltaTime;
            if (reattached || status.SyncIn <= 0) { MedicalEffects.PushHealth(__instance); status.SyncIn = 1; }
        }
    }
}
