using System;
using System.Collections.Generic;

namespace DDEN.LoongNova
{
    internal static class BreachTargets
    {
        private static readonly HashSet<string> Scripts = new HashSet<string>(StringComparer.Ordinal)
        {
            "MineScript", "GunmineScript", "BearTrap", "CoilScript", "SpikeStabberScript", "FanTrap",
            "CeilingTurret", "TurretScript", "SawbladeScript", "GroundGlass", "DamagingCrate",
            "StalactiteDropper", "ScrapEaterScript", "BatteryRecharger", "GuardSpawner", "GeyserScript",
            "CrystalBehaviour", "RadioactiveObject", "LampScript", "GrabberPlant", "BounceShroom",
            "SoundCannon", "JumpPadScript", "PistonScript", "BarbedFence", "BananaPlantSlip"
        };
        // Some mushroom hazards reuse generic collision/animation components rather than their own script.
        private static readonly HashSet<string> TrapIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "grabbershroom", "mushroomdropper", "bouncecap", "fungalibungali", "mushroomplatform",
            "spikestabber", "sidestabber", "sidestabberflip", "skullcrusher", "jumppad", "soundcannon",
            "turret", "ceilingturret", "gunmine", "landmine", "coil", "fan", "sawblade",
            "piston", "pistonright", "pistonleft", "barbedwirefence", "spentfuel", "bloodsuckerden"
        };
        internal static bool IsScript(string name) => name != null && Scripts.Contains(name);
        internal static bool IsTrapId(string id) => id != null && TrapIds.Contains(id);
        internal static bool IsTarget(string script, string id, bool animal)
        {
            if (IsTrapId(id)) return true; // Explicit stationary traps remain traps even when tagged as organic.
            if (script == "GrabberPlant" || script == "BounceShroom" || script == "SpikeStabberScript") return true;
            if (animal) return false;
            return IsScript(script);
        }
    }
}
