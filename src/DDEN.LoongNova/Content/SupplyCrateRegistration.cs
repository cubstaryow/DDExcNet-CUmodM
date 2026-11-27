using System;
using CUCoreLib.Data;
using CUCoreLib.Registries;

namespace DDEN.LoongNova
{
    internal static partial class Registration
    {
        private static void RegisterSupplyCrates()
        {
            var sealedBox = new CustomItemInfo
            {
                fullName = Text("supplySealedName"), description = Text("supplySealedDescription"),
                category = "container", tags = "container", weight = SupplyCrateRules.SealedWeight,
                usable = true, useAction = SupplyCrate.Open, destroyAtZeroCondition = false,
                value = 0, untradeable = true, DropPool = DropPool.None, WorldSpawnPerChunk = 0
            };
            sealedBox.AddSpawnComponent<SupplyCrate>();
            ItemRegistry.Register(SupplyCrateRules.SealedId, (ItemInfo)sealedBox,
                LoadSprite("supply-crate-sealed.png", "duffelbag", pixelWidth: 32, worldLength: 2, preserveFrame: true), 0);
            ItemRegistry.Register(SupplyCrateRules.OpenId, (ItemInfo)new CustomItemInfo
            {
                fullName = Text("supplyOpenName"), description = Text("supplyOpenDescription"),
                category = "container", tags = "container", weight = SupplyCrateRules.OpenWeight,
                usable = false, destroyAtZeroCondition = true, value = 0, untradeable = true,
                decayMinutes = SupplyCrateRules.DecayMinutes, decayInfo = SupplyCrateRules.DecayInfo,
                rotSpeed = 1.666f / SupplyCrateRules.DecayMinutes,
                DropPool = DropPool.None, WorldSpawnPerChunk = 0,
                Container = new ContainerProperties
                {
                    Capacity = SupplyCrateRules.Capacity, MaxWeightPerItem = SupplyCrateRules.MaxItemWeight,
                    EncumbranceReduction = SupplyCrateRules.EncumbranceMultiplier,
                    ItemsVisible = false, TagRestriction = Array.Empty<string>()
                }
            }, LoadSprite("supply-crate-open.png", "duffelbag", pixelWidth: 32, worldLength: 2, preserveFrame: true), 0);
        }
    }
}
