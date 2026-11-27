using System;
using System.Collections.Generic;
using CUCoreLib.Data;
using CUCoreLib.Helpers;
using DDEN.Shared;
using HarmonyLib;
using UnityEngine;

namespace DDEN.LoongNova
{
    [StatusOptions(Key = "dden.loongnova.limb-growth.v1", SaveEnabled = true, ClientAuthoritative = false)]
    public sealed class LimbGrowth : LimbStatus
    {
        public float Remaining;
        public int[] Targets = Array.Empty<int>();
    }
    internal static class DelayedRegrowth
    {
        internal static bool Schedule(Limb anchor, int maximum)
        {
            var status = anchor.GetStatus<LimbGrowth>();
            if (status.Remaining > 0) return false;
            var plan = MedicalRules.GrowthPlan(anchor, part => part.connectedLimbs, part => part.dismembered,
                part => part.body == anchor.body && !part.prostheticInfo.hasProsthetic && !part.isHead && !part.isVital && !part.isAbdomen,
                part => part.distanceToHeart, maximum);
            if (plan.Count == 0) return false;
            var targets = new List<int>();
            foreach (var part in plan) { int index = Array.IndexOf(anchor.body.limbs, part); if (index >= 0) targets.Add(index); }
            if (targets.Count == 0) return false;
            status.Targets = targets.ToArray(); status.Remaining = MedicalRules.GrowthSeconds; return true;
        }

        internal static bool Complete(Limb anchor, LimbGrowth status)
        {
            bool changed = false;
            var targets = status.Targets ?? Array.Empty<int>();
            for (int i = 0; i < Math.Min(MedicalRules.GrowthLimit, targets.Length); i++)
            {
                int index = targets[i];
                if (index < 0 || index >= anchor.body.limbs.Length) continue;
                var part = anchor.body.limbs[index];
                if (part == null || !part.dismembered || part.prostheticInfo.hasProsthetic || part.isHead || part.isVital || part.isAbdomen) continue;
                bool attached = false;
                foreach (var neighbor in part.connectedLimbs)
                    if (neighbor != null && neighbor.body == anchor.body && !neighbor.dismembered && neighbor.distanceToHeart < part.distanceToHeart) attached = true;
                if (!attached) continue;
                PartialRegrowth.Restore(part); changed = true;
            }
            status.Remaining = 0; status.Targets = Array.Empty<int>(); return changed;
        }
    }
    [HarmonyPatch(typeof(Body), "Update")]
    internal static class LimbGrowthPatch
    {
        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        internal static void Grow(Body __instance)
        {
            bool changed = false;
            foreach (var anchor in __instance.limbs)
            {
                var status = anchor.GetStatus<LimbGrowth>();
                if (status.Remaining <= 0) continue;
                // A body hook also sees inactive, newly severed injection sites and can cancel their saved work.
                if (!__instance.alive || anchor.dismembered)
                { status.Remaining = 0; status.Targets = Array.Empty<int>(); continue; }
                status.Remaining = Mathf.Max(0, status.Remaining - MedicalRules.TreatmentStep(status.Remaining, Time.deltaTime));
                if (Mp.IsRunning && !Mp.IsServer || status.Remaining > 0) continue;
                changed |= DelayedRegrowth.Complete(anchor, status);
            }
            if (changed) MedicalEffects.PushHealth(__instance);
        }
    }
}
