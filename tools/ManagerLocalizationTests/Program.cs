using BepInEx.Configuration;
using DDEN.Shared;
using DDExcNet.CUmodM;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Globalization;
using System.Text.RegularExpressions;

int checks = 0;
void Check(bool ok, string why) { checks++; if (!ok) throw new Exception(why); }
foreach (string key in ManagerText.Keys)
{
    Check(ManagerText.HasEnglish(key), "English catalog covers " + key);
    var zh = ManagerText.Pattern(key, ManagerLanguage.Chinese);
    var en = ManagerText.Pattern(key, ManagerLanguage.English);
    string[] Slots(string s) => Regex.Matches(s, @"\{(\d+)(?:[^}]*)\}").Select(m => m.Groups[1].Value).OrderBy(x => x).ToArray();
    Check(Slots(zh).SequenceEqual(Slots(en)), "format arguments match for " + key);
    string.Format(CultureInfo.InvariantCulture, zh, Enumerable.Repeat<object>(1, 12).ToArray());
    string.Format(CultureInfo.InvariantCulture, en, Enumerable.Repeat<object>(1, 12).ToArray());
}
Check(ManagerText.Keys.Count() > 80, "full manager catalog embedded");
ManagerText.Language = ManagerLanguage.English;
Check(ManagerText.Get("nav.overview") == "Overview", "English selection applied");
ManagerText.Language = (ManagerLanguage)99;
Check(ManagerText.Get("nav.overview") == "总览", "unknown language safely falls back to Chinese");
Check(ManagerText.Resolve(new Dictionary<string,string>{{"k","中文"}}, new Dictionary<string,string>(), "k", ManagerLanguage.English) == "中文", "missing English falls back to Chinese");
Check(ManagerText.Get("footer", 1).Length > 0, "format mismatch does not escape into the GUI");
var parsed = ManagerText.Parse("# comment\nkey = <color=#abc>test</color>\\nnext\n");
Check(parsed["key"] == "<color=#abc>test</color>\nnext", "parser preserves rich text and intentional newlines");
ManagerText.Language = ManagerLanguage.English;
string unknown = @"未知第三方模组 L:\测试\user.cfg";
Check(ManagerText.CompatibilityNote(unknown) == unknown, "unknown third-party text and paths are unchanged");
Check(ManagerText.CompatibilityVersion("4 个模组同时改它") == "4 mods patching this", "patch counts are localized for display only");
Check(ManagerText.ModuleName("mpfix", "多人修复") == "Multiplayer fixes" && ManagerText.ModuleName("other", "自定义名称") == "自定义名称", "module titles use stable IDs without translating unknown names");

