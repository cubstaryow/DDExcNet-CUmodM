using System;
using DDEN.Shared;
using UnityEngine;

namespace DDEN.LoongNova
{
    internal static class CannonRevival
    {
        internal static Vector2 Fire(Body owner, Vector2 origin, Vector2 direction, CannonRound profile)
        {
            if (Mp.IsRunning && (!Mp.IsServer || !Mp.CoreReady)) throw new InvalidOperationException("Revival ammunition requires authority");
            var hits = Physics2D.RaycastAll(origin, direction, profile.Range, LayerMask.GetMask("Ground", "Body", "Limb", "Descriptor"));
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                if (hit.collider == null) continue;
                Utils.GetBody(hit.collider.gameObject, out var target);
                if (target == owner) continue;
                var building = hit.collider.GetComponent<BuildingEntity>();
                if (hit.collider.gameObject.layer != 6 && target == null && (building == null || building.cantHit)) continue;
                // A wall, living character or unrelated corpse still stops the round. It never inflicts normal damage.
                if (target != null) RevivalEffects.TryRevive(target);
                HealingFields.Create(hit.point);
                return hit.point;
            }
            return origin + direction * profile.Range;
        }
    }
}
