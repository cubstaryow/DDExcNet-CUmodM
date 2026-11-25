using System;

namespace DDEN.LoongNova
{
    // Chance per successful draw of the matching category, not per container or per generated loot pool.
    internal static class LootRules
    {
        internal const double DragonChance = 0.005;
        internal const double ToolChance = 0.001;
        internal const double GunChance = 0.0005;
        internal const double CellChance = 0.001;
        internal const double ElderChance = 0.25;
        internal static bool IsTrader(int source) => source == 16 || source == 32 || source == 64 || source == 112;
        internal static bool IsContainer(int source) => source == 8 || source == 128 || source == 256;
        internal static bool Eligible(int source, string category) => (IsTrader(source) || IsContainer(source))
            && (category == "tool" || category == "utility" || category == "container");

        internal static string Select(int source, string category, double roll)
        {
            if (!Eligible(source, category) || double.IsNaN(roll) || roll < 0 || roll > 1) return null;
            if (category == "tool")
            {
                double dragon = IsTrader(source) ? DragonChance : 0;
                if (roll < dragon) return Registration.DragonId;
                if (roll < dragon + ToolChance) return Registration.WrenchId;
            }
            else if (category == "utility")
            {
                if (roll < GunChance) return Registration.GunId;
                if (roll < GunChance + CellChance) return Registration.CellId;
            }
            else if (roll < ToolChance) return Registration.BeltId;
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
