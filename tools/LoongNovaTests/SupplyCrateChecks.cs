using DDEN.LoongNova;
using Mono.Cecil;
using Newtonsoft.Json.Linq;

internal static class SupplyCrateChecks
{
    private sealed class Made
    {
        internal readonly string Id;
        internal Made(string id) { Id = id; }
    }
    private static List<SupplyCandidate> Catalog()
    {
        var result = new List<SupplyCandidate>();
        void Add(string id, float weight, SupplyPool pool = SupplyPool.None) => result.Add(new SupplyCandidate(id, weight, pool));
        Add(MedicalCatalog.InjectorId, 0.5f); Add(MedicalCatalog.MedicalId, 0.1f); Add(MedicalCatalog.RegenerationId, 0.1f);
        Add(Registration.ChargeGunId, 3); Add(Registration.GunId, 2); Add(Registration.CellId, 0.3f);
        Add(Registration.WrenchId, 0.8f); Add(Registration.BeltId, 1.5f);
        Add("largebattery", 0.5f); Add("mediumbattery", 0.2f); Add("nutrientbar", 0.5f); Add("waterbottle", 1.25f); Add("scaffoldingpack", 2);
        Add("riflemagazine", 0.5f); Add("boxof12gauge", 0.4f); Add("smallmagazine", 0.3f);
        for (int i = 0; i < 7; i++) Add(LootRules.BonusRound(i), i == 2 ? 0.5f : 0.3f);
        Add("bandage", 0.9f, SupplyPool.Medical); Add("mod-med", 0.7f, SupplyPool.Medical); Add("too-heavy-med", 10, SupplyPool.Medical);
        Add("helmet", 0.8f, SupplyPool.Equipment); Add("mod-gear", 4.9f, SupplyPool.Equipment);
        Add("bag", 4, SupplyPool.Container); Add("mod-bag", 5, SupplyPool.Container);
        Add("pickaxe", 2, SupplyPool.Tool); Add("mod-tool", 5, SupplyPool.Tool);
        return result;
    }
    internal static void Run(Action<bool, string> check)
    {
        check(SupplyCrateRules.SealedWeight == 5 && SupplyCrateRules.OpenWeight == 2 && SupplyCrateRules.Capacity == 20
            && SupplyCrateRules.MaxItemWeight == 5 && Math.Abs(SupplyCrateRules.EncumbranceMultiplier - 0.9) < 0.000001
            && SupplyCrateRules.DecayMinutes == 240 && SupplyCrateRules.DecayInfo == 5,
            "Supply crates must retain 20u capacity, 5u item limit, reduce weight by 10 percent and decay after four moving hours with contents");
        var catalog = Catalog(); var weights = catalog.ToDictionary(c => c.Id, c => c.Weight);
        var expected = new[]
        {
            new[] { MedicalCatalog.InjectorId, MedicalCatalog.InjectorId, MedicalCatalog.MedicalId, MedicalCatalog.MedicalId, MedicalCatalog.RegenerationId, MedicalCatalog.RegenerationId }
                .Concat(Enumerable.Repeat("bandage", 6)).ToArray(),
            new[] { Registration.CellId, "largebattery", "largebattery", "mediumbattery", "mediumbattery" }
                .Concat(Enumerable.Repeat("nutrientbar", 6)).Concat(Enumerable.Repeat("waterbottle", 4)).Append("scaffoldingpack").ToArray(),
            new[] { Registration.ChargeGunId }.Concat(Enumerable.Repeat(CannonAmmoState.BioId, 10)).ToArray(),
            new[] { Registration.GunId, Registration.CellId, Registration.CellId, "riflemagazine", "riflemagazine", "boxof12gauge", "boxof12gauge", "smallmagazine", "smallmagazine" },
            new[] { Registration.WrenchId, Registration.BeltId, Registration.CellId, "helmet", "helmet", "bag", "pickaxe" }
        };
        for (int variant = 0; variant < 5; variant++)
        {
            int roundIndex = 0;
            var contents = SupplyCrateRules.Contents(variant, catalog, count => 0, () => { roundIndex++; return 0; });
            check(contents.SequenceEqual(expected[variant]), "Each supply variant must contain its own equipment and requested consumables");
            check(roundIndex == (variant == 2 ? 10 : 0), "Only the cannon bundle may draw exactly ten loose rounds");
            check(contents.All(id => weights[id] <= 5) && contents.Sum(id => weights[id]) <= 20,
                "Every complete preset must fit the original 20u container and 5u item limit");
            var opening = new SupplyCrateOpening(); int spent = 0; var removed = new List<Made>(); var stored = new List<Made>();
            bool ok = opening.TryOpen(contents, () => new Made(SupplyCrateRules.OpenId), id => new Made(id),
                (box, item) => { stored.Add(item); return true; }, removed.Add, () => spent++, out var box, out var error);
            check(ok && box.Id == SupplyCrateRules.OpenId && error == null && spent == 1 && removed.Count == 0
                && stored.Select(m => m.Id).SequenceEqual(contents), "A successful opening must fill one real container before spending one sealed crate");
            check(!opening.TryOpen(contents, () => throw new Exception("Must not create twice"), id => new Made(id),
                (target, item) => true, removed.Add, () => spent++, out var duplicate, out _)
                && duplicate == null && spent == 1, "Repeated use callbacks must never duplicate an opened crate");
        }
        var variantCounts = Enumerable.Range(0, 9).Select(SupplyCrateRules.PickVariant).GroupBy(v => v).OrderBy(g => g.Key).Select(g => g.Count());
        check(variantCounts.SequenceEqual(new[] { 2, 2, 1, 2, 2 }), "Five supply weights must be exactly 2:2:1:2:2");
        var random = new Random(174);
        for (int n = 0; n < 1000; n++)
        {
            var rounds = SupplyCrateRules.Contents(2, catalog, count => 0, random.NextDouble);
            int mysteries = rounds.Count(id => id == CannonAmmoState.RevivalId);
            string bundled = CannonSpawnWeights.Pick(1, mysteries == 0);
            check(rounds.Count == 11 && mysteries + (bundled == CannonAmmoState.RevivalId ? 1 : 0) <= 1,
                "Ten loose rounds plus the cannon's initial round must never include more than one mysterious round");
            var equipment = SupplyCrateRules.Contents(4, catalog, count => count - 1, random.NextDouble);
            check(equipment.Count == 7 && equipment.Sum(id => weights[id]) <= 20 && equipment.All(id => weights[id] <= 5),
                "Random mod equipment must reserve room for the remaining container and tool");
        }
        var forcedMystery = SupplyCrateRules.Contents(2, catalog, count => 0, () => 1);
        check(forcedMystery.Count(id => id == CannonAmmoState.RevivalId) == 1
            && forcedMystery.Skip(2).All(id => id == CannonAmmoState.BreachId), "After a mysterious round, subsequent draws must use only the other six profiles");
        check(CannonSpawnWeights.Pick(0, new double[] { 0, 0, 0, 0, 0, 0, 1 }, false) == null,
            "Excluding revival from a revival-only weight set must terminate rather than reroll forever");
        check(SupplyCrateRules.PlayerCount(false, new object[] { "NPC", "NPC" }) == 1
            && SupplyCrateRules.PlayerCount(true, new object[] { "host", "dead-client", null!, "client" }) == 3
            && SupplyCrateRules.PlayerCount(true, Array.Empty<object>()) == 1,
            "Singleplayer counts as one; connected dead players count, null entries and world NPCs do not");
        for (int players = 1; players <= 64; players++)
        foreach (int extra in new[] { 0, 1, 2 })
            check(SupplyCrateRules.DropCount(players, extra) == players + extra, "Every elder must drop between player count and player count plus two sealed crates");
        var gate = new BossDropGate();
        check(gate.TryClaim() && !gate.TryClaim() && !gate.TryRoll(0), "The crate reward must claim each actual boss death once independently of dragon probability");
        foreach (int variant in new[] { -1, 5 })
        {
            bool rejected = false;
            try { SupplyCrateRules.Contents(variant, catalog, count => 0, () => 0); } catch (ArgumentOutOfRangeException) { rejected = true; }
            check(rejected, "Invalid bundle IDs must not silently generate an unrelated kit");
        }
        bool noMedicine = false;
        try { SupplyCrateRules.Contents(0, catalog.Where(c => c.Pools != SupplyPool.Medical).ToArray(), count => 0, () => 0); }
        catch (InvalidOperationException) { noMedicine = true; }
        check(noMedicine, "Missing random pools must fail before consuming the sealed crate");

        var plan = SupplyCrateRules.Contents(0, catalog, count => 0, () => 0);
        for (int failure = -1; failure < plan.Count; failure++)
        foreach (bool throwFailure in new[] { false, true })
        {
            var opening = new SupplyCrateOpening(); var made = new List<Made>(); var removed = new List<Made>(); int spent = 0, calls = 0;
            Made Create(string id) { var item = new Made(id); made.Add(item); return item; }
            bool ok = opening.TryOpen(plan, () => failure == -1 ? null : Create(SupplyCrateRules.OpenId), id =>
            {
                if (calls++ != failure) return Create(id);
                if (throwFailure) throw new Exception("Injected spawn failure");
                return null;
            }, (box, item) => true, removed.Add, () => spent++, out var result, out var error);
            check(!ok && result == null && error != null && spent == 0 && removed.Count == made.Count
                && made.All(removed.Contains), "Every failed spawn must roll back all prepared objects while preserving the sealed crate");
            check(opening.TryOpen(plan, () => Create(SupplyCrateRules.OpenId), Create, (box, item) => true,
                removed.Add, () => spent++, out _, out _) && spent == 1, "Failed openings must remain retryable");
        }
        foreach (bool throwFailure in new[] { false, true })
        {
            var opening = new SupplyCrateOpening(); var removed = new List<Made>(); int spent = 0;
            bool ok = opening.TryOpen(plan, () => new Made(SupplyCrateRules.OpenId), id => new Made(id),
                (box, item) => throwFailure ? throw new Exception("Injected store failure") : false,
                removed.Add, () => spent++, out _, out var error);
            check(!ok && error != null && spent == 0 && removed.Count == 2,
                "A refused or failed native container insertion must remove both container and item without spending the sealed crate");
        }
        var reentrant = new SupplyCrateOpening(); int created = 0, consumed = 0;
        reentrant.TryOpen(plan, () =>
        {
            check(!reentrant.TryOpen(plan, () => { created++; return new Made("duplicate"); }, id => new Made(id),
                (box, item) => true, _ => { }, () => consumed++, out _, out _), "Reentrant callbacks must not start a second opening");
            created++; return new Made(SupplyCrateRules.OpenId);
        }, id => new Made(id), (box, item) => true, _ => { }, () => consumed++, out _, out _);
        check(created == 1 && consumed == 1, "Reentrancy must consume and create exactly once");
    }

