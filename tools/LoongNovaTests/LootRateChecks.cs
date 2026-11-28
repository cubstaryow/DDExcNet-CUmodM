using DDEN.LoongNova;
using Mono.Cecil;

internal static class LootRateChecks
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (int source in new[] { 2, 8, 16, 32, 64, 112, 128, 256 })
        foreach (string category in new[] { "tool", "utility", "ammo", "container", "medical" })
        {
            // Expected outcomes in 100,000 successful category draws; keep these independent of production constants.
            var expected = new Dictionary<string, int>();
            if (LootRules.Eligible(source, category))
            {
                if (category == "tool")
                {
                    if (LootRules.IsTrader(source)) expected[Registration.DragonId] = 1000;
                    else if (source == 8) expected[Registration.DragonId] = 500;
                    expected[Registration.WrenchId] = 500;
                    expected[Registration.PickaxeId] = 2000;
                    expected[Registration.SmelterId] = 1500;
                }
                else if (category == "container") expected[Registration.BeltId] = 100;
                else if (category == "medical")
                {
                    expected[MedicalCatalog.InjectorId] = 1000;
                    expected[MedicalCatalog.MedicalId] = 800;
                    expected[CannonAmmoState.RevivalId] = 100;
                    if (source == 2 || LootRules.IsTrader(source)) expected[MedicalCatalog.RegenerationId] = 400;
                }
                else
                {
                    if (category == "utility")
                    {
                        expected[Registration.GunId] = 100;
                        expected[Registration.ChargeGunId] = 100;
                        expected[Registration.CellId] = 800;
                        expected[SpatialCatalog.ChargerId] = 400;
                    }
                    for (int n = 0; n < 6; n++) expected[LootRules.BonusRound(n)] = 600;
                    expected[CannonAmmoState.RevivalId] = 100;
                }
            }
            var direct = new Dictionary<string, int>();
            for (int i = 0; i < 100000; i++)
            {
                string id = LootRules.Select(source, category, (i + 0.5) / 100000);
                if (id != null) direct[id] = direct.GetValueOrDefault(id) + 1;
            }
            check(direct.Count == expected.Count && expected.All(p => direct.GetValueOrDefault(p.Key) == p.Value),
                $"Exact revised direct rates, source {source}, category {category}");
            var bonus = new Dictionary<string, int>();
            int samples = expected.Values.Sum() * 20;
            for (int i = 0; i < samples; i++)
            {
                string id = ArsenalLootPool.Pick(source, category, (i + 0.5) / samples);
                bonus[id] = bonus.GetValueOrDefault(id) + 1;
            }
            check(bonus.Count == expected.Count && expected.All(p => bonus.GetValueOrDefault(p.Key) == p.Value * 20),
                $"The extra pool must normalize existing probabilities into weights without a second rarity roll: {source}/{category}");
            check(ArsenalLootPool.Pick(source, category, 0) == (expected.Count == 0 ? null : expected.Keys.First())
                && (expected.Count == 0 || expected.ContainsKey(ArsenalLootPool.Pick(source, category, 1))),
                "Inclusive zero/one random endpoints must stay inside the eligible bonus pool");
        }
        int awarded = 0;
        for (int i = 0; i < 100000; i++)
            if (ArsenalLootPool.TryPick(8, "tool", (i + 0.5) / 100000, 0.9) != null) awarded++;
        check(awarded == 40000 && ArsenalLootPool.TryPick(8, "tool", 0.4, 0) == null,
            "A CCL bonus-entry hit must award an item exactly 40 percent of the time");
        foreach (int source in new[] { 0, 1, 4, 511 })
        foreach (string category in new[] { "tool", "utility", "medical", "ammo", "battery", "food", "custom", "", null! })
            check(ArsenalLootPool.TryPick(source, category, 0, 0) == null,
                "The extra pool must not bypass unsupported sources or add a separate battery pathway");
        foreach (double invalid in new[] { -1d, double.NaN, double.PositiveInfinity })
            check(ArsenalLootPool.TryPick(8, "tool", invalid, 0) == null
                && ArsenalLootPool.TryPick(8, "tool", 0, invalid) == null,
                "Invalid randomness must not manufacture an extra-pool item");
        check(ArsenalLootPool.Pick(8, "battery", 0) == null && LootRules.Select(8, "battery", 0) == null,
            "Battery-specific handling was explicitly declined");
    }

    internal static void Inspect(AssemblyDefinition assembly, Action<bool, string> check)
    {
        var bridge = assembly.MainModule.Types.Single(t => t.Name == "LootPoolPatches");
        var prefix = bridge.Methods.Single(m => m.Name == "Prefix");
        var calls = prefix.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().ToArray();
        check(calls.Any(m => m.DeclaringType.Name == "ArsenalLootPool" && m.Name == "TryPick")
            && calls.Any(m => m.DeclaringType.FullName == "UnityEngine.Random" && m.Name == "Range")
            && prefix.Parameters.Any(p => p.Name == "__state" && p.IsOut),
            "The installed CCL prefix must actually sample and resolve the extra entry and pass its outcome to the postfix");
        check(prefix.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "pool")
            && prefix.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "_sourcePools")
            && calls.Any(m => m.Name == "get_IsServer"),
            "The real bridge must count category plus explicit-source entries and exclude clients");
        var postfix = bridge.Methods.Single(m => m.Name == "Postfix");
        check(postfix.Parameters.Any(p => p.Name == "__state")
            && postfix.Body.Instructions.Any(i => i.Operand is MethodReference m && m.DeclaringType.Name == "LootRules" && m.Name == "Select"),
            "The existing direct rare-item roll must remain installed alongside the bonus branch");
        using var ccl = AssemblyDefinition.ReadAssembly(File.Exists(@"L:\SteamLibrary\steamapps\common\Casualties Unknown\BepInEx\plugins\CUCoreLib.dll") ? @"L:\SteamLibrary\steamapps\common\Casualties Unknown\BepInEx\plugins\CUCoreLib.dll" : @"L:\SteamLibrary\steamapps\common\Casualties Unknown\BepInEx\plugins\Together\CUCoreLib.dll");
        var registry = ccl.MainModule.Types.Single(t => t.FullName == "CUCoreLib.Registries.DropPoolRegistry");
        check(registry.Fields.Any(f => f.Name == "ExplicitPools" && f.IsStatic && f.FieldType.FullName.Contains("DropPool"))
            && registry.Methods.Any(m => m.Name == "TryGetRandomItemId" && m.Parameters.Count == 3 && m.Parameters[2].IsOut),
            "The bonus bridge must match the installed CCL source pools and draw API");
    }
}
