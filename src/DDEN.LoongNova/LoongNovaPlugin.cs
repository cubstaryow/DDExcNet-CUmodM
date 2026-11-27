using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using CUCoreLib.Registries;
using DDEN.Shared;
using HarmonyLib;
using UnityEngine;

namespace DDEN.LoongNova
{
    [BepInPlugin(Guid, "DDEN LoongNova", ReleaseVersion)]
    [BepInDependency("net.cucorelib", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("CasualtiesMP", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class LoongNovaPlugin : BaseUnityPlugin, IDdenModule, IDdenModuleVersion, IDdenModuleHealth
    {
        public const string Guid = "com.dden.loongnova";
        public const string ReleaseVersion = "1.1.0";
        internal static LogChannel Log;
        internal static readonly WeaponService Service = new WeaponService();
        private Harmony _harmony;
        private string _problem;
        private readonly ModuleItemPage _itemPage = new ModuleItemPage();

        public string Id => "loongnova";
        public string DisplayName => Registration.Text("module");
        public string Version => ReleaseVersion;
        public string Summary => Registration.Text("summary");
        public bool Ready => _problem == null && (!Mp.IsRunning || Service.Ready);
        public DdenHealth Health => _problem != null ? DdenHealth.Severe : Ready ? DdenHealth.Ok : DdenHealth.Warn;
        public string HealthNote => _problem != null ? "装备加载失败，请查看日志" : (Ready ? "" : Registration.Text("unavailable"));
        public string MinimumManagerVersion => "2.0.0";
        public string TestedManagerVersion => "2.1.0";
        public string MinimumSharedVersion => "2.0.1";
        public string TestedSharedVersion => "2.0.1";
        public int RequiredSharedApiVersion => DdenApi.Version;
        public IEnumerable<ConfigEntryBase> ConfigEntries => Config.Entries();

        private void Awake()
        {
            Log = DDEN.Shared.Log.For(Logger, "LoongNova");
            ConfigMigration.Retire(Config, "UI/StandaloneKey");
            try
            {
                Registration.Register();
                SaveRegistry.RegisterItemProvider(Guid + ".energy.v1", new GunSaveProvider());
                _harmony = new Harmony(Guid);
                _harmony.PatchAll(typeof(WeaponPatches));
                _harmony.PatchAll(typeof(ChargeHudPatch));
                _harmony.PatchAll(typeof(CannonRackControls));
                _harmony.PatchAll(typeof(CannonSightPatch));
                _harmony.PatchAll(typeof(CannonInterruption));
                _harmony.PatchAll(typeof(CellRefillMatching));
                _harmony.PatchAll(typeof(EquipmentPatches));
                _harmony.PatchAll(typeof(MedicalPatches));
                _harmony.PatchAll(typeof(MedicalTradePatch));
                _harmony.PatchAll(typeof(InjectorGrip));
                _harmony.PatchAll(typeof(DrillPodSource));
                _harmony.PatchAll(typeof(RegenerationComaPatch));
                _harmony.PatchAll(typeof(LimbGrowthPatch));
                _harmony.PatchAll(typeof(MedicalTreatmentPatch));
                _harmony.PatchAll(typeof(DragonLifeBlessingPatch));
                _harmony.PatchAll(typeof(HealingFieldTreatmentPatch));
                _harmony.PatchAll(typeof(InventoryStateVisuals));
                _harmony.PatchAll(typeof(DragonizationPatches));
                _harmony.PatchAll(typeof(BeltInteractionPatches));
                _harmony.PatchAll(typeof(BeltWearPatches));
                _harmony.PatchAll(typeof(LootPoolPatches));
                _harmony.PatchAll(typeof(CannonPurchaseAmmoPatch));
                _harmony.PatchAll(typeof(CannonDropAmmoPatch));
                _harmony.PatchAll(typeof(ElderDragonDrop));
            }
            catch (Exception error)
            {
                _problem = error.Message;
                Log.Exception(error, "LoongNova initialization");
            }
            DdenHub.Register(this);
        }



        private void Update()
        {
            if (_problem != null) return;
            Service.Tick();
            PulseVisual.Tick();
            HealingFields.Tick();
            try { CannonUtility.Tick(); }
            catch (Exception error) { CannonUtility.Clear(); Log.Exception(error, "Utility ammunition update"); }
        }

        public void DrawPage(Rect area) => _itemPage.Draw(area);
        public bool DrawSettings(Rect area) => false;



        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            new Harmony(Guid + ".mp").UnpatchSelf();
            PulseVisual.Dispose();
            HealingFields.Clear();
            CannonUtility.Clear();
            CannonBreach.ClearDoors();
            CannonChargeAudio.Dispose();
            DdenHub.SetPanelVisible(Id, false);
        }
    }
}
