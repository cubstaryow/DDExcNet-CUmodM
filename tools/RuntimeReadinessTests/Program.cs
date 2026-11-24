using System.Reflection;
using DDEN.Shared;
using Newtonsoft.Json.Linq;

int checks = 0;
void Check(bool pass, string why) { checks++; if (!pass) throw new Exception(why); }
void Dormant(string label)
{
    int net = Together.Net.Reads, core = CUCoreLib.Networking.MultiplayerApi.Reads, bridge = CUCoreLib.Networking.MultiplayerBridge.Calls;
    Check(!Mp.TogetherReady && !Mp.IsRunning && !Mp.IsServer && !Mp.IsClient && !Mp.CoreAvailable && !Mp.BridgeReady && !Mp.CoreReady, label + ": flags fail closed");
    Check(!Mp.RegisterClientHandler("test", _ => {}) && !Mp.RegisterServerHandler("test", _ => null) && !Mp.RegisterSyncModule("test", () => new JObject(), _ => {}), label + ": no registrations");
    Check(!Mp.RequestServer("test", new object(), _ => {}) && !Mp.Broadcast("test", new object()) && !Mp.SendToServer("test", new object()), label + ": no sends");
    Check(!Mp.UnchippedIsIndividual && Mp.PeerName() == null && !Mp.RunSettingUnchipped(), label + ": no native MP rule reads");
    Mp.UnchippedIsIndividual = true;
    Check(Together.Net.Reads == net && CUCoreLib.Networking.MultiplayerApi.Reads == core && CUCoreLib.Networking.MultiplayerBridge.Calls == bridge, label + ": dormant APIs were never called");
}
Compat.HasMp = false; Compat.HasCore = true; Together.Plugin.Initialized = true;
Dormant("assembly present but plugin absent");
Compat.HasMp = true; Together.Plugin.Initialized = false;
Dormant("installed but no injection");
Together.Plugin.Initialized = true; Together.Plugin.ForcedOff = true;
Dormant("explicitly disabled MP");
Together.Plugin.ForcedOff = false;
Together.Plugin.DisableReadFails = true;
Dormant("force-disable status unavailable");
Together.Plugin.DisableReadFails = false;
Check(Mp.TogetherReady && !Mp.IsRunning, "initialized MP menu is distinct from a running session");
CUCoreLib.Networking.MultiplayerApi.Receivers = false;
Check(Mp.CoreAvailable && !Mp.CoreReady, "CCL bridge existence does not imply receivers installed");
Check(!Mp.RegisterClientHandler("test", _ => {}), "bridge handler registration waits for receivers too");
Together.Net.Running = true; Together.Net.Server = true;
Check(Mp.IsRunning && Mp.IsServer && !Mp.IsClient, "native roles work after initialization");
Check(!Mp.RequestServer("test", new object(), _ => {}), "sends wait for CCL receiver readiness");
CUCoreLib.Networking.MultiplayerApi.Receivers = true;
Check(Mp.CoreReady && Mp.RequestServer("test", new object(), _ => {}), "bridge recovers after late initialization");
Compat.HasCore = false;
Check(Mp.TogetherReady && Mp.IsRunning && !Mp.CoreReady, "missing CCL leaves independent MP features usable");
Compat.HasCore = true; Together.Plugin.Initialized = false;
Dormant("readiness lost after being active");
Together.Plugin.Initialized = true;
Check(Mp.TogetherReady && Mp.CoreReady, "readiness is live, not cached false or permanently disabled");

