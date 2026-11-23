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
    /// 第一件装备：实验性钛合金钻机（Experimental Ti-alloy drill）——重型钻机的升级版。
    /// CCL 前置、多人兼容、DDEN L1 软接入（实验性 + 中英双语）。
    /// 中英文本走 CCL 的 zh-CN.json / EN.json（与 DLL 同目录，CCL 自动加载）。
    /// </summary>
    [BepInPlugin(Guid, "DS Tool Extension", "1.0.2")]
    [BepInDependency("net.cucorelib", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.a4kk.enhancedtools", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.dstool.extension";
        public const string ItemId = "ExperimentalTi-alloydrill";

        internal const string FallbackName = "实验性钛合金钻机";
        internal const string FallbackDescription = "重型钻机的钛合金升级版本，钻头自加热，效率更高";

        internal static Plugin Instance;
        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;

        internal static void LogInfo(string message) => Log?.LogInfo(message);
        internal static void LogWarning(string message) => Log?.LogWarning(message);

        // 与原版重型钻机一致的表现字段
        private const float KnockBack = 180f;
        private const float RotateAmount = 6f;
        private const float Volume = 0.8f;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            try
            {
                Enabled = Config.Bind("General", "Enabled", true, "启用 DS 工具扩展。");
                bool hasET = Chainloader.PluginInfos.ContainsKey("com.a4kk.enhancedtools");
                if (Enabled.Value)
                {
                    RegisterItem();
                    RegisterRecipe(hasET);
                }
                new Harmony(Guid).PatchAll(typeof(Plugin));
                DdenBridge.DdenDocs();
                Logger.LogInfo($"[DS Tool Extension] 已加载：实验性钛合金钻机（配方={(hasET ? "ET 版" : "普通版")}）。");
            }
            catch (Exception ex)
            {
                Logger.LogError("[DS Tool Extension] 初始化失败：" + ex);
            }
        }

        private void RegisterItem()
        {
            Sprite sprite = AssetLoader.LoadSpriteFromPluginFolder(this, "images/Experimental Ti-alloy drill.png", 8f);
            if (sprite == null)
                Logger.LogWarning("[DS Tool Extension] 未找到图标 'images/Experimental Ti-alloy drill.png'（应与 DLL 同目录）。");

            var info = new CustomItemInfo
            {
                fullName = FallbackName,
                description = FallbackDescription,
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
                useAction = delegate (Body body, Item item)
                {
                    if (item.condition > 0f && body.Attack(new AttackInfo
                    {
                        damage = 125f,
                        structuralDamage = 150f,
                        attackCooldownMult = 0.3f,
                        distance = 4.5f,
                        knockBack = KnockBack,
                        cooldown = 0.12f,
                        attackAnim = null,
                        staminaUse = 0f,
                        piercing = false,
                        swingSounds = new string[1],
                        volume = Volume,
                        physicalSwing = false,
                        rotateAmount = RotateAmount,
                        doAttackAnim = false
                    }, 0))
                    {
                        // 重型钻机的一半耗电（0.0023809525 / 2）
                        item.battery.DrainCharge(0.00119047625f);
                    }
                }
            };

            info.AddSpawnComponent<DrillLoopSound>();
            ItemRegistry.Register(ItemId, info, sprite);
        }

        /// <summary>
        /// 双配方：装 EnhancedTools 用工程化/可改造品质，否则用可捶打/可切割品质。
        /// 材料与制作等级（INT 17，同增强钛镐）一致。
        /// </summary>
        private static void RegisterRecipe(bool hasET)
        {
            var recipe = new Recipe
            {
                INT = 17,
                category = (Recipes.RecipeCategory)1,
                result = new RecipeResult
                {
                    id = ItemId,
                    amount = 1,
                    isLiquid = false,
                    resultCondition = 1f
                },
                items = new List<RecipeItem>
                {
                    new RecipeItem(0f) { specific = true, specificId = "heavydrill" },
                    new RecipeItem(0f) { specific = true, specificId = "processedcopper" },
                    new RecipeItem(0.25f) { specific = true, specificId = "mediumbattery" },
                    new RecipeItem(0.8f) { specific = true, specificId = "circuitboard" },
                    new RecipeItem(0.8f) { specific = true, specificId = "circuitboard" },
                    new RecipeItem(0f) { specific = true, specificId = "bundleofwires" },
                    new RecipeItem(0f) { specific = true, specificId = "bundleofwires" },
                    new RecipeItem(0f) { specific = true, specificId = "bundleofwires" },
                    new RecipeItem(0f) { specific = true, specificId = "nails" },
                    new RecipeItem(0f) { specific = true, specificId = "scrapcube" },
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

        // CCL 的 Locale.LoadLanguage 后缀会先合并 zh-CN.json / EN.json 到 Locale.currentLang；
        // 这里再（priority 低 = 后执行）把物品文本更新为当前语言，切换语言即时生效。
        [HarmonyPatch(typeof(Locale), "LoadLanguage")]
        [HarmonyPriority(100)]
        [HarmonyPostfix]
        private static void Locale_LoadLanguage_Postfix()
        {
            try
            {
                if (Item.GlobalItems == null || !Item.GlobalItems.TryGetValue(ItemId, out var info) || info == null) return;
                info.fullName = LocaleRegistry.Get("item", ItemId, FallbackName);
                info.description = LocaleRegistry.Get("item", ItemId + "dsc", FallbackDescription);
            }
            catch (Exception ex)
            {
                Instance?.Logger.LogWarning("[DS Tool Extension] 语言同步失败：" + ex.Message);
            }
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
                    "启用后新增实验性钛合金钻机。",
                    "装好后在物品/合成里出现。",
                    "是 · 尚需游戏内验证",
                    "只注册一个物品与一个配方，开销可忽略。", false
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
                case "启用后新增实验性钛合金钻机。": return "Adds the Experimental Ti-alloy drill when enabled.";
                case "装好后在物品/合成里出现。": return "Appears in items/crafting once installed.";
                case "是 · 尚需游戏内验证": return "Yes · needs in-game verification";
                case "只注册一个物品与一个配方，开销可忽略。": return "Registers one item and one recipe; negligible cost.";
                default: return zh;
            }
        }
    }
}
