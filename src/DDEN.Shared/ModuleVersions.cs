using System;
using System.Globalization;
using System.Reflection;

[assembly: AssemblyMetadata("DDEN.ApiVersion", "1")]

namespace DDEN.Shared
{
    /// <summary>Shared product releases and public API generations are independent.</summary>
    public static class DdenApi
    {
        public const int Version = 1;
        public static int CurrentVersion => Version;
        public static readonly string SharedVersion = ReadRelease(typeof(DdenApi).Assembly);
        private static string ReadRelease(Assembly assembly)
        {
            string value = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return string.IsNullOrEmpty(value) ? assembly.GetName().Version.ToString() : value.Split('+')[0];
        }
    }

    /// <summary>Optional opt-in. Existing IDdenModule implementations remain unchanged.</summary>
    public interface IDdenModuleVersion
    {
        string MinimumManagerVersion { get; }
        string TestedManagerVersion { get; }
        string MinimumSharedVersion { get; }
        string TestedSharedVersion { get; }
        int RequiredSharedApiVersion { get; }
    }

    public enum DdenReleaseStatus { NotChecked, Current, Older, Newer, Unrecorded, Unknown }

    public sealed class DdenVersionResult
    {
        public DdenReleaseStatus ReleaseStatus { get; internal set; }
        public string Installed, Recorded, MinimumManager, TestedManager, MinimumShared, TestedShared;
        public int RequiredApi;
        public bool ManagerTooOld, SharedTooOld, ApiMismatch, InvalidDeclaration;
        public DdenVersionResult(DdenReleaseStatus status = DdenReleaseStatus.NotChecked) { ReleaseStatus = status; }
        public bool HasCompatibilityWarning => ManagerTooOld || SharedTooOld || ApiMismatch || InvalidDeclaration;
        public bool Checked => ReleaseStatus != DdenReleaseStatus.NotChecked;
    }

    public static class DdenVersionPolicy
    {
        public static DdenReleaseStatus CompareRelease(string installed, string recorded)
        {
            if (string.IsNullOrEmpty(recorded)) return DdenReleaseStatus.Unrecorded;
            if (!DdenReleaseVersion.TryParse(installed, out var actual) || !DdenReleaseVersion.TryParse(recorded, out var latest)) return DdenReleaseStatus.Unknown;
            int compare = actual.CompareTo(latest);
            return compare == 0 ? DdenReleaseStatus.Current : compare < 0 ? DdenReleaseStatus.Older : DdenReleaseStatus.Newer;
        }
        public static DdenVersionResult Evaluate(string installed, string recorded, string manager, IDdenModuleVersion declaration, int runtimeApi)
            => Evaluate(installed, recorded, manager, DdenApi.SharedVersion, declaration, runtimeApi);
        public static DdenVersionResult Evaluate(string installed, string recorded, string manager, string shared, IDdenModuleVersion declaration, int runtimeApi)
        {
            var result = new DdenVersionResult { Installed = installed, Recorded = recorded, ReleaseStatus = DdenReleaseStatus.NotChecked };
            if (declaration == null) return result;
            result.MinimumManager = declaration.MinimumManagerVersion;
            result.TestedManager = declaration.TestedManagerVersion;
            result.MinimumShared = declaration.MinimumSharedVersion;
            result.TestedShared = declaration.TestedSharedVersion;
            result.RequiredApi = declaration.RequiredSharedApiVersion;
            if (string.IsNullOrWhiteSpace(result.MinimumManager) && string.IsNullOrWhiteSpace(result.TestedManager) && string.IsNullOrWhiteSpace(result.MinimumShared) && string.IsNullOrWhiteSpace(result.TestedShared) && result.RequiredApi == 0) return result;
            result.ReleaseStatus = CompareRelease(installed, recorded);
            if (!string.IsNullOrEmpty(result.MinimumManager))
            {
                if (DdenReleaseVersion.TryParse(result.MinimumManager, out var minimum) && DdenReleaseVersion.TryParse(manager, out var current)) result.ManagerTooOld = current.CompareTo(minimum) < 0;
                else result.InvalidDeclaration = true;
            }
            if (!string.IsNullOrEmpty(result.MinimumShared))
            {
                if (DdenReleaseVersion.TryParse(result.MinimumShared, out var minimum) && DdenReleaseVersion.TryParse(shared, out var current)) result.SharedTooOld = current.CompareTo(minimum) < 0;
                else result.InvalidDeclaration = true;
            }
            if (!string.IsNullOrEmpty(result.TestedShared) && !DdenReleaseVersion.TryParse(result.TestedShared, out _)) result.InvalidDeclaration = true;
            if (!string.IsNullOrEmpty(result.TestedManager) && !DdenReleaseVersion.TryParse(result.TestedManager, out _)) result.InvalidDeclaration = true;
            result.InvalidDeclaration |= result.RequiredApi < 0;
            result.ApiMismatch = result.RequiredApi > 0 && result.RequiredApi != runtimeApi;
            return result;
        }
    }

