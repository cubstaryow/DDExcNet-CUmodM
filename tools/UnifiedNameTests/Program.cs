using System.Reflection;
using DDEN.MPfix;
using Together;
using TMPro;
using UnityEngine;

int checks = 0;
void Check(bool ok, string why) { checks++; if (!ok) throw new Exception(why); }
object Call(string name, params object[] args) => typeof(PlayerNameRepair).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);

const ulong steam = 76561198000000001;
Check(NameDisplayRules.Choose("中?玩家", "中文玩家", steam) == "中文玩家", "partial missing-glyph substitution recovers through the shared rules");
Check(NameDisplayRules.Choose("中?别名", "中文玩家", steam) == "中?别名", "different custom alias is not mistaken for damaged Steam nickname");
string persona = steam.ToString(); int calls = 0;
var cache = new NameDisplayCache(id => { calls++; return persona; });
object playerKey = new object();
cache.Capture(playerKey, steam.ToString(), steam);
var history = cache.CaptureSnapshot(playerKey, steam.ToString(), steam);
Check(cache.Resolve(playerKey, steam.ToString(), steam, 0) == steam.ToString(), "unavailable Steam name initially preserves native fallback");
persona = "中文玩家";
Check(cache.Resolve(history, 1) == steam.ToString() && calls == 1, "all views share one bounded Steam lookup");
cache.Invalidate();
Check(cache.Resolve(history, 1) == "中文玩家", "native persona event lets historical message recover");
Check(cache.Resolve(playerKey, steam.ToString(), steam, 1) == cache.Resolve(history, 1) && calls == 2, "live and chat views share the same result and lookup");
persona = steam.ToString();
Check(cache.Resolve(history, 7) == "中文玩家", "transient numeric response cannot erase recovered nickname");
cache.Clear();
Check(cache.Resolve(history, 8) == "中文玩家", "detached historical snapshot retains its last successful name");
var alias = new object(); cache.Capture(alias, "CustomAlias", steam);
persona = "另一个中文"; cache.Invalidate();
Check(cache.Resolve(alias, "CustomAlias", steam, 9) == "CustomAlias", "explicit English alias is preserved");
var oldAlias = cache.CaptureSnapshot(alias, "CustomAlias", steam);
cache.Capture(alias, "NewAlias", steam);
Check(cache.Resolve(alias, "NewAlias", steam, 9) == "NewAlias" && cache.Resolve(oldAlias, 9) == "CustomAlias", "live alias changes do not rewrite historical custom aliases");
var delayedHistory = cache.CaptureMessage(alias, "OlderAlias", "NewAlias", steam);
Check(cache.Resolve(delayedHistory, 9) == "OlderAlias", "history first bound after a rename preserves its original alias");
var rebound = new object(); cache.Capture(rebound, "原来的玩家", steam);
Check(cache.Resolve(rebound, "42", 0, 9) == "42", "reused player binding never leaks another Steam identity");
var lan = new object(); cache.Capture(lan, "局域网玩家", 0);
int before = calls;
Check(cache.Resolve(lan, "???", 0, 9) == "局域网玩家" && calls == before, "LAN raw Chinese is preserved without Steam lookup");

