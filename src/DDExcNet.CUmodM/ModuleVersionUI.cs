using System;
using System.Collections.Generic;
using DDEN.Shared;
using UnityEngine;

namespace DDExcNet.CUmodM
{
    public partial class HubPlugin
    {
        private sealed class VersionPresentation
        {
            internal DdenVersionResult Result;
            internal int Language;
            internal string Summary, Details, Requirements, Warning, Caption;
            internal float Width = -1, Height;
            internal readonly GUIContent Content = new GUIContent();
        }
        private static string ReleaseText(DdenReleaseStatus status) => ManagerText.Get("versions.release." + status);
        private readonly Dictionary<IDdenModule, VersionPresentation> _versionText = new Dictionary<IDdenModule, VersionPresentation>();
        private VersionPresentation VersionText(IDdenModule module)
        {
            var result = ModuleVersionCatalog.Check(module);
            if (_versionText.TryGetValue(module, out var text) && ReferenceEquals(text.Result, result) && text.Language == DdenLocalization.Revision) return text;
            text = new VersionPresentation { Result = result, Language = DdenLocalization.Revision };
            string status = ReleaseText(result.ReleaseStatus);
            if (!result.Checked) text.Summary = text.Details = text.Requirements = status;
            else
            {
                string minimum = string.IsNullOrEmpty(result.MinimumManager) ? T("versions.unspecified") : result.MinimumManager;
                string tested = string.IsNullOrEmpty(result.TestedManager) ? T("versions.unspecified") : result.TestedManager;
                string minimumShared = string.IsNullOrEmpty(result.MinimumShared) ? T("versions.unspecified") : result.MinimumShared;
                string testedShared = string.IsNullOrEmpty(result.TestedShared) ? T("versions.unspecified") : result.TestedShared;
                text.Requirements = T("versions.requirements", minimum, tested, minimumShared, testedShared, result.RequiredApi > 0 ? result.RequiredApi.ToString() : T("versions.unspecified"));
                text.Summary = T("versions.summary", status, result.Recorded ?? T("versions.unspecified"), minimum, minimumShared);
                if (result.ManagerTooOld) text.Warning = T("versions.managerOld", ModuleVersionCatalog.ManagerRelease, minimum);
                if (result.SharedTooOld) text.Warning = (text.Warning == null ? "" : text.Warning + "\n") + T("versions.sharedOld", DdenApi.SharedVersion, minimumShared);
                if (result.ApiMismatch) text.Warning = (text.Warning == null ? "" : text.Warning + "\n") + T("versions.apiMismatch", result.RequiredApi, DdenApi.CurrentVersion);
                if (result.InvalidDeclaration) text.Warning = (text.Warning == null ? "" : text.Warning + "\n") + T("versions.invalid");
                text.Details = T("versions.installed", result.Installed ?? T("versions.unspecified"), result.Recorded ?? T("versions.unspecified")) + "\n" + text.Requirements;
                if (text.Warning != null) text.Details += "\n" + text.Warning;
                text.Details += "\n" + T("versions.localCatalog");
            }
            text.Caption = (text.Warning ?? text.Summary).Replace('\n', ' ');
            text.Content.text = text.Warning == null ? text.Requirements : text.Warning + "\n" + text.Requirements;
            _versionText[module] = text; return text;
        }
        private void DrawModuleVersionLine(Rect rect, IDdenModule module)
        {
            var text = VersionText(module);
            string caption = text.Caption;
            Color old = GUI.contentColor;
            try
            {
                GUI.contentColor = text.Result.HasCompatibilityWarning ? DdenUI.Bad : text.Result.ReleaseStatus == DdenReleaseStatus.Older || text.Result.ReleaseStatus == DdenReleaseStatus.Unknown ? DdenUI.Warn
                    : text.Result.ReleaseStatus == DdenReleaseStatus.Current ? DdenUI.Good : text.Result.ReleaseStatus == DdenReleaseStatus.Newer ? DdenUI.Accent : DdenUI.TextDim;
                GUI.Label(rect, DdenUI.ElideToWidth(caption, rect.width, DdenUI.Sub), DdenUI.Sub);
            }
            finally { GUI.contentColor = old; }
            DdenUI.Hover(rect, text.Details);
        }
        private float DrawModuleVersionBand(Rect rect, IDdenModule module, bool warningsOnly = false)
        {
            var text = VersionText(module);
            if (!text.Result.Checked || (warningsOnly && !text.Result.HasCompatibilityWarning)) return 0;
            if (text.Width != rect.width)
            { text.Width = rect.width; text.Height = Math.Max(24, DdenUI.Wrap.CalcHeight(text.Content, Math.Max(1, rect.width - 12)) + 8); }
            var band = new Rect(rect.x, rect.y, rect.width, text.Height);
            DdenUI.Fill(band, text.Result.HasCompatibilityWarning ? new Color(.35f, .07f, .03f, .9f) : DdenUI.Panel);
            DdenUI.Frame(band, text.Result.HasCompatibilityWarning ? DdenUI.Bad : DdenUI.AccentDim);
            GUI.Label(new Rect(band.x + 6, band.y + 4, band.width - 12, band.height - 8), text.Content, DdenUI.Wrap);
            DdenUI.Hover(band, text.Details);
            return text.Height + 6;
        }
        private static string VersionModuleName(IDdenModule module)
        {
            try { return ModuleName(module); }
            catch { return module.GetType().Name; }
        }
        private void DrawModuleVersionWarning()
        {
            IDdenModule first = null; int problems = 0;
            foreach (var module in DdenHub.Modules)
                if (ModuleVersionCatalog.Check(module).HasCompatibilityWarning) { if (first == null) first = module; problems++; }
            bool apiMismatch = DdenApi.CurrentVersion != ModuleVersionCatalog.SupportedApi;
            if (problems == 0 && !apiMismatch) return;
            float scale = Mathf.Clamp(CfgScale.Value, .75f, 2f);
            Matrix4x4 old = GUI.matrix; int oldDepth = GUI.depth;
            try
            {
                GUI.depth = -1100;
                GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1));
                float width = Math.Min(760, Screen.width / scale - 16);
                float y = !CfgIgnoreEnv.Value && !DdenGate.VersionOk && DdenGate.HasParticipants ? 64 : 12;
                var box = new Rect((Screen.width / scale - width) / 2, y, width, 50);
                DdenUI.Fill(box, new Color(.4f, .07f, .03f, .94f)); DdenUI.Frame(box, DdenUI.Bad);
                GUI.Label(new Rect(box.x + 8, box.y + 4, box.width - 16, 18), T("versions.notice"), DdenUI.Row);
                string reason = apiMismatch ? T("versions.apiMismatch", ModuleVersionCatalog.SupportedApi, DdenApi.CurrentVersion)
                    : VersionModuleName(first) + ": " + VersionText(first).Caption;
                string detail = T("versions.open", reason, CfgToggleKey.Value.MainKey);
                GUI.Label(new Rect(box.x + 8, box.y + 25, box.width - 16, 18), DdenUI.ElideToWidth(detail, box.width - 16, DdenUI.Sub), DdenUI.Sub);
            }
            finally { GUI.matrix = old; GUI.depth = oldDepth; }
        }
    }
}