string root = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath(".");
string temp = Path.Combine(Path.GetTempPath(), "DDEN-manager-language-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    string file = Path.Combine(temp, "manager.cfg");
    var cfg = new ConfigFile(file, true);
    var language = cfg.Bind("UI", "Language", ManagerLanguage.Chinese);
    var on = cfg.Bind("Compat", "ScanOnStart", true);
    var scale = cfg.Bind("UI", "Scale", 1.1f, new ConfigDescription("界面缩放", new AcceptableValueRange<float>(.75f, 2)));
    var key = cfg.Bind("UI", "ToggleKey", new KeyboardShortcut(UnityEngine.KeyCode.F8));
    var experiment = cfg.Bind("联机合成", "修“无法生成物品”（FixModItemCraftSpawn）", true);
    DdenDocs.Add(experiment.Definition.Section, experiment.Definition.Key, "功能", "问题", "方案", "是（实验性）");
    DdenDocs.TryGet(experiment, out var originalDoc);
    string rawMarker = originalDoc.Experimental;
    string definitions = string.Join("|", cfg.Select(pair => pair.Key.Section + "/" + pair.Key.Key));
    language.SettingChanged += (s,e) => ManagerText.Language = language.Value;
    var r = new UnityEngine.Rect(0, 0, 650, 46);
    foreach (var mode in new[] { ManagerLanguage.Chinese, ManagerLanguage.English })
    {
        bool beforeLanguage = on.Value;
        language.Value = mode; ManagerText.Language = mode;
        Check(on.Value == beforeLanguage, "language change does not toggle functional settings in " + mode);
        Check(ManagerConfigUI.Experimental(experiment), "experimental prefix remains effective in " + mode);
        Check(originalDoc.Experimental == rawMarker, "experimental metadata stays raw in " + mode);
        Check(DdenDocs.Format(experiment).Contains(mode == ManagerLanguage.English ? "Experimental: Yes" : "实验性：是"), "marker presentation follows language");
        bool old = on.Value; DdenUI.ClickCheck = true;
        ManagerConfigUI.Draw(r, new[] { on });
        Check(on.Value != old, "localized checkbox writes its original config entry in " + mode);
        Check(DdenUI.Labels.Contains(ManagerText.Get("cfg.ScanOnStart")), "own configuration label follows selected language in " + mode);
        DdenUI.NextText = "1.25"; ManagerConfigUI.Draw(r, new[] { scale });
        Check(scale.Value == 1.25f, "localized number control writes typed float in " + mode);
        DdenUI.NextText = "20"; ManagerConfigUI.Draw(r, new[] { scale });
        Check(scale.Value == 2, "acceptable range clamping remains active in " + mode);
        DdenUI.NextText = "NaN"; ManagerConfigUI.Draw(r, new[] { scale });
        Check(scale.Value == 2, "non-finite numeric input is rejected in " + mode);
        DdenUI.ClickButton = true; ManagerConfigUI.Draw(r, new[] { key });
        Check(ReferenceEquals(KeyCapture.Target, key), "hotkey capture receives original entry in " + mode);
        Check(string.Join("|", cfg.Select(pair => pair.Key.Section + "/" + pair.Key.Key)) == definitions, "language does not rename persisted definitions in " + mode);
    }
    DdenUI.ClickButton = true; ManagerConfigUI.Draw(r, new[] { language });
    Check(language.Value == ManagerLanguage.Chinese && ManagerText.Language == ManagerLanguage.Chinese, "own settings language control fires normal setting change event");
    language.Value = ManagerLanguage.English; cfg.Save();
    var loaded = new ConfigFile(file, false).Bind("UI", "Language", ManagerLanguage.Chinese);
    Check(loaded.Value == ManagerLanguage.English, "language survives cfg reload");
}
finally
{
    string target = Path.GetFullPath(temp);
    if (!target.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
        || !Path.GetFileName(target).StartsWith("DDEN-manager-language-", StringComparison.Ordinal)) throw new Exception("unexpected test cleanup path");
    Directory.Delete(target, true);
}

using var manager = AssemblyDefinition.ReadAssembly(Path.Combine(root, "src/DDExcNet.CUmodM/bin/Release/DDExcNet-CUmodM.dll"));
using var shared = AssemblyDefinition.ReadAssembly(Path.Combine(root, "src/DDEN.Shared/bin/Release/DDEN.Shared.dll"));
foreach (string suffix in new[] { "manager.zh-CN.txt", "manager.en.txt", "about.en.txt" })
    Check(manager.MainModule.Resources.Count(r => r.Name.EndsWith(suffix, StringComparison.Ordinal)) == 1,
        "language resource embedded in main manager DLL without satellite dependency: " + suffix);
string hubSource = File.ReadAllText(Path.Combine(root, "src/DDExcNet.CUmodM/HubPlugin.cs"));
foreach (Match call in Regex.Matches(hubSource, "\\bT\\(\"([^\"]+)\""))
    Check(ManagerText.Has(call.Groups[1].Value), "manager UI text ID exists: " + call.Groups[1].Value);
Check(manager.Name.Version == new Version(2,1,0,0), "manager assembly version is 2.1.0");
var plugin = manager.MainModule.Types.Single(t => t.Name == "HubPlugin");
Check((string)plugin.CustomAttributes.Single(a => a.AttributeType.Name == "BepInPlugin").ConstructorArguments[2].Value == "2.1.0", "manager plugin metadata version matches assembly");
Check(shared.Name.Version == new Version(2,0,1,0), "Shared independent release is 2.0.1");
var route = plugin.Methods.Single(m => m.Name == "DrawContent");
foreach (var id in new[] { "overview", "compat", "config", "about", "config:", "ext:", "extconfig:" })
    Check(route.Body.Instructions.Any(i => i.Operand is string s && s == id), "native page route preserved: " + id);