TMP_Settings.defaultFontAsset = new TMP_FontAsset { name = "GameMain" };
var chinese = new TMP_FontAsset("中文玩家缺") { name = "GameChinese", sourceFontFile = new Font("中文玩家缺") };
TMP_Settings.fallbackFontAssets = new() { chinese };
Resources.Loaded = Array.Empty<UnityEngine.Object>();
CoopModAssets.gamefont = new Font();
var harmony = new HarmonyLib.Harmony();
Check(PlayerNameRepair.Install(harmony), "production name adapters resolve native callback contracts");
Check(!harmony.Hooks.Any(h => h.Target.DeclaringType == typeof(NetBody) && h.Target.Name == "ApplyNameAndColor"), "no patch writes NetBody names or refreshes location arrows");
Check(harmony.Hooks.Any(h => h.Target.Name == "LateUpdate" && h.Postfix?.Name == "RefreshNametag"), "labels update inside the native visual callback");
Check(harmony.Hooks.Any(h => h.Target.Name == "MeasureChatLine" && h.Prefix?.Name == "RefreshChatLine"), "chat cache updates before original layout");
Chat.BeforeCompile = m => Call("ChatName", m);
Chat.AfterCompile = m => Call("ChatCompiled", m);
var player = new ScavPlayer { playerName = steam.ToString(), SteamId = steam };
Call("Capture", player, steam.ToString());
PlayerNameRepair.UpdateLifecycle();
var message = new Chat.ChatMsgContainer(player);
Check(message.name == steam.ToString() && message.result_nametag.Contains(steam.ToString()), "native message initially compiles numeric name");
var label = new TMP_Text { text = player.playerName, font = TMP_Settings.defaultFontAsset };
var go = new GameObject(); go.Components.Add(label); label.gameObject = go;
var visual = new CharStatusVisuals { plr = player, nametag = go };
Call("PrepareNametag", visual);
KSteam.Names[steam] = "中文玩家"; PlayerNameRepair.Invalidate();
Time.unscaledTime = 1;
Call("RefreshNametag", visual); Call("RefreshChatLine", message);
Check(label.text == "中文玩家" && message.name == "中文玩家" && message.result_nametag == "[中文玩家]: ", "native label and old chat recover together from the unified cache");
Check(player.playerName == steam.ToString(), "logical player name remains unchanged");
Check(NetBody.NameRefreshCalls == 0 && NetBody.ArrowAlpha == 0.2f, "recovery never invokes MP name/color refresh or wakes arrows");
int compiles = message.Compiles, lookups = KSteam.Calls;
for (int i = 0; i < 60; i++) { Call("RefreshChatLine", message); Call("RefreshNametag", visual); }
Check(message.Compiles == compiles && KSteam.Calls == lookups, "unchanged frames neither recompile history nor query Steam again");
player.Destroyed = true; message.plr = null;
KSteam.Names[steam] = steam.ToString(); PlayerNameRepair.Invalidate(); Time.unscaledTime = 8;
Call("RefreshChatLine", message);
Check(message.name == "中文玩家" && message.result_nametag.Contains("中文玩家"), "departed player keeps historical Chinese name through later Steam failure");
var system = new Chat.ChatMsgContainer(steam.ToString());
Call("RefreshChatLine", system);
Check(system.name == steam.ToString() && system.Compiles == 1, "plain system text is never guessed into a player identity");
MPfixPlugin.CfgFixPlayerNames.Value = false; PlayerNameRepair.UpdateLifecycle();
Call("RefreshChatLine", message);
Check(message.name == steam.ToString() && label.text == steam.ToString(), "disabling restores native chat name and label text");
MPfixPlugin.CfgFixPlayerNames.Value = true; PlayerNameRepair.Invalidate(); Time.unscaledTime = 9;
Call("RefreshChatLine", message);
Check(message.name == "中文玩家", "re-enabling retains the historical sender snapshot");
var custom = new ScavPlayer { playerName = "CustomAlias", SteamId = steam };
Call("Capture", custom, "CustomAlias");
var customMessage = new Chat.ChatMsgContainer(custom); Call("RefreshChatLine", customMessage);
Check(customMessage.name == "CustomAlias", "chat adapter preserves explicit English alias");
var lateBinding = new Chat.ChatMsgContainer("256"); lateBinding.plr = custom;
Time.unscaledTime = 10; Call("RefreshChatLine", lateBinding);
Check(lateBinding.name == "CustomAlias", "unresolved line can recover when native MP supplies a player binding");
Check(system.name == steam.ToString(), "later player messages never rebind a plain numeric system label");
PlayerNameRepair.Stop();
Check(Font.Created == 0 && TMP_FontAsset.Created == 0, "unified repair uses game glyphs without system fonts or generated atlases");
var rawField = typeof(ScavPlayer).GetField("playerName");
var addColor = typeof(Chat).GetMethod("AddColorTags");
var joinReads = new[] {
    new HarmonyLib.CodeInstruction(System.Reflection.Emit.OpCodes.Ldfld, rawField),
    new HarmonyLib.CodeInstruction(System.Reflection.Emit.OpCodes.Stloc_0),
    new HarmonyLib.CodeInstruction(System.Reflection.Emit.OpCodes.Ldfld, rawField),
    new HarmonyLib.CodeInstruction(System.Reflection.Emit.OpCodes.Call, addColor)
};
var joinOutput = ((IEnumerable<HarmonyLib.CodeInstruction>)Call("AnnouncementReads", joinReads, typeof(ScavPlayer).GetMethod("Start"))).ToArray();
Check(joinOutput[0].opcode == System.Reflection.Emit.OpCodes.Ldfld, "join preserves identity reads outside announcement formatting");
Check(joinOutput[2].opcode == System.Reflection.Emit.OpCodes.Call && ((MethodInfo)joinOutput[2].operand).Name == "GuiDisplay", "join announcement uses unified display name");
foreach (var method in new[] { typeof(ServerMain).GetMethod("UpdateSyncClientsToClients"), typeof(Lang).GetMethod("TryParseIndividualTag") })
{
    var output = ((IEnumerable<HarmonyLib.CodeInstruction>)Call("AnnouncementReads", new[] { new HarmonyLib.CodeInstruction(System.Reflection.Emit.OpCodes.Ldfld, rawField) }, method)).Single();
    Check(output.opcode == System.Reflection.Emit.OpCodes.Call && ((MethodInfo)output.operand).Name == (method.DeclaringType == typeof(Lang) ? "Display" : "GuiDisplay"), "finish/tag name reads use unified display name");
}
MPfixPlugin.CfgFixPlayerNames.Value = false;
var liveAnnouncementPlayer = new ScavPlayer { playerName = "native" };
Check((string)Call("GuiDisplay", liveAnnouncementPlayer) == "native", "announcement adapter restores native name when repair is disabled");
Console.WriteLine($"PASS {checks} unified name/cache/chat checks; Unity/Steam/native callbacks simulated");

