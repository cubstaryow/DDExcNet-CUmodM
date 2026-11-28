using DDEN.LoongNova;
using Mono.Cecil;
using Newtonsoft.Json.Linq;

internal static class SpatialChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var activation = new SpatialPowerState();
        check(!activation.Activate(199.999f, true) && !activation.Cell.Loaded, "A cell below 200 energy is rejected without installing or draining it");
        check(activation.Activate(200, true) && activation.Cell.Loaded && activation.Activated && activation.Energy == 0,
            "Exactly 200 energy activates once and leaves a recoverable zero-energy installed cell");
        check(!activation.Activate(1000, false) && activation.Energy == 0, "An occupied socket cannot consume another activation fee");
        activation.SelfCharge(10);
        check(activation.Energy == 10 && activation.Cell.Favourite, "The installed source cell recovers exactly one energy per second");
        var resumed = new SpatialPowerState();
        check(resumed.Restore(activation.Capture()) && resumed.Activated && resumed.Energy == 10,
            "Activation is saved and restored without charging the startup fee again");
        resumed.SelfCharge(2000); check(resumed.Energy == 1000, "Source-cell self charging clamps at its original capacity");
        resumed.Clear(); resumed.SelfCharge(100); check(!resumed.Activated && resumed.Energy == 0, "Removing a source cell stops all self charging");
        check(!RecipeUnlockRules.Allows(2, SpatialCatalog.CrateId) && RecipeUnlockRules.Allows(2, SpatialCatalog.CrateId, true),
            "Obtaining an ordinary supply crate is an additional requirement for seeing or crafting the microspace crate");
        var box = new SpatialPowerState();
        check(box.Multiplier == 0.9f && !box.Cell.Loaded, "An empty socket retains ten-percent reduction");
        box.Load(1000, true);
        check(box.Multiplier == 0 && box.Cell.Favourite, "Powered space crates reduce contents weight by 100 percent");
        box.Advance(20000, false);
        check(box.Energy == 1000, "Empty crates consume no battery energy");
        for (int second = 1; second <= 10000; second++)
        {
            box.Advance(1, true);
            check(second == 10000 ? box.Energy == 0 && box.Multiplier == 0.9f : box.Energy > 0 && box.Multiplier == 0,
                "A full installed cell runs for precisely ten thousand occupied seconds");
        }
        check(box.Cell.Loaded, "A depleted installed cell remains available for ejection");
        box.Load(123.375f, true); box.Advance(0.125, true);
        var data = box.Capture(); var restored = new SpatialPowerState();
        check(restored.Restore(data) && Math.Abs(restored.Energy - 123.3625) < 1e-8 && restored.Cell.Favourite,
            "Fractional energy and favourites survive a complete save roundtrip");
        var bad = (JObject)data.DeepClone(); bad["exactEnergy"] = double.NaN;
        double before = restored.Energy;
        check(!restored.Restore(bad) && restored.Energy == before, "A corrupt precise-energy payload cannot mutate a good state");
        restored.Advance(double.PositiveInfinity, true); restored.Advance(-1, true);
        check(restored.Energy == before, "Invalid elapsed times cannot charge or drain a cell");
        foreach (float capacity in new[] { 50f, 100f, 300f, 1000f })
        {
            check(Math.Abs(SpatialChargeRules.Charge(0, capacity, 1) - 5.4 / capacity) < 1e-7,
                "Every native and Dragonbreath battery charges at four times native station energy per second");
            check(SpatialChargeRules.Charge(0, capacity, capacity / 5.4 + 0.001) == 1, "Charging clamps at full and never overfills");
        }
        check(SpatialChargeRules.Charge(0.2f, 0, 1) == 0.2f && SpatialChargeRules.Charge(0.2f, 1000, double.NaN) == 0.2f,
            "Invalid battery capacities and times do not corrupt condition");
        check(!RecipeUnlockRules.Allows(1, SpatialCatalog.ComponentId) && RecipeUnlockRules.Allows(2, SpatialCatalog.CrateId, true)
            && RecipeUnlockRules.Allows(2, SpatialCatalog.ChargerId), "Spatial recipes retain the per-player wrench unlock requirement");
    }
    internal static void Inspect(AssemblyDefinition assembly, Action<bool, string> check)
    {
        var types = assembly.MainModule.Types;
        var pod = types.Single(t => t.Name == "PodSalvage");
        var consume = pod.Methods.Single(m => m.Name == "Consume").Body.Instructions;
        check(!consume.Any(i => i.Operand is MethodReference m && m.Name == "set_health"), "Pod reclamation never invokes native death/loot by setting zero health");
        foreach (string field in new[] { "itemsDropOnDestroy", "alwaysDrop", "guaranteedDropAmount", "dropChanceMultiplier" })
            check(consume.Any(i => i.Operand is FieldReference f && f.Name == field), "No-drop reclamation clears every native loot route");
        check(pod.Methods.Any(m => m.Name == "Sync") && consume.Any(i => i.Operand is string s && s == "Destroy"), "MP pod removal resolves and calls native entity destruction");
        var drill = types.Single(t => t.Name == "DrillPodSource").Methods.Single(m => m.Name == "Give").Body.Instructions;
        int removed = drill.ToList().FindIndex(i => i.Operand is MethodReference m && m.DeclaringType.Name == "PodSalvage" && m.Name == "Consume");
        int charged = drill.ToList().FindIndex(i => i.Operand is FieldReference f && f.Name == "condition" && i.OpCode == Mono.Cecil.Cil.OpCodes.Stfld);
        check(removed >= 0 && charged > removed, "The drill pod is consumed before charging wrench durability or cases");
        var capsule = types.Single(t => t.Name == "CapsuleMembraneSource");
        var eligible = capsule.Methods.Single(m => m.Name == "Eligible").Body.Instructions;
        check(eligible.Any(i => i.Operand is string s && s == "dropcapsule")
            && eligible.Any(i => i.Operand is FieldReference f && f.Name == "isKeypad"), "Only the native keypad drop capsule is reclaimed");
        var device = types.Single(t => t.Name == "MicroSpaceCrate");
        check(device.Methods.Single(m => m.Name == "Settle").Body.Instructions.Any(i => i.Operand is MethodReference m && m.DeclaringType.Name == "SpatialChargeRules" && m.Name == "Charge"),
            "The real device settling path executes the tested charge formula");
        check(!device.Methods.Single(m => m.Name == "Refresh").Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "condition" && i.OpCode == Mono.Cecil.Cil.OpCodes.Stfld),
            "Container durability must remain independent from installed battery charge");
        var patches = types.Single(t => t.Name == "SpatialPatches");
        check(patches.Methods.Single(m => m.Name == "Decay").CustomAttributes.Any(a => a.AttributeType.Name == "HarmonyPriority")
            && patches.Methods.Single(m => m.Name == "Decay").Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "Frozen"),
            "A first-priority decay guard covers native MP's inlined decay as well as ordinary item decay");
        check(patches.Methods.Single(m => m.Name == "Inventory").Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "Loaded"),
            "A second cell is stored for charging after the source socket has been filled");
        check(patches.Methods.Single(m => m.Name == "EmptyCrate").Body.Instructions.Any(i => i.Operand is string s && s == SpatialCatalog.CrateId),
            "Only the spatial upgrade ingredient is restricted to an empty ordinary crate");
        var registration = types.Single(t => t.Name == "Registration");
        var baseRecipes = registration.Methods.Single(m => m.Name == "RegisterRecipes").Body.Instructions;
        var calls = baseRecipes.Where(i => i.Operand is MethodReference m && (m.Name == "AddRecipe" || m.Name == "RegisterSpatialRecipes")).ToList();
        check(calls.Count == 8 && ((MethodReference)calls.Last().Operand).Name == "RegisterSpatialRecipes",
            "New spatial recipes append after all seven original equipment recipes, retaining the original eighteen recipe indices");
        var spatialItems = registration.Methods.Single(m => m.Name == "RegisterSpatialItems").Body.Instructions;
        foreach (float weight in new[] { 0.01f, 0.1f, 2.4f, 1.5f })
            check(spatialItems.Any(i => i.Operand is float value && value == weight), "Spatial registration must retain the requested item weight: " + weight);
        check(types.Single(t => t.Name == "SpatialTradePatches").Methods.Single(m => m.Name == "Keep").Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "preference"),
            "Merchant reluctance must use the native trader preference field");
        var notice = types.Single(t => t.Name == "EnergyCellNotice").Methods.Single(m => m.Name == "Display").Body.Instructions;
        check(notice.Any(i => i.Operand is string value && value == "watch") && notice.Any(i => i.Operand is MethodReference m && m.Name == "TalkDelayed"),
            "Low-energy installation errors must use the native electronic-watch Talker presentation");
        var language = LocaleChecks.Chinese(assembly);
        check(language.Value<string>("rechargerActivationNeed") == "能量单元剩余能源不足\n无法启动内置力场", "Activation failure retains the exact two-line head notice");
        var recipes = registration.Methods.Single(m => m.Name == "RegisterSpatialRecipes").Body.Instructions;
        foreach (string id in new[] { SpatialCatalog.ComponentId, SpatialCatalog.CrateId, SpatialCatalog.ChargerId, "handcrank", "titaniumsheet", "bundleofwires", SupplyCrateRules.OpenId })
            check(recipes.Any(i => i.Operand is string s && s == id), "All requested spatial recipes and materials are present: " + id);
        var save = types.Single(t => t.Name == "GunSaveProvider");
        check(save.Methods.Single(m => m.Name == "Capture").Body.Instructions.Any(i => i.Operand is MethodReference m && m.DeclaringType.Name == "SpatialPowerState" && m.Name == "Capture")
            && save.Methods.Single(m => m.Name == "Restore").Body.Instructions.Any(i => i.Operand is MethodReference m && m.DeclaringType.Name == "SpatialPowerState" && m.Name == "Restore"),
            "The actual CCL save provider persists and restores the installed source cell");
        using var game = AssemblyDefinition.ReadAssembly(@"L:\SteamLibrary\steamapps\common\Casualties Unknown\CasualtiesUnknown_Data\Managed\Assembly-CSharp.dll");
        check(game.MainModule.Types.Single(t => t.Name == "BatteryRecharger").Methods.Single(m => m.Name == "Update").Body.Instructions.Any(i => i.Operand is float value && value == 1.35f),
            "The installed native station still uses the measured 1.35 energy per second baseline");
    }
}
