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
            Names(); Dependencies(dir); Assemblies(root);
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
        AuthoritySetting<bool> aggressive = file.Bind("联机修正", "激进（AggressiveConverge）", true);
        AuthoritySetting<bool> entertainment = file.Bind("娱乐", "道生一 一生二（DoublePurchase）", true);
        var verbose = file.Bind("调试", "详细日志（Verbose）", true);
        var hotkey = file.Bind("界面", "热键", "F10");
        var entries = new ConfigEntryBase[] { master, opt, craft.Entry, names.Entry, damage.Entry, aggressive.Entry, entertainment.Entry, verbose, hotkey };
        var absent = new FeatureDependencies(entries, _ => false);
        absent.EnforceAll();
        Check(master.Value && verbose.Value && absent.Available(hotkey), "missing MP leaves local master/debug/UI editable");
        Check(!opt.Value && !craft.Value && !names.Value && !damage.Value && !aggressive.Value && !entertainment.Value, "missing MP forces all functional bools off");
        Check(!absent.Available(names.Entry) && absent.Reason(names.Entry).Contains("缺少"), "disabled controls have an explanation");
        names.Local.Value = true; absent.Enforce(names.Entry);
        Check(!names.Value, "external configuration edits can be re-enforced");

        var noCore = new FeatureDependencies(entries, guid => guid == "CasualtiesMP");
        foreach (var setting in new IAuthoritySetting[] { craft, names, damage, aggressive, entertainment })
        { var item = setting; setting.Available = () => noCore.Available(item.Entry); }
        names.Value = damage.Value = entertainment.Value = true;
        noCore.EnforceAll();
        Check(names.Value && damage.Value && entertainment.Value, "missing CCL keeps independent MP display/damage/entertainment usable");
        Check(!craft.Value && !aggressive.Value && !noCore.Available(opt), "missing CCL blocks generation, relay and host authority");
        var overlay = new AuthorityOverlay(new IAuthoritySetting[] { craft, names, damage, aggressive, entertainment }, null);
        var snapshot = overlay.Capture(Guid.NewGuid().ToString("N"), 1, true);
        snapshot.Values[craft.Id].Text = "true";
        snapshot.Values[aggressive.Id].Text = "true";
        Check(overlay.Apply(snapshot), "valid host snapshot decodes");
        Check(!craft.Value && !aggressive.Value && names.Value, "host authority cannot reactivate a missing dependency");
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
        var fonts = all.Single(t => t.Name == "PlayerNameFonts");
        Check(!Types(new[] { fonts }).SelectMany(t => t.Methods).Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).Any(i => i.Operand is MethodReference m && (m.Name == "GetOSInstalledFontNames" || m.Name == "CreateDynamicFontFromOSFont" || m.DeclaringType.FullName == "TMPro.TMP_FontAsset" && m.Name == "CreateFontAsset")), "name font repair never queries or creates system fonts or generated fallback atlases");
        Check(!name.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).Any(i =>
            i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f && f.Name == "playerName" && f.DeclaringType.FullName == "Together.ScavPlayer"), "name repair never writes logical name identity");
        var nativeTypes = Types(mp.MainModule.Types).ToDictionary(t => t.FullName);
        foreach (var spec in new[] {
            ("Together.ScavPlayer", "ApplyNameAndColor"), ("Together.ScavPlayer", "get_AllPlayers"),
            ("Together.KSteam", "GetSteamUsername"), ("Together.KSteam", "OnPersonaStateChange"),
            ("Together.NetBody", "ApplyNameAndColor"), ("Together.CharStatusVisuals", "ApplyNameAndColor"),
            ("Together.UIMainMenu", "_GUI___DrawPlayerInList"), ("Together.UIMainMenu", "_GUI__PlrListDrawPlr"),
            ("Together.UIInGame", "_GUI__DrawPlayerDirection"), ("Together.UIInGame", "_GUI_DoGeneralMPGui"),
            ("Together.UIInGame", "_GUI_PlayerInteractionGui"), ("Together.UI.TeammateList.Roster", "Touch"),
            ("Together.UI.TeammateList.Roster", "ReadBlood"), ("Together.UI.TeammateList.Roster", "GameFont"),
            ("Together.MinigameMPManager/MinigameRemotePeerHand", "Start"),
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
