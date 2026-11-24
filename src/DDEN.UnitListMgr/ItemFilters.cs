using System;

namespace DDEN.UnitListMgr
{
    [Flags]
    internal enum ItemSource { Native = 1, Ccl = 2, Other = 4, All = 7 }
    [Flags]
    internal enum ItemKind { Container = 1, Tool = 2, Material = 4, Medical = 8, Food = 16, Liquid = 32, Wearable = 64, Utility = 128, Other = 256 }
    internal static class ItemFilters
    {
        internal static ItemKind Classify(string category, string tags, bool wearable, bool liquid)
        {
            ItemKind result = 0;
            switch ((category ?? "").ToLowerInvariant())
            {
                case "container": result |= ItemKind.Container; break;
                case "tool": result |= ItemKind.Tool; break;
                case "medical": case "drug": result |= ItemKind.Medical; break;
                case "food": result |= ItemKind.Food; break;
                case "water": result |= ItemKind.Liquid; break;
                case "utility": result |= ItemKind.Utility; break;
            }
            foreach (string tag in (tags ?? "").Split(','))
            {
                string name = tag.Trim().ToLowerInvariant();
                if (name == "material" || name == "component") result |= ItemKind.Material;
                if (name == "tool") result |= ItemKind.Tool;
            }
            if (wearable) result |= ItemKind.Wearable;
            if (liquid) result |= ItemKind.Liquid;
            return result == 0 ? ItemKind.Other : result;
        }
        // OR within one group; AND between origin and use. No selected use means all uses.
        internal static bool Matches(ItemSource source, ItemKind kind, ItemSource sources, ItemKind kinds)
            => (source & sources) != 0 && (kinds == 0 || (kind & kinds) != 0);
    }
}
