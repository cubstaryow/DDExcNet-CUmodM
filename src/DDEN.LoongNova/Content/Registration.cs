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
    internal static partial class Registration
    {
        internal const string ChargeGunId = "dden.loongnova.charge-prototype";
        internal static bool IsGun(string id) => id == GunId || id == ChargeGunId;
        internal const string GunId = "dden.loongnova.dragon-scorch";
        internal const string CellId = "dden.loongnova.dragon-breath-cell";
        internal const string BeltId = "dden.loongnova.gravity-belt";
        internal const string MatterId = "dden.loongnova.dragon-breath-matter";
        internal const string DragonId = "dden.loongnova.dragon";
        internal const string PickaxeId = "dden.loongnova.rock-dragon-pickaxe";
        internal const string WrenchId = "dden.loongnova.dragon-claw-wrench";
        internal const string DragonQuality = "loongnova.dragonization";
        internal static BatteryInfo CellStats { get; private set; }
        internal static Sprite CannonSprite, BioRoundSprite, DemolitionRoundSprite;
        private static readonly Dictionary<string, Sprite> RoundIcons = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> RoundNames = new Dictionary<string, string>(StringComparer.Ordinal);
        internal static Sprite RoundSprite(string id) => id != null && RoundIcons.TryGetValue(id, out var sprite) ? sprite : null;
        internal static string RoundName(string id) => id != null && RoundNames.TryGetValue(id, out var key) ? Text(key) : id ?? "";
        internal static Sprite GunSprite { get; private set; }
        internal static Sprite GunLoadedSprite { get; private set; }
        internal static Sprite WingFoldedSprite { get; private set; }
        internal static Sprite WingOpenSprite { get; private set; }
        private static JObject _text;
        internal static string Text(string key) => _text?.Value<string>(key) ?? key;

        private static Stream Resource(string path) => ContentResources.Open(typeof(Registration).Assembly, path);

        internal static void Register()
        {
            using (var reader = new StreamReader(Resource("Content.json"))) _text = JObject.Parse(reader.ReadToEnd());
            var gun = new CustomItemInfo
            {
                fullName = Text("gunName"), description = Text("gunDescription"),
                category = "utility", tags = "gun,cangetwet", weight = 2f, value = 48,
                slotRotation = -90, onlyHoldInHands = true, usable = true, usableWithLMB = true,
                autoAttack = true, destroyAtZeroCondition = false, rec = new Recognition(10),
                DropPool = DropPool.None, WorldSpawnPerChunk = 0,
                Gun = new GunProperties
                {
                    AmmoType = GunScript.AmmoType.Rifle, FiringMode = GunScript.FiringMode.Auto,
                    FeedType = GunScript.FeedType.Mag, MagCapacity = 100, KnockBack = 2,
                    StructureDamage = 625, AnimalDamage = 225, Loudness = 2.5f,
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
                DropPool = DropPool.None, WorldSpawnPerChunk = 0
            };
            CellStats = new BatteryInfo { maxCharge = EnergyState.Capacity };
            foreach (var field in typeof(ItemInfo).GetFields(BindingFlags.Instance | BindingFlags.Public))
                field.SetValue(CellStats, field.GetValue(cell));
            CellStats.SetTags();
            GunSprite = LoadSprite("dragon-scorch-rifle-v2.png", "rifle", widthScale: 1.2f);
            GunLoadedSprite = LoadSprite("dragon-scorch-rifle.png", "rifle", widthScale: 1.2f);
            var originalGun = Resources.Load<GameObject>("rifle").GetComponent<GunScript>();
            gun.Gun.BarrelOffset = originalGun.barrel.localPosition;
            gun.Gun.NormalSprite = gun.Gun.RackedSprite = GunLoadedSprite;
            gun.Gun.NormalSpriteNoMag = gun.Gun.RackedSpriteNoMag = GunSprite;
            using (ItemRegistry.BeginOwnerRegistration(LoongNovaPlugin.Guid))
            {
                ItemRegistry.Register(GunId, (ItemInfo)gun, GunSprite, 0);
                ItemRegistry.Register(CellId, (ItemInfo)cell, LoadSprite("dragon-breath-cell.png", "riflemagazine"), 0);
                RegisterEquipment();
                RegisterChargeGun(originalGun);
                RegisterMedical();
                RegisterSupplyCrates();
            }
            using (RecipeRegistry.BeginOwnerRegistration(LoongNovaPlugin.Guid)) RegisterRecipes();
        }

        private static void RegisterChargeGun(GunScript reference)
        {
            CannonSprite = LoadSprite("charge-sniper-cannon.png", "rifle", pivot: new Vector2(0.25f, 0.4f), pixelWidth: 64, worldLength: 5);
            BioRoundSprite = LoadSprite("bio-erasure-round.png", "riflemagazine", pixelWidth: 12, worldLength: 1, preserveFrame: true);
            DemolitionRoundSprite = LoadSprite("demolition-round-v2.png", "riflemagazine", pixelWidth: 12, worldPixelsPerUnit: 36, preserveFrame: true);
            var prototype = new CustomItemInfo
            {
                fullName = Text("chargeName"), description = Text("chargeDescription"),
                category = "utility", tags = "gun,cangetwet", weight = 3f, value = 40,
                slotRotation = -90, onlyHoldInHands = true, usable = true, usableWithLMB = true,
                autoAttack = true, destroyAtZeroCondition = false, rec = new Recognition(10),
                DropPool = DropPool.None, WorldSpawnPerChunk = 0,
                Gun = new GunProperties
                {
                    AmmoType = GunScript.AmmoType.Rifle, FiringMode = GunScript.FiringMode.Auto,
                    FeedType = GunScript.FeedType.Mag, MagCapacity = 1, KnockBack = 6,
                    StructureDamage = 0, AnimalDamage = 0, Loudness = 2.5f,
                    DesiredGasTime = 0.5f, ShotsPerFire = 1, ConditionLossPerShot = 0.2f,
                    BarrelOffset = new Vector2(CannonSprite.bounds.max.x, CannonSprite.bounds.size.y * 0.18f), BeforeAttack = Trigger,
                    NormalSprite = CannonSprite, RackedSprite = CannonSprite,
                    NormalSpriteNoMag = CannonSprite, RackedSpriteNoMag = CannonSprite
                }
            };
            prototype.AddSpawnComponent<DragonGun>();
            prototype.AddSpawnComponent<CannonSight>();
            prototype.AddSpawnComponent<CannonChargeAudio>();
            ItemRegistry.Register(ChargeGunId, (ItemInfo)prototype, CannonSprite, 0);
            RegisterRound(CannonAmmoState.BioId, "bio", BioRoundSprite, new CannonRound(1, 750, 1, 0, 4, 1));
            RegisterRound(CannonAmmoState.DemolitionId, "demo", DemolitionRoundSprite, new CannonRound(200, 1, 20, 20, 5, 1));
            var explosiveIcon = LoadSprite("explosive-round.png", "riflemagazine", pixelWidth: 12, worldPixelsPerUnit: 36, preserveFrame: true);
            RegisterRound(CannonAmmoState.ExplosiveId, "explosive", explosiveIcon, new CannonRound(500, 400, 1, 0, 1, 1, 12), 0.5f);
            RegisterRound(CannonAmmoState.DrillId, "drill", LoadSprite("drill-round.png", "riflemagazine", pixelWidth: 12, worldPixelsPerUnit: 36, preserveFrame: true),
                new CannonRound(0, 0, 1, 0, 1, effect: CannonEffect.Drill, range: 150, size: 4), value: 30);
            RegisterRound(CannonAmmoState.DrainId, "drain", LoadSprite("spatial-drain-round.png", "riflemagazine", pixelWidth: 12, worldPixelsPerUnit: 36, preserveFrame: true),
                new CannonRound(0, 0, 1, 0, 1, effect: CannonEffect.Drain), value: 20);
            RegisterRound(CannonAmmoState.BreachId, "breach", LoadSprite("emp-breach-round.png", "riflemagazine", pixelWidth: 12, worldPixelsPerUnit: 36, preserveFrame: true),
                new CannonRound(0, 0, 1, 0, 1, effect: CannonEffect.Breach, size: 25), value: 25);
            RegisterRound(CannonAmmoState.RevivalId, "revival", LoadSprite("revival-round.png", "riflemagazine", pixelWidth: 12, worldPixelsPerUnit: 36, preserveFrame: true),
                new CannonRound(0, 0, 1, 0, 1, effect: CannonEffect.Revive), value: 60, recognition: 25);
            // Twelve-pixel caliber at 36 PPU matches the unchanged 12x36 bio cartridge.
            var carrier = LoadSprite("universal-ammo-carrier.png", "casing", pixelWidth: 12, worldPixelsPerUnit: 36, preserveFrame: true);
            ItemRegistry.Register(CannonAmmoState.CarrierId, (ItemInfo)new CustomItemInfo
            {
                fullName = Text("carrierName"), description = Text("carrierDescription"),
                category = "custom", tags = "material", weight = 0.1f, value = 0, untradeable = true,
                DropPool = DropPool.None, WorldSpawnPerChunk = 0, destroyAtZeroCondition = false
            }, carrier, 0);
            RoundIcons[CannonAmmoState.CarrierId] = carrier;
            RoundNames[CannonAmmoState.CarrierId] = "carrierName";
        }
        private static void RegisterRound(string id, string key, Sprite sprite, CannonRound parameters, float weight = 0.3f, int value = 10, int recognition = 5)
        {
            ItemRegistry.Register(id, (ItemInfo)new CustomItemInfo
            {
                fullName = Text(key + "Name"), description = Text(key + "Description"),
                CustomData = new Dictionary<string, object> { [CannonRoundRegistry.DataKey] = parameters.Parameters() },
                category = "ammo", tags = "ammo", weight = weight, value = value, rec = new Recognition(recognition),
                destroyAtZeroCondition = false, DropPool = DropPool.None, WorldSpawnPerChunk = 0
            }, sprite, 0);
            CannonRoundRegistry.Register(id, parameters.Parameters());
            RoundIcons[id] = sprite; RoundNames[id] = key + "Name";
        }
        private static Sprite NativeSprite(string id) => Resources.Load<GameObject>(id).GetComponent<SpriteRenderer>().sprite;

        private static void RegisterEquipment()
        {
            LocaleRegistry.RegisterCraftingQuality(DragonQuality, Text("dragonQuality"));

            var prefab = Resources.Load<GameObject>("belt");
            var container = prefab.GetComponent<Container>();
            var swap = prefab.GetComponent<WearableSpriteSwap>();
            var beltSprite = LoadSprite("gravity-belt-v2.png", "belt", worldLength: 3);
            var waistReference = swap != null && swap.sprites.Count > 0 ? swap.sprites[0] : NativeSprite("belt");
            WingFoldedSprite = LoadSprite("dragon-wing-folded-side.png", "belt", waistReference,
                new Vector2(0.8f, 0.9f), widthScale: 1.05f, pixelWidth: 32);
            WingOpenSprite = LoadSprite("dragon-wing-open-side.png", "belt", waistReference,
                new Vector2(0.8f, 0.58f), widthScale: 2.1f, pixelWidth: 64);
            var belt = new CustomItemInfo
            {
                fullName = Text("beltName"), description = Text("beltDescription"),
                category = "container", weight = 1.5f, value = 45, rec = new Recognition(10),
                wearable = true, desiredWearLimb = "DownTorso", wearSlotId = "belt", wearableVisualOffset = 2,
                wearableHitDurabilityLossMultiplier = 0, destroyAtZeroCondition = false,
                decayInfo = 0, rotSpeed = 0, usable = false, DropPool = DropPool.None, WorldSpawnPerChunk = 0,
                Container = new ContainerProperties
                {
                    Capacity = 6, MaxWeightPerItem = container.maxWeightPerItem,
                    EncumbranceReduction = 0.6f, ItemsVisible = container.itemsVisible,
                    TagRestriction = container.tagRestriction
                },
                WornSprite = LoadSprite("dragon-wing-waist-side.png", "belt", waistReference),
                MultiWornSprites = new Dictionary<string, Sprite>
                {
                    ["UpTorso"] = WingFoldedSprite
                },
                MultiWornSpriteOffsets = new Dictionary<string, Vector2> { ["UpTorso"] = new Vector2(-0.35f, 0.25f) }
            };
            // Authenticated 1000-capacity socket; native BatteryItem slots cap at 300. New belts stay empty.
            belt.AddSpawnComponent<GravityBelt>();
            ItemRegistry.Register(BeltId, (ItemInfo)belt, beltSprite, 0);
            ItemRegistry.Register(MatterId, (ItemInfo)new CustomItemInfo
            {
                fullName = Text("matterName"), description = Text("matterDescription"),
                category = "custom", tags = "material", weight = 0.5f, value = 8,
                usable = false, destroyAtZeroCondition = true, DropPool = DropPool.None, WorldSpawnPerChunk = 0
            }, LoadSprite("dragon-breath-matter.png", "turbulentcrystalshard"), 0);
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
            }, LoadSprite("dragon-claw-wrench.png", "wrench", worldLength: 3), 0);

            ItemRegistry.Register(PickaxeId, (ItemInfo)new CustomItemInfo
            {
                fullName = Text("pickaxeName"), description = Text("pickaxeDescription"),
                category = "tool", tags = "tool", weight = 1.5f, value = 20,
                slotRotation = -90, rec = new Recognition(5), usable = true, usableWithLMB = true,
                autoAttack = true, destroyAtZeroCondition = true, rotSpeed = 0,
                useAction = UsePickaxe, DropPool = DropPool.None, WorldSpawnPerChunk = 0
            }, LoadSprite("rock-dragon-pickaxe.png", "pickaxe", pivot: new Vector2(0.5f, 0.25f), pixelWidth: 40, worldLength: 4), 0);
        }

        internal static void UseWrench(Body body, Item item)
        {
            if (body == null || item == null) return;
            var strike = new AttackInfo
            {
                structuralDamage = 75, damage = 50,
                cooldown = 0.5f, attackCooldownMult = 1f / 3.5f,
                distance = 5.2f, knockBack = 70, staminaUse = 0.5f,
                attackAnim = Resources.Load<GameObject>("SwingAnim"),
                swingSounds = new[] { "BSSwing1", "BSSwing2", "BSSwing3", "BSSwing4" },
                volume = 0.5f, rotateAmount = 10
            };
            if (body.Attack(strike, body.handSlot))
                item.condition = Mathf.Max(0, item.condition - 0.00000125f);
        }

        internal static void UsePickaxe(Body body, Item item)
        {
            if (body == null || item == null || item.condition <= 0) return;
            var strike = new AttackInfo
            {
                structuralDamage = 375, damage = 10,
                cooldown = 0.1f, attackCooldownMult = 1f / 3.5f,
                distance = 5.2f, knockBack = 70, staminaUse = 0.5f,
                attackAnim = Resources.Load<GameObject>("SwingAnim"),
                swingSounds = new[] { "BSSwing1", "BSSwing2", "BSSwing3", "BSSwing4" },
                volume = 0.5f, rotateAmount = 10
            };
            if (body.Attack(strike, body.handSlot)) item.condition = PickaxeWear.Spend(item.condition);
        }
        private static bool Trigger(ref object state, GunScript gun, Body body, Item item)
        {
            if (item != null && item.id == GunId)
                DragonGun.For(item).Request("shoot", body);
            return false;
        }

        private static Sprite LoadSprite(string resource, string reference, Sprite sizeReference = null, Vector2? pivot = null, float widthScale = 1, int pixelWidth = 0, float worldLength = 0, float worldPixelsPerUnit = 0, bool preserveFrame = false)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            using (var stream = Resource("images/" + resource))
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
            if (preserveFrame) { minX = minY = 0; maxX = texture.width - 1; maxY = texture.height - 1; }
            var original = sizeReference ?? Resources.Load<GameObject>(reference).GetComponent<SpriteRenderer>().sprite;
            int width = maxX - minX + 1, height = maxY - minY + 1;
            int targetWidth = pixelWidth > 0 ? pixelWidth : reference == "rifle" ? 64 : reference == "wrench" ? 48 : reference == "belt" ? 32 : 16;
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
            // CCL normalizes item transform scale to one; explicit world length must live in the sprite PPU.
            float ppu = worldPixelsPerUnit > 0 ? worldPixelsPerUnit : worldLength > 0 ? Math.Max(targetWidth, targetHeight) / worldLength
                : targetWidth / (original.bounds.size.x * widthScale);
            return Sprite.Create(compact, new Rect(0, 0, targetWidth, targetHeight), pivot ?? new Vector2(0.5f, 0.5f), ppu);
        }

        private static RecipeItem Material(string id) => new RecipeItem(0) { specific = true, specificId = id };
        private static RecipeItem Fluid(float amount) => new RecipeItem(amount) { specific = true, specificId = "biochem", isLiquid = true };
        private static RecipeItem Tool(string quality) => new RecipeItem(quality == DragonQuality ? DragonizationRules.WearFraction : 0)
        {
            quality = new CraftingQuality(quality, quality == DragonQuality ? DragonizationRules.RequiredUnits : 1),
            destroyItem = false
        };
        private static void AddRecipe(string result, List<RecipeItem> items, int intelligence = 18, bool isRepair = false)
        {
            RecipeRegistry.Register(new Recipe
            {
                INT = intelligence, isRepair = isRepair, category = (Recipes.RecipeCategory)1,
                result = new RecipeResult { id = result, amount = 1, resultCondition = 1 }, items = items
            });
        }
        private static void RegisterRecipes()
        {
            foreach (var recipe in ArsenalRecipes.Create()) RecipeRegistry.Register(recipe);
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
            AddRecipe(CellId, new List<RecipeItem> { depleted, Fluid(160), Material("flammablepowder"), Tool(DragonQuality) }, isRepair: true);
            AddRecipe(BeltId, new List<RecipeItem>
            {
                Material("belt"), Material("turbulentcrystalshard"), Material("gravbag"),
                Material(MatterId), Material(MatterId), Material("canvas"), Material("canvas"),
                Material("titaniumsheet"), Material("titaniumsheet"), Material("titaniumrod"), Tool(DragonQuality)
            });
            AddRecipe(PickaxeId, new List<RecipeItem>
            {
                Material(MatterId), Material(MatterId), Material("titaniumrod"), Material("titaniumrod"), Tool(DragonQuality)
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
