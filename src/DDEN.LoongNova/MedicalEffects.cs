using System;
using System.Collections.Generic;
using System.Reflection;
using CUCoreLib.Data;
using CUCoreLib.Helpers;
using DDEN.Shared;
using HarmonyLib;
using UnityEngine;

namespace DDEN.LoongNova
{
    [StatusOptions(Key = "dden.loongnova.regeneration-coma.v1", SaveEnabled = true, ClientAuthoritative = false)]
    public sealed class RegenerationComa : BodyStatus
    {
        public float Remaining;
    }
    [StatusOptions(Key = "dden.loongnova.medical-treatment.v1", SaveEnabled = true, ClientAuthoritative = false)]
    public sealed class MedicalTreatment : LimbStatus
    {
        public float Remaining;
    }
    [StatusOptions(Key = "dden.loongnova.dragon-life-blessing.v1", SaveEnabled = true, ClientAuthoritative = false)]
    public sealed class DragonLifeBlessing : BodyStatus
    {
        public float Delay;
        public float Remaining;
    }

    internal static class MedicalEffects
    {
        private static MethodInfo _forceHealth;
        internal static bool CanInject(Limb limb, string id)
        {
            if (limb == null || limb.body == null || !limb.body.alive || limb.dismembered || limb.prostheticInfo.hasProsthetic) return false;
            return id == MedicalCatalog.MedicalId || id == MedicalCatalog.RegenerationId
                && limb.GetStatus<LimbGrowth>().Remaining <= 0 && Branches(limb).Count > 0;
        }
        private static List<Limb> Branches(Limb limb) => MedicalRules.MissingBranches(limb, part => part.connectedLimbs,
            part => part.dismembered, part => part.body == limb.body && !part.prostheticInfo.hasProsthetic && !part.isVital && !part.isHead && !part.isAbdomen,
            part => part.distanceToHeart);

        internal static void Apply(Limb limb, string id)
        {
            if (Mp.IsRunning && !Mp.IsServer) throw new InvalidOperationException("Medical effects require authority");
            var body = limb.body;
            if (id == MedicalCatalog.RegenerationId)
            {
                if (!DelayedRegrowth.Schedule(limb, UnityEngine.Random.Range(1, MedicalRules.GrowthLimit + 1))) return;
                limb.muscleHealth = MedicalRules.QuarterHealth(limb.muscleHealth);
                limb.skinHealth = MedicalRules.QuarterHealth(limb.skinHealth);
                body.bloodVolume = MedicalRules.RegenerationBlood(body.bloodVolume);
                body.GetStatus<RegenerationComa>().Remaining = MedicalRules.ComaSeconds;
                body.consciousness = 0; body.Ragdoll();
            }
            else if (id == MedicalCatalog.MedicalId)
            {
                var treatment = limb.GetStatus<MedicalTreatment>();
                treatment.Remaining = Mathf.Max(treatment.Remaining, MedicalRules.TreatmentSeconds);
                limb.SetDisinfect(MedicalRules.DisinfectSeconds);
                var blessing = body.GetStatus<DragonLifeBlessing>();
                if (blessing.Remaining <= 0) blessing.Delay = MedicalRules.TreatmentSeconds;
                blessing.Remaining = MedicalRules.BlessingSeconds;
            }
            Sound.Play("syringe", body.transform.position);
            if (WoundView.view != null && WoundView.view.body == body)
                WoundView.view.AddImageToLimb(limb, Resources.Load<Sprite>("Special/injectionWound"), false);
            PushHealth(body);
        }

        internal static void PushHealth(Body body)
        {
            if (!Mp.IsRunning || !Mp.IsServer) return;
            if (_forceHealth == null) _forceHealth = Reflect.Type("Together.MedicalSync")?.GetMethod("ForceHealth", BindingFlags.Public | BindingFlags.Static);
            var netBody = NetworkAccess.NetBody(body);
            if (_forceHealth == null || netBody == null) throw new MissingMethodException("Together medical health sync");
            _forceHealth.Invoke(null, new[] { netBody });
        }

