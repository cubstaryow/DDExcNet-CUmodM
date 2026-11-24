using System;
using DDEN.Shared;
namespace DDEN.NetPerfTracker
{
    internal static class ModuleText
    {
        internal static readonly DdenTextCatalog Catalog = new DdenTextCatalog(typeof(ModuleText).Assembly, "DDEN.NetPerfTracker.Resources.localization.en.json");
        internal static string L(string text) => Catalog.Text(text);
        internal static string LF(FormattableString text) => Catalog.Format(text);
    }
}