var allConfig = plugin.Methods.Single(m => m.Name == "DrawAllConfig");
Check(allConfig.Body.Instructions.Any(i => i.Operand is MethodReference m && m.DeclaringType.Name == "ManagerConfigUI" && m.Name == "Draw"), "only manager settings use localized own renderer");
Check(allConfig.Body.Instructions.Any(i => i.Operand is MethodReference m && m.DeclaringType.Name == "DdenUI" && m.Name == "DrawConfigGrouped"), "module configs retain original policy renderer");
var renderer = shared.MainModule.Types.Single(t => t.Name == "DdenUI");
Check(renderer.Methods.Where(m => m.Name == "ConfigRow" && m.HasBody).SelectMany(m => m.Body.Instructions).Any(i => i.Operand is MethodReference m && m.Name == "IsExperimental"), "renderer uses the central experimental state classifier");
ManagerText.Language = ManagerLanguage.Chinese;
Check(ConfigNavigation.Selected("config:mpfix", "mpfix"), "internal sidebar remains selected in settings");
Check(!ConfigNavigation.Selected("config:perf", "mpfix"), "other settings do not select the wrong module");
Check(ConfigNavigation.Back("mpfix", true) == "mpfix" && ConfigNavigation.Back(null, false) == "overview", "back follows home availability");
Check(ConfigNavigation.Manager.StartsWith("config:") && ConfigNavigation.Manager != "config:manager", "manager config has a reserved route");
var folds = new ConfigFolds();
Check(folds.Height("module:mpfix", 200) == 236, "expanded height includes all rows");
folds.Toggle("module:mpfix");
Check(folds.Height("module:mpfix", 200) == 36 && folds.Expanded("module:perf"), "fold hides only one mod and reduces measured height");
ManagerText.Language = ManagerLanguage.English;
Check(!folds.Expanded("module:mpfix"), "language change retains stable fold identity");
folds.Toggle("module:mpfix");
Check(folds.Height("module:mpfix", 200) == 236, "reopening restores the complete height");

var metadataCfg = new ConfigFile(Path.Combine(Path.GetTempPath(), "DDEN-display-contract-not-saved.cfg"), false);
metadataCfg.SaveOnConfigSet = false;
var metadataEntry = metadataCfg.Bind("Test", "Key", true, "Original description");
DdenDocs.Add("Test", "Key", "Original", "", "", "是（旧备注）", "低（旧开销）");
Check(DdenDocs.IsExperimental(metadataEntry), "legacy experimental prefix still works");
var policy = new PresentationPolicy { Data = new DdenConfigMetadata { IsExperimental = false,
    PerformanceImpact = DdenPerformanceImpact.Low, PerformanceNote = "仅在打开界面时" } };
Check(!DdenDocs.IsExperimental(metadataEntry, policy), "explicit false overrides legacy true");
Check(DdenDocs.Format(metadataEntry, policy).Contains("Performance: Low, Only when opening the UI"), "canonical performance grade and separate translated note");
ManagerText.Language = ManagerLanguage.Chinese;
Check(DdenDocs.Format(metadataEntry, policy).Contains("性能影响：低，仅在打开界面时"), "Chinese grade presentation");
policy.Data.IsExperimental = null;
Check(DdenDocs.IsExperimental(metadataEntry, policy), "missing explicit state falls back to the legacy state");
DdenDocs.AddStructured("Test", "Key", "Original", "", "", false, DdenPerformanceImpact.High, performanceNote: "说明不会改变等级");
Check(!DdenDocs.IsExperimental(metadataEntry) && DdenDocs.Format(metadataEntry).Contains("性能影响：高，说明不会改变等级"), "typed internal metadata separates note from state");
Check(DdenDocs.ParseImpact("low") == DdenPerformanceImpact.Low && DdenDocs.ParseImpact("HIGH") == DdenPerformanceImpact.High
    && DdenDocs.ParseImpact("enormous") == DdenPerformanceImpact.Unspecified && DdenDocs.ParseImpact("999") == DdenPerformanceImpact.Unspecified, "grades validate canonical names and unknown input");
Check(typeof(DdenDocs).GetMethod("Add", new[] { typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(bool) }) != null,
    "legacy L1 reflection signature remains available");
