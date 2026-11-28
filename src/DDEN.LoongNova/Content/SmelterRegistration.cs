using System.Collections.Generic;
using CUCoreLib.Data;
using CUCoreLib.Registries;
using UnityEngine;
namespace DDEN.LoongNova
{
    internal static partial class Registration
    {
        internal const string SmelterId="dden.loongnova.portable-dragonflame-smelter";
        internal const string SmeltingQuality="loongnova.smelting";
        private static void RegisterSmelter()
        {
            LocaleRegistry.RegisterCraftingQuality(SmeltingQuality,Text("smeltingQuality"));
            var sprite=LoadSprite("portable-dragonflame-smelter.png","handcrank",pixelWidth:16,worldLength:2,preserveFrame:true);
            // Native moodles call SetNativeSize on a canvas with reference PPU 100.
            // Fit the same unmodified artwork inside 48x48 UI pixels, independently of its world size.
            var icon=Sprite.Create(sprite.texture,sprite.rect,new Vector2(0.5f,0.5f),Mathf.Max(sprite.rect.width,sprite.rect.height)/0.48f);
            MoodleRegistry.RegisterBody<SmelterComfort>((body,status)=>status.Active?new StatusMoodleDefinition{IconSprite=icon,Intensity=0,Name=Text("comfortName"),Description=Text("comfortDescription")}:null);
            ItemRegistry.Register(SmelterId,(ItemInfo)new CustomItemInfo
            {
                fullName=Text("smelterName"),description=Text("smelterDescription"),
                category="tool",tags="tool",weight=3.5f,value=20,rec=new Recognition(5),
                qualities=new List<CraftingQuality>{new CraftingQuality(SmeltingQuality,10000),new CraftingQuality("heatsource",10000)},
                destroyAtZeroCondition=true,rotSpeed=0,DropPool=DropPool.None,WorldSpawnPerChunk=0
            },sprite,0);
        }
        private static void RegisterSmeltingRecipes()
        {
            Smelt("woodcube","charcoal",4);Smelt("rawcopper","processedcopper");Smelt("ilmenitechunk","titaniumslab");
        }
        private static void Smelt(string ore,string metal,int amount=1)
        {
            var ingredients=new List<RecipeItem>();
            for(int i=0;i<amount;i++)ingredients.Add(new RecipeItem(0){specific=true,specificId=ore,destroyItem=true});
            ingredients.Add(new RecipeItem(0.01f){quality=new CraftingQuality(SmeltingQuality,100),destroyItem=false});
            RecipeRegistry.Register(new Recipe
            {
                INT=0,category=Recipes.RecipeCategory.Materials,
                result=new RecipeResult{id=metal,amount=1,resultCondition=1},
                items=ingredients
            });
        }
    }
}
