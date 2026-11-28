using DDEN.LoongNova;
using Mono.Cecil;
using Newtonsoft.Json.Linq;

internal static class ProgressionChecks
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (int stage in new[] { 0, 1, 2 })
        {
            check(RecipeUnlockRules.Allows(stage, "rifle") && RecipeUnlockRules.Allows(stage, "other.mod.item"), "Progression never gates vanilla or other mods");
            check(RecipeUnlockRules.Allows(stage, Registration.WrenchId) == (stage >= 1), "The wrench recipe requires first acquiring the Dragon");
            foreach (var recipe in ArsenalRecipes.Create())
                check(RecipeUnlockRules.Allows(stage, recipe.result.id) == (stage == 2), "All arsenal, utility and medical recipes require acquiring the wrench");
            check(RecipeUnlockRules.Acquire(stage, Registration.DragonId) == Math.Max(stage, 1), "A Dragon grants only the wrench stage and never revokes knowledge");
            check(RecipeUnlockRules.Acquire(stage, Registration.WrenchId) == 2, "A looted/purchased wrench directly grants both stages");
            check(RecipeUnlockRules.Acquire(stage, Registration.CellId) == stage, "Obtaining other equipment does not unlock recipes");
        }
        foreach (string id in new[] { Registration.MatterId, Registration.CellId, Registration.GunId, Registration.BeltId, Registration.PickaxeId })
            check(!RecipeUnlockRules.Allows(1, id) && RecipeUnlockRules.Allows(2, id), "Every equipment/material/refill result requires stage two");
        int first = RecipeUnlockRules.Observe(0, new[] { Registration.DragonId });
        int second = RecipeUnlockRules.Observe(0, Array.Empty<string>());
        check(first == 1 && second == 0, "Players keep independent progression");
        check(RecipeUnlockRules.Observe(first, Array.Empty<string>()) == 1, "Dropping or consuming an acquired Dragon does not revoke the unlock");
        check(RecipeUnlockRules.Observe(0, new[] { Registration.WrenchId, Registration.DragonId }) == 2
            && RecipeUnlockRules.Observe(0, new[] { Registration.DragonId, Registration.WrenchId }) == 2, "Inventory ordering does not affect unlocks");
    }
    internal static void Inspect(AssemblyDefinition assembly, Action<bool, string> check, string root)
    {
        var types = assembly.MainModule.Types;
        var hooks = types.Single(t => t.Name == "RecipeUnlockPatches");
        check(hooks.Methods.Single(m => m.Name == "EndCommit").ReturnType.FullName == "System.Void", "The MP finalizer restores scope without replacing original exceptions");
        check(hooks.Fields.Any(f => f.Name == "_craftBody" && f.CustomAttributes.Any(a => a.AttributeType.Name == "ThreadStaticAttribute")), "Craft actor scopes must not leak across threads");
        var visible = hooks.Methods.Single(m => m.Name == "Visible").Body.Instructions;
        check(visible.Any(i => i.Operand is MethodReference m && m.Name == "Allows"), "The actual native visibility getter is gated by actor progression");
        var plugin = types.Single(t => t.Name == "LoongNovaPlugin").Methods.Single(m => m.Name == "Awake").Body.Instructions;
        check(plugin.Any(i => i.Operand is MethodReference m && m.Name == "RegisterBodyProvider"), "Progression must use CCL's player-scoped body save provider");
        check(plugin.Any(i => i.Operand is TypeReference t && t.Name == "RecipeUnlockPatches"), "The plugin must install actual progression hooks");
        var receiver = types.Single(t => t.Name == "WeaponService").Methods.Single(m => m.Name == "Receive").Body.Instructions;
        check(receiver.Any(i => i.Operand is string s && s == "recipes")
            && receiver.Any(i => i.Operand is MethodReference m && m.DeclaringType.Name == "NetworkAccess" && m.Name == "Body"), "Recipe queries resolve the actor from authenticated sender identity");
        using var mp = AssemblyDefinition.ReadAssembly(@"L:\SteamLibrary\steamapps\common\Casualties Unknown\BepInEx\plugins\Together\CasualtiesMP.dll");
        var commit = mp.MainModule.Types.Single(t => t.FullName == "Together.CraftCommit").Methods.Single(m => m.Name == "Commit");
        check(commit.Parameters.Count == 5 && commit.Parameters[3].ParameterType.FullName == "Recipe", "Installed MP must match the scoped commit adapter");
        var code = commit.Body.Instructions;
        int match = code.ToList().FindIndex(i => i.Operand is MethodReference m && m.Name == "CheckIfTheseItemsAreEnoughForTheRecipe");
        int lift = code.ToList().FindIndex(i => i.Operand is MethodReference m && m.Name == "LiftIngredients");
        check(match >= 0 && lift > match && code.Skip(match).Take(lift - match).Any(i => i.Operand is MethodReference m && m.Name == "Deny"),
            "A locked MP recipe must enter the native denial path before ingredients are lifted or consumed");
        foreach (string language in new[] { "EN", "zh-CN" })
        {
            var locale = JObject.Parse(File.ReadAllText(Path.Combine(root, "src/DDEN.LoongNova/Locales", language + ".json")));
            string all = locale.ToString();
            check(!all.Contains("实验性龙息单元") && !all.Contains("Experimental Dragonbreath Cell") && !all.Contains("Loong Arsenal"), "Old cell names and old English branding must be absent");
            check(locale["other"]!["dden.loongnova.unlockNeedsDragon"] != null && locale["other"]!["dden.loongnova.unlockNeedsWrench"] != null, "Both stages have localized lock hints");
            string wrench = locale["item"]![Registration.WrenchId + "dsc"]!.Value<string>()!;
            check(!wrench.Contains("%"), "Wrench descriptions must leave Dragonization durability cost to native quality displays");
        }
        var english = JObject.Parse(File.ReadAllText(Path.Combine(root, "src/DDEN.LoongNova/Locales/EN.json")));
        var chinese = JObject.Parse(File.ReadAllText(Path.Combine(root, "src/DDEN.LoongNova/Locales/zh-CN.json")));
        foreach (var name in ((JObject)chinese["item"]!).Properties().Where(p => !p.Name.EndsWith("dsc") && p.Value.Value<string>()!.StartsWith("龙域武库")))
            check(english["item"]![name.Name]!.Value<string>()!.StartsWith("DDArsenal-"), "Every branded item must use the requested English prefix");
        check(chinese["item"]![Registration.CellId]!.Value<string>() == "龙域武库-龙息能量单元"
            && english["item"]![Registration.CellId]!.Value<string>() == "DDArsenal-Dragonbreath Energy Cell", "The cell has its new Chinese and English names without changing item identity");
    }
}