foreach (var catalogFile in new[] { "DDEN.MPfix", "DDEN.NetPerfTracker" })
{
    var dictionary = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string,string>>(File.ReadAllText(Path.Combine(root, "src", catalogFile, "Resources/localization.en.json")));
    foreach (var pair in dictionary)
    {
        string[] Slots(string s) => Regex.Matches(s, @"\{(\d+)(?:[^}]*)\}").Select(m => m.Groups[1].Value).OrderBy(x => x).ToArray();
        Check(!string.IsNullOrWhiteSpace(pair.Value), "nonempty translation: " + pair.Key);
        Check(Slots(pair.Key).SequenceEqual(Slots(pair.Value)), "module format slots preserved: " + pair.Key);
        string.Format(CultureInfo.InvariantCulture, pair.Value, Enumerable.Repeat<object>(1, 20).ToArray());
    }
}
ManagerText.Language = ManagerLanguage.English;
Check(DDEN.MPfix.ModuleText.L("无芯片同步") == "Unchipped sync" && DDEN.NetPerfTracker.ModuleText.L("等待采样") == "Waiting for samples", "modules share the manager language");
Check(DDEN.MPfix.ModuleText.L("用户名称：无芯片同步") == "用户名称：无芯片同步", "exact translation never rewrites pieces of user text");
string entryIdentity = metadataEntry.Definition.Section + "/" + metadataEntry.Definition.Key;
DdenLocalization.Register(metadataCfg, DDEN.MPfix.ModuleText.Catalog);
Check(DdenLocalization.ConfigText(metadataEntry, "无芯片同步") == "Unchipped sync" && metadataEntry.Value
    && metadataEntry.Definition.Section + "/" + metadataEntry.Definition.Key == entryIdentity, "catalog translates display only and preserves original cfg identity and value");
DdenLocalization.Unregister(metadataCfg);
Check(DdenLocalization.ConfigText(metadataEntry, "无芯片同步") == "无芯片同步", "unregistered config is not translated using another mod catalog");
// Inspect production IL for entry points that cannot be exercised without a Unity window.
Check(plugin.Methods.Single(m => m.Name == "DrawOverview").Body.Instructions.Any(i => i.Operand is string text && text == ConfigNavigation.Manager), "overview links to the manager settings route");
Check(plugin.Methods.Single(m => m.Name == "DrawSidebar").Body.Instructions.Any(i => i.Operand is MethodReference method && method.Name == "Selected"), "sidebar uses module/settings selection helper");
var external = shared.MainModule.Types.Single(t => t.Name == "DdenExternal");
foreach (string member in new[] { "DdenConfigMetadata", "DdenLocalize" })
    Check(external.Methods.Single(m => m.Name == "TryBuild").Body.Instructions.Any(i => i.Operand is string text && text == member), "optional external bridge: " + member);

var l1 = new DdenExternal.Block();
int metadataReads = 0;
l1.ReadMetadata = e => { metadataReads++; return new Dictionary<string,string> {
    ["label"] = "Human label", ["description"] = "Human description", ["problem"] = "Problem", ["method"] = "Method",
    ["needsRestart"] = "true", ["experimental"] = "false", ["performance"] = "low", ["performanceNote"] = "Only on demand" }; };
Check(!DdenDocs.IsExperimental(metadataEntry, l1) && l1.Metadata(metadataEntry).PerformanceImpact == DdenPerformanceImpact.Low, "L1 string tokens become typed flags");
Check(DdenDocs.ConfigLabel(metadataEntry, l1) == "Human label" && metadataEntry.Definition.Key == "Key", "L1 display label never changes config identity");
Check(DdenDocs.Format(metadataEntry,l1).Contains("Only on demand") && DdenDocs.Format(metadataEntry,l1).Contains("Restart the game"), "L1 notes and restart flag are formatted by manager");
Check(metadataReads == 1, "L1 immutable metadata is cached per entry");
l1.Translate = (text, language) => throw new InvalidOperationException("fixture");
Check(l1.Localize("original", "en") == "original", "broken external localization falls back safely");
Check(l1.Translate == null, "a broken translator is disabled instead of throwing every GUI frame");
l1.Translate = (text, language) => "";
Check(l1.Localize("original", "en") == "original", "empty external translations fall back to the original text");
var legacyBlock = new DdenExternal.Block();
Check(legacyBlock.Metadata(metadataEntry) == null && legacyBlock.Localize("original", "en") == "original", "old external providers need no new members");

