using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using DDEN.Shared;
using UnityEngine;

namespace DDEN.UnitListMgr
{
    internal sealed class ItemDefaults
    {
        internal string Id, Name, Battery;
        internal float Condition = 1, Capacity, BatteryLimit;
        internal int? Rounds, MaxRounds;
        internal bool HasGun, HasBattery, HasMagazine, UsesMagazine;
        internal readonly List<LiquidAmount> Liquids = new List<LiquidAmount>();
    }

    internal static class Catalog
    {
        internal static object Read(object value, string key) => value == null ? null : Reflect.GetInstance(value, key);
        internal static string Name(string id)
        {
            try
            {
                if (Item.GlobalItems != null && Item.GlobalItems.TryGetValue(id, out var info))
                {
                    string name = info.fullName;
                    if (!string.IsNullOrWhiteSpace(name)) return Regex.Replace(name, "<[^>]*>", "");
                }
            }
            catch { }
            return id ?? "";
        }
        internal static string LiquidName(string id)
        {
            try
            {
                if (global::Liquids.Registry.TryGetValue(id, out var info))
                {
                    string name = info.localeFromItem ? Locale.GetItem(id) : Locale.GetOther(info.localeName);
                    if (!string.IsNullOrWhiteSpace(name)) return Regex.Replace(name, "<[^>]*>", "");
                }
            }
            catch { }
            return id;
        }
        internal static HashSet<string> CclIds()
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            object registry = Reflect.GetStatic("CUCoreLib.Registries.ItemRegistry", "RegisteredItems");
            if (Read(registry, "Keys") is IEnumerable keys)
                foreach (object key in keys) if (key is string id) ids.Add(id);
            return ids;
        }
        internal static ItemKind Kind(string id)
        {
            if (Item.GlobalItems == null || !Item.GlobalItems.TryGetValue(id, out var info)) return ItemKind.Other;
            bool liquid = info is LiquidItemInfo liquidInfo && liquidInfo.capacity > 0 || Read(info, "Syringe") != null;
            ItemKind kind = ItemFilters.Classify(info.category, info.tags, info.wearable, liquid);
            if (Read(info, "Container") != null) kind = (kind & ~ItemKind.Other) | ItemKind.Container;
            return kind;
        }
        internal static List<string> Items() => Item.GlobalItems?.Keys.OrderBy(Name, StringComparer.CurrentCulture).ToList() ?? new List<string>();
        internal static List<string> Batteries() => Item.GlobalItems?.Where(x => x.Value is BatteryInfo).Select(x => x.Key).OrderBy(Name).ToList() ?? new List<string>();
        internal static List<string> LiquidIds() => global::Liquids.Registry?.Keys.OrderBy(LiquidName).ToList() ?? new List<string>();
        internal static GameObject Prefab(string id)
        {
            var resource = Resources.Load<GameObject>(id);
            if (resource != null) return resource;
            var call = Reflect.Type("CUCoreLib.Helpers.CustomInstantiate")?.GetMethod("ResolvePrefab", BindingFlags.Public | BindingFlags.Static);
            return call?.Invoke(null, new object[] { id }) as GameObject;
        }
        internal static float Capacity(string id)
        {
            if (Item.GlobalItems == null || !Item.GlobalItems.TryGetValue(id, out var info)) return 0;
            object syringe = Read(info, "Syringe");
            if (syringe != null && Read(syringe, "Capacity") is float capacity) return capacity;
            return info is LiquidItemInfo liquid ? liquid.capacity : 0;
        }

        internal static ItemDefaults Inspect(string id)
        {
            var result = new ItemDefaults { Id = id, Name = Name(id) };
            if (Item.GlobalItems == null || !Item.GlobalItems.TryGetValue(id, out var info)) throw new InvalidOperationException("Item is no longer registered");
            var prefab = Prefab(id);
            if (prefab == null || prefab.GetComponent<Item>() == null) throw new InvalidOperationException("No item template");
            result.Condition = prefab.GetComponent<Item>().condition;
            // Inspection reads a template, never activates/clones it to obtain a preview.
            var ammo = prefab.GetComponent<AmmoScript>();
            if (ammo != null) { result.Rounds = ammo.maxRounds; result.MaxRounds = ammo.maxRounds; }
            var gun = prefab.GetComponent<GunScript>();
            result.HasGun = gun != null; result.HasMagazine = false;
            if (gun != null) { result.Rounds = 0; result.MaxRounds = gun.magCapacity; result.UsesMagazine = gun.feedType == GunScript.FeedType.Mag; }
            result.Capacity = Capacity(id);
            object syringe = Read(info, "Syringe");
            var contents = syringe != null ? Read(syringe, "DefaultContents") as IEnumerable
                : (info as LiquidItemInfo)?.defaultContents;
            if (contents != null)
                foreach (var entry in contents)
                    if (entry is LiquidStack liquid) result.Liquids.Add(new LiquidAmount { Id = liquid.liquidId, Amount = liquid.amount });
            if (result.Capacity > 0) result.Condition = Mathf.Clamp01(result.Liquids.Sum(x => x.Amount) / result.Capacity);
            var battery = prefab.GetComponent<BatteryItem>();
            object custom = Read(info, "Battery");
            result.HasBattery = battery != null || custom != null;
            if (result.HasBattery)
            {
                var preset = custom != null ? Read(custom, "Preset") : battery.preset;
                string size = preset?.ToString() ?? "Small";
                result.BatteryLimit = size == "Small" ? 50 : size == "Medium" ? 100 : 300;
                bool spawn = custom != null ? !(Read(custom, "SpawnWithBattery") is bool b) || b : !battery.notSpawnWithBattery;
                result.Battery = spawn ? size.ToLowerInvariant() + "battery" : "";
                if (!spawn) result.Condition = 0;
                else if (custom != null && Read(custom, "StartCharge") is float charge && charge >= 0)
                    result.Condition = charge <= 1 ? charge : Mathf.Clamp01(charge / result.BatteryLimit);
            }
            return result;
        }
    }
}
