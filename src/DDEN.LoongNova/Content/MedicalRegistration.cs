using System.Collections.Generic;
using CUCoreLib.Data;
using CUCoreLib.Registries;
using UnityEngine;

namespace DDEN.LoongNova
{
    internal static partial class Registration
    {
        internal static Sprite InjectorEmptySprite, InjectorLoadedSprite;
        private static void RegisterMedical()
        {
            var pistol = NativeSprite("pistol");
            var grip = new Vector2(pistol.pivot.x / pistol.rect.width, pistol.pivot.y / pistol.rect.height);
            InjectorEmptySprite = LoadSprite("medical-injector-empty.png", "syringe", pivot: grip, pixelWidth: 36, worldPixelsPerUnit: 18, preserveFrame: true);
            InjectorLoadedSprite = LoadSprite("medical-injector.png", "syringe", pivot: grip, pixelWidth: 36, worldPixelsPerUnit: 18, preserveFrame: true);
            var injector = new CustomItemInfo
            {
                fullName = Text("injectorName"), description = Text("injectorDescription"), category = "medical", tags = "medicine,belttool",
                weight = 0.5f, value = 40, rec = new Recognition(7), usableOnLimb = true, ignoreDepression = true,
                // Registration runs before Item.SetupItems; use the native pistol's fixed rotation without querying its table.
                slotRotation = -90f,
                usable = true, destroyAtZeroCondition = false, useLimbAction = MedicalInjector.Inject, useAction = MedicalInjector.Unload,
                DropPool = DropPool.None, WorldSpawnPerChunk = 0
            };
            injector.AddSpawnComponent<MedicalInjector>();
            ItemRegistry.Register(MedicalCatalog.InjectorId, (ItemInfo)injector, InjectorEmptySprite, 0);
            var regeneration = RegisterAmpoule(MedicalCatalog.RegenerationId, "regeneration", "regeneration-ampoule.png", 40);
            var medical = RegisterAmpoule(MedicalCatalog.MedicalId, "medical", "medical-ampoule.png", 20);
            ItemRegistry.Register(MedicalCatalog.EmptyId, (ItemInfo)new CustomItemInfo
            {
                fullName = Text("emptyAmpouleName"), description = Text("emptyAmpouleDescription"), category = "custom", tags = "material",
                weight = 0.1f, value = 0, untradeable = true, usable = false, destroyAtZeroCondition = false,
                DropPool = DropPool.None, WorldSpawnPerChunk = 0
            }, LoadSprite("ampoule.png", "syringe", pixelWidth: 10, worldPixelsPerUnit: 30, preserveFrame: true), 0);
            MoodleRegistry.RegisterLimb<LimbGrowth>((limb, status) => status.Remaining <= 0 ? null : new StatusMoodleDefinition
            {
                IconSprite = regeneration, Intensity = 0, Name = Text("growthBuffName"),
                Description = string.Format(Text("growthBuffDescription"), limb.fullName, status.Remaining, status.Targets?.Length ?? 0), Important = true
            });
            MoodleRegistry.RegisterBody<RegenerationComa>((body, status) => status.Remaining <= 0 ? null : new StatusMoodleDefinition
            {
                IconSprite = regeneration, Intensity = 2, Name = Text("comaBuffName"),
                Description = string.Format(Text("comaBuffDescription"), status.Remaining), Important = true
            });
            MoodleRegistry.RegisterLimb<MedicalTreatment>((limb, status) => status.Remaining <= 0 || limb.dismembered || limb.prostheticInfo.hasProsthetic ? null : new StatusMoodleDefinition
            {
                IconSprite = medical, Intensity = 0, Name = Text("treatmentBuffName"),
                Description = string.Format(Text("treatmentBuffDescription"), limb.fullName, status.Remaining), Important = true
            });
            MoodleRegistry.RegisterBody<DragonLifeBlessing>((body, status) => status.Remaining <= 0 || status.Delay > 0 ? null : new StatusMoodleDefinition
            {
                IconSprite = medical, Intensity = 0, Name = Text("blessingBuffName"),
                Description = string.Format(Text("blessingBuffDescription"), status.Remaining), Important = true
            });
            MoodleRegistry.RegisterBody<HealingFieldTreatment>((body, status) => status.Remaining <= 0 ? null : new StatusMoodleDefinition
            {
                IconSprite = RoundSprite(CannonAmmoState.RevivalId), Intensity = 0, Name = Text("fieldBuffName"),
                Description = Text("fieldBuffDescription"), Important = true
            });
        }
        private static Sprite RegisterAmpoule(string id, string key, string asset, int value)
        {
            var sprite = LoadSprite(asset, "syringe", pixelWidth: 10, worldPixelsPerUnit: 30, preserveFrame: true);
            ItemRegistry.Register(id, (ItemInfo)new CustomItemInfo
            {
                fullName = Text(key + "Name"), description = Text(key + "Description"), category = "medical", tags = "medicine,medicalampoule",
                weight = 0.1f, value = value, rec = new Recognition(6), usable = false, usableOnLimb = false,
                destroyAtZeroCondition = false, DropPool = DropPool.None, WorldSpawnPerChunk = 0,
                CustomData = new Dictionary<string, object> { [MedicalCatalog.DataKey] = id == MedicalCatalog.RegenerationId ? "regeneration" : "medical" }
            }, sprite, 0);
            return sprite;
        }
    }
}
