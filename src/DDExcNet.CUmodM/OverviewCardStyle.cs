using DDEN.Shared;

namespace DDExcNet.CUmodM
{
    internal enum OverviewCardState { Loaded, Running, Problem }

    internal static class OverviewCardStyle
    {
        internal const float ModuleHeight = 56, ModuleStep = 62;
        internal const float UtilityHeight = 50, UtilityStep = 56;
        internal static bool VersionMismatch(DdenVersionResult version) => version != null && version.Checked &&
            (version.HasCompatibilityWarning || VersionMismatch(version.ReleaseStatus));
        internal static bool VersionMismatch(DdenReleaseStatus status) => status == DdenReleaseStatus.Older
            || status == DdenReleaseStatus.Newer || status == DdenReleaseStatus.Unknown;
        internal static OverviewCardState State(DdenVersionResult version, bool ready, bool enabled, bool running, DdenHealth health)
        {
            if (VersionMismatch(version) || !ready || health != DdenHealth.Ok) return OverviewCardState.Problem;
            return enabled && running ? OverviewCardState.Running : OverviewCardState.Loaded;
        }
    }
}
