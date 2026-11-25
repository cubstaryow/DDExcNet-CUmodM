using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using CUCoreLib.Data;
using CUCoreLib.Helpers;
using CUCoreLib.Registries;

namespace DsToolExtension
{
    /// <summary>
    /// DS Tool Extension（DS 工具扩展）
    /// 第一件：实验性钛合金钻机（ExperimentalTi-alloydrill）——重型钻机升级版；
    /// 第二件：实验性等离子束发射器（Experimentalplasmaemitter）——等离子切割器升级版。
    /// CCL 前置、多人兼容、DDEN L1 软接入（实验性 + 中英双语）。
    /// </summary>
    [BepInPlugin(Guid, "DS Tool Extension", "1.2.10")]
    [BepInDependency("net.cucorelib", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.a4kk.enhancedtools", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.dstool.extension";
        public const string DrillItemId = "ExperimentalTi-alloydrill";
        public const string PlasmaItemId = "Experimentalplasmaemitter";
        public const string LegPackItemId = "Experimentalspeciallegpack";

        internal const string DrillFallbackName = "实验性钛合金钻机";
        internal const string DrillFallbackDescription = "重型钻机的钛合金升级版本，钻头自加热，效率更高";
        internal const string PlasmaFallbackName = "实验性等离子束发射器";
        internal const string PlasmaFallbackDescription = "去掉了等离子流的限制模块，采用钛合金重做散热系统以达到在相同时间内可以射出更多的等离子束";
        internal const string LegPackFallbackName = "实验性特种腿包";
        internal const string LegPackFallbackDescription = "对特种腿包进行扩展升级，并且在此基础上加入了实验性电子组件，能在关键时刻提供保护";

        internal static Plugin Instance;
        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> DebugLog;

        internal static void LogInfo(string message) => Log?.LogInfo(message);
        internal static void LogWarning(string message) => Log?.LogWarning(message);

        // 钻机表现字段（沿用重型钻机）
        private const float DrillKnockBack = 180f;
        private const float DrillRotateAmount = 6f;
        private const float DrillVolume = 0.8f;

        private float _nextSweep;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            try
            {
                Enabled = Config.Bind("DS Tool Extension", "Enabled", true, "启用 DS 工具扩展。");
                DebugLog = Config.Bind("DS Tool Extension", "DebugLog", false, "输出循环音效等调试日志（默认关闭）。");
                bool hasET = Chainloader.PluginInfos.ContainsKey("com.a4kk.enhancedtools");
                if (Enabled.Value)
                {
                    RegisterDrill();
                    RegisterPlasmaEmitter();
                    RegisterLegPack();
                    RegisterDrillRecipe(hasET);
                    RegisterPlasmaRecipe(hasET);
                    RegisterLegPackRecipe(hasET);
                }
                new Harmony(Guid).PatchAll(typeof(Plugin));
                DdenBridge.DdenDocs();
                Logger.LogInfo($"[DS Tool Extension] 已加载：钻机 + 等离子束发射器（钻机配方={(hasET ? "ET 版" : "普通版")}）。");
            }
            catch (Exception ex)
            {
                Logger.LogError("[DS Tool Extension] 初始化失败：" + ex);
            }
        }

        private void Update()
        {
            // 兜底：物品 Parent 变化未触发时，每 1 秒重新检查不在槽位里的钻机音效组件。
            if (UnityEngine.Time.unscaledTime < _nextSweep) return;
            _nextSweep = UnityEngine.Time.unscaledTime + 1f;
            DrillLoopSound.SweepAll();
        }

        private void RegisterDrill()
        {
            Sprite sprite = AssetLoader.LoadSpriteFromPluginFolder(this, "images/Experimental Ti-alloy drill.png", 8f);
            if (sprite == null)
                Logger.LogWarning("[DS Tool Extension] 未找到图标 'images/Experimental Ti-alloy drill.png'（应与 DLL 同目录）。");

            var info = new CustomItemInfo
            {
                fullName = DrillFallbackName,
                description = DrillFallbackDescription,
                category = "tool",
                tags = "tool",
                weight = 4f,
                value = 60,
                usable = true,
                usableWithLMB = true,
                usableOnLimb = false,
                destroyAtZeroCondition = false,
                autoAttack = true,
                onlyHoldInHands = true,
                slotRotation = 0f,
                rec = new Recognition(12),
                qualities = new List<CraftingQuality> { new CraftingQuality("hammering", 100f) },
                Battery = new BatteryProperties
                {
                    Preset = BatteryItem.BatteryPreset.Large,
                    MaxCharge = 300f,
                    SpawnWithBattery = true
                },
                SpriteScale = 1f,
                DropPool = DropPool.AllTraders | DropPool.ContainerCrate | DropPool.DropCapsule | DropPool.CapsuleContainer,
                SpawnFrequency = 1,
                useAction = delegate (Body body, Item item)
                {
                    if (item.condition > 0f && body.Attack(new AttackInfo
                    {
                        damage = 125f,
                        structuralDamage = 150f,
                        attackCooldownMult = 0.3f,
                        distance = 4.5f,
                        knockBack = DrillKnockBack,
                        cooldown = 0.12f,
                        attackAnim = null,
                        staminaUse = 0f,
                        piercing = false,
                        swingSounds = new string[1],
                        volume = DrillVolume,
                        physicalSwing = false,
                        rotateAmount = DrillRotateAmount,
                        doAttackAnim = false
                    }, 0))
                    {
                        // 重型钻机的一半耗电（0.0023809525 / 2）
                        item.battery.DrainCharge(0.00119047625f);
                    }
                }
            };

            info.AddSpawnComponent<DrillLoopSound>();
            ItemRegistry.Register(DrillItemId, info, sprite);
        }

        private void RegisterPlasmaEmitter()
        {
            Sprite sprite = AssetLoader.LoadSpriteFromPluginFolder(this, "images/Experimental plasma emitter.png", 8f);
            if (sprite == null)
                Logger.LogWarning("[DS Tool Extension] 未找到图标 'images/Experimental plasma emitter.png'（应与 DLL 同目录）。");

            var info = new CustomItemInfo
            {
                fullName = PlasmaFallbackName,
                description = PlasmaFallbackDescription,
                category = "tool",
                tags = "tool",
                weight = 2.5f,
                value = 60,
                usable = true,
                usableWithLMB = true,
                usableOnLimb = true,
                destroyAtZeroCondition = false,
                autoAttack = true,
                onlyHoldInHands = true,
                slotRotation = 0f,
                rec = new Recognition(12),
                Battery = new BatteryProperties
                {
                    Preset = BatteryItem.BatteryPreset.Large,
                    MaxCharge = 300f,
                    SpawnWithBattery = true
                },
                SpriteScale = 1f,
                DropPool = DropPool.AllTraders | DropPool.ContainerCrate | DropPool.DropCapsule | DropPool.CapsuleContainer,
                SpawnFrequency = 1,
                useAction = delegate (Body body, Item item)
                {
                    if (item.condition > 0f && body.Attack(new AttackInfo
                    {
                        damage = 30f,
                        structuralDamage = 30f,
                        attackCooldownMult = 0.4f,
                        distance = 6.5f,
                        knockBack = 0f,
                        cooldown = 0.07f,
                        attackAnim = Resources.Load<GameObject>("LaserAnim"),
                        staminaUse = 0f,
                        piercing = false,
                        swingSounds = new string[1] { "laser" },
                        volume = 0.8f,
                        physicalSwing = false,
                        rotateAmount = 2f,
                        doAttackAnim = false,
                        metalMoreDamage = true
                    }, 0))
                    {
                        // 等离子切割器的一半耗电（0.001 / 2）
                        item.battery.DrainCharge(0.0005f);
                    }
                },
                useLimbAction = delegate (Limb limb, Item item)
                {
                    if (item.condition > 0f)
                    {
                        item.battery.DrainCharge(0.001f);
                        limb.skinHealth -= 12f;
                        limb.bleedAmount *= 0.2f;
                        limb.pain += 45f;
                        Sound.Play("laser", limb.transform.position);
                    }
                }
            };

            ItemRegistry.Register(PlasmaItemId, info, sprite);
        }

        private void RegisterLegPack()
        {
            Sprite sprite = AssetLoader.LoadSpriteFromPluginFolder(this, "images/Experimental special leg pack.png", 8f);
            if (sprite == null)
                Logger.LogWarning("[DS Tool Extension] 未找到图标 'images/Experimental special leg pack.png'（应与 DLL 同目录）。");

            var info = new CustomItemInfo
            {
                fullName = LegPackFallbackName,
                description = LegPackFallbackDescription,
                category = "custom",
                tags = "battery",
                rec = new Recognition(14),
                weight = 0.7f,
                value = 45,
                decayMinutes = 240f,
                decayInfo = 5,
                destroyAtZeroCondition = false,
                usable = false,
                Container = new ContainerProperties
                {
                    Capacity = 11.5f,
                    MaxWeightPerItem = 4.5f,
                    EncumbranceReduction = 0.05f
                },
                Battery = new BatteryProperties
                {
                    Preset = BatteryItem.BatteryPreset.Large,
                    MaxCharge = 300f,
                    SpawnWithBattery = true
                },
                WornSprite = sprite,
                WornSpriteOffset = new Vector2(0f, -0.3f),
                wearable = true,
                desiredWearLimb = "ThighF",
                wearSlotId = "thigh",
                wearableArmor = 0.35f,
                wearableHitDurabilityLossMultiplier = 0.15f,
                wearableVisualOffset = 5,
                SpriteScale = 0.45f,
                DropPool = DropPool.AllTraders | DropPool.ContainerCrate | DropPool.DropCapsule | DropPool.CapsuleContainer,
                SpawnFrequency = 1,
                InventoryIconScale = 1.5f
            };

            info.AddSpawnComponent<BatteryEncumbranceReduction>();
            ItemRegistry.Register(LegPackItemId, info, sprite);
        }

        /// <summary>
        /// 钻机双配方：装 EnhancedTools 用工程化/可改造品质，否则用可捶打/可切割品质。
        /// 材料：重型钻机、厚钛板（titaniumslab）、中型电池（≥25%）、电路板（≥80%）×2、
        /// 一捆导线 ×3、铁钉、钛棒（titaniumrod）、生化流体 50mL。
        /// </summary>
        private static void RegisterDrillRecipe(bool hasET)
        {
            var recipe = new Recipe
            {
                INT = 17,
                category = (Recipes.RecipeCategory)1,
                result = new RecipeResult
                {
                    id = DrillItemId,
                    amount = 1,
                    isLiquid = false,
                    resultCondition = 1f
                },
                items = new List<RecipeItem>
                {
                    new RecipeItem(0f) { specific = true, specificId = "heavydrill" },
                    new RecipeItem(0f) { specific = true, specificId = "titaniumslab" },
                    new RecipeItem(0.25f) { specific = true, specificId = "mediumbattery" },
                    new RecipeItem(0.8f) { specific = true, specificId = "circuitboard" },
                    new RecipeItem(0.8f) { specific = true, specificId = "circuitboard" },
                    new RecipeItem(0f) { specific = true, specificId = "bundleofwires" },
                    new RecipeItem(0f) { specific = true, specificId = "bundleofwires" },
                    new RecipeItem(0f) { specific = true, specificId = "bundleofwires" },
                    new RecipeItem(0f) { specific = true, specificId = "nails" },
                    new RecipeItem(0f) { specific = true, specificId = "titaniumrod" },
                    new RecipeItem(50f) { specific = true, specificId = "biochem", isLiquid = true },
                    hasET
                        ? new RecipeItem(0f) { quality = new CraftingQuality("engineering", 1f), destroyItem = false }
                        : new RecipeItem(0f) { quality = new CraftingQuality("hammering", 1f), destroyItem = false },
                    hasET
                        ? new RecipeItem(0f) { quality = new CraftingQuality("modification", 4f), destroyItem = false }
                        : new RecipeItem(0f) { quality = new CraftingQuality("cutting", 1f), destroyItem = false }
                }
            };
            RecipeRegistry.Register(recipe);
        }

        /// <summary>
        /// 等离子束发射器配方（INT 17，同钻机）：等离子切割器、加工铜、中型电池（≥25%）、
        /// 电路板（≥80%）、一捆导线 ×4、铁钉 ×2、厚钛板、钛板 ×2、生化流体 50mL；
        /// 普通版用可捶打/可切割，ET 版用工程化/可改造。
        /// </summary>
        private static void RegisterPlasmaRecipe(bool hasET)
        {
            var recipe = new Recipe
            {
                INT = 17,
                category = (Recipes.RecipeCategory)1,
                result = new RecipeResult
                {
                    id = PlasmaItemId,
                    amount = 1,
                    isLiquid = false,
                    resultCondition = 1f
                },
                items = new List<RecipeItem>
                {
                    new RecipeItem(0f) { specific = true, specificId = "plasmacutter" },
                    new RecipeItem(0f) { specific = true, specificId = "processedcopper" },
                    new RecipeItem(0.25f) { specific = true, specificId = "mediumbattery" },
                    new RecipeItem(0.8f) { specific = true, specificId = "circuitboard" },
                    new RecipeItem(0f) { specific = true, specificId = "bundleofwires" },
                    new RecipeItem(0f) { specific = true, specificId = "bundleofwires" },
                    new RecipeItem(0f) { specific = true, specificId = "bundleofwires" },
                    new RecipeItem(0f) { specific = true, specificId = "bundleofwires" },
                    new RecipeItem(0f) { specific = true, specificId = "nails" },
                    new RecipeItem(0f) { specific = true, specificId = "nails" },
                    new RecipeItem(0f) { specific = true, specificId = "titaniumslab" },
                    new RecipeItem(0f) { specific = true, specificId = "titaniumsheet" },
                    new RecipeItem(0f) { specific = true, specificId = "titaniumsheet" },
                    new RecipeItem(50f) { specific = true, specificId = "biochem", isLiquid = true },
                    hasET
                        ? new RecipeItem(0f) { quality = new CraftingQuality("engineering", 1f), destroyItem = false }
                        : new RecipeItem(0f) { quality = new CraftingQuality("hammering", 1f), destroyItem = false },
                    hasET
                        ? new RecipeItem(0f) { quality = new CraftingQuality("modification", 4f), destroyItem = false }
                        : new RecipeItem(0f) { quality = new CraftingQuality("cutting", 1f), destroyItem = false }
                }
            };
            RecipeRegistry.Register(recipe);
        }

        /// <summary>
        /// 实验性特种腿包双配方（INT 14）：
        /// ET 版用工程化/可改造/可切割；普通版用可切割/可捶打。
        /// </summary>
        private static void RegisterLegPackRecipe(bool hasET)
        {
            var items = new List<RecipeItem>
            {
                new RecipeItem(0f) { specific = true, specificId = hasET ? "opslegpouch" : "legpouch" },
                new RecipeItem(0f) { specific = true, specificId = "titaniumsheet" }
            };
            if (hasET)
            {
                items.Add(new RecipeItem(0f) { specific = true, specificId = "titaniumsheet" });
                items.Add(new RecipeItem(0f) { specific = true, specificId = "titaniumsheet" });
            }
            else
            {
                for (int i = 0; i < 4; i++) items.Add(new RecipeItem(0f) { specific = true, specificId = "titaniumsheet" });
            }
            items.Add(new RecipeItem(0f) { specific = true, specificId = "titaniumrod" });
            if (!hasET) items.Add(new RecipeItem(0f) { specific = true, specificId = "titaniumrod" });
            items.Add(new RecipeItem(0f) { specific = true, specificId = "processedcopper" });
            if (!hasET) items.Add(new RecipeItem(0f) { specific = true, specificId = "processedcopper" });
            items.Add(new RecipeItem(0.8f) { specific = true, specificId = "circuitboard" });
            if (!hasET) items.Add(new RecipeItem(0.8f) { specific = true, specificId = "circuitboard" });
            items.Add(new RecipeItem(0f) { specific = true, specificId = "bundleofwires" });
            items.Add(new RecipeItem(0f) { specific = true, specificId = "bundleofwires" });
            if (hasET)
            {
                items.Add(new RecipeItem(0f) { specific = true, specificId = "flexiglass" });
                items.Add(new RecipeItem(0f) { specific = true, specificId = "flexiglass" });
            }
            else
            {
                items.Add(new RecipeItem(0f) { specific = true, specificId = "flexiglass" });
                items.Add(new RecipeItem(0f) { specific = true, specificId = "flexiglass" });
                items.Add(new RecipeItem(0f) { specific = true, specificId = "flexiglass" });
            }
            items.Add(new RecipeItem(0f) { specific = true, specificId = "plasticchunk" });
            items.Add(new RecipeItem(0f) { specific = true, specificId = "plasticchunk" });
            items.Add(new RecipeItem(hasET ? 35f : 45f) { specific = true, specificId = "biochem", isLiquid = true });
            if (hasET)
            {
                items.Add(new RecipeItem(0f) { quality = new CraftingQuality("engineering", 1f), destroyItem = false });
                items.Add(new RecipeItem(0f) { quality = new CraftingQuality("modification", 4f), destroyItem = false });
                items.Add(new RecipeItem(0f) { quality = new CraftingQuality("cutting", 1f), destroyItem = false });
            }
            else
            {
                items.Add(new RecipeItem(0f) { quality = new CraftingQuality("cutting", 1f), destroyItem = false });
                items.Add(new RecipeItem(0f) { quality = new CraftingQuality("hammering", 1f), destroyItem = false });
            }

            RecipeRegistry.Register(new Recipe
            {
                INT = 14,
                category = (Recipes.RecipeCategory)1,
                result = new RecipeResult
                {
                    id = LegPackItemId,
                    amount = 1,
                    isLiquid = false,
                    resultCondition = 1f
                },
                items = items
            });
        }

        // CCL 的 Locale.LoadLanguage 后缀会先合并 zh-CN.json / EN.json 到 Locale.currentLang；
        // 这里再（priority 低 = 后执行）把物品文本更新为当前语言，切换语言即时生效。
        [HarmonyPatch(typeof(Locale), "LoadLanguage")]
        [HarmonyPriority(100)]
        [HarmonyPostfix]
        private static void Locale_LoadLanguage_Postfix()
        {
            try
            {
                UpdateItemText(DrillItemId, DrillFallbackName, DrillFallbackDescription);
                UpdateItemText(PlasmaItemId, PlasmaFallbackName, PlasmaFallbackDescription);
                UpdateItemText(LegPackItemId, LegPackFallbackName, LegPackFallbackDescription);
            }
            catch (Exception ex)
            {
                Instance?.Logger.LogWarning("[DS Tool Extension] 语言同步失败：" + ex.Message);
            }
        }

        private static void UpdateItemText(string id, string fallbackName, string fallbackDescription)
        {
            if (Item.GlobalItems == null || !Item.GlobalItems.TryGetValue(id, out var info) || info == null) return;
            info.fullName = LocaleRegistry.Get("item", id, fallbackName);
            info.description = LocaleRegistry.Get("item", id + "dsc", fallbackDescription);
        }
    }

