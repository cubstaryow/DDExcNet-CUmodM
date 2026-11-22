using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using HeatingCageFix;
using DDEN.Shared;
using Mono.Cecil;
using Newtonsoft.Json.Linq;
using CUCoreLib.Networking;

public static class ExternalFixture
{
    public static ConfigFile DdenConfig { get; set; }
    public static string DdenTitle => "外部配置测试";
    public static string Status = "已启用";
    public static string DdenSummary => Status;
    public static string DdenUnavailable(ConfigEntryBase entry) => entry.Definition.Key == "Blocked" ? "缺少前置" : null;
    public static void DdenDraw(UnityEngine.Rect area) { }
}
internal static class Program
{
    private static int _checks;
    private static void Check(bool ok, string message) { _checks++; if (!ok) throw new Exception(message); }
    private static void Main()
    {
        string temp = Path.Combine(Path.GetTempPath(), "HeatingCageFix-tests-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try { Runtime(); Wire(); Networking(); External(temp); Assemblies(); Console.WriteLine("PASS " + _checks + " checks: real ET-Fix runtime/save/network code with simulated Unity/transport, real DDEN L1 discovery, compiled dependency and installed API checks."); }
        finally
        {
            string target = Path.GetFullPath(temp);
            if (!target.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(target).StartsWith("HeatingCageFix-tests-")) throw new Exception("unsafe cleanup");
            Directory.Delete(target, true);
        }
    }
    private static Item Cage()
    {
        var go = new UnityEngine.GameObject(); var item = go.AddComponent<Item>(); item.id = CageRules.ItemId;
        go.AddComponent<FakeCageFire>(); CageRuntime.Track(item); return item;
    }
    private static void Invoke(string method, params object[] args) => typeof(CagePatches).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
    private static void Runtime()
    {
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        typeof(CageRuntime).GetField("_fireType", flags).SetValue(null, typeof(FakeCageFire));
        typeof(CageRuntime).GetField("_active", flags).SetValue(null, typeof(FakeCageFire).GetField("active", BindingFlags.Instance | BindingFlags.NonPublic));
        typeof(CageRuntime).GetField("_setActive", flags).SetValue(null, typeof(FakeCageFire).GetMethod("SetActive", BindingFlags.Instance | BindingFlags.NonPublic));
        var first = Cage(); var second = Cage(); var a = first.GetComponent<CageState>(); var b = second.GetComponent<CageState>();
        var clone = new UnityEngine.GameObject(); var cloneItem = clone.AddComponent<Item>(); cloneItem.id = CageRules.ItemId;
        clone.AddComponent<FakeCageFire>(); var blank = clone.AddComponent<CageState>();
        Check(CageRuntime.Track(cloneItem) == blank && blank.Item == cloneItem && CageRuntime.States.Contains(blank), "prefab-cloned controller is initialized and registered again");
        Check(first.decayMultiplier == 1 && a.On, "new open cage keeps native decay rate");
        CageRuntime.Set(a, false);
        Check(first.decayMultiplier == 0 && !CageRuntime.IsOn(first), "closing cage stops native and MP formulas through the instance multiplier");
        first.decayMultiplier = 1;
        typeof(CageRuntime).GetMethod("AfterItemStart", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { first });
        Check(first.decayMultiplier == 0, "item start settles the state after native Awake ordering and finalized item IDs");
        Check(second.decayMultiplier == 1 && CageRuntime.IsOn(second), "closing one cage does not change another cage");
        first.condition = .6f; CageRuntime.Tick();
        Check(first.condition == .6f, "repair never rolls back legitimate durability damage");
        CageRuntime.Set(a, true); CageRuntime.Set(a, false); CageRuntime.Set(a, true);
        Check(a.On && first.decayMultiplier == 1, "rapid legitimate toggles are not discarded by a time debounce");
        CageRuntime.Set(a, false); Plugin.Decay.Value = false; CageRuntime.ApplySettings();
        Check(first.decayMultiplier == 1, "disabling decay repair restores original multiplier");
        Plugin.Decay.Value = true; CageRuntime.ApplySettings();
        Check(first.decayMultiplier == 0, "reenabling immediately uses current off state");
        Plugin.Master.Value = false; CageRuntime.ApplySettings();
        Check(first.decayMultiplier == 1, "master off releases owned multiplier change");
        Plugin.Master.Value = true; CageRuntime.ApplySettings();
        var request = new RecipeItem { quality = new CraftingQuality { id = "heatsource" } };
        var original = new List<Item>(); var exclusions = original; var items = new List<Item> { first, second };
        CagePatches.FilterHeatSources(request, items, ref exclusions);
        Check(exclusions != original && original.Count == 0 && items.Count == 2, "filter copies exclusions without changing a shared item pool");
        Check(exclusions.Contains(first) && !exclusions.Contains(second), "closed cage excluded while another open heat source remains available");
        bool result = true; CagePatches.FilterHeatType(request, first, ref result); Check(!result, "UI ingredient-purpose check agrees with matching");
        request.specific = true; exclusions = original; CagePatches.FilterHeatSources(request, items, ref exclusions);
        Check(exclusions == original, "specific cage repair/material recipes unaffected");
        request.specific = false; request.isLiquid = true; CagePatches.FilterHeatSources(request, items, ref exclusions);
        Check(exclusions == original, "liquid quality requests unaffected");
        request.isLiquid = false; request.quality.id = "firestarter"; CagePatches.FilterHeatSources(request, items, ref exclusions);
        Check(exclusions == original, "unrelated crafting quality unaffected");
        request.quality.id = "heatsource"; Plugin.Heat.Value = false; CagePatches.FilterHeatSources(request, items, ref exclusions);
        Check(exclusions == original, "heat repair switch is independent"); Plugin.Heat.Value = true;
        JToken native = JObject.Parse("{'customData':{'unknown':'保留'},'lightEnabled':true}");
        object[] capture = { first, native }; Invoke("CaptureState", capture); var saved = (JToken)capture[1];
        Check(saved["customData"].Value<string>("unknown") == "保留" && saved.Value<bool>("lightEnabled"), "normal CCL save payload keeps unknown fields");
        Check(CageRules.Load(saved) == false, "closed state stored alongside CCL data");
        CageRuntime.Set(a, true); var context = new FakeSaveContext(); Invoke("RestoreState", first, saved, context);
        Check(!a.On && first.decayMultiplier == 0 && context.Deferred.Count == 1, "load restores state and schedules final ordering after CCL");
        context.Finish(); Check(!a.On && first.condition == .6f, "deferred restore never changes condition");
        CageRuntime.Set(a, true); context = new FakeSaveContext(); Invoke("RestoreState", first, saved, context);
        CageRuntime.Set(a, true); CageRuntime.Tick(); context.Finish();
        Check(a.On, "old deferred load cannot undo a later user toggle");
        Plugin.Remember.Value = false; JToken untouched = new JObject(); capture = new object[] { first, untouched }; Invoke("CaptureState", capture);
        Check(ReferenceEquals(capture[1], untouched) && !untouched.HasValues, "disabled persistence leaves native payload untouched");
        Check(CageRules.Load(new JValue("bad")) == null && CageRules.Load(JObject.Parse("{'cagefix':{'version':'bad','on':true}}")) == null, "malformed save state ignored safely");
        Check(CageRules.Load(new JObject()) == null, "old saves keep native default behavior");
        var malformedPayload = new JArray(1, 2); Check(ReferenceEquals(CageRules.Save(malformedPayload, false), malformedPayload), "non-object provider payload from another mod is preserved");
        Plugin.Remember.Value = true; CageRuntime.Set(a, false); first.decayMultiplier = .25f; CageRuntime.Clear();
        Check(first.decayMultiplier == .25f && first.condition == .6f, "cleanup respects another mod's later multiplier and durability changes");
    }
    private static void Wire()
    {
        string nonce = System.Guid.NewGuid().ToString("N"); var key = new CageKey(12, 3); var expected = new HashSet<CageKey> { key };
        var query = CageWire.Request(nonce, expected); query["items"][0]["on"] = true;
        var reply = CageWire.Reply(query, true, _ => false);
        Check(CageWire.Response(reply, nonce, expected, out var states, out bool enabled) && enabled && !states[key], "client supplied state ignored; only host getter determines reply");
        Check(!CageWire.Response(reply, "old-session", expected, out _, out _), "stale nonce rejected");
        Check(!CageWire.Response(reply, nonce, new HashSet<CageKey> { new CageKey(12, 4) }, out _, out _), "entity version reuse rejected");
        Check(!CageWire.Response(reply, nonce, new HashSet<CageKey> { new CageKey(13, 3) }, out _, out _), "unrequested entity rejected");
        var bad = (JObject)reply.DeepClone(); bad["items"][0]["on"] = "true";
        Check(!CageWire.Response(bad, nonce, expected, out _, out _), "string boolean not trusted");
        bad = (JObject)reply.DeepClone(); ((JArray)bad["items"]).Add(bad["items"][0].DeepClone());
        Check(!CageWire.Response(bad, nonce, expected, out _, out _), "duplicate reply identities rejected");
        var disabled = CageWire.Reply(query, false, _ => throw new Exception("disabled host must not read state"));
        Check(CageWire.Response(disabled, nonce, expected, out states, out enabled) && !enabled && states.Count == 0, "disabled host only reports disabled marker");
        var duplicate = CageWire.Request(nonce, new[] { key, key });
        Check(!CageWire.Reply(duplicate, true, _ => true).HasValues, "duplicate query rejected");
        var tooMany = CageWire.Request(nonce, Enumerable.Range(1, CageWire.Batch + 1).Select(i => new CageKey((ushort)i, 1)));
        Check(!CageWire.Reply(tooMany, true, _ => true).HasValues, "query batch bound enforced");
        foreach (var value in new JToken[] { -1, 0, 65536, "12", 12.5, JToken.Parse("999999999999999999999999999999999999999") })
        {
            var wrong = CageWire.Request(nonce, expected); wrong["items"][0]["id"] = value;
            Check(!CageWire.Reply(wrong, true, _ => true).HasValues, "malformed numeric identity rejected without overflow");
        }
    }
    private static void External(string temp)
    {
        ExternalFixture.DdenConfig = new ConfigFile(Path.Combine(temp, "external.cfg"), true);
        var blocked = ExternalFixture.DdenConfig.Bind("Test", "Blocked", false);
        var allowed = ExternalFixture.DdenConfig.Bind("Test", "Allowed", true);
        var method = typeof(DdenExternal).GetMethod("TryBuild", BindingFlags.Static | BindingFlags.NonPublic);
        var block = (DdenExternal.Block)method.Invoke(null, new object[] { Assembly.GetExecutingAssembly(), "Fixture" });
        Check(block != null && block.Draw != null && block.Title == ExternalFixture.DdenTitle && block.Entries.Count() == 2, "actual L1 scanner discovers config even with a custom page");
        Check(!block.CanEditConfig(blocked, true) && block.ConfigUnavailableReason(blocked) == "缺少前置", "optional zero-DDEN-reference availability callback disables missing-dependency control");
        Check(block.CanEditConfig(allowed, true) && !block.CanEditConfig(allowed, false), "other external controls remain editable with normal gates");
        allowed.BoxedValue = false; Check(!allowed.Value, "external entry remains the originating config object");
        Check(block.CurrentSummary == "已启用", "external status read from original bridge");
        ExternalFixture.Status = "已关闭";
        typeof(DdenExternal.Block).GetField("_summaryAt", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(block, unchecked(Environment.TickCount - 2000));
        Check(block.CurrentSummary == "已关闭", "overview and sidebar status refresh after external setting changes");
    }
    private static CageState ClientCage(ushort id = 12, byte version = 3)
    {
        var item = Cage(); var tracker = item.gameObject.AddComponent<Together.SyncInfoGameObjectTracker>();
        tracker.syncInfo = new Together.SyncInfo { Id = id, Version = version, go = item.gameObject };
        return item.GetComponent<CageState>();
    }
    private static void ResetClient()
    {
        CageRuntime.Clear(); MultiplayerApi.IsServer = false; MultiplayerApi.IsRunning = true; Plugin.Master.Value = Plugin.Network.Value = true;
        Together.Net.ClientEntityManager = new object(); WorldGeneration.world = new WorldGeneration();
        UnityEngine.Time.unscaledTime = 0; CageNetwork.Reset(); MultiplayerBridge.Requests.Clear(); MultiplayerBridge.Broadcasts = 0;
    }
    private static JToken Answer(MultiplayerBridge.Pending pending, bool on)
        => CageWire.Reply(pending.Payload, true, _ => on);
    private static void Networking()
    {
        CageNetwork.Install(); ResetClient(); var state = ClientCage(); CageNetwork.Tick();
        Check(MultiplayerBridge.Requests.Count == 1, "new connected cage requests host state once");
        var pending = MultiplayerBridge.Requests[0]; pending.Callback(Answer(pending, false));
        Check(!state.On && state.Item.decayMultiplier == 0 && !state.NeedsSync, "verified host off state applies actual runtime and decay repair");
        CageNetwork.Tick(); Check(MultiplayerBridge.Requests.Count == 1, "stable state does not poll host");

        ResetClient(); state = ClientCage(); CageNetwork.Tick(); pending = MultiplayerBridge.Requests[0];
        CageRuntime.Set(state, false); CageRuntime.Set(state, true); pending.Callback(Answer(pending, false));
        Check(state.On, "late reply cannot undo a later local use");
        UnityEngine.Time.unscaledTime = .6f; CageNetwork.Tick();
        Check(MultiplayerBridge.Requests.Count == 2, "later use triggers a fresh bounded host confirmation");
        pending = MultiplayerBridge.Requests[1]; pending.Callback(Answer(pending, false)); Check(!state.On, "latest host response determines final switch state");

        ResetClient(); state = ClientCage(); CageNetwork.Tick(); pending = MultiplayerBridge.Requests[0];
        state.Item.GetComponent<Together.SyncInfoGameObjectTracker>().syncInfo.Version = 4;
        pending.Callback(Answer(pending, false)); Check(state.On, "reply for a previous entity generation cannot apply after rebind");
        UnityEngine.Time.unscaledTime = .6f; CageNetwork.Tick();
        Check(MultiplayerBridge.Requests.Count == 2 && MultiplayerBridge.Requests[1].Payload["items"][0].Value<int>("version") == 4, "rebound cage queries current generation");

        ResetClient(); state = ClientCage(); CageNetwork.Tick(); pending = MultiplayerBridge.Requests[0];
        Together.Net.ClientEntityManager = new object(); pending.Callback(Answer(pending, false));
        Check(state.On, "old peer-manager reply rejected before next regular update");
        CageNetwork.Tick(); Check(MultiplayerBridge.Requests.Count == 2, "new connection obtains its own state query");

        ResetClient(); state = ClientCage(); CageNetwork.Tick(); pending = MultiplayerBridge.Requests[0];
        WorldGeneration.world.biomeDepth++; pending.Callback(Answer(pending, false));
        Check(state.On, "old layer response rejected immediately");

        ResetClient(); state = ClientCage(); CageNetwork.Tick(); pending = MultiplayerBridge.Requests[0];
        WorldGeneration.world.worldExists = false; pending.Callback(Answer(pending, false));
        Check(state.On, "closing world cannot receive a pending state write");

        ResetClient(); state = ClientCage(); CageNetwork.Tick(); pending = MultiplayerBridge.Requests[0];
        Plugin.Master.Value = false; pending.Callback(Answer(pending, false));
        Check(state.On, "master off prevents an in-flight network write"); Plugin.Master.Value = true;

        ResetClient(); state = ClientCage(); CageNetwork.Tick(); pending = MultiplayerBridge.Requests[0]; pending.Callback(Answer(pending, true));
        MultiplayerBridge.ClientHandler(JObject.Parse("{'on':false,'id':12,'version':3}"));
        Check(state.On && state.NeedsSync, "forwarded/forged notice only schedules verification and never applies supplied state");
        UnityEngine.Time.unscaledTime = .6f; CageNetwork.Tick(); pending = MultiplayerBridge.Requests.Last(); pending.Callback(Answer(pending, true));
        Check(state.On, "host verification defeats a forged off notice");

        ResetClient(); state = ClientCage(); CageNetwork.Tick();
        foreach (float time in new[] { 3.1f, 6.2f, 9.3f, 12.4f }) { UnityEngine.Time.unscaledTime = time; CageNetwork.Tick(); }
        Check(MultiplayerBridge.Requests.Count == 3, "unanswered snapshot retries stop at three");

        ResetClient(); for (ushort i = 1; i <= 130; i++) ClientCage(i);
        CageNetwork.Tick(); pending = MultiplayerBridge.Requests[0];
        Check(((JArray)pending.Payload["items"]).Count == 128, "large cage groups are packet-bounded"); pending.Callback(Answer(pending, true));
        UnityEngine.Time.unscaledTime = .6f; CageNetwork.Tick();
        Check(((JArray)MultiplayerBridge.Requests[1].Payload["items"]).Count == 2, "remaining cages continue in next batch");

        ResetClient(); state = ClientCage(); MultiplayerApi.IsServer = true;
        CageNetwork.Reset(); CageRuntime.Set(state, false);
        var request = CageWire.Request(System.Guid.NewGuid().ToString("N"), new[] { new CageKey(12, 3) }); request["items"][0]["on"] = true;
        var response = MultiplayerBridge.ServerHandler(request);
        Check(response["items"][0].Value<bool>("on") == false && !state.On, "actual server query ignores client state and reads host-owned component");
        CageNetwork.Tick(); UnityEngine.Time.unscaledTime = .3f; CageNetwork.Tick();
        Check(MultiplayerBridge.Broadcasts == 1, "host changes coalesce to a small notification");
        UnityEngine.Time.unscaledTime = 20; CageNetwork.Tick(); Check(MultiplayerBridge.Broadcasts == 1, "host does not broadcast unchanged state periodically");
        ResetClient(); CageRuntime.Clear();
    }
    private static void Assemblies()
    {
        var paths = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().ToDictionary(a => a.Key, a => a.Value);
        using var assembly = AssemblyDefinition.ReadAssembly(paths["HeatingCageFixAssembly"]);
        Check(assembly.Name.Name == "ET-Fix" && assembly.Name.Version == new Version(1, 7, 2, 0), "renamed assembly identity and version match v1.6.0");
        var metadata = assembly.MainModule.Types.Single(t => t.FullName == "HeatingCageFix.Plugin").CustomAttributes.Single(a => a.AttributeType.FullName == "BepInEx.BepInPlugin");
        Check((string)metadata.ConstructorArguments[0].Value == "com.dden.heatingcagefix" && (string)metadata.ConstructorArguments[1].Value == "ET-Fix" && (string)metadata.ConstructorArguments[2].Value == "1.7.2", "plugin rename preserves the existing GUID/config identity and version");
        Check(!assembly.MainModule.AssemblyReferences.Any(a => a.Name.StartsWith("DDEN", StringComparison.Ordinal) || a.Name == "EnhancedTools" || a.Name == "CasualtiesMP" || a.Name == "CUCoreLib"), "standalone ET-Fix has no hard DDEN, ET, MP or CCL reference");
        var bridge = assembly.MainModule.Types.Single(t => t.FullName == "HeatingCageFix.DdenBridge");
        Check(bridge.IsPublic && bridge.Methods.Any(m => m.Name == "get_DdenConfig" && m.IsPublic && m.IsStatic), "ET-Fix exports the documented public static bridge");
        Check(!bridge.Methods.Any(m => m.Name == "DdenDraw"), "ET-Fix delegates its entire settings page to the manager");
        using var et = AssemblyDefinition.ReadAssembly(Path.Combine(paths["GameDir"], "BepInEx", "plugins", "EnhancedTools", "EnhancedTools.dll"));
        var fire = et.MainModule.Types.Single(t => t.Name == "HeatingCageFire");
        Check(fire.Fields.Any(f => f.Name == "active" && f.FieldType.FullName == "System.Boolean") && fire.Methods.Any(m => m.Name == "SetActive" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "System.Boolean"), "installed ET switch fields and method match repair hooks");
        using var ccl = AssemblyDefinition.ReadAssembly(Path.Combine(paths["GameDir"], "BepInEx", "plugins", "CUCoreLib.dll"));
        var provider = ccl.MainModule.Types.Single(t => t.Name == "BuiltInItemRuntimeSaveProvider");
        Check(provider.Methods.Any(m => m.Name == "Capture" && m.ReturnType.FullName == "Newtonsoft.Json.Linq.JToken") && provider.Methods.Any(m => m.Name == "Restore" && m.Parameters.Any(p => p.Name == "context")), "installed CCL persistence hook signatures exist");
        var method = assembly.MainModule.Types.Single(t => t.Name == "CagePatches").Methods.Single(m => m.Name == "CaptureState");
        Check(method.Parameters.Any(p => p.Name == "__result" && p.ParameterType.IsByReference), "compiled capture hook writes only provider result");
    }
}
