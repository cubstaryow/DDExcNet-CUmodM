using System.Collections.Generic;

namespace DDEN.LoongNova
{
    internal static class ArsenalRecipes
    {
        internal static IEnumerable<Recipe> Create()
        {
            yield return Make(CannonAmmoState.CarrierId, new List<RecipeItem>
            {
                Material(Registration.MatterId), Material("casing"), Material("casing"), Material("casing"), Material("casing"),
                Material("titaniumsheet"), Material("processedcopper"), Dragonization(100)
            });
            yield return Make(CannonAmmoState.BioId, new List<RecipeItem>
            {
                Material(CannonAmmoState.CarrierId), new RecipeItem(90) { specific = true, specificId = "mindwipe", isLiquid = true, destroyItem = true },
                Material("flammablepowder"), Material("flammablepowder"), Material("titaniumrod"), Dragonization(100)
            });
            yield return Make(CannonAmmoState.DemolitionId, new List<RecipeItem>
            {
                Material(CannonAmmoState.CarrierId), Material("titaniumrod"), Material("nails"), Material("nails"), Material("nails"), Material("nails"), Material("nails"), Material("nails"),
                Material("flammablepowder"), Dragonization(100)
            });
            yield return Make(CannonAmmoState.ExplosiveId, new List<RecipeItem>
            {
                Material(CannonAmmoState.CarrierId), Material("dynamite"), Material("scrappanel"), Dragonization(100)
            });
            yield return Make(Registration.ChargeGunId, new List<RecipeItem>
            {
                Material(Registration.MatterId), Material(Registration.MatterId), Material("titaniumsheet"), Material("titaniumsheet"),
                Material("bundleofwires"), Material("bundleofwires"), Material("bundleofwires"), Material(Registration.CellId),
                Material("scraptube"), Material("flexiglass"), Material("flexiglass"), Material("rifle"), Dragonization(200)
            });
            yield return Make(CannonAmmoState.DrillId, new List<RecipeItem>
            {
                Material(Registration.PickaxeId), Material(CannonAmmoState.CarrierId), Dragonization(100)
            });
            yield return Make(CannonAmmoState.DrainId, new List<RecipeItem>
            {
                Material("spacedrain", consume: false), Material(CannonAmmoState.CarrierId), Material("flammablepowder"),
                Material("flexiglass"), Material("titaniumsheet"), Dragonization(100)
            });
            yield return Make(CannonAmmoState.BreachId, new List<RecipeItem>
            {
                Material(CannonAmmoState.CarrierId), Material("smallbattery", BatteryMinimum), Material("smallbattery", BatteryMinimum),
                Material("flexiglass"), Material("bundleofwires"), Material("bundleofwires"), Material("flammablepowder"),
                Material("titaniumsheet"), Dragonization(100)
            });
            yield return Make(MedicalCatalog.EmptyId, new List<RecipeItem>
            {
                Liquid("biochem", 10), Material("flexiglass"), Material("flexiglass"), Material(Registration.MatterId), Dragonization(100)
            }, (Recipes.RecipeCategory)2);
            yield return Make(MedicalCatalog.MedicalId, new List<RecipeItem>
            {
                Material(MedicalCatalog.EmptyId), Liquid("lrdserum", 20), Liquid("antivenom", 10), LiquidQuality("disinfectant", 10),
                Liquid("painkillers", 10), Liquid("biochem", 10), Liquid("antirad", 10), Material("flammablepowder"),
                Dragonization(100), LiquidQuality("opiate", 10), LiquidQuality("water", 20)
            }, (Recipes.RecipeCategory)2);
            yield return Make(MedicalCatalog.InjectorId, new List<RecipeItem>
            {
                Material("pistol"), Material(Registration.MatterId), Material("flexiglass"), Material("flexiglass"),
                Material("lcdscreen"), Material("titaniumsheet"), Material("syringe"), Dragonization(200)
            }, (Recipes.RecipeCategory)2);
        }
        // Smallest float above 0.9f: the native >= matcher must reject exactly 90% charge.
        internal const float BatteryMinimum = 0.90000004f;
        private static Recipe Make(string id, List<RecipeItem> items, Recipes.RecipeCategory category = (Recipes.RecipeCategory)1) => new Recipe
        {
            INT = 18, category = category,
            result = new RecipeResult { id = id, amount = 1, resultCondition = 1 }, items = items
        };
        private static RecipeItem Material(string id, float minimum = 0, bool consume = true) => new RecipeItem(minimum)
        { specific = true, specificId = id, destroyItem = consume };
        private static RecipeItem Liquid(string id, float ml) => new RecipeItem(ml) { specific = true, specificId = id, isLiquid = true };
        private static RecipeItem LiquidQuality(string id, float units) => new RecipeItem(0)
        { quality = new CraftingQuality(id, units), isLiquid = true };
        private static RecipeItem Dragonization(float units) => new RecipeItem(DragonizationRules.Cost(units))
        {
            quality = new CraftingQuality(Registration.DragonQuality, units), destroyItem = false
        };
    }
}