ExternalBridgeFixture.DdenConfig = metadataCfg;
var discovered = (DdenExternal.Block)typeof(DdenExternal).GetMethod("TryBuild", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
    .Invoke(null, new object[] { typeof(ExternalBridgeFixture).Assembly, "Fixture" })!;
Check(discovered != null && discovered.Type == typeof(ExternalBridgeFixture), "real L1 scanner discovers optional bridge members");
Check(DdenDocs.IsExperimental(metadataEntry, discovered) && discovered.Metadata(metadataEntry).PerformanceImpact == DdenPerformanceImpact.High, "real L1 metadata delegate binds");
Check(DdenDocs.Format(metadataEntry, discovered).Contains("High, English fixture note"), "real L1 localization delegate binds");
Check(ExternalBridgeFixture.Language == "en" && ExternalBridgeFixture.LanguageCalls == 1, "external home page receives initial language");
var registeredBlocks = (List<DdenExternal.Block>)DdenExternal.Blocks;
registeredBlocks.Add(discovered);
ManagerText.Language = ManagerLanguage.Chinese;
Check(ExternalBridgeFixture.Language == "zh-CN" && ExternalBridgeFixture.LanguageCalls == 2 && metadataEntry.Value, "language change notifies registered pages without altering cfg values");
ManagerText.Language = ManagerLanguage.Chinese;
Check(ExternalBridgeFixture.LanguageCalls == 2, "unchanged language does not resend notifications");
registeredBlocks.Clear();

var legacyEntry = metadataCfg.Bind("Legacy", "Flag", true);
DdenDocs.Add("Legacy", "Flag", "Original", "", "", "是 · 旧备注", "中（旧备注）");
var invalidMetadata = new DdenExternal.Block { ReadMetadata = e => new Dictionary<string,string> { ["experimental"] = "maybe", ["performance"] = "huge" } };
Check(DdenDocs.IsExperimental(legacyEntry, invalidMetadata) && DdenDocs.Format(legacyEntry, invalidMetadata).Contains("性能影响：中，旧备注"), "invalid explicit flags fall back to legacy instead of false or none");
Check(DdenDocs.Format(legacyEntry).Contains("实验性：是，旧备注"), "legacy non-parenthesized notes are preserved");

DdenDocs.Add("Legacy", "Flag", "Original", "", "", "否", "高于轻量跟踪；默认关闭");
Check(DdenDocs.Format(legacyEntry).Contains("性能影响：高于轻量跟踪；默认关闭"), "legacy prose is not mistaken for a canonical grade prefix");

// Independent product releases, optional API-v1 requirements and SemVer precedence.
foreach (var test in new[] {
    ("2.0", "2.0.0", DdenReleaseStatus.Current), ("v2", "2.0.0.0", DdenReleaseStatus.Current),
    ("1.7.3", "1.7.4", DdenReleaseStatus.Older), ("1.7.10", "1.7.4", DdenReleaseStatus.Newer),
    ("2.0.0-alpha", "2.0.0-alpha", DdenReleaseStatus.Current), ("2.0.0-alpha", "2.0.0", DdenReleaseStatus.Older),
    ("2.0.0", "2.0.0-alpha", DdenReleaseStatus.Newer), ("2.0.0-alpha.2", "2.0.0-alpha.10", DdenReleaseStatus.Older),
    ("2.0.0-alpha.99999999999999999999999999", "2.0.0-alpha.10", DdenReleaseStatus.Newer),
    ("2.0.0+build.7", "2.0", DdenReleaseStatus.Current), ("bad", "2.0", DdenReleaseStatus.Unknown),
    ("2.0.0-alpha.01", "2.0", DdenReleaseStatus.Unknown), ("2.0+", "2.0", DdenReleaseStatus.Unknown),
    ("-2.0", "2.0", DdenReleaseStatus.Unknown), ("02.0", "2.0", DdenReleaseStatus.Unknown),
    ("2.0", "bogus", DdenReleaseStatus.Unknown) })
    Check(DdenVersionPolicy.CompareRelease(test.Item1, test.Item2) == test.Item3, "release precedence: " + test.Item1 + " / " + test.Item2);
var declaration = new VersionedModuleFixture { Id = "mpfix", Version = "1.7.4", MinimumManagerVersion = "2.0.0", TestedManagerVersion = "2.0.0", MinimumSharedVersion = "2.0.0", TestedSharedVersion = "2.0.0", RequiredSharedApiVersion = 1 };
var compatible = DdenVersionPolicy.Evaluate("1.7.4", "1.7.4", "2.0", declaration, 1);
Check(compatible.Checked && !compatible.HasCompatibilityWarning && compatible.ReleaseStatus == DdenReleaseStatus.Current, "minimum is inclusive and module/product versions need not match");
var oldManager = DdenVersionPolicy.Evaluate("1.7.4", "1.7.4", "1.7.5", declaration, 1);
Check(oldManager.ManagerTooOld && oldManager.ReleaseStatus == DdenReleaseStatus.Current, "latest module can still require a newer manager");
Check(DdenVersionPolicy.Evaluate("1.7.3", "1.7.4", "2.0", declaration, 1).ReleaseStatus == DdenReleaseStatus.Older, "outdated module is independent from manager compatibility");
Check(!DdenVersionPolicy.Evaluate("anything", "1.7.4", "2.0", null, 1).Checked, "undeclared legacy modules are exempt even if ID is recorded");
Check(!DdenVersionPolicy.Evaluate("1", "1", "2", new VersionedModuleFixture(), 1).Checked, "empty optional requirements remain exempt");
Check(DdenVersionPolicy.Evaluate("1.7.4", "1.7.4", "2.0", declaration, 2).ApiMismatch, "API generation mismatch is distinct from product version");
declaration.MinimumManagerVersion = "2.2.0";
Check(ModuleVersionCatalog.Check(declaration).ManagerTooOld, "manager catalog checks declared minimum");
declaration.MinimumManagerVersion = "2.0.0";
DdenHub.Notify();
Check(!ModuleVersionCatalog.Check(declaration).ManagerTooOld, "changed requirements invalidate cached result");
declaration.Version = "1.7.3";
DdenHub.Notify();
Check(ModuleVersionCatalog.Check(declaration).ReleaseStatus == DdenReleaseStatus.Older, "changed release invalidates cache");
declaration.MinimumManagerVersion = "nonsense";
DdenHub.Notify();
Check(ModuleVersionCatalog.Check(declaration).InvalidDeclaration, "malformed requirements are not silently called compatible");
declaration.Id = "future-module";
Check(DdenVersionPolicy.Evaluate("3.0", null, "2.0", declaration, 1).ReleaseStatus == DdenReleaseStatus.Unrecorded, "unrecorded release is explicit");
foreach (DdenReleaseStatus state in Enum.GetValues<DdenReleaseStatus>()) Check(ManagerText.Has("versions.release." + state), "release status translation exists: " + state);
using (var mp = AssemblyDefinition.ReadAssembly(Path.Combine(root, "src/DDEN.MPfix/bin/Release/DDEN.MPfix.dll")))
    Check(mp.Name.Version == new Version(1,8,0,0), "MPfix independent assembly version");
using (var perf = AssemblyDefinition.ReadAssembly(Path.Combine(root, "src/DDEN.NetPerfTracker/bin/Release/DDEN.NetPerfTracker.dll")))
    Check(perf.Name.Version == new Version(2,3,1,0), "tracker independent assembly version");
using (var editor = AssemblyDefinition.ReadAssembly(Path.Combine(root, "src/DDEN.SaveEditor/bin/Release/DDEN.SaveEditor.dll")))
{
    Check(editor.Name.Version == new Version(2,0,1,0), "editor numeric assembly version");
    Check(((string)editor.CustomAttributes.Single(a => a.AttributeType.Name == "AssemblyInformationalVersionAttribute").ConstructorArguments[0].Value).StartsWith("2.0.1-alpha"), "editor preserves alpha release metadata");
    var editorPlugin = editor.MainModule.Types.Single(t => t.Name == "SaveEditorPlugin");
    Check((string)editorPlugin.CustomAttributes.Single(a => a.AttributeType.Name == "BepInPlugin").ConstructorArguments[2].Value == "2.0.1", "editor loader uses a valid numeric version");
    Check((string)editorPlugin.Fields.Single(f => f.Name == "ReleaseVersion").Constant == "2.0.1-alpha", "editor module display preserves alpha");
}
Check(shared.MainModule.Types.Single(t => t.Name == "DdenApi").Fields.Single(f => f.Name == "Version").Constant.Equals(1), "Shared v2 exports API generation 1");
foreach (string file in new[] { "src/DDEN.MPfix/bin/Release/DDEN.MPfix.dll", "src/DDEN.NetPerfTracker/bin/Release/DDEN.NetPerfTracker.dll", "src/DDEN.SaveEditor/bin/Release/DDEN.SaveEditor.dll" })
{
    using var assembly = AssemblyDefinition.ReadAssembly(Path.Combine(root, file));
    var module = assembly.MainModule.Types.Single(t => t.Interfaces.Any(i => i.InterfaceType.Name == "IDdenModule"));
    Check(module.Interfaces.Any(i => i.InterfaceType.Name == "IDdenModuleVersion"), "built-in module declares compatibility: " + file);
    Check(module.Methods.Single(m => m.Name == "get_MinimumManagerVersion").Body.Instructions.Any(i => i.Operand is string v && v == "2.0.0"), "module declares manager ≥2.0.0");
}

Check(!ModuleVersionCatalog.Check(new LegacyVersionFixture()).Checked, "legacy version getter is never queried for compatibility checks");
var brokenVersion = new BrokenVersionFixture();
Check(ModuleVersionCatalog.Check(brokenVersion).InvalidDeclaration && ModuleVersionCatalog.Check(brokenVersion).InvalidDeclaration && brokenVersion.Reads == 1, "broken requirement getter is isolated and cached instead of retried every frame");
Check(ModuleVersionCatalog.ManagerRelease == manager.Name.Version.ToString(3) && ModuleVersionCatalog.SupportedApi == DdenApi.CurrentVersion, "manager catalog matches its independent release and supported API");
Check(new BepInEx.BepInPlugin("test.dden.version", "fixture", "2.0.0-alpha").Version == null && new BepInEx.BepInPlugin("test.dden.version", "fixture", "2.0.0").Version != null, "installed loader requires numeric version; alpha remains release metadata");
Check(plugin.Methods.Single(m => m.Name == "OnGUI").Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "DrawModuleVersionWarning"), "module requirements warn even with the manager panel closed");
foreach (string binary in new[] { "DDEN.MPfix", "DDEN.NetPerfTracker", "DDEN.SaveEditor", "DDEN.NetPerfTracker.Enhanced", "DDEN.LoongNova", "DDEN.UnitListMgr" })
{
    using var component = AssemblyDefinition.ReadAssembly(Path.Combine(root, "src", binary, "bin/Release", binary + ".dll"));
    var moduleType = component.MainModule.Types.Single(t => t.Interfaces.Any(i => i.InterfaceType.Name == "IDdenModule"));
    string id = moduleType.Methods.Single(m => m.Name == "get_Id").Body.Instructions.Select(i => i.Operand).OfType<string>().Single();
    string release = ((string)component.CustomAttributes.Single(a => a.AttributeType.Name == "AssemblyInformationalVersionAttribute").ConstructorArguments[0].Value).Split('+')[0];
    Check(ModuleVersionCatalog.Latest(id) == release, "recorded release matches shipped component: " + id);
}