string root = args.Length == 0 ? Path.GetFullPath(".") : args[0];
using (var fix = Mono.Cecil.AssemblyDefinition.ReadAssembly(Path.Combine(root, "src/DDEN.MPfix/bin/Release/DDEN.MPfix.dll")))
{
    var plugin = fix.MainModule.Types.Single(t => t.Name == "MPfixPlugin");
    bool Calls(Mono.Cecil.MethodDefinition method, string name) => method.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference call && call.FullName.Contains(name));
    Check(Calls(plugin.Methods.Single(m => m.Name == "get_Active"), "get_TogetherReady"), "compiled MPfix master execution requires native readiness");
    Check(Calls(plugin.Methods.Single(m => m.Name == "get_Ready"), "get_TogetherReady"), "manager availability uses native readiness independently of CCL");
    var install = plugin.Methods.Single(m => m.Name == "InstallRuntimeSupport");
    Check(Calls(install, "get_TogetherReady") && Calls(install, "get_CoreReady"), "deferred patch/channel installation has both readiness gates");
    foreach (string name in new[] { "ForceApply", "RequestNow", "ToggleHostState", "ApplyLocal", "OnHostState" })
        Check(Calls(plugin.Methods.Single(m => m.Name == name), "get_CoreReady"), "manual/state chip entry gate: " + name);
}
using (var item = Mono.Cecil.AssemblyDefinition.ReadAssembly(Path.Combine(root, "src/DDEN.UnitListMgr/bin/Release/DDEN.UnitListMgr.dll")))
{
    var service = item.MainModule.Types.Single(t => t.Name == "SpawnService");
    var canGive = service.Methods.Single(m => m.Name == "CanGive").Body.Instructions;
    int firstReturn = canGive.ToList().FindIndex(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ret);
    int rule = canGive.ToList().FindIndex(i => i.Operand is Mono.Cecil.MethodReference call && call.Name == "Rule");
    Check(firstReturn >= 0 && rule > firstReturn, "single-player returns before reading native MP permissions");
    Check(service.Methods.Single(m => m.Name == "Tick").Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference call && call.Name == "get_CoreReady"), "item service never registers dormant bridge handlers");
    Check(service.Methods.Single(m => m.Name == "Receive").Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference call && call.Name == "get_CoreReady"), "queued callbacks cannot execute after bridge readiness is lost");
}
Console.WriteLine($"PASS {checks} runtime readiness/API suppression/compiled dependency checks; no game started");

public class WorldGeneration
{
    public static WorldGeneration world;
    public bool worldExists, unchippedMode;
    public static Dictionary<string, object> runSettings = new();
}
namespace DDEN.Shared
{
    public static class Compat { public static bool HasMp, HasCore; public static bool PluginLoaded(string id) => id == "CasualtiesMP" ? HasMp : id == "net.cucorelib" && HasCore; }
    public static class Log { public static void Debug(string text) { } public static void Error(string text) { } }
    public static class Reflect
    {
        public static Type Type(string name) => typeof(Reflect).Assembly.GetType(name);
        public static object GetStatic(string type, string name) => GetStatic(Type(type), name);
        public static object GetStatic(Type type, string name)
        {
            if (!Mp.TogetherReady && type?.Namespace?.StartsWith("Together") == true) throw new Exception("Dormant MP state accessed");
            return type?.GetField(name)?.GetValue(null) ?? type?.GetProperty(name)?.GetValue(null);
        }
        public static object GetInstance(object target, string name) => target?.GetType().GetField(name)?.GetValue(target);
        public static void MutateStructMember(string type, string name, Action<object> mutate) { if (!Mp.TogetherReady) throw new Exception("Dormant rule mutation"); }
        public static bool TrySetInstance(object target, string name, object value) => true;
    }
}
namespace Together
{
    public static class Plugin { public static bool Initialized, ForcedOff, DisableReadFails; public static bool SUCCESFULLY_INITIALIZED => Initialized; public static bool FORCE_DISABLE_MP_MOD => DisableReadFails ? throw new InvalidOperationException() : ForcedOff; }
    public static class Net
    {
        public static bool Running, Server; public static int Reads;
        public static bool IsRunning { get { Reads++; return Running; } }
        public static bool IsServer { get { Reads++; return Server; } }
        public static bool IsClient { get { Reads++; return Running && !Server; } }
    }
}
namespace CUCoreLib.Networking
{
    public static class MultiplayerApi
    {
        public static bool Receivers; public static int Reads;
        public static bool IsAvailable { get { Reads++; return true; } }
        public static bool BridgeReceiversInstalled { get { Reads++; return Receivers; } }
    }
    public static class MultiplayerBridge
    {
        public static int Calls;
        public static bool RegisterClientHandler(string c, Action<JToken> h) { Calls++; return true; }
        public static bool RegisterServerHandler(string c, Func<JToken,JToken> h) { Calls++; return true; }
        public static bool RegisterModule(string c, Func<JObject> h, Action<JObject> a) { Calls++; return true; }
        public static bool RequestServer(string c, object p, Action<JToken> h, bool reliable) { Calls++; return true; }
        public static bool Broadcast(string c, object p, bool self, bool reliable) { Calls++; return true; }
        public static bool SendToServer(string c, object p, bool reliable) { Calls++; return true; }
    }
    public static class MultiplayerSyncRegistry { public static bool RegisterModule(string c, Func<JObject> h, Action<JObject> a) { MultiplayerBridge.Calls++; return true; } }
}
