using System;
using System.Reflection;
using CUCoreLib.Helpers;
using DDEN.Shared;
using UnityEngine;

namespace DDEN.LoongNova
{
    internal static class PartialRegrowth
    {
        internal static void Restore(Limb part)
        {
            if (Mp.IsRunning && !Mp.IsServer) throw new InvalidOperationException("Limb growth requires authority");
            // Reattach restores the entire extremity. Activate only the planned segment instead.
            part.transform.localPosition = Vector3.zero;
            part.dismembered = false; part.gameObject.SetActive(true);
            part.Heal(); part.MendBone(); part.UnDislocate();
            part.muscleHealth = part.skinHealth = MedicalRules.NewLimbHealth;
            part.GetStatus<MedicalTreatment>().Remaining = 0;
            var collider = part.GetComponent<Collider2D>();
            foreach (var neighbor in part.body.limbs)
            {
                var other = neighbor.GetComponent<Collider2D>();
                if (collider != null && other != null && other != collider) Physics2D.IgnoreCollision(collider, other, true);
            }
            var utility = Reflect.Type("CasualtiesTogetherUtils.Util");
            if (utility == null) return;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
            utility.GetMethod("RestoreDetachedLimb", flags).Invoke(null, new object[] { part });
            utility.GetMethod("IgnoreOwnLimbCollisions", flags).Invoke(null, new object[] { part.body });
            utility.GetMethod("RebindLimbHinges", flags).Invoke(null, new object[] { part.body });
        }
    }
}
