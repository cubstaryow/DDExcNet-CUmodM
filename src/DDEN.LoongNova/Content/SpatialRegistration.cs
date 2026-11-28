using System;
using System.Collections.Generic;
using CUCoreLib.Data;
using CUCoreLib.Registries;
using UnityEngine;

namespace DDEN.LoongNova
{
    internal static partial class Registration
    {
        private static void RegisterSpatialItems()
        {
            ItemRegistry.Register(SpatialCatalog.MembraneId, (ItemInfo)new CustomItemInfo
            {
                fullName = Text("membraneName"), description = Text("membraneDescription"),
                category = "custom", tags = "material", weight = 0.01f, value = 0, untradeable = true,
                DropPool = DropPool.None, WorldSpawnPerChunk = 0
            }, LoadSprite("spatial-membrane.png", "flexiglass", pixelWidth: 8, preserveFrame: true), 0);
            ItemRegistry.Register(SpatialCatalog.ComponentId, (ItemInfo)new CustomItemInfo
            {
                fullName = Text("spatialComponentName"), description = Text("spatialComponentDescription"),
                category = "custom", tags = "material", weight = 0.1f, value = 0, untradeable = true,
                DropPool = DropPool.None, WorldSpawnPerChunk = 0
            }, LoadSprite("dual-space-component.png", "flexiglass", pixelWidth: 8, preserveFrame: true), 0);
            var crate = new CustomItemInfo
            {
                fullName = Text("spatialCrateName"), description = Text("spatialCrateDescription"),
                category = "container", tags = "container", weight = 2.4f,
                usable = true, useAction = MicroSpaceCrate.Unload, destroyAtZeroCondition = true,
                value = 0, untradeable = true, decayMinutes = SupplyCrateRules.DecayMinutes,
                decayInfo = SupplyCrateRules.DecayInfo, rotSpeed = 1.666f / SupplyCrateRules.DecayMinutes,
                DropPool = DropPool.None, WorldSpawnPerChunk = 0,
                Container = new ContainerProperties
                {
                    Capacity = 20, MaxWeightPerItem = SupplyCrateRules.MaxItemWeight,
                    EncumbranceReduction = 0.9f, ItemsVisible = false, TagRestriction = Array.Empty<string>()
                }
            };
            crate.AddSpawnComponent<MicroSpaceCrate>();
            ItemRegistry.Register(SpatialCatalog.CrateId, (ItemInfo)crate,
                LoadSprite("micro-space-supply-crate.png", "duffelbag", pixelWidth: 32, worldLength: 2, preserveFrame: true), 0);
            var recharger = new CustomItemInfo
            {
                fullName = Text("rechargerName"), description = Text("rechargerDescription"),
                category = "container", tags = "container", weight = 1.5f, value = 60, untradeable = false,
                usable = true, useAction = MicroSpaceCrate.Unload, destroyAtZeroCondition = false,
                DropPool = DropPool.None, WorldSpawnPerChunk = 0,
                Container = new ContainerProperties
                {
                    Capacity = 1, MaxWeightPerItem = 1, EncumbranceReduction = 0,
                    ItemsVisible = false, TagRestriction = new[] { "battery" }
                }
            };
            recharger.AddSpawnComponent<MicroSpaceCrate>();
            ItemRegistry.Register(SpatialCatalog.ChargerId, (ItemInfo)recharger, LoadSprite("spatial-recharger.png", "handcrank", RechargerPlaceholder(), pixelWidth: 16, preserveFrame: true), 0);
        }
        private static Sprite RechargerPlaceholder()
        {
            if (ItemRegistry.TryGetIcon("autocharger", out var icon) && icon != null) return icon;
            var prefab = Resources.Load<GameObject>("autocharger") ?? Resources.Load<GameObject>("rechargingstation") ?? Resources.Load<GameObject>("handcrank");
            return prefab != null && prefab.TryGetComponent<SpriteRenderer>(out var renderer) && renderer.sprite != null ? renderer.sprite : NativeSprite("smallbattery");
        }
        private static void RegisterSpatialRecipes()
        {
            AddRecipe(SpatialCatalog.ChargerId, new List<RecipeItem>
            {
                Material(SpatialCatalog.ComponentId), Material(SpatialCatalog.ComponentId), Material(MatterId),
                Material("titaniumsheet"), Material("titaniumsheet"), Material("handcrank"), Tool(DragonQuality)
            });
            AddRecipe(SpatialCatalog.ComponentId, new List<RecipeItem>
            { Material(SpatialCatalog.MembraneId), Material(SpatialCatalog.MembraneId), Tool(DragonQuality), Material(MatterId) });
            AddRecipe(SpatialCatalog.CrateId, new List<RecipeItem>
            {
                Material(SpatialCatalog.ComponentId), Material(SpatialCatalog.ComponentId), Material(SpatialCatalog.ComponentId),
                Material(SupplyCrateRules.OpenId), new RecipeItem(DragonizationRules.Cost(200))
                { quality = new CraftingQuality(DragonQuality, 200), destroyItem = false },
                Material("bundleofwires"), Material("bundleofwires"), Material("titaniumsheet")
            });
        }
    }
}
