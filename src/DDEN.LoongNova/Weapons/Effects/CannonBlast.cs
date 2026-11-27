using System;
using System.Collections.Generic;
using DDEN.Shared;
using UnityEngine;

namespace DDEN.LoongNova
{
    internal static class CannonBlast
    {
        internal static Vector2 Fire(Body owner, Vector2 origin, Vector2 direction, CannonRound profile)
        {
            if (Mp.IsRunning && (!Mp.IsServer || !Mp.CoreReady)) throw new InvalidOperationException("Blast requires authority");
            var hits = Physics2D.RaycastAll(origin, direction, 200, LayerMask.GetMask("Ground", "Body", "Limb", "Descriptor"));
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                if (hit.collider == null) continue;
                if (Utils.GetBody(hit.collider.gameObject, out var hitBody) && hitBody == owner) continue;
                var building = hit.collider.GetComponent<BuildingEntity>();
                if (hit.collider.gameObject.layer != 6 && hitBody == null && (building == null || building.cantHit)) continue;
                Explode(hit.point, profile);
                return hit.point;
            }
            // A miss has no impact point: no airburst at an arbitrary maximum distance.
            return origin + direction * 200;
        }
        private static void Explode(Vector2 point, CannonRound profile)
        {
            var seen = new HashSet<BuildingEntity>();
            foreach (var collider in Physics2D.OverlapCircleAll(point, profile.BlastRadius))
            {
                var building = collider.GetComponent<BuildingEntity>();
                if (building == null || !seen.Add(building) || building.cantHit || building.indestructible || building.health <= 0) continue;
                float damage = building.animal ? profile.Creature : profile.Structure;
                building.health -= damage;
                if (building != null && building.animal) building.gameObject.SendMessage("AnimalHit", damage, SendMessageOptions.DontRequireReceiver);
            }
            // Terrain and blast effects use the native synchronized explosion path. Creature damage is
            // applied once above, not again by its random structural damage. Humanoid health is untouched.
            WorldGeneration.CreateExplosion(Parameters(point, profile.BlastRadius));
        }
        internal static ExplosionParams Parameters(Vector2 point, float radius) => new ExplosionParams
        {
            position = point, range = radius, structuralDamage = 0, velocity = 0,
            muscleDamage = new RangeF(0, 0), skinDamage = new RangeF(0, 0), skinDamageChance = 0,
            boneBreakChance = 0, dislocationChance = 0, disfigureChance = 0,
            bleedChance = 0, bleedAmount = new RangeF(0, 0), shrapnelChance = 0, dismemberChance = 0
        };
    }
}
