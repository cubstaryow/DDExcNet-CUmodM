using DDEN.Shared;
using UnityEngine;

namespace DDExcNet.CUmodM
{
    public partial class HubPlugin
    {
        private OverviewCardState? _managerCardState;
        private OverviewCardState GetManagerCardState()
        {
            if (_managerCardState.HasValue) return _managerCardState.Value;
            bool problem = DdenApi.CurrentVersion != ModuleVersionCatalog.SupportedApi
                || OverviewCardStyle.VersionMismatch(DdenVersionPolicy.CompareRelease(DdenApi.SharedVersion, ModuleVersionCatalog.SharedRelease))
                || OverviewCardStyle.VersionMismatch(DdenVersionPolicy.CompareRelease(Info.Metadata.Version.ToString(), ModuleVersionCatalog.ManagerRelease));
            _managerCardState = problem ? OverviewCardState.Problem : OverviewCardState.Running;
            return _managerCardState.Value;
        }
        private static Color CardLineColor(OverviewCardState state) => state == OverviewCardState.Problem ? DdenUI.Bad
            : state == OverviewCardState.Running ? DdenUI.Good : DdenUI.Accent;
        private static void DrawCardLine(Rect card, OverviewCardState state)
            => DdenUI.Fill(new Rect(card.x, card.y, 3, card.height), CardLineColor(state));

        private static bool CardHovered(Rect card) => Event.current != null && card.Contains(Event.current.mousePosition);

        private static bool ModuleRunning(IDdenModule module)
        {
            try { return (module as IDdenModuleActivity)?.IsRunning ?? false; }
            catch { return false; }
        }
        private static string CardActivityText(OverviewCardState state) => T(state == OverviewCardState.Running ? "cards.running"
            : state == OverviewCardState.Problem ? "cards.problem" : "cards.loaded");
        private void DescribeModuleCard(Rect card, IDdenModule module, OverviewCardState state, string summary, bool ready, DdenHealth health)
        {
            if (!CardHovered(card)) return;
            string detail = ModuleName(module) + " " + module.Version + "\n" + CardActivityText(state);
            if (!string.IsNullOrEmpty(summary)) detail += " · " + summary;
            if (!ready) detail += "\n" + T("cards.notReady");
            if (health != DdenHealth.Ok && !string.IsNullOrEmpty(HealthNote(module))) detail += "\n" + HealthNote(module);
            DdenUI.Hover(card, detail + "\n" + VersionText(module).Details);
        }
    }
}
