using DDEN.LoongNova;
using Mono.Cecil;
using Newtonsoft.Json.Linq;
using System.Globalization;
using System.Text.RegularExpressions;

internal static class LocaleChecks
{
    private static JObject Read(AssemblyDefinition assembly, string language)
    {
        using var stream = ((EmbeddedResource)assembly.MainModule.Resources.Single(r => r.Name == "DDEN.LoongNova.Locales." + language + ".json")).GetResourceStream();
        using var reader = new StreamReader(stream);
        return JObject.Parse(reader.ReadToEnd());
    }
    internal static JObject Chinese(AssemblyDefinition assembly)
    {
        var catalog = new LoongTextCatalog(Read(assembly, "EN"), Read(assembly, "zh-CN"));
        var result = new JObject();
        foreach (var key in catalog.Keys) result[key] = catalog.Text(key, "zh-CN");
        return result;
    }
    internal static void Inspect(AssemblyDefinition assembly, Action<bool, string> check, string root)
    {
        var en = Read(assembly, "EN"); var zh = Read(assembly, "zh-CN");
        var catalog = new LoongTextCatalog(en, zh);
        foreach (var section in new[] { "item", "other" })
        {
            var english = (JObject)en[section]!; var chinese = (JObject)zh[section]!;
            check(english.Properties().Select(p => p.Name).Order().SequenceEqual(chinese.Properties().Select(p => p.Name).Order()),
                "Native language sections must have identical key sets in EN and zh-CN");
            foreach (var property in english.Properties())
            {
                string a = property.Value.Value<string>()!; string b = chinese.Value<string>(property.Name)!;
                check(!string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) && !Regex.IsMatch(a, "[\\u4e00-\\u9fff]"),
                    "Each shipped English and Chinese entry must be present and translated: " + property.Name);
                var formatsA = Regex.Matches(a, @"\{\d+(?:[^{}]*)\}").Select(m => m.Value).Order();
                var formatsB = Regex.Matches(b, @"\{\d+(?:[^{}]*)\}").Select(m => m.Value).Order();
                check(formatsA.SequenceEqual(formatsB), "Language variants must preserve identical composite format arguments: " + property.Name);
                check(property.Name.StartsWith("dden.loongnova.", StringComparison.Ordinal) || property.Name == "cqloongnova.dragonization",
                    "The locale overlay must not replace other modules' or vanilla keys");
                _ = string.Format(CultureInfo.InvariantCulture, a, 12.375, 1.2375, 2);
                _ = string.Format(CultureInfo.InvariantCulture, b, 12.375, 1.2375, 2);
            }
        }
        foreach (var item in LoongTextCatalog.Items)
        foreach (string suffix in item.Key == "refitted" ? new[] { "Name", "Description" } : new[] { "Name", "Description", "Card", "Recipe" })
        foreach (string language in new[] { "EN", "zh-CN" })
        {
            string key = item.Key + suffix;
            check(catalog.Text(key, language) != key && !string.IsNullOrWhiteSpace(catalog.Text(key, language)),
                "Every item card and native description must resolve in both languages: " + key);
        }
        check(catalog.Text("module", "EN") == "DDArsenal" && catalog.Text("module", "zh-CN") == "龙域武库"
            && catalog.Text("module", "fr") == "DDArsenal", "Game language changes must choose the current locale and fall back to English");
        check(catalog.Text("gunName", "EN", (section, key) => section == "item" && key == Registration.GunId ? "External override" : null) == "External override",
            "Native/CCL loose-file translations must take precedence over embedded defaults");
        check(catalog.Text("module", "fr", (section, key) => key == "dden.loongnova.module" ? "Arsenal local" : null) == "Arsenal local",
            "Additional game languages must work through ordinary CCL overlays without new module branches");
        foreach (string key in new[] { "supplySealedDescription", "supplyOpenDescription" })
        {
            string a = catalog.Text(key, "EN"), b = catalog.Text(key, "zh-CN");
            check(a.Count(c => c == '.') == 1 && b.Count(c => c == '。') == 1 && !Regex.IsMatch(a + b, @"\d"),
                "Both crate descriptions must be one sentence explaining identity/use without detailed numeric parameters");
        }
        foreach (string language in new[] { "EN", "zh-CN" })
        {
            string path = Path.Combine(root, "src/DDEN.LoongNova/Locales", language + ".json");
            check(JToken.DeepEquals(Read(assembly, language), JObject.Parse(File.ReadAllText(path))),
                "Native locale resources must exactly match the independently editable source language files");
        }
        check(!assembly.MainModule.Resources.Any(r => r.Name.EndsWith("Content.json")), "The old mixed content resource must be removed");
        var types = assembly.MainModule.Types;
        var text = types.Single(t => t.Name == "Registration").Methods.Single(m => m.Name == "Text").Body.Instructions;
        check(text.Any(i => i.Operand is MethodReference m && m.DeclaringType.Name == "LoongLocalization" && m.Name == "Text"),
            "The actual item/UI/Buff text accessor must call the localized catalog");
        check(types.Single(t=>t.Name=="LoongLocalization").Methods.Single(m=>m.Name=="Text").Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.DeclaringType.Name=="DdenLocalization"&&m.Name=="get_Language"),"LoongNova follows the manager language rather than a conflicting game table");
        var hooks = types.Single(t => t.Name == "LoongLocalePatches");
        check(hooks.Methods.Single(m => m.Name == "LanguageLoaded").Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "RefreshItems")
            && hooks.Methods.Single(m => m.Name == "ItemText").Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "RefreshItem"),
            "Game language reloads and cached ItemInfo reads must refresh LoongNova display fields");
        var refresh = types.Single(t => t.Name == "LoongLocalization").Methods.Single(m => m.Name == "RefreshItem").Body.Instructions;
        check(refresh.Where(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Stfld).All(i => i.Operand is FieldReference f && (f.Name == "fullName" || f.Name == "description")),
            "Localization must never modify item identities, weapon stats, save state or other behavioral fields");
        check(types.Single(t => t.Name == "LoongNovaPlugin").Methods.Single(m => m.Name == "Awake").Body.Instructions.Any(i => i.Operand is TypeReference t && t.Name == "LoongLocalePatches"),
            "The real plugin must install both language hooks");
        var normalize = types.Single(t => t.Name == "DragonGun").Methods.Single(m => m.Name == "Normalize").Body.Instructions;
        check(normalize.Any(i => i.Operand is float value && value == 0.25f) && normalize.Any(i => i.Operand is float value && value == 0.2f),
            "Beam rifle wear must become 0.25 percent while the charge cannon retains 0.2 percent");
        var registration = types.Single(t => t.Name == "Registration").Methods.Single(m => m.Name == "Register").Body.Instructions;
        bool CapacityIsFifty(Mono.Collections.Generic.Collection<Mono.Cecil.Cil.Instruction> code, string field) => code.Select((ins, i) => (ins, i))
            .Any(p => p.ins.Operand is FieldReference f && f.Name == field && p.i > 0
                && code.Skip(Math.Max(0, p.i - 4)).Take(Math.Min(4, p.i)).Any(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ldc_I4_S && Convert.ToInt32(i.Operand) == 50));
        check(CapacityIsFifty(registration, "MagCapacity") && CapacityIsFifty(normalize, "magCapacity"),
            "Registered and runtime magazine displays must both derive the fifty-shot capacity from the 1000-energy cell");
        var cannon = types.Single(t => t.Name == "CannonService").Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions);
        check(cannon.Any(i => i.Operand is float value && value == 0.002f) && !cannon.Any(i => i.Operand is float value && value == 0.0025f),
            "Changing beam-rifle wear must not change cannon ammunition firing wear");
        using var ccl = AssemblyDefinition.ReadAssembly(File.Exists(@"L:\SteamLibrary\steamapps\common\Casualties Unknown\BepInEx\plugins\CUCoreLib.dll") ? @"L:\SteamLibrary\steamapps\common\Casualties Unknown\BepInEx\plugins\CUCoreLib.dll" : @"L:\SteamLibrary\steamapps\common\Casualties Unknown\BepInEx\plugins\Together\CUCoreLib.dll");
        check(ccl.MainModule.Types.Single(t => t.FullName == "CUCoreLib.Helpers.LocaleLoader").Methods.Any(m => m.Name == "FindEmbeddedOverlayResources"),
            "The installed CCL must support the chosen native-format embedded locale overlays");
    }
}
