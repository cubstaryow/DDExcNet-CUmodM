using System;
using DDEN.Shared;
namespace DDEN.NetPerfTracker.Enhanced
{
    internal static class ModuleText
    {
        internal static readonly DdenTextCatalog Catalog = new DdenTextCatalog(typeof(ModuleText).Assembly, "DDEN.NetPerfTracker.Enhanced.Resources.localization.en.json");
        internal static string L(string text) => Catalog.Text(text);
    }
}