    /// <summary>Numeric releases (1-4 components) and SemVer prereleases. Build metadata does not affect precedence.</summary>
    public sealed class DdenReleaseVersion : IComparable<DdenReleaseVersion>
    {
        private readonly int[] _numbers;
        private readonly string[] _prerelease;
        private DdenReleaseVersion(int[] numbers, string[] prerelease) { _numbers = numbers; _prerelease = prerelease; }
        public static bool TryParse(string value, out DdenReleaseVersion version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(value)) return false;
            value = value.Trim();
            if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase)) value = value.Substring(1);
            int buildAt = value.IndexOf('+');
            if (buildAt >= 0)
            {
                if (!Identifiers(value.Substring(buildAt + 1), false, out _)) return false;
                value = value.Substring(0, buildAt);
            }
            string[] pre = null;
            int preAt = value.IndexOf('-');
            if (preAt >= 0)
            {
                if (!Identifiers(value.Substring(preAt + 1), true, out pre)) return false;
                value = value.Substring(0, preAt);
            }
            var parts = value.Split('.'); var numbers = new int[4];
            if (parts.Length == 0 || parts.Length > 4) return false;
            for (int i = 0; i < parts.Length; i++)
                if (parts[i].Length == 0 || (parts[i].Length > 1 && parts[i][0] == '0') || !int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i])) return false;
            version = new DdenReleaseVersion(numbers, pre); return true;
        }
        private static bool Identifiers(string value, bool rejectNumericZeroes, out string[] parts)
        {
            parts = value.Split('.');
            foreach (string part in parts)
            {
                if (part.Length == 0) return false;
                bool numeric = true;
                foreach (char ch in part)
                {
                    bool digit = ch >= '0' && ch <= '9'; numeric &= digit;
                    if (!digit && !(ch >= 'a' && ch <= 'z') && !(ch >= 'A' && ch <= 'Z') && ch != '-') return false;
                }
                if (rejectNumericZeroes && numeric && part.Length > 1 && part[0] == '0') return false;
            }
            return true;
        }
        private static bool Numeric(string value) { foreach (char ch in value) if (ch < '0' || ch > '9') return false; return true; }
        public int CompareTo(DdenReleaseVersion other)
        {
            if (other == null) return 1;
            for (int i = 0; i < 4; i++) { int c = _numbers[i].CompareTo(other._numbers[i]); if (c != 0) return c; }
            if (_prerelease == null) return other._prerelease == null ? 0 : 1;
            if (other._prerelease == null) return -1;
            for (int i = 0; i < Math.Min(_prerelease.Length, other._prerelease.Length); i++)
            {
                string a = _prerelease[i], b = other._prerelease[i]; bool an = Numeric(a), bn = Numeric(b);
                int c = an && bn ? (a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b))
                    : an != bn ? (an ? -1 : 1) : string.CompareOrdinal(a, b);
                if (c != 0) return c;
            }
            return _prerelease.Length.CompareTo(other._prerelease.Length);
        }
    }
}