declaration.MinimumManagerVersion = "2.0.0";
declaration.MinimumSharedVersion = "2.0.0";
var oldShared = DdenVersionPolicy.Evaluate("1.7.4", "1.7.4", "2.0", "1.9.9", declaration, 1);
Check(oldShared.SharedTooOld && !oldShared.ManagerTooOld && !oldShared.ApiMismatch, "current manager and API cannot hide an outdated Shared product");
var onlyOldManager = DdenVersionPolicy.Evaluate("1.7.4", "1.7.4", "1.9", "2.0", declaration, 1);
Check(onlyOldManager.ManagerTooOld && !onlyOldManager.SharedTooOld, "manager and Shared minimums are independent");
Check(!DdenVersionPolicy.Evaluate("1.7.4", "1.7.4", "2.0", "2.1", declaration, 1).HasCompatibilityWarning, "newer Shared product with matching API is allowed");
Check(DdenVersionPolicy.Evaluate("1.7.4", "1.7.4", "2.0", "2.0.0-alpha", declaration, 1).SharedTooOld, "Shared prerelease is below required stable release");
declaration.MinimumSharedVersion = "invalid";
Check(DdenVersionPolicy.Evaluate("1.7.4", "1.7.4", "2.0", "2.0", declaration, 1).InvalidDeclaration, "invalid Shared requirement is explicit");
foreach (string binary in new[] { "DDEN.MPfix", "DDEN.NetPerfTracker", "DDEN.SaveEditor", "DDEN.NetPerfTracker.Enhanced", "DDEN.LoongNova", "DDEN.UnitListMgr" })
{
    using var component = AssemblyDefinition.ReadAssembly(Path.Combine(root, "src", binary, "bin/Release", binary + ".dll"));
    var moduleType = component.MainModule.Types.Single(t => t.Interfaces.Any(i => i.InterfaceType.Name == "IDdenModule"));
    Check(moduleType.Methods.Single(m => m.Name == "get_MinimumSharedVersion").Body.Instructions.Any(i => i.Operand is string v && v == "2.0.1"), "module independently declares Shared ≥2.0.1");
}

