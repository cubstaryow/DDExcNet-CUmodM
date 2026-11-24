using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CUCoreLib.Data;
using CUCoreLib.Registries;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DDEN.LoongNova
{
    internal static class Registration
    {
        internal const string GunId = "dden.loongnova.dragon-scorch";
        internal const string CellId = "dden.loongnova.dragon-breath-cell";
        internal const string BeltId = "dden.loongnova.gravity-belt";
        internal const string MatterId = "dden.loongnova.dragon-breath-matter";
        internal const string DragonId = "dden.loongnova.dragon";
        internal const string WrenchId = "dden.loongnova.dragon-claw-wrench";
        internal const string DragonQuality = "loongnova.dragonization";
        internal static BatteryInfo CellStats { get; private set; }
        internal static Sprite GunSprite { get; private set; }
        private static JObject _text;
        internal static string Text(string key) => _text?.Value<string>(key) ?? key;

        private static Stream Resource(string suffix)
        {
            var assembly = typeof(Registration).Assembly;
            return assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(name => name.EndsWith(suffix, StringComparison.Ordinal)));
        }

        internal static void Register()
        {
            using (var reader = new StreamReader(Resource("Content.json"))) _text = JObject.Parse(reader.ReadToEnd());
            var gun = new CustomItemInfo
            {
                fullName = Text("gunName"), description = Text("gunDescription"),
                category = "utility", tags = "gun,cangetwet", weight = 2f, value = 48,
                slotRotation = -90, onlyHoldInHands = true, usable = true, usableWithLMB = true,
                autoAttack = true, destroyAtZeroCondition = false, rec = new Recognition(10),
                DropPool = Sources, WorldSpawnPerChunk = 0,
                Gun = new GunProperties
                {
                    AmmoType = GunScript.AmmoType.Rifle, FiringMode = GunScript.FiringMode.Auto,
                    FeedType = GunScript.FeedType.Mag, MagCapacity = 20, KnockBack = 2,
                    StructureDamage = 3000, AnimalDamage = 650, Loudness = 2.5f,
                    DesiredGasTime = DragonGun.ShotInterval, ShotsPerFire = 1, VerticalSpread = 0,
                    ConditionLossPerShot = 0.2f, BeforeAttack = Trigger
                }
            };
            gun.AddSpawnComponent<DragonGun>();
            var cell = new CustomItemInfo
            {
                fullName = Text("cellName"), description = Text("cellDescription"),
                category = "utility", tags = "battery", weight = 0.3f, value = 32,
                usable = false, destroyAtZeroCondition = false, rec = new Recognition(6),
                DropPool = Sources, WorldSpawnPerChunk = 0
            };
            CellStats = new BatteryInfo { maxCharge = EnergyState.Capacity };
            foreach (var field in typeof(ItemInfo).GetFields(BindingFlags.Instance | BindingFlags.Public))
                field.SetValue(CellStats, field.GetValue(cell));
            CellStats.SetTags();
            GunSprite = LoadSprite("dragon-scorch-rifle.png", "rifle");
            var originalGun = Resources.Load<GameObject>("rifle").GetComponent<GunScript>();
            gun.Gun.BarrelOffset = originalGun.barrel.localPosition;
            gun.Gun.NormalSprite = gun.Gun.RackedSprite = gun.Gun.NormalSpriteNoMag = gun.Gun.RackedSpriteNoMag = GunSprite;
            using (ItemRegistry.BeginOwnerRegistration(LoongNovaPlugin.Guid))
            {
                ItemRegistry.Register(GunId, (ItemInfo)gun, GunSprite, 0.02f);
                ItemRegistry.Register(CellId, (ItemInfo)cell, LoadSprite("dragon-breath-cell.png", "riflemagazine"), 0.05f);
                RegisterEquipment();
            }
            using (RecipeRegistry.BeginOwnerRegistration(LoongNovaPlugin.Guid)) RegisterRecipes();
        }

        private const DropPool Sources = DropPool.AllTraders | DropPool.Corpse | DropPool.ContainerCrate
            | DropPool.DropCapsule | DropPool.CapsuleContainer;

        private static Sprite NativeSprite(string id) => Resources.Load<GameObject>(id).GetComponent<SpriteRenderer>().sprite;

        private static void RegisterEquipment()
        {
            LocaleRegistry.RegisterCraftingQuality(DragonQuality, Text("dragonQuality"));
            var prefab = Resources.Load<GameObject>("belt");
            var container = prefab.GetComponent<Container>();
            var swap = prefab.GetComponent<WearableSpriteSwap>();
            var belt = new CustomItemInfo
            {
                fullName = Text("beltName"), description = Text("beltDescription"),
                category = "container", weight = 1.5f, value = 45, rec = new Recognition(10),
                wearable = true, desiredWearLimb = "DownTorso", wearSlotId = "belt", wearableVisualOffset = 2,
                wearableHitDurabilityLossMultiplier = 0, destroyAtZeroCondition = false,
                decayInfo = 0, rotSpeed = 0, usable = false, DropPool = Sources, WorldSpawnPerChunk = 0,
                Container = new ContainerProperties
                {
                    Capacity = 6, MaxWeightPerItem = container.maxWeightPerItem,
                    EncumbranceReduction = 0.6f, ItemsVisible = container.itemsVisible,
                    TagRestriction = container.tagRestriction
                },
                WornSprite = swap != null && swap.sprites.Count > 0 ? swap.sprites[0] : NativeSprite("belt")
            };
            // No BatteryProperties: every newly generated or crafted belt has an empty energy socket.
            belt.AddSpawnComponent<GravityBelt>();
            ItemRegistry.Register(BeltId, (ItemInfo)belt, NativeSprite("belt"), 0.02f);
            ItemRegistry.Register(MatterId, (ItemInfo)new CustomItemInfo
            {
                fullName = Text("matterName"), description = Text("matterDescription"),
                category = "custom", tags = "material", weight = 0.5f, value = 8,
                usable = false, destroyAtZeroCondition = true, DropPool = DropPool.None, WorldSpawnPerChunk = 0
            }, NativeSprite("turbulentcrystalshard"), 0);
            ItemRegistry.Register(DragonId, (ItemInfo)new CustomItemInfo
            {
                fullName = Text("dragonName"), description = Text("dragonDescription"),
                category = "custom", tags = "material", weight = 0.5f, value = 35,
                usable = false, DropPool = DropPool.None, WorldSpawnPerChunk = 0
            }, LoadSprite("dragon_cloud.png", "turbulentcrystalshard"), 0);
            ItemRegistry.Register(WrenchId, (ItemInfo)new CustomItemInfo
            {
                fullName = Text("wrenchName"), description = Text("wrenchDescription"),
                category = "tool", tags = "tool,backflip", weight = 0.8f, value = 35,
                slotRotation = -90, rec = new Recognition(5), usable = true, usableWithLMB = true,
                autoAttack = true, destroyAtZeroCondition = false, rotSpeed = 0,
                useAction = UseWrench,
                qualities = new List<CraftingQuality>
                {
                    new CraftingQuality("cutting", 100000), new CraftingQuality("hammering", 100000),
                    new CraftingQuality(DragonQuality, 100000)
                },
                DropPool = DropPool.None, WorldSpawnPerChunk = 0
            }, NativeSprite("wrench"), 0);
        }

        internal static void UseWrench(Body body, Item item)
        {
            float before = item.condition;
            Item.GetItem("wrench").useAction?.Invoke(body, item);
            if (item != null && item.condition < before)
                item.condition = before - (before - item.condition) * 0.001f;
        }

        private static bool Trigger(ref object state, GunScript gun, Body body, Item item)
        {
            if (item != null && item.id == GunId)
                DragonGun.For(item).Request("shoot", body);
            return false;
        }

        private static Sprite LoadSprite(string resource, string reference)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            using (var stream = Resource(resource))
            using (var bytes = new MemoryStream())
            {
                stream.CopyTo(bytes);
                if (!texture.LoadImage(bytes.ToArray())) throw new InvalidDataException(resource);
            }
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            var pixels = texture.GetPixels32();
            int minX = texture.width, minY = texture.height, maxX = -1, maxY = -1;
            for (int y = 0; y < texture.height; y++)
            for (int x = 0; x < texture.width; x++)
            {
                if (pixels[y * texture.width + x].a < 32) continue;
                minX = Math.Min(minX, x); minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
            }
            if (maxX < minX) throw new InvalidDataException("Empty sprite: " + resource);
            var original = Resources.Load<GameObject>(reference).GetComponent<SpriteRenderer>().sprite;
            int width = maxX - minX + 1, height = maxY - minY + 1;
            int targetWidth = reference == "rifle" ? 64 : 16;
            int targetHeight = Math.Max(1, Mathf.RoundToInt((float)height * targetWidth / width));
            var compact = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
            };
            var sampled = new Color32[targetWidth * targetHeight];
            for (int y = 0; y < targetHeight; y++)
            for (int x = 0; x < targetWidth; x++)
            {
                int sourceX = minX + Math.Min(width - 1, (int)((x + 0.5f) * width / targetWidth));
                int sourceY = minY + Math.Min(height - 1, (int)((y + 0.5f) * height / targetHeight));
                sampled[y * targetWidth + x] = pixels[sourceY * texture.width + sourceX];
            }
            compact.SetPixels32(sampled);
            compact.Apply(false, false); // CCL snapshots need a readable texture for serialization.
            UnityEngine.Object.Destroy(texture);
            float ppu = targetWidth / original.bounds.size.x;
            return Sprite.Create(compact, new Rect(0, 0, targetWidth, targetHeight), new Vector2(0.5f, 0.5f), ppu);
        }

        private static RecipeItem Material(string id) => new RecipeItem(0) { specific = true, specificId = id };
        private static RecipeItem Fluid(float amount) => new RecipeItem(amount) { specific = true, specificId = "biochem", isLiquid = true };
        private static RecipeItem Tool(string quality) => new RecipeItem(0) { quality = new CraftingQuality(quality, 1), destroyItem = false };
        private static void AddRecipe(string result, List<RecipeItem> items, int intelligence = 18)
        {
            RecipeRegistry.Register(new Recipe
            {
                INT = intelligence, category = (Recipes.RecipeCategory)1,
                result = new RecipeResult { id = result, amount = 1, resultCondition = 1 }, items = items
            });
        }
        private static void RegisterRecipes()
        {
            AddRecipe(MatterId, new List<RecipeItem>
            {
                Material("flammablepowder"), Fluid(50), Material("titaniumsheet"),
                Material("bundleofwires"), Tool(DragonQuality)
            }, 16);
            AddRecipe(CellId, new List<RecipeItem>
            {
                Material(MatterId), Material(MatterId), Material("titaniumsheet"), Material("titaniumrod"),
                Material("largebattery"), Fluid(100), Tool(DragonQuality)
            });
            AddRecipe(GunId, new List<RecipeItem>
            {
                Material(MatterId), Material(MatterId), Material(MatterId), Material(MatterId),
                Material("titaniumsheet"), Material("titaniumsheet"), Material("titaniumrod"), Material("titaniumrod"),
                Material("rifle"), Tool(DragonQuality)
            });
            var depleted = Material(CellId);
            depleted.noMaxCondition = true;
            AddRecipe(CellId, new List<RecipeItem> { depleted, Fluid(160), Material("flammablepowder") });
            AddRecipe(BeltId, new List<RecipeItem>
            {
                Material("belt"), Material("turbulentcrystalshard"), Material("gravbag"),
                Material(MatterId), Material(MatterId), Material("canvas"), Material("canvas"),
                Material("titaniumsheet"), Material("titaniumsheet"), Material("titaniumrod"), Tool(DragonQuality)
            });
            AddRecipe(WrenchId, new List<RecipeItem>
            {
                Material("wrench"), Fluid(100), Material("bundleofwires"), Material("bundleofwires"), Material("bundleofwires"),
                Material("scrappanel"), Material("scrappanel"), Material("scrappanel"),
                Material("scraptube"), Material("scraptube"), Material("scraptube"), Material("string"), Material("string"),
                Material("canvas"), Material("canvas"), Material(DragonId), Tool("hammering"), Tool("cutting")
            }, 14);
        }
    }
}
