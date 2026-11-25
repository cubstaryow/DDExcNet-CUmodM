using System.Collections.Generic;
using DDEN.Shared;

namespace DDExcNet.CUmodM
{
    /// <summary>Bundled knowledge only; never inferred from the installed module or fetched from the network.</summary>
    internal static class ModuleVersionCatalog
    {
        internal const string ManagerRelease = "2.1.0";
        internal const string SharedRelease = "2.0.1";
        internal const int SupportedApi = 1;
        private static readonly Dictionary<string, string> Recorded = new Dictionary<string, string>(System.StringComparer.Ordinal)
        {
            ["mpfix"] = "1.8.0", ["perf"] = "2.3.1", ["perfdeep"] = "0.2.1-alpha", ["saveeditor"] = "2.0.1-alpha",
            ["unitlistmgr"] = "0.1.2-alpha", ["loongnova"] = "1.0.1"
        };
        internal static readonly IReadOnlyList<string> KnownIds = new[] { "mpfix", "perf", "perfdeep", "saveeditor", "unitlistmgr", "loongnova" };
        internal static string Latest(string id) => id != null && Recorded.TryGetValue(id, out var version) ? version : null;
        private static readonly Dictionary<IDdenModule, DdenVersionResult> Results = new Dictionary<IDdenModule, DdenVersionResult>();
        static ModuleVersionCatalog() { DdenHub.Changed += Invalidate; }
        internal static void Invalidate() => Results.Clear();
        internal static DdenVersionResult Check(IDdenModule module)
        {
            if (Results.TryGetValue(module, out var cached)) return cached;
            DdenVersionResult result;
            var declaration = module as IDdenModuleVersion;
            // No metadata/version getters are polled for legacy modules, even while the panel is closed.
            if (declaration == null) result = DdenVersionPolicy.Evaluate(null, null, ManagerRelease, null, DdenApi.CurrentVersion);
            else
            {
                try
                {
                    result = DdenVersionPolicy.Evaluate(null, null, ManagerRelease, DdenApi.SharedVersion, declaration, DdenApi.CurrentVersion);
                    if (result.Checked) result = DdenVersionPolicy.Evaluate(module.Version, Latest(module.Id), ManagerRelease, DdenApi.SharedVersion, declaration, DdenApi.CurrentVersion);
                }
                catch { result = new DdenVersionResult(DdenReleaseStatus.Unknown) { InvalidDeclaration = true }; }
            }
            Results[module] = result; return result;
        }
    }
}