    // DDEN CUmodM 的 L1 接入（零引用，反射约定）：实验性 + 中英双语
    public static class DdenBridge
    {
        private static string _language = "zh-CN";

        public static ConfigFile DdenConfig => Plugin.Instance != null ? Plugin.Instance.Config : null;

        public static string DdenTitle => "DS Tool Extension";

        public static string DdenSummary =>
            Plugin.Instance == null ? Ts("未加载", "Not loaded") : Ts("运行中", "Running");

        public static void DdenSetLanguage(string language)
        {
            _language = string.Equals(language, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "zh-CN";
        }

        public static string DdenLocalize(string original, string language)
        {
            if (string.IsNullOrEmpty(original)) return original;
            if (!string.Equals(language, "en", StringComparison.OrdinalIgnoreCase)) return original;
            return DdenText.En(original);
        }

        public static void DdenDocs()
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "DDEN.Shared")?.GetType("DDEN.Shared.DdenDocs");
            var method = type?.GetMethod("Add", new[] { typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(bool) });
            if (method == null || DdenConfig == null) return;
            foreach (var pair in DdenConfig)
                method.Invoke(null, new object[]
                {
                    pair.Key.Section, pair.Key.Key, pair.Value.Description.Description,
                    "启用后新增实验性钛合金钻机与实验性等离子束发射器。",
                    "装好后在物品/合成里出现。",
                    "是 · 尚需游戏内验证",
                    "只注册两个物品与一个配方，开销可忽略。", false
                });
        }

        private static string Ts(string zh, string en) => _language == "en" ? en : zh;
    }

    internal static class DdenText
    {
        internal static string En(string zh)
        {
            if (zh == null) return null;
            switch (zh)
            {
                case "启用 DS 工具扩展。": return "Enable DS Tool Extension.";
                case "未加载": return "Not loaded";
                case "运行中": return "Running";
                case "启用后新增实验性钛合金钻机与实验性等离子束发射器。": return "Adds the Experimental Ti-alloy drill and the Experimental plasma emitter when enabled.";
                case "装好后在物品/合成里出现。": return "Appears in items/crafting once installed.";
                case "是 · 尚需游戏内验证": return "Yes · needs in-game verification";
                case "只注册两个物品与一个配方，开销可忽略。": return "Registers two items and one recipe; negligible cost.";
                default: return zh;
            }
        }
    }
}
