using CUCoreLib.Helpers;
using CUCoreLib.Registries;
using DDEN.MPfix;
using UnityEngine;

int checks = 0;
void Check(bool ok, string why) { checks++; if (!ok) throw new Exception(why); }
GameObject Dead(string id)
{
    var dead = new GameObject { Destroyed = true };
    CustomInstantiate._templateCache[id] = dead;
    ItemRegistry.RegisteredItems[id] = new object();
    return dead;
}
GameObject Resolve(string id)
{
    ModItemTemplateRepair.Before(id, out bool state);
    GameObject result = null;
    Exception error = null;
    try { result = CustomInstantiate.GetOrCreateTemplate(id); }
    catch (Exception ex) { error = ex; }
    error = ModItemTemplateRepair.Finish(id, state, result, error);
    if (error != null) throw error;
    return result;
}

Check(ModItemTemplateRepair.Install(new HarmonyLib.Harmony()), "installed CCL contract resolved");
Dead("gravbagv2");
var rebuilt = Resolve("gravbagv2");
Check(rebuilt != null && CustomInstantiate.Builds == 1 && CustomInstantiate.Clears == 1, "dead Unity reference evicted and rebuilt once");
Check(ReferenceEquals(Resolve("gravbagv2"), rebuilt) && CustomInstantiate.Builds == 1, "live template reused without construction");
Dead("gravbagv2");
Check(Resolve("gravbagv2") == null && CustomInstantiate.Builds == 1, "repeated destruction is bounded by cooldown");
Time.unscaledTime = 11;
Check(Resolve("gravbagv2") != null && CustomInstantiate.Builds == 2, "recovery allowed after cooldown");
Dead("opslegpouch");
Check(Resolve(" OPSLEGPOUCH ") != null && CustomInstantiate.Builds == 3, "case and whitespace follow CCL IDs");
Dead("absent"); ItemRegistry.RegisteredItems.Remove("absent");
int clears = CustomInstantiate.Clears;
Check(Resolve("absent") == null && CustomInstantiate.Clears == clears, "missing registration never invented or evicted");
Dead("off"); MPfixPlugin.CfgFixModItemCraftSpawn.Value = false;
Check(Resolve("off") == null && CustomInstantiate.Clears == clears, "existing repair switch disables cache changes");
MPfixPlugin.CfgFixModItemCraftSpawn.Value = true; MPfixPlugin.Active = false;
Check(Resolve("off") == null && CustomInstantiate.Clears == clears, "master off leaves CCL untouched");
MPfixPlugin.Active = true; Mp.IsRunning = false;
Check(Resolve("off") == null && CustomInstantiate.Clears == clears, "singleplayer leaves CCL untouched");
Mp.IsRunning = true;
var intact = new GameObject { Item = null }; CustomInstantiate._templateCache["intact"] = intact;
ItemRegistry.RegisteredItems["intact"] = new object();
Check(ReferenceEquals(Resolve("intact"), intact) && CustomInstantiate.Clears == clears, "live objects never evicted or destroyed even if malformed");
Dead("broken"); var failure = new InvalidOperationException("template initialization failed"); CustomInstantiate.Failure = failure;
try { Resolve("broken"); throw new Exception("swallowed failure"); }
catch (InvalidOperationException ex) { Check(ReferenceEquals(ex, failure), "original constructor exception preserved"); }
int builds = CustomInstantiate.Builds;
CustomInstantiate._templateCache["broken"] = new GameObject { Destroyed = true };
Check(Resolve("broken") == null && CustomInstantiate.Builds == builds, "failed rebuild is not retried during cooldown");
CustomInstantiate.Failure = null;
ItemRegistry.RegisteredItems["fresh"] = new object();
clears = CustomInstantiate.Clears;
Check(Resolve("fresh") != null && CustomInstantiate.Clears == clears, "uncached items follow normal first construction");
Dead("null-result"); CustomInstantiate.ReturnNull = true;
builds = CustomInstantiate.Builds;
Check(Resolve("null-result") == null && CustomInstantiate.Builds == builds + 1, "null construction result remains a failure without a second attempt");
Check(MPfixPlugin.Log.Entries.Any(s => s.Contains("已重建")) && MPfixPlugin.Log.Entries.Any(s => s.Contains("初始化") || s.Contains("initialization")), "success and constructor failure diagnostics retained");
Console.WriteLine($"PASS {checks} template recovery checks; Unity/CCL storage simulated, no game execution");