player = new ScavPlayer { playerName = steam.ToString(), SteamId = steam };
var bodyForDisplay = new NetBody { plr = player, NativeBodyName = "" };
MPfixPlugin.Active = true; Mp.IsRunning = true; MPfixPlugin.CfgFixPlayerNames.Value = true;
KSteam.Names[steam] = "中文玩家"; PlayerNameRepair.Invalidate();
Check((string)Call("TooltipDisplay", bodyForDisplay) == "中文玩家", "hover and medical bodyName recover Chinese persona without rewriting empty native bodyName");
Check(bodyForDisplay.bodyName == "" && player.playerName == steam.ToString(), "new medical and hover adapters leave identity and native bodyName unchanged");
MPfixPlugin.CfgFixPlayerNames.Value = false;
Check((string)Call("TooltipDisplay", bodyForDisplay) == "", "disabled bodyName adapter preserves exact empty original");
bodyForDisplay.NativeBodyName = "42";
Check((string)Call("TooltipDisplay", bodyForDisplay) == "42", "disabled bodyName adapter preserves network ID fallback");
MPfixPlugin.CfgFixPlayerNames.Value = true;
var npc = new NetBody { NativeBodyName = "生物" };
Check((string)Call("TooltipDisplay", npc) == "生物", "non-player body names are preserved");
var viewerLabel = new TMP_Text { text = steam.ToString(), font = TMP_Settings.defaultFontAsset };
Call("PrepareViewerName", viewerLabel, player);
Check(viewerLabel.text == "中文玩家", "medical viewer callback binds Chinese display and game font together");
KSteam.Names[steam] = "中文"; PlayerNameRepair.Invalidate(); Time.unscaledTime += 1;
PlayerNameRepair.UpdateLifecycle();
Check(viewerLabel.text == "中文", "medical viewer text follows persona updates without regenerating the medical panel");
var translate = typeof(TogetherChineseRepair).GetMethod("Translate", BindingFlags.Static | BindingFlags.NonPublic);
object[] translated = { "wv_viewers", "bad older game translation" }; translate.Invoke(null, translated);
Check((string)translated[1] == "查看者", "Chinese built-in dictionary takes precedence over stale game overrides");
translated = new object[] { "rule_name_RagdollImpact", "Ragdoll impact" }; translate.Invoke(null, translated);
Check((string)translated[1] == "布娃娃冲击", "missing 5.5.5 Chinese key has a scoped fallback");
Lang.CurrentLocale = "EN"; translated = new object[] { "wv_viewers", "Viewers" }; translate.Invoke(null, translated);
Check((string)translated[1] == "Viewers", "Together language preview is respected; English is never forced to Chinese");
Console.WriteLine("PASS " + checks + " unified-name and Chinese display checks");