    internal static void Inspect(AssemblyDefinition assembly, Action<bool, string> check, string root)
    {
        var types = assembly.MainModule.Types;
        var registration = types.Single(t => t.Name == "Registration");
        var register = registration.Methods.Single(m => m.Name == "RegisterSupplyCrates").Body.Instructions;
        check(registration.Methods.Single(m => m.Name == "Register").Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "RegisterSupplyCrates"),
            "Both crate registrations must be reachable from the actual startup entry");
        float[] Values(string field) => register.Select((ins, i) => (ins, i))
            .Where(p => p.ins.Operand is FieldReference f && f.Name == field && p.i > 0 && register[p.i - 1].Operand is float)
            .Select(p => (float)register[p.i - 1].Operand).ToArray();
        check(Values("weight").SequenceEqual(new[] { 5f, 2f }) && Values("Capacity").SequenceEqual(new[] { 20f })
            && Values("MaxWeightPerItem").SequenceEqual(new[] { 5f }) && Values("EncumbranceReduction").SequenceEqual(new[] { 0.9f })
            && Values("decayMinutes").SequenceEqual(new[] { 240f }),
            "The real CCL definitions must carry the requested weights/capacity/reduction, not just the pure rules");
        check(register.Any(i => i.Operand is FieldReference f && f.Name == "TagRestriction")
            && register.Any(i => i.Operand is MethodReference m && m.Name == "Empty" && m.DeclaringType.FullName == "System.Array"),
            "The open crate must have an empty tag filter to accept all item types");
        check(register.Select((ins, i) => (ins, i)).Any(p => p.ins.Operand is FieldReference f && f.Name == "decayInfo"
                && p.i > 0 && register[p.i - 1].OpCode == Mono.Cecil.Cil.OpCodes.Ldc_I4_5),
            "The actual crate must use native contents-plus-movement decay flags");
        var crate = types.Single(t => t.Name == "SupplyCrate");
        var open = crate.Methods.Single(m => m.Name == "Open").Body.Instructions;
        check(open.Any(i => i.Operand is MethodReference m && m.Name == "get_IsServer")
            && open.Any(i => i.Operand is MethodReference m && m.DeclaringType.Name == "SupplyCrateOpening" && m.Name == "TryOpen")
            && !open.Any(i => i.Operand is MethodReference m && (m.Name == "Request" || m.Name == "Broadcast")),
            "Opening must be host-only, use the tested one-shot transaction and rely on the native use relay");
        var store = crate.Methods.Single(m => m.Name == "Store").Body.Instructions;
        check(store.Any(i => i.Operand is MethodReference m && m.DeclaringType.Name == "Container" && m.Name == "LoadItem")
            && store.Any(i => i.Operand is MethodReference m && m.DeclaringType.Name == "CannonSpawnAmmo" && m.Name == "Give")
            && store.Any(i => i.Operand is MethodReference m && m.Name == "GrantSpawnDose"),
            "Supply contents must use native container loading and preserve cannon/injector initial loadouts");
        check(store.Any(i => i.Operand is FieldReference f && f.Name == "maxRounds")
            && store.Any(i => i.Operand is FieldReference f && f.Name == "rounds"), "The actual preset magazines must be filled to their native maximum");
        var elder = types.Single(t => t.Name == "ElderDragonDrop");
        var death = elder.Methods.Single(m => m.Name == "AfterDeath").Body.Instructions;
        check(death.Any(i => i.Operand as string == SupplyCrateRules.SealedId)
            && death.Any(i => i.Operand is MethodReference m && m.Name == "DropCount")
            && death.Any(i => i.Operand is MethodReference m && m.Name == "TryClaim"), "Actual elder deaths must give player-scaled crates on a one-time death claim");
        check(elder.Methods.Single(m => m.Name == "BeforeDeath").Body.Instructions.Any(i => i.Operand as string == "AllPlayers"),
            "The host must snapshot the real connected player registry rather than count bodies or only survivors");
        var catalogBuild = types.Single(t => t.Name == "SupplyLootCatalog").Methods.Single(m => m.Name == "Build").Body.Instructions;
        check(catalogBuild.Any(i => i.Operand is FieldReference f && f.Name == "GlobalItems")
            && catalogBuild.Any(i => i.Operand as string == SupplyCrateRules.SealedId)
            && catalogBuild.Any(i => i.Operand as string == SupplyCrateRules.OpenId),
            "Random supplies must include registered mods and exclude both crate IDs from their own contents");
        var naturalIds = types.Single(t => t.Name == "LootRules").Methods.Single(m => m.Name == "Select").Body.Instructions
            .Concat(types.Single(t => t.Name == "ArsenalLootPool").Methods.Single(m => m.Name == "Entries").Body.Instructions)
            .Select(i => i.Operand as string).ToArray();
        check(!naturalIds.Contains(SupplyCrateRules.SealedId) && !naturalIds.Contains(SupplyCrateRules.OpenId),
            "Neither crate may be selected through ordinary rare drops or the CCL bonus branch");
        check(types.Single(t => t.Name == "LoongNovaPlugin").Methods.Single(m => m.Name == "Awake").Body.Instructions
            .Any(i => i.Operand is TypeReference t && t.Name == "SupplyCratePatches"), "The use-delegate repair must be installed");
        foreach (var pair in new[] { ("supply-crate-open.png", "打开的箱子.png"), ("supply-crate-sealed.png", "关闭的箱子.png") })
        {
            var resource = (EmbeddedResource)assembly.MainModule.Resources.Single(r => r.Name == "DDEN.LoongNova.images." + pair.Item1);
            using var stream = resource.GetResourceStream(); using var bytes = new MemoryStream(); stream.CopyTo(bytes);
            string reference = Path.Combine(root, "arts", pair.Item2);
            if (!File.Exists(reference)) reference = Path.Combine(root, "src/DDEN.LoongNova/images", pair.Item1);
            check(bytes.ToArray().SequenceEqual(File.ReadAllBytes(reference)),
                "Crate artwork must match the supplied PNG byte-for-byte: " + pair.Item2);
        }
        using var contentStream = ((EmbeddedResource)assembly.MainModule.Resources.Single(r => r.Name == "DDEN.LoongNova.Content.json")).GetResourceStream();
        using var reader = new StreamReader(contentStream); var content = JObject.Parse(reader.ReadToEnd());
        check(content.Value<string>("supplySealedName") == "龙域武库-封闭补给箱" && content.Value<string>("supplyOpenName") == "龙域武库-补给箱",
            "Both requested crate names must be present in shipped content");
        using var mp = AssemblyDefinition.ReadAssembly(@"L:\SteamLibrary\steamapps\common\Casualties Unknown\BepInEx\plugins\Together\CasualtiesMP.dll");
        var use = mp.MainModule.Types.Single(t => t.FullName == "Together.Body_UseItem_MultiplayerPatch");
        check(use.Methods.Any(m => m.Name == "Server_RequestUseItem") && use.Methods.Any(m => m.Name == "Client_RequestUseItemRelay")
            && use.Methods.Single(m => m.Name == "Server_RequestUseItem").Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "CheckIfBodyReachThisItem"),
            "The installed native MP use protocol must still validate reach and relay the crate action");
        var decay = mp.MainModule.Types.Single(t => t.FullName == "Together.Item_HandleDecay_MultiplayerPatch").Methods.Single(m => m.Name == "Prefix").Body.Instructions;
        check(decay.Any(i => i.Operand is MethodReference m && m.Name == "get_IsClient")
            && decay.Any(i => i.Operand is MethodReference m && m.DeclaringType.Name == "BodyGetterOverrider" && m.Name == "GetBody"),
            "Native MP decay must ignore clients and use the actual carrier's movement instead of the host camera body");
    }
}
