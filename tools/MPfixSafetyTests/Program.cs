using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using BepInEx.Configuration;
using DDEN.MPfix;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class Program
{
    private static int _checks;
    private static void Check(bool condition, string message)
    { _checks++; if (!condition) throw new Exception(message); }
    private static void Main(string[] args)
    {
        string root = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath(".");
        string dir = Path.Combine(Path.GetTempPath(), "DDEN-mpfix-safety-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Names(); Dependencies(dir); Assemblies(root); DirectionNameReads(root);
            Console.WriteLine("PASS " + _checks + " checks (names, missing dependencies, authority override, installed API contracts, compiled spawn/identity boundaries)");
        }
        finally
        {
            string target = Path.GetFullPath(dir);
            if (!target.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(target).StartsWith("DDEN-mpfix-safety-", StringComparison.Ordinal)) throw new Exception("unexpected cleanup path");
            Directory.Delete(target, true);
        }
    }
    private static void Names()
    {
        const ulong steam = 76561198000000001;
        Check(NameDisplayRules.Choose("", "中文主机", steam) == "中文主机", "empty host name uses persona");
        Check(NameDisplayRules.Choose(steam.ToString(), "中文客机", steam) == "中文客机", "numeric fallback restored");
        Check(NameDisplayRules.Choose("???", "中文", steam) == "中文", "question marks restored");
        Check(NameDisplayRules.Choose("Ab_1", "中文Ab_1", steam) == "中文Ab_1", "partially stripped mixed name restored");
        Check(NameDisplayRules.Choose("ChosenAlias", "中文Ab_1", steam) == "ChosenAlias", "explicit different English alias preserved");
        Check(NameDisplayRules.Choose("Alpha", "Alpha", steam) == "Alpha", "English unaffected");
        string damaged = new string("中文Ab".Select(c => (char)(byte)c).ToArray());
        Check(NameDisplayRules.Choose(damaged, "中文Ab", steam) == "中文Ab", "lossy one-byte name matched to persona");
        Check(NameDisplayRules.Choose("LAN_user", null, 0) == "LAN_user", "LAN logical name preserved");
        Check(NameDisplayRules.Choose(null, null, 0) == "玩家", "unavailable persona never yields blank");
        Check(NameDisplayRules.Choose("EMPTY_STRING", steam.ToString(), steam) == "EMPTY_STRING", "numeric Steam lookup failure not treated as a recovered name");
        Check(NameDisplayRules.Clean("a\r\n\t\0\u202eb<color=red>\ud800c\udc00") == "abcolor=redc", "control, bidi, markup and isolated surrogates removed");
        string emoji = string.Concat(Enumerable.Repeat("😀", 40));
        string bounded = NameDisplayRules.Clean(emoji);
        Check(bounded.Length == 64 && bounded.EnumerateRunes().Count() == 32, "length bound never splits surrogate pairs");
        foreach (string original in new[] { "Alice", "中文", "", "??", steam.ToString() })
        {
            string copy = original;
            NameDisplayRules.Choose(original, "测试", steam);
            Check(original == copy, "display resolver never mutates input identity");
        }
    }
    private static void Dependencies(string dir)
    {
        var file = new ConfigFile(Path.Combine(dir, "client.cfg"), true);
        var master = file.Bind("总开关", "启用本模块（MasterEnabled）", true);
        var opt = file.Bind("总开关", "采用权威主机配置（UseHostAuthorityConfig）", true);
        AuthoritySetting<bool> craft = file.Bind("联机合成", "修（FixModItemCraftSpawn）", true);
        AuthoritySetting<bool> names = file.Bind("玩家名称", "尝试（FixPlayerNames）", true);
        AuthoritySetting<bool> damage = file.Bind("联机修正", "修（SyncBodyDamage）", true);
        AuthoritySetting<bool> quiet = file.Bind("联机修正", "商人界面不再刷“prefab 为空”（QuietTraderUIModItems）", true);
        AuthoritySetting<bool> entertainment = file.Bind("娱乐", "道生一 一生二（DoublePurchase）", true);
        var verbose = file.Bind("调试", "详细日志（Verbose）", true);
        var hotkey = file.Bind("界面", "热键", "F10");
        var entries = new ConfigEntryBase[] { master, opt, craft.Entry, names.Entry, damage.Entry, quiet.Entry, entertainment.Entry, verbose, hotkey };
        var absent = new FeatureDependencies(entries, _ => false);
        absent.EnforceAll();
        Check(master.Value && verbose.Value && absent.Available(hotkey), "missing MP leaves local master/debug/UI editable");
        Check(!opt.Value && !craft.Value && !names.Value && !damage.Value && !quiet.Value && !entertainment.Value, "missing MP forces all functional bools off");
        Check(!absent.Available(names.Entry) && absent.Reason(names.Entry).Contains("缺少"), "disabled controls have an explanation");
        names.Local.Value = true; absent.Enforce(names.Entry);
        Check(!names.Value, "external configuration edits can be re-enforced");

        var noCore = new FeatureDependencies(entries, guid => guid == "CasualtiesMP");
        foreach (var setting in new IAuthoritySetting[] { craft, names, damage, quiet, entertainment })
        { var item = setting; setting.Available = () => noCore.Available(item.Entry); }
        names.Value = damage.Value = entertainment.Value = true;
        noCore.EnforceAll();
        Check(names.Value && damage.Value && entertainment.Value, "missing CCL keeps independent MP display/damage/entertainment usable");
        Check(!craft.Value && !quiet.Value && !noCore.Available(opt), "missing CCL blocks generation, relay and host authority");
        var overlay = new AuthorityOverlay(new IAuthoritySetting[] { craft, names, damage, quiet, entertainment }, null);
        var snapshot = overlay.Capture(Guid.NewGuid().ToString("N"), 1, true);
        snapshot.Values[craft.Id].Text = "true";
        snapshot.Values[quiet.Id].Text = "true";
        Check(overlay.Apply(snapshot), "valid host snapshot decodes");
        Check(!craft.Value && !quiet.Value && names.Value, "host authority cannot reactivate a missing dependency");
        overlay.Release();
        Check(!craft.Value && names.Value, "releasing host authority preserves dependency restrictions");
        var ready = new FeatureDependencies(entries, _ => true);
        Check(entries.All(ready.Available), "all controls available when required mods exist");
        Check(!craft.Local.Value, "installing dependency does not silently re-enable a saved-off switch");
    }
    private static IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types)
        { yield return type; foreach (var child in Types(type.NestedTypes)) yield return child; }
    }
    private static void DirectionNameReads(string root)
    {
        // Execute the actual compiled transpiler against installed MP metadata; no Unity methods run.
        string game = @"L:\SteamLibrary\steamapps\common\Casualties Unknown";
        string output = Path.Combine(root, "src/DDEN.MPfix/bin/Release");
        string[] search = { output, Path.Combine(game, "BepInEx/core"), Path.Combine(game, "CasualtiesUnknown_Data/Managed"), Path.Combine(game, "BepInEx/plugins/Together") };
        System.Reflection.Assembly Resolve(System.Runtime.Loader.AssemblyLoadContext context, System.Reflection.AssemblyName name)
        {
            foreach (string folder in search)
            {
                string path = Path.Combine(folder, name.Name + ".dll");
                if (File.Exists(path)) return context.LoadFromAssemblyPath(path);
            }
            return null;
        }
        var load = System.Runtime.Loader.AssemblyLoadContext.Default;
        load.Resolving += Resolve;
        try
        {
            var fix = load.LoadFromAssemblyPath(Path.Combine(output, "DDEN.MPfix.dll"));
            var mp = load.LoadFromAssemblyPath(Path.Combine(game, "BepInEx/plugins/Together/CasualtiesMP.dll"));
            var harmony = load.LoadFromAssemblyPath(Path.Combine(game, "BepInEx/core/0Harmony.dll"));
            var type = fix.GetType("DDEN.MPfix.PlayerNameRepair", true);
            var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
            var body = mp.GetType("Together.NetBody", true);
            var getter = body.GetProperty("playerName").GetMethod;
            type.GetField("_bodyNameGetter", flags).SetValue(null, getter);
            type.GetField("_nativeName", flags).SetValue(null, mp.GetType("Together.ScavPlayer").GetField("playerName"));
            type.GetField("_netApply", flags).SetValue(null, body.GetMethods().Single(m => m.Name == "ApplyNameAndColor"));
            var original = mp.GetType("Together.UIInGame").GetMethod("_GUI__DrawPlayerDirection", flags);
            var instructionType = harmony.GetType("HarmonyLib.CodeInstruction", true);
            var transpile = type.GetMethod("DisplayReads", flags);
            var operand = instructionType.GetField("operand");
            var opcode = instructionType.GetField("opcode");
            var noop = typeof(string).GetProperty("Length").GetMethod;
            foreach (var call in new[] { System.Reflection.Emit.OpCodes.Callvirt, System.Reflection.Emit.OpCodes.Call })
            {
                object instruction = Activator.CreateInstance(instructionType, new object[] { call, getter });
                object unrelated = Activator.CreateInstance(instructionType, new object[] { System.Reflection.Emit.OpCodes.Callvirt, noop });
                var input = Array.CreateInstance(instructionType, 2);
                input.SetValue(instruction, 0); input.SetValue(unrelated, 1);
                var rewritten = ((System.Collections.IEnumerable)transpile.Invoke(null, new object[] { input, original })).Cast<object>().ToArray();
                Check(rewritten.Length == 2 && ReferenceEquals(rewritten[0], instruction), "getter rewrite keeps instruction identity/labels");
                Check(((System.Reflection.MethodInfo)operand.GetValue(rewritten[0])).Name == "GuiBodyName"
                    && (System.Reflection.Emit.OpCode)opcode.GetValue(rewritten[0]) == System.Reflection.Emit.OpCodes.Call,
                    "installed NetBody property read routes to display resolver: " + call);
                Check(ReferenceEquals(operand.GetValue(rewritten[1]), noop), "unrelated property calls remain unchanged");
            }
        }
        finally { load.Resolving -= Resolve; }
    }
    private static void Assemblies(string root)
    {
        string game = @"L:\SteamLibrary\steamapps\common\Casualties Unknown";
        using var mpfix = AssemblyDefinition.ReadAssembly(Path.Combine(root, "src/DDEN.MPfix/bin/Release/DDEN.MPfix.dll"));
        using var shared = AssemblyDefinition.ReadAssembly(Path.Combine(root, "src/DDEN.Shared/bin/Release/DDEN.Shared.dll"));
        using var ccl = AssemblyDefinition.ReadAssembly(Path.Combine(game, "BepInEx/plugins/CUCoreLib.dll"));
        using var mp = AssemblyDefinition.ReadAssembly(Path.Combine(game, "BepInEx/plugins/Together/CasualtiesMP.dll"));
        var all = Types(mpfix.MainModule.Types).ToArray();
        var spawn = all.Single(t => t.FullName == "DDEN.MPfix.Utils_Create_ModItemPatch");
        var prefix = spawn.Methods.Single(m => m.Name == "Prefix");
        Check(prefix.ReturnType.FullName == "System.Void", "generation prefix never suppresses normal generation");
        var prefixCalls = prefix.Body.Instructions.Where(i => i.Operand is MethodReference).Select(i => (MethodReference)i.Operand).ToArray();
        Check(prefixCalls.Length == 1 && prefixCalls[0].Name == "NoteId", "generation prefix observes purchases and cannot instantiate/register items");
        var recovery = spawn.Methods.Single(m => m.Name == "TryFailedSpawn");
        Check(recovery.Body.Instructions.Count(i => i.Operand is MethodReference m && m.DeclaringType.Name == "ModItemFactory" && m.Name == "Create") == 1,
            "recovery contains one complete factory call, no alternate second construction path");
        Check(!recovery.Body.Instructions.Any(i => i.Operand is MethodReference m && m.DeclaringType.FullName == "UnityEngine.Object" && m.Name.StartsWith("Instantiate")), "recovery never clones inactive templates itself");
        foreach (var asm in new[] { mpfix, shared })
            Check(!asm.MainModule.AssemblyReferences.Any(r => r.Name == "CUCoreLib"), asm.Name.Name + " can load without its optional CCL assembly");
        var name = all.Single(t => t.Name == "PlayerNameRepair");
        var lifecycle = name.Methods.Single(m => m.Name == "UpdateLifecycle");
        Check(!lifecycle.Body.Instructions.Any(i => i.Operand is MethodReference m
            && (m.Name == "Invoke" || m.Name == "GetValue" || m.Name == "Display" || m.Name == "GetComponent"
                || m.DeclaringType.Name == "PlayerNameFonts" && m.Name == "Tick")),
            "lifecycle never polls players, redraws labels or invokes native name refresh");
        Check(name.Methods.Single(m => m.Name == "RefreshNametag").Body.Instructions.Any(i => i.Operand is MethodReference m
            && m.DeclaringType.Name == "PlayerNameRepair" && m.Name == "Display")
            && name.Methods.Single(m => m.Name == "Install").Body.Instructions.Any(i => i.Operand is string s && s == "LateUpdate"),
            "native visual refresh reads unified display names without invoking native name/color refresh");
        Check(!name.Methods.Any(m => m.Name == "BodyName"), "display repair no longer rewrites NetBody name callback arguments");
        foreach (string method in new[] { "Display", "ChatName" })
            Check(name.Methods.Single(m => m.Name == method).Body.Instructions.Any(i => i.Operand is MethodReference m
                && m.DeclaringType.Name == "NameDisplayCache" && m.Name == "Resolve"), method + " uses the shared name resolver");
        Check(!name.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).Any(i => i.Operand is MethodReference m
            && m.DeclaringType.Name == "NameDisplayRules" && m.Name == "Choose"), "UI adapters contain no duplicate name recovery rules");
        var fonts = all.Single(t => t.Name == "PlayerNameFonts");
        Check(!Types(new[] { fonts }).SelectMany(t => t.Methods).Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).Any(i => i.Operand is MethodReference m && (m.Name == "GetOSInstalledFontNames" || m.Name == "CreateDynamicFontFromOSFont" || m.DeclaringType.FullName == "TMPro.TMP_FontAsset" && m.Name == "CreateFontAsset")), "name font repair never queries or creates system fonts or generated fallback atlases");
        Check(!name.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).Any(i =>
            i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f && f.Name == "playerName" && f.DeclaringType.FullName == "Together.ScavPlayer"), "name repair never writes logical name identity");
        var nativeTypes = Types(mp.MainModule.Types).ToDictionary(t => t.FullName);
        var direction = nativeTypes["Together.UIInGame"].Methods.Single(m => m.Name == "_GUI__DrawPlayerDirection");
        Check(direction.Body.Instructions.Any(i => i.OpCode == OpCodes.Callvirt && i.Operand is MethodReference m
            && m.Name == "get_playerName" && m.DeclaringType.FullName == "Together.NetBody"), "installed Shift pointer uses the property getter regression case");
        var template = all.Single(t => t.Name == "ModItemTemplateRepair");
        Check(!Types(new[] { template }).SelectMany(t => t.Methods).Where(m => m.HasBody).SelectMany(m => m.Body.Instructions)
            .Any(i => i.Operand is MethodReference m && (m.Name == "Instantiate" || m.Name == "InstantiateReturn" || m.Name == "SetActive" || m.Name == "Destroy")),
            "template repair never spawns, activates, protects or destroys item objects");
        var cclTypes = Types(ccl.MainModule.Types).ToDictionary(t => t.FullName);
        var cclFactory = cclTypes["CUCoreLib.Helpers.CustomInstantiate"];
        Check(cclFactory.Fields.Any(f => f.Name == "_templateCache")
            && cclFactory.Methods.Any(m => m.Name == "ClearTemplateCache" && m.Parameters.Count == 1)
            && cclFactory.Methods.Any(m => m.Name == "GetOrCreateTemplate" && m.ReturnType.FullName == "UnityEngine.GameObject"), "installed CCL cache rebuild API matches");
        Check(cclTypes["CUCoreLib.Registries.ItemRegistry"].Fields.Any(f => f.Name == "RegisteredItems"), "installed CCL registration table is a field");
        foreach (var spec in new[] {
            ("Together.ScavPlayer", "ApplyNameAndColor"), ("Together.ScavPlayer", "get_AllPlayers"),
            ("Together.KSteam", "GetSteamUsername"), ("Together.KSteam", "OnPersonaStateChange"),
            ("Together.NetBody", "ApplyNameAndColor"), ("Together.CharStatusVisuals", "ApplyNameAndColor"),
            ("Together.CharStatusVisuals", "LateUpdate"), ("Together.Chat", "MeasureChatLine"),
            ("Together.UIMainMenu", "_GUI___DrawPlayerInList"), ("Together.UIMainMenu", "_GUI__PlrListDrawPlr"),
            ("Together.UIInGame", "_GUI__DrawPlayerDirection"), ("Together.UIInGame", "_GUI_DoGeneralMPGui"),
            ("Together.UIInGame", "_GUI_PlayerInteractionGui"), ("Together.UI.TeammateList.Roster", "Touch"),
            ("Together.UI.TeammateList.Roster", "ReadBlood"), ("Together.UI.TeammateList.Roster", "GameFont"),
            ("Together.MinigameMPManager/MinigameRemotePeerHand", "Start"),
            ("Together.MinigameMPManager/MinigameRemotePeerHand", "Update"),
            ("Together.Chat", "Client_ChatMessageReceive"), ("Together.Chat", "_GUI_DrawChat"),
            ("Together.UIBullshit", "FocusedChatFont"),
            ("Together.Chat/ChatMsgContainer", "Compile"),
            ("Together.PlayerCamera_TryCraft_MultiplayerPatch", "RecipeResult_SpawnResult") })
            Check(nativeTypes.TryGetValue(spec.Item1, out var nativeType) && nativeType.Methods.Any(m => m.Name == spec.Item2), "installed MP hook exists: " + spec);
        var bridge = Types(ccl.MainModule.Types).Single(t => t.FullName == "CUCoreLib.Networking.MultiplayerBridge");
        foreach (var spec in new[] {
            ("Broadcast", new[] { "System.String", "System.Object", "System.Boolean", "System.Boolean" }),
            ("SendToServer", new[] { "System.String", "System.Object", "System.Boolean" }),
            ("RequestServer", new[] { "System.String", "System.Object", "System.Action`1<Newtonsoft.Json.Linq.JToken>", "System.Boolean" }),
            ("RegisterClientHandler", new[] { "System.String", "System.Action`1<Newtonsoft.Json.Linq.JToken>" }),
            ("RegisterServerHandler", new[] { "System.String", "System.Func`2<Newtonsoft.Json.Linq.JToken,Newtonsoft.Json.Linq.JToken>" }) })
            Check(bridge.Methods.Any(m => m.IsPublic && m.Name == spec.Item1 && m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(spec.Item2)), "installed bridge signature matches: " + spec.Item1);
    }
}
