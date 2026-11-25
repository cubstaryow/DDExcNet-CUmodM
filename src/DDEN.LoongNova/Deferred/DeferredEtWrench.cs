using System.Collections.Generic;
using CUCoreLib.Data;
using CUCoreLib.Registries;
using DDEN.Shared;
using UnityEngine;

namespace DDEN.LoongNova
{
    // Reserved only. No plugin lifecycle, registry call or configuration switch activates this item.
    internal static class DeferredEtWrench
    {
        internal const string Id = "dden.loongnova.dragon-claw-refitted-wrench";
        internal const string EtGuid = "com.a4kk.enhancedtools";
        internal const string EtWrenchId = "refittedwrench1";

        internal static bool CanEnable(bool requested, bool etLoaded, bool inSave) => requested && etLoaded && !inSave;

        // A future activation must check ET, restrict changes to the menu and wire local delegates after CCL snapshots.
        internal static CustomItemInfo CreateDefinition(Sprite icon)
        {
            ItemRegistry.TryGetItemInfo(EtWrenchId, out var original);
            var info = new CustomItemInfo
            {
                fullName = Registration.Text("refittedName"), description = Registration.Text("refittedDescription"),
                category = "tool", tags = "tool,belttool,backflip", weight = 0.8f, value = 70,
                slotRotation = -90, rec = new Recognition(12), usable = true, usableWithLMB = true,
                usableOnLimb = true, autoAttack = true, destroyAtZeroCondition = false,
                useAction = Registration.UseWrench, useLimbAction = original?.useLimbAction,
                qualities = new List<CraftingQuality>
                {
                    new CraftingQuality("cutting", 100000), new CraftingQuality("hammering", 100000),
                    new CraftingQuality(Registration.DragonQuality, 100000), new CraftingQuality("modification", 100000)
                },
                DropPool = DropPool.None, WorldSpawnPerChunk = 0, Icon = icon
            };
            info.AddSpawnComponent<DeferredEtWrenchBehavior>();
            return info;
        }

        internal static Recipe CreateRecipe() => new Recipe
        {
            INT = 18, category = (Recipes.RecipeCategory)1,
            result = new RecipeResult { id = Id, amount = 1, resultCondition = 1 },
            items = new List<RecipeItem>
            {
                new RecipeItem(0) { specific = true, specificId = EtWrenchId },
                new RecipeItem(0) { specific = true, specificId = Registration.WrenchId },
                new RecipeItem(0) { specific = true, specificId = Registration.DragonId },
                // One intact ET schematic supplies two engineering units and is consumed completely.
                new RecipeItem(1) { quality = new CraftingQuality("engineering", 2), destroyItem = true }
            }
        };
    }

    internal sealed class DeferredEtWrenchBehavior : MonoBehaviour
    {
        private void Start()
        {
            var item = GetComponent<Item>();
            if (item == null || item.id != DeferredEtWrench.Id || !Compat.PluginLoaded(DeferredEtWrench.EtGuid)) return;
            var repair = Reflect.Type("EnhancedTools.RefittedWrenchRepairScript");
            if (repair != null && GetComponent(repair) == null) gameObject.AddComponent(repair);
        }
    }
}