Check(!ModuleVersionCatalog.Check(new EmptyVersionFixture()).Checked, "empty requirements are exempt before accessing a potentially unsupported release getter");
Check(shared.CustomAttributes.Any(a => a.AttributeType.Name == "AssemblyMetadataAttribute" && (string)a.ConstructorArguments[0].Value == "DDEN.ApiVersion" && (string)a.ConstructorArguments[1].Value == "1"), "API generation is recorded separately in Shared assembly metadata");

// Card states use activity flags rather than localized summaries or "enabled" alone.
var currentCardVersion = DdenVersionPolicy.Evaluate("1.7.4", "1.7.4", "2.0", "2.0", new VersionedModuleFixture { MinimumManagerVersion = "2.0", MinimumSharedVersion = "2.0" }, 1);
Check(OverviewCardStyle.State(currentCardVersion, true, true, false, DdenHealth.Ok) == OverviewCardState.Loaded, "offline or paused enabled module is blue/loaded");
Check(OverviewCardStyle.State(currentCardVersion, true, true, true, DdenHealth.Ok) == OverviewCardState.Running, "active work is green/running");
Check(OverviewCardStyle.State(currentCardVersion, true, false, true, DdenHealth.Ok) == OverviewCardState.Loaded, "disabled gate cannot remain green");
Check(OverviewCardStyle.State(currentCardVersion, false, true, true, DdenHealth.Ok) == OverviewCardState.Problem, "missing dependencies override active state");
Check(OverviewCardStyle.State(currentCardVersion, true, true, true, DdenHealth.Warn) == OverviewCardState.Problem, "runtime problem overrides active state");
foreach (string installed in new[] { "1.7.3", "1.7.5", "unreadable" })
{
    var mismatch = DdenVersionPolicy.Evaluate(installed, "1.7.4", "2.0", "2.0", new VersionedModuleFixture { MinimumManagerVersion = "2.0" }, 1);
    Check(OverviewCardStyle.State(mismatch, true, true, true, DdenHealth.Ok) == OverviewCardState.Problem, "record mismatch is red and described in tooltip: " + installed);
}
Check(!OverviewCardStyle.VersionMismatch(DdenReleaseStatus.NotChecked) && !OverviewCardStyle.VersionMismatch(DdenReleaseStatus.Unrecorded), "exempt and unrecorded modules are not falsely marked mismatched");
Check(OverviewCardStyle.State(oldShared, true, true, true, DdenHealth.Ok) == OverviewCardState.Problem, "Shared requirement mismatch overrides green");
var overviewBody = plugin.Methods.Single(m => m.Name == "DrawOverview").Body.Instructions;
var rectHeights = overviewBody.Select((instruction, index) => (instruction,index))
    .Where(v => v.index > 0 && v.instruction.Operand is MethodReference method && method.Name == ".ctor" && method.DeclaringType.Name == "Rect")
    .Select(v => overviewBody[v.index - 1].Operand).OfType<float>().ToArray();
