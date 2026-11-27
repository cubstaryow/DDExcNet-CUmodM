using System;
using System.Collections.Generic;

namespace DDEN.LoongNova
{
    internal static class ArsenalLootPool
    {
        internal const double AwardChance = 0.25;
        internal static string TryPick(int source, string category, double awardRoll, double weightRoll)
        {
            if (double.IsNaN(awardRoll) || awardRoll < 0 || awardRoll >= AwardChance) return null;
            return Pick(source, category, weightRoll);
        }
        internal readonly struct Entry
        {
            internal readonly string Id;
            internal readonly double Weight;
            internal Entry(string id, double weight) { Id = id; Weight = weight; }
        }

        // Source/category restrictions also apply to the bonus pool; crafting-only items have no entries.
        internal static IEnumerable<Entry> Entries(int source, string category)
        {
            if (!LootRules.Eligible(source, category)) yield break;
            if (category == "tool")
            {
                if (LootRules.IsTrader(source)) yield return new Entry(Registration.DragonId, LootRules.DragonChance);
                else if (source == 8) yield return new Entry(Registration.DragonId, LootRules.CrateDragonChance);
                yield return new Entry(Registration.WrenchId, LootRules.WrenchChance);
                yield return new Entry(Registration.PickaxeId, LootRules.PickaxeChance);
            }
            else if (category == "container") yield return new Entry(Registration.BeltId, LootRules.BeltChance);
            else if (category == "medical")
            {
                yield return new Entry(MedicalCatalog.InjectorId, LootRules.InjectorChance);
                if (source == 2 || LootRules.IsTrader(source)) yield return new Entry(MedicalCatalog.RegenerationId, LootRules.RegenerationChance);
                yield return new Entry(MedicalCatalog.MedicalId, LootRules.MedicalChance);
                yield return new Entry(CannonAmmoState.RevivalId, LootRules.RevivalMedicalChance);
            }
            else
            {
                if (category == "utility")
                {
                    yield return new Entry(Registration.GunId, LootRules.GunChance);
                    yield return new Entry(Registration.ChargeGunId, LootRules.GunChance);
                    yield return new Entry(Registration.CellId, LootRules.CellChance);
                }
                for (int i = 0; i < 6; i++) yield return new Entry(LootRules.BonusRound(i), LootRules.AmmoChance);
                yield return new Entry(CannonAmmoState.RevivalId, LootRules.RevivalAmmoChance);
            }
        }

        internal static string Pick(int source, string category, double roll)
        {
            if (double.IsNaN(roll) || roll < 0 || roll > 1) return null;
            double total = 0;
            foreach (var entry in Entries(source, category)) total += entry.Weight;
            if (total <= 0) return null;
            double cursor = roll * total;
            string last = null;
            foreach (var entry in Entries(source, category))
            {
                last = entry.Id;
                if (cursor < entry.Weight) return entry.Id;
                cursor -= entry.Weight;
            }
            return last; // Unity Random.value can equal one.
        }
    }
}
