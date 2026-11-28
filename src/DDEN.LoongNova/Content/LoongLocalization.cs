using System;
using System.Collections.Generic;
using System.IO;
using CUCoreLib.Registries;
using HarmonyLib;
using Newtonsoft.Json.Linq;

namespace DDEN.LoongNova
{
    internal static class LoongLocalization
    {
        private static readonly LoongTextCatalog Catalog = new LoongTextCatalog(Read("EN"), Read("zh-CN"));
        private static readonly Func<string, string, string> Current = ReadCurrent;
        private static readonly Dictionary<string, (string Name, string Description)> ItemKeys = BuildItemKeys();
        private static JObject Read(string language)
        {
            using (var stream = ContentResources.Open(typeof(LoongLocalization).Assembly, "Locales/" + language + ".json"))
            using (var reader = new StreamReader(stream)) return JObject.Parse(reader.ReadToEnd());
        }
        private static Dictionary<string, (string, string)> BuildItemKeys()
        {
            var keys = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
            foreach (var pair in LoongTextCatalog.Items) keys[pair.Value] = (pair.Key + "Name", pair.Key + "Description");
            return keys;
        }
        private static string ReadCurrent(string section, string key)
        {
            var language = Locale.currentLang;
            var table = section == "item" ? language?.main : language?.other;
            return table != null && table.TryGetValue(key, out var text) ? text : null;
        }
        internal static string Text(string key)
        {
            string text = Catalog.Text(key, Locale.currentLangName, Current);
            if (key != "wrenchDescription") return text;
            string capsule = Locale.currentLang != null ? Locale.GetBuilding("dropcapsule")
                : Locale.currentLangName != null && Locale.currentLangName.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "空投舱" : "drop capsule";
            return string.Format(text, capsule);
        }
        internal static void RefreshItem(string id, ItemInfo info)
        {
            if (info == null || id == null || !ItemKeys.TryGetValue(id, out var keys)) return;
            info.fullName = Text(keys.Name); info.description = Text(keys.Description);
        }
        internal static void RefreshItems()
        {
            foreach (var id in ItemKeys.Keys)
            {
                if (Item.GlobalItems != null && Item.GlobalItems.TryGetValue(id, out var info)) RefreshItem(id, info);
                if (ItemRegistry.TryGetCustomInfo(id, out var registered)) RefreshItem(id, (ItemInfo)registered);
            }
            RefreshItem(Registration.CellId, Registration.CellStats);
        }
    }
    [HarmonyPatch]
    internal static class LoongLocalePatches
    {
        [HarmonyPatch(typeof(Item), nameof(Item.GetItem)), HarmonyPostfix, HarmonyPriority(Priority.Last)]
        private static void ItemText(string id, ref ItemInfo __result) => LoongLocalization.RefreshItem(id, __result);

        [HarmonyPatch(typeof(Locale), nameof(Locale.LoadLanguage)), HarmonyPostfix, HarmonyPriority(Priority.Last), HarmonyAfter("net.cucorelib")]
        private static void LanguageLoaded() => LoongLocalization.RefreshItems();
    }
}