Check(rectHeights.Contains(56f) && rectHeights.Contains(50f) && !rectHeights.Contains(78f) && !rectHeights.Contains(74f), "compiled overview restores original module/utility card heights");
Check(overviewBody.Any(i => i.Operand is MethodReference m && m.Name == "DrawCardLine") && overviewBody.Any(i => i.Operand is MethodReference m && m.Name == "DescribeModuleCard"), "compiled overview uses full-height status stripes and hover descriptions");
Check(!overviewBody.Any(i => i.Operand is MethodReference m && m.Name == "DrawModuleVersionLine"), "version diagnostics no longer add a visible third row");
var drawStripe = plugin.Methods.Single(m => m.Name == "DrawCardLine").Body.Instructions;
Check(drawStripe.Any(i => i.Operand is MethodReference m && m.Name == "get_height"), "stripe uses the entire card height");
foreach (string binary in new[] { "DDEN.MPfix", "DDEN.NetPerfTracker" })
{
    using var component = AssemblyDefinition.ReadAssembly(Path.Combine(root, "src", binary, "bin/Release", binary + ".dll"));
    var moduleType = component.MainModule.Types.Single(t => t.Interfaces.Any(i => i.InterfaceType.Name == "IDdenModule"));
    Check(moduleType.Interfaces.Any(i => i.InterfaceType.Name == "IDdenModuleActivity"), "explicit activity contract: " + binary);
    var getter = moduleType.Methods.Single(m => m.Name == "get_IsRunning").Body.Instructions;
    Check(getter.Any(i => i.Operand is MethodReference m && m.Name == (binary == "DDEN.MPfix" ? "get_IsRunning" : "get_Recording")), "activity follows live MP state or actual sampling: " + binary);
}

Console.WriteLine($"PASS {checks} manager localization checks (catalog, real cfg entries, markers, routes, versions); GUI input simulated");
