using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DDEN.Shared;
using HarmonyLib;
using Newtonsoft.Json;

namespace DDEN.NetPerfTracker.Enhanced
{
    internal static class DeepPatchEvidence
    {
        internal static string Capture()
        {
            var methods = new List<object>();
            var targets = new List<TargetSpec>(TimingTargets.All);
            // Evidence only: these non-void/argument-taking methods are never instrumented here.
            targets.Add(new TargetSpec("Together.SyncInfo", "CreateObject", "evidence", instance: true));
            targets.Add(new TargetSpec("PlayerCamera", "TryPerformInventoryAction", "evidence", assembly: "Assembly-CSharp", instance: true));
            targets.Add(new TargetSpec("Body", "DropItem", "evidence", assembly: "Assembly-CSharp", instance: true));
            targets.Add(new TargetSpec("Body", "DropWearable", "evidence", assembly: "Assembly-CSharp", instance: true));
            foreach (var target in targets)
            {
                try
                {
                    var assembly = Reflect.FindAssembly(target.Assembly);
                    var type = assembly?.GetType(target.Type, false);
                    var candidates = type?.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic
                        | (target.Instance ? BindingFlags.Instance : BindingFlags.Static)).Where(method => method.Name == target.Method).Take(4).ToArray();
                    if (candidates == null || candidates.Length == 0)
                    { methods.Add(new { Name = target.Name, Available = false }); continue; }
                    foreach (var method in candidates)
                    {
                        var patches = Harmony.GetPatchInfo(method);
                        methods.Add(new
                        {
                            Name = target.Name, Signature = method.ToString(), Available = true,
                            Assembly = assembly.GetName().Name, Version = assembly.GetName().Version.ToString(),
                            Mvid = assembly.ManifestModule.ModuleVersionId.ToString(),
                            Prefixes = Owners(patches?.Prefixes), Postfixes = Owners(patches?.Postfixes),
                            Transpilers = Owners(patches?.Transpilers), Finalizers = Owners(patches?.Finalizers),
                            Truncated = patches != null && (patches.Prefixes.Count > 16 || patches.Postfixes.Count > 16
                                || patches.Transpilers.Count > 16 || patches.Finalizers.Count > 16)
                        });
                    }
                }
                catch { methods.Add(new { Name = target.Name, Available = false }); }
            }
            return JsonConvert.SerializeObject(new { Schema = 1, CapturedUtc = DateTime.UtcNow.ToString("O"),
                Scope = "current patches at export; not proof of causation or historical ownership", Targets = methods });
        }
        private static object[] Owners(IEnumerable<Patch> patches) => patches?.Take(16)
            .Select(patch => (object)new { Owner = patch.owner, Priority = patch.priority }).ToArray() ?? Array.Empty<object>();
    }
}
