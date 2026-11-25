using System;
using System.IO;
using System.Linq;
using BepInEx.Bootstrap;
using Newtonsoft.Json;

namespace DDEN.NetPerfTracker
{
    // Export-time inventory only. Never reads string settings (passwords, addresses, paths).
    internal static class PluginEvidence
    {
        internal static void Write(TextWriter writer)
        {
            try
            {
                writer.WriteLine("# pluginEvidenceScope=exportTime installed=" + Chainloader.PluginInfos.Count
                    + " limit=128 booleanSettings=localOnly maxPerSelectedPlugin=64");
                var plugins = Chainloader.PluginInfos.Values.OrderBy(value => value.Metadata.GUID, StringComparer.Ordinal).Take(128);
                foreach (var plugin in plugins)
                {
                    try
                    {
                        var assembly = plugin.Instance?.GetType().Assembly;
                        bool selected = plugin.Metadata.GUID.StartsWith("com.dden.", StringComparison.Ordinal)
                            || (assembly?.GetName().Name.IndexOf("CpuOptimization", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0;
                        var options = selected && plugin.Instance != null ? plugin.Instance.Config
                            .Where(entry => entry.Value.SettingType == typeof(bool)).Take(64)
                            .ToDictionary(entry => entry.Key.Section + "/" + entry.Key.Key, entry => (bool)entry.Value.BoxedValue) : null;
                        writer.WriteLine("PLUGIN_EVIDENCE payload=" + JsonConvert.SerializeObject(new
                        {
                            Guid = plugin.Metadata.GUID, Name = plugin.Metadata.Name, Version = plugin.Metadata.Version?.ToString(),
                            Assembly = assembly?.GetName().Name, Mvid = assembly?.ManifestModule.ModuleVersionId.ToString(),
                            LocalBooleanSettings = options
                        }));
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}
