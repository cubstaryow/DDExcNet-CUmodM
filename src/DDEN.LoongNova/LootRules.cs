using System;

namespace DDEN.LoongNova
{
    // Chance per successful draw of the matching category, not per container or per generated loot pool.
    internal static class LootRules
    {
        internal const double DragonChance = 0.005;
        internal const double WrenchChance = 0.005;
        internal const double PickaxeChance = 0.008;
        internal const double BeltChance = 0.001;
        internal const double InjectorChance = 0.005;
        internal const double MedicalChance = 0.004;
        internal const double RegenerationChance = 0.0005;
        internal const double RevivalMedicalChance = 0.001;
        internal const double RevivalAmmoChance = 0.0005;
        internal const double GunChance = 0.0005;
        internal const double CellChance = 0.004;
        internal const double AmmoChance = 0.004;
        internal const double CrateDragonChance = 0.001;
        internal const double ElderChance = 0.25;
        internal static bool IsTrader(int source) => source == 16 || source == 32 || source == 64 || source == 112;
        internal static bool IsContainer(int source) => source == 8 || source == 128 || source == 256;
        internal static bool Eligible(int source, string category) => (IsTrader(source) || IsContainer(source) || source == 2 && category == "medical")
            && (category == "tool" || category == "utility" || category == "container" || category == "ammo" || category == "medical");

        internal const int RoundCount = 7;
        internal static string BonusRound(int index) => index == 0 ? CannonAmmoState.BioId
            : index == 1 ? CannonAmmoState.DemolitionId : index == 2 ? CannonAmmoState.ExplosiveId : index == 3 ? CannonAmmoState.DrillId
            : index == 4 ? CannonAmmoState.DrainId : index == 5 ? CannonAmmoState.BreachId : index == 6 ? CannonAmmoState.RevivalId : null;
        private static string SelectAmmo(double roll)
        {
            if (roll < AmmoChance) return CannonAmmoState.BioId;
            if (roll < AmmoChance * 2) return CannonAmmoState.DemolitionId;
            if (roll < AmmoChance * 3) return CannonAmmoState.ExplosiveId;
            if (roll < AmmoChance * 4) return CannonAmmoState.DrillId;
            if (roll < AmmoChance * 5) return CannonAmmoState.DrainId;
            if (roll < AmmoChance * 6) return CannonAmmoState.BreachId;
            if (roll < AmmoChance * 6 + RevivalAmmoChance) return CannonAmmoState.RevivalId;
            return null;
        }

        internal static string Select(int source, string category, double roll)
        {
            if (!Eligible(source, category) || double.IsNaN(roll) || roll < 0 || roll > 1) return null;
            if (category == "medical")
            {
                if (roll < InjectorChance) return MedicalCatalog.InjectorId;
                if (roll < InjectorChance + RegenerationChance) return source == 2 || IsTrader(source) ? MedicalCatalog.RegenerationId : null;
                if (roll < InjectorChance + RegenerationChance + MedicalChance) return MedicalCatalog.MedicalId;
                if (roll < InjectorChance + RegenerationChance + MedicalChance + RevivalMedicalChance) return CannonAmmoState.RevivalId;
                return null;
            }
            if (category == "tool")
            {
                double dragon = IsTrader(source) ? DragonChance : source == 8 ? CrateDragonChance : 0;
                if (roll < dragon) return Registration.DragonId;
                if (roll < dragon + WrenchChance) return Registration.WrenchId;
                if (roll < dragon + WrenchChance + PickaxeChance) return Registration.PickaxeId;
            }
            else if (category == "utility")
            {
                if (roll < GunChance) return Registration.GunId;
                if (roll < GunChance * 2) return Registration.ChargeGunId;
                if (roll < GunChance * 2 + CellChance) return Registration.CellId;
                return SelectAmmo(roll - (GunChance * 2 + CellChance));
            }
            else if (category == "ammo") return SelectAmmo(roll);
            else if (roll < BeltChance) return Registration.BeltId;
            return null;
        }
    }

    internal sealed class BossDropGate
    {
        private bool _used;
        internal bool TryRoll(double roll)
        {
            if (_used || double.IsNaN(roll) || roll < 0 || roll > 1) return false;
            _used = true;
            return roll < LootRules.ElderChance;
        }
    }
}