        internal static void RecoverBlood(Body body, float seconds)
        {
            if (Mp.IsRunning && !Mp.IsServer || seconds <= 0 || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
            body.septicShock = Mathf.Max(0, body.septicShock - MedicalRules.BloodRecoveryPerSecond * seconds);
            body.venomCurrent = Mathf.Max(0, body.venomCurrent - MedicalRules.BloodRecoveryPerSecond * seconds);
            body.venomTotal = Mathf.Max(0, body.venomTotal - MedicalRules.BloodRecoveryPerSecond * seconds);
            body.bloodViscosity = Mathf.MoveTowards(body.bloodViscosity, 0, seconds);
            body.radiationSickness = Mathf.Max(0, body.radiationSickness - MedicalRules.RadiationRecoveryPerSecond * seconds);
        }
    }

    [HarmonyPatch(typeof(Body), "Update")]
    internal static class RegenerationComaPatch
    {
        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        internal static void Hold(Body __instance)
        {
            // CCL saves this status and supplies host-authoritative values to the affected client.
            var status = __instance.GetStatus<RegenerationComa>();
            if (status.Remaining <= 0) return;
            status.Remaining = MedicalRules.AdvanceComa(status.Remaining, Time.deltaTime);
            __instance.consciousness = 0;
        }
    }

    [HarmonyPatch(typeof(Limb), "Update")]
    internal static class MedicalTreatmentPatch
    {
        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        internal static void Treat(Limb __instance)
        {
            var status = __instance.GetStatus<MedicalTreatment>();
            if (status.Remaining <= 0) return;
            if (__instance.dismembered || __instance.prostheticInfo.hasProsthetic || !__instance.body.alive) { status.Remaining = 0; return; }
            float seconds = MedicalRules.TreatmentStep(status.Remaining, Time.deltaTime);
            status.Remaining = Mathf.Max(0, status.Remaining - seconds);
            if (Mp.IsRunning && !Mp.IsServer) return; // Native health packets carry the host's result.
            __instance.muscleHealth = MedicalRules.HealHealth(__instance.muscleHealth, MedicalRules.BurstHealthPerSecond * seconds);
            __instance.skinHealth = MedicalRules.HealHealth(__instance.skinHealth, MedicalRules.BurstHealthPerSecond * seconds);
            __instance.infectionAmount = Mathf.Max(0, __instance.infectionAmount - MedicalRules.BurstInfectionPerSecond * seconds);
            if (__instance.infectionAmount == 0) __instance.infected = false;
            __instance.bleedAmount = Mathf.Max(0, __instance.bleedAmount - MedicalRules.BurstBleedPerSecond * seconds);
            __instance.pain = Mathf.Max(0, __instance.pain - MedicalRules.BurstPainPerSecond * seconds);
            __instance.SetDisinfect(status.Remaining);
        }
    }
    [HarmonyPatch(typeof(Body), "Update")]
    internal static class DragonLifeBlessingPatch
    {
        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        internal static void Bless(Body __instance)
        {
            var status = __instance.GetStatus<DragonLifeBlessing>();
            if (status.Remaining <= 0) return;
            if (!__instance.alive) { status.Remaining = status.Delay = 0; return; }
            float recovery = MedicalRules.TreatmentStep(status.Delay + status.Remaining, Time.deltaTime);
            float delay = MedicalRules.TreatmentStep(status.Delay, Time.deltaTime);
            status.Delay = Mathf.Max(0, status.Delay - delay);
            float seconds = MedicalRules.TreatmentStep(status.Remaining, Time.deltaTime - delay);
            status.Remaining = Mathf.Max(0, status.Remaining - seconds);
            if (Mp.IsRunning && !Mp.IsServer) return;
            MedicalEffects.RecoverBlood(__instance, recovery);
            if (seconds <= 0) return;
            foreach (var limb in __instance.limbs)
            {
                if (limb == null || limb.dismembered || limb.prostheticInfo.hasProsthetic) continue;
                limb.muscleHealth = MedicalRules.HealHealth(limb.muscleHealth, MedicalRules.BlessingHealthPerSecond * seconds);
                limb.skinHealth = MedicalRules.HealHealth(limb.skinHealth, MedicalRules.BlessingHealthPerSecond * seconds);
                limb.infectionAmount = Mathf.Max(0, limb.infectionAmount - MedicalRules.BlessingInfectionPerSecond * seconds);
                if (limb.infectionAmount == 0) limb.infected = false;
                limb.pain = Mathf.Max(0, limb.pain - MedicalRules.BlessingPainPerSecond * seconds);
            }
        }
    }
}
