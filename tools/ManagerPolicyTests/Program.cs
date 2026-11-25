using BepInEx.Configuration;
using BepInEx.Logging;
using DDEN.Shared;
using UnityEngine;
using Mono.Cecil;
using Mono.Cecil.Cil;
using DLog = DDEN.Shared.Log;

int checks = 0;
void Check(bool value, string why) { checks++; if (!value) throw new Exception(why); }
var busy = new HashSet<KeyCode>();
KeyCode Choose(KeyCode key = KeyCode.F11, bool modified = false, bool auto = true) => ManagerHotkeyPolicy.Choose(key, modified, auto, busy.Contains);
Check(Choose() == KeyCode.F11, "default F11 retained when free");
foreach (var key in new[] { KeyCode.F11, KeyCode.F10, KeyCode.F9, KeyCode.F8, KeyCode.F7, KeyCode.F6, KeyCode.F5, KeyCode.F4, KeyCode.F3, KeyCode.F2 })
{ busy.Add(key); Check((int)Choose() == (int)key - 1, "conflict walks downwards: " + key); }
busy.Add(KeyCode.F1); Check(Choose() == KeyCode.None, "all occupied disables key instead of stealing F12");
busy.Clear(); Check(Choose(KeyCode.F12, auto:false) == KeyCode.F11, "F12 stays reserved even when automatic conflict handling is off");
Check(Choose(KeyCode.None) == KeyCode.None, "explicit disabled binding is retained");
Check(Choose(KeyCode.Insert) == KeyCode.Insert, "custom free key is retained");
busy.Add(KeyCode.F11); Check(Choose(modified:true) == KeyCode.F11 && Choose(auto:false) == KeyCode.F11, "modified binding and manual conflict policy retained");
using var manager = new ManualLogSource("Manager");
using var tracker = new ManualLogSource("Perf");
using var module = new ManualLogSource("Module");
var managerLines = new List<LogEventArgs>(); var trackerLines = new List<LogEventArgs>(); var moduleLines = new List<LogEventArgs>();
manager.LogEvent += (_, e) => managerLines.Add(e); tracker.LogEvent += (_, e) => trackerLines.Add(e); module.LogEvent += (_, e) => moduleLines.Add(e);
var channel = DLog.For(module, "MPfix");
DLog.RegisterSink("manager", manager, 10); DLog.Verbose = false;
channel.Warn("warning"); Check(managerLines.Count == 1 && moduleLines.Count == 0, "all module logs flow to manager without tracker");
DLog.RegisterSink("perf", tracker, 20); DLog.Attach(manager,"Shared",false);
channel.Error("error"); DLog.Info("shared");
Check(trackerLines.Count == 2 && trackerLines[0].Data.ToString().Contains("[MPfix]") && trackerLines[0].Level == LogLevel.Error, "tracker receives original module tag and error level");
channel.Debug("hidden"); Check(trackerLines.Count == 2, "global verbosity starts off");
DLog.Verbose = true; var later = DLog.For(module,"Later",false); later.Debug("shown");
Check(trackerLines.Count == 3 && channel.Verbose && later.Verbose, "manager toggle applies immediately to existing and later channels");
DLog.RegisterSink("manager",manager,10); channel.Info("still perf"); Check(trackerLines.Count == 4, "manager load order cannot steal tracker sink");
DLog.UnregisterSink("perf"); channel.Info("fallback"); Check(managerLines.Count == 2, "tracker unload restores manager sink");
DLog.UnregisterSink("manager");
string temp = Path.Combine(Path.GetTempPath(),"dden-policy-"+Guid.NewGuid().ToString("N")+".cfg");
try
{
    File.WriteAllText(temp,"[UI]\nStandaloneKey = F9\nScale = 1.75\n[Debug]\nVerbose = true\n");
    var config = new ConfigFile(temp,false); var keep = config.Bind("UI","Scale",1f);
    ConfigMigration.Retire(config,"UI/StandaloneKey","Debug/Verbose");
    var saved = File.ReadAllText(temp);
    Check(!saved.Contains("StandaloneKey") && !saved.Contains("Verbose"), "orphaned retired definitions are removed from saved config");
    Check(saved.Contains("Scale") && keep.Value == 1.75f, "unrelated existing user setting is retained");
    var bound = config.Bind("UI","OldBound",true); ConfigMigration.Retire(config,"UI/OldBound");
    Check(!config.ContainsKey(new ConfigDefinition("UI","OldBound")), "bound retired definitions are removed too");
}
finally { if(File.Exists(temp)) File.Delete(temp); }

string root = Path.GetFullPath(".");
var modules = new[] { ("DDExcNet.CUmodM","DDExcNet-CUmodM"), ("DDEN.MPfix","DDEN.MPfix"), ("DDEN.NetPerfTracker","DDEN.NetPerfTracker"), ("DDEN.NetPerfTracker.Enhanced","DDEN.NetPerfTracker.Enhanced"), ("DDEN.LoongNova","DDEN.LoongNova"), ("DDEN.SaveEditor","DDEN.SaveEditor"), ("DDEN.UnitListMgr","DDEN.UnitListMgr") };
foreach(var (folder,binary) in modules)
{
    using var assembly = AssemblyDefinition.ReadAssembly(Path.Combine(root,"src",folder,"bin/Release",binary+".dll"));
    var plugin = assembly.MainModule.Types.Single(t=>t.CustomAttributes.Any(a=>a.AttributeType.Name=="BepInPlugin"));
    var pluginVersion = (string)plugin.CustomAttributes.Single(a=>a.AttributeType.Name=="BepInPlugin").ConstructorArguments[2].Value;
    Check(Version.Parse(pluginVersion) == new Version(assembly.Name.Version.Major,assembly.Name.Version.Minor,assembly.Name.Version.Build), "loader and assembly versions match: "+folder);
    var awake = plugin.Methods.Single(m=>m.Name=="Awake");
    var configBinds = awake.Body.Instructions.Where(i=>i.Operand is MethodReference m && m.Name=="Bind").ToArray();
    if(folder!="DDExcNet.CUmodM")
        Check(!awake.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="DrawStandalone"), "no standalone UI load: "+folder);
    Check(!plugin.Methods.Any(m=>m.HasBody && m.Body.Instructions.Any(i=>i.Operand is MethodReference call && call.Name=="DrawStandalone")), "all module windows route through manager: "+folder);
    if(folder=="DDEN.MPfix") Check(!assembly.MainModule.Types.Any(t=>t.Name.StartsWith("InventoryRetry")), "retired client sync patch removed from assembly");
    if(folder is "DDEN.NetPerfTracker" or "DDEN.NetPerfTracker.Enhanced")
    {
        string key = folder=="DDEN.NetPerfTracker" ? "启用采样（Enabled）" : "启用深层计时";
        var all = awake.Body.Instructions; int at=all.ToList().FindIndex(i=>i.Operand is string s && s==key);
        Check(at>=0 && all[at+1].OpCode==OpCodes.Ldc_I4_0, "performance master defaults off: "+folder);
        if(folder=="DDEN.NetPerfTracker") { int cpu=all.ToList().FindIndex(i=>i.Operand is string s && s=="CPU与GPU阶段计时"); Check(cpu>=0 && all[cpu+1].OpCode==OpCodes.Ldc_I4_1,"CPU stage tracking defaults on"); }
    }
}
Console.WriteLine("PASS "+checks+" manager policy/config/logging checks; no game started");