var medicalBody = new Component { gameObject = new GameObject() };
var medicalNetBody = new NetBody { plr = player }; medicalBody.gameObject.Components.Add(medicalNetBody);
WoundView.view.nameText = new TMP_Text { text = "old", font = TMP_Settings.defaultFontAsset };
WoundView.view.bodyStatText = new TMP_Text { text = "old", font = TMP_Settings.defaultFontAsset };
Call("PrepareMedicalTitle", medicalBody);
Check(WoundView.view.nameText.text == "中文", "medical title uses the same bound player name and font as viewers");
medicalNetBody.IsLocal = true; WoundView.view.nameText.text = "local species"; WoundView.view.bodyStatText.text = "local stats";
Call("PrepareMedicalTitle", medicalBody); Time.unscaledTime += 1; PlayerNameRepair.UpdateLifecycle();
Check(WoundView.view.nameText.text == "local species" && WoundView.view.bodyStatText.text == "local stats", "returning to local medical view releases remote aliases without overwriting native species and stats");
foreach(string property in new[] { "bodyName", "playerName" })
{
    var getter = typeof(NetBody).GetProperty(property).GetMethod;
    var instructions = new[] { new HarmonyLib.CodeInstruction(System.Reflection.Emit.OpCodes.Callvirt,getter) };
    var output = ((IEnumerable<HarmonyLib.CodeInstruction>)Call("DisplayReads", instructions, typeof(NetBody).GetMethod("DoMouseHoverTooltip"))).Single();
    Check(((MethodInfo)output.operand).Name == (property == "bodyName" ? "TooltipDisplay" : "TmpBodyDisplay"), "medical and hover preserve distinct native getter fallback: " + property);
    instructions = new[] { new HarmonyLib.CodeInstruction(System.Reflection.Emit.OpCodes.Callvirt,getter) };
    output = ((IEnumerable<HarmonyLib.CodeInstruction>)Call("AnnouncementReads", instructions, typeof(Lang).GetMethod("TryParseIndividualTag"))).Single();
    Check(((MethodInfo)output.operand).Name == (property == "bodyName" ? "TooltipDisplay" : "TmpBodyDisplay"), "serialized language display tag covers getter: " + property);
}
Console.WriteLine("PASS " + checks + " name/medical/Chinese display checks; UI callbacks simulated");

Mp.IsRunning = false; PlayerNameRepair.UpdateLifecycle();
Check(PlayerNameRepair.FontEnabled && !PlayerNameRepair.Enabled, "disconnect releases multiplayer aliases while Chinese lobby fonts stay available");
var boundLabels = (System.Collections.ICollection)typeof(PlayerNameRepair).GetField("LabelStates", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
Check(boundLabels.Count == 0, "disconnect releases old world label bindings instead of retaining them in the lobby");
PlayerNameRepair.Stop();
Console.WriteLine("PASS " + checks + " unified display lifecycle checks");
