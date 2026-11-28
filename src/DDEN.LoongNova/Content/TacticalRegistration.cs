using CUCoreLib.Data;
using CUCoreLib.Registries;
using UnityEngine;
namespace DDEN.LoongNova
{
    internal static partial class Registration
    {
        internal const string TacticalGogglesId = "dden.loongnova.tactical-goggles";
        private static void RegisterTacticalGoggles()
        {
            var prefab = Resources.Load<GameObject>("autozoomgoggles");
            var swap = prefab.GetComponent<WearableSpriteSwap>();
            var original = prefab.GetComponent<SpriteRenderer>().sprite;
            var sprite = LoadSprite("tactical-goggles.png", "autozoomgoggles", original, pixelWidth: (int)original.rect.width, preserveFrame: true);
            ItemRegistry.Register(TacticalGogglesId, (ItemInfo)new CustomItemInfo
            {
                fullName = Text("tacticalGogglesName"), description = Text("tacticalGogglesDescription"),
                category = "nospawn", tags = "equipment", weight = 0.2f, value = 0, untradeable = true,
                wearable = true, desiredWearLimb = "Head", wearSlotId = "eyes", wearableVisualOffset = 5,
                WornSprite = swap != null && swap.sprites.Count > 0 ? swap.sprites[0] : original,
                wearableHitDurabilityLossMultiplier = 0, destroyAtZeroCondition = false,
                decayInfo = 0, rotSpeed = 0, usable = false, rec = new Recognition(0),
                DropPool = DropPool.None, WorldSpawnPerChunk = 0
            }, sprite, 0);
        }
    }
}
