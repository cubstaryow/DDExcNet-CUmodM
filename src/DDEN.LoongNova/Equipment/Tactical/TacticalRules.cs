using System;

namespace DDEN.LoongNova
{
    internal enum TacticalKind { Trap, Turret, Sound, Creature }
    internal static class TacticalRules
    {
        internal const int CellsPerFrame = 384, TargetsPerFrame = 128, MaxWarnings = 8;
        internal const int ElderBiome = 4;
        internal const float ElderRange = 1000, TraderRange = 500;
        internal static bool LocatorAvailable(bool sceneLoaded,bool living)=>sceneLoaded&&living;
        internal static bool TraderSignal(float squared) => squared >= 0 && squared <= TraderRange * TraderRange;
        internal static bool ElderSignal(int biome, bool living, bool active, float distanceSquared) => biome == ElderBiome
            && living && active && distanceSquared >= 0 && distanceSquared <= ElderRange * ElderRange;
        internal static bool ExtraWarning(TacticalKind kind, float dx, float dy, float range)
        {
            if (float.IsNaN(dx) || float.IsNaN(dy) || float.IsInfinity(dx) || float.IsInfinity(dy)) return false;
            if (kind == TacticalKind.Trap) return Math.Abs(dx) <= 4 && dy < 0 && dy >= -32;
            if (kind != TacticalKind.Turret && kind != TacticalKind.Sound) return false;
            return range > 0 && dx * dx + dy * dy <= range * range;
        }
        internal static bool HazardScript(string name) => (BreachTargets.IsScript(name) || name == "CaveTicks" || name == "CaveTickSpawner" || name == "CrystalUnstable")
            && name != "LampScript" && name != "BatteryRecharger" && name != "ScrapEaterScript";
    }
}
