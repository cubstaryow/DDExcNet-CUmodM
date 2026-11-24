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
    [BepInPlugin(Guid, "DDEN LoongNova", "0.1.0")]
    [BepInDependency("net.cucorelib", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("CasualtiesMP", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class LoongNovaPlugin : BaseUnityPlugin, IDdenModule, IDdenModuleVersion, IDdenModuleHealth
    {
        public const string Guid = "com.dden.loongnova";
        internal static ManualLogSource Log;
        internal static readonly WeaponService Service = new WeaponService();
        private Harmony _harmony;
        private ConfigEntry<KeyboardShortcut> _key;
        private KeyCode _openKey;
        private bool _visible;
        private Vector2 _scroll;
        private string _problem;

        public string Id => "loongnova";
        public string DisplayName => Registration.Text("module");
        public string Version => "0.1.0-alpha";
        public string Summary => Registration.Text("summary");
        public bool Ready => _problem == null && (!Mp.IsRunning || Service.Ready);
        public DdenHealth Health => _problem != null ? DdenHealth.Severe : Ready ? DdenHealth.Ok : DdenHealth.Warn;
        public string HealthNote => _problem ?? (Ready ? "" : Registration.Text("unavailable"));
        public string MinimumManagerVersion => "2.0.0";
        public string TestedManagerVersion => "2.0.0";
        public string MinimumSharedVersion => "2.0.0";
        public string TestedSharedVersion => "2.0.0";
        public int RequiredSharedApiVersion => DdenApi.Version;
        public IEnumerable<ConfigEntryBase> ConfigEntries => Config.Entries();

        private void Awake()
        {
            Log = Logger;
            try
            {
                Registration.Register();
                SaveRegistry.RegisterItemProvider(Guid + ".energy.v1", new GunSaveProvider());
                _harmony = new Harmony(Guid);
                _harmony.PatchAll(typeof(WeaponPatches));
                _harmony.PatchAll(typeof(EquipmentPatches));
                _harmony.PatchAll(typeof(MerchantPatches));
                _key = Config.Bind("UI", "StandaloneKey", new KeyboardShortcut(KeyCode.F6), "无管理器时打开龙灼线束状态页 / Open LoongNova without the manager");
                _openKey = Compat.ResolveHotkey(_key, Guid, !DdenHub.ManagerLoaded);
                _key.SettingChanged += KeyChanged;
            }
            catch (Exception error)
            {
                _problem = error.Message;
                Logger.LogError(error);
            }
            DdenHub.Register(this);
        }

        private void KeyChanged(object sender, EventArgs args) => _openKey = _key.Value.MainKey;

        private void Update()
        {
            if (_problem != null) return;
            Service.Tick();
            PulseVisual.Tick();
            if (!DdenHub.ManagerLoaded && Input.GetKeyDown(_openKey)) _visible = !_visible;
            if (DdenHub.ManagerLoaded) _visible = false;
            DdenHub.SetPanelVisible(Id, _visible);
            if (_visible) DdenBlocker.Show(0.12f);
        }

        public void DrawPage(Rect area)
        {
            float y = area.y;
            Action<string, float> row = (text, height) =>
            {
                DdenUI.Label(new Rect(area.x + 8, y, area.width - 16, height), text, DdenUI.Row);
                y += height + 8;
            };
            row(DisplayName + " · " + Version, 25);
            var body = PlayerCamera.main != null ? PlayerCamera.main.body : null;
            var held = body != null ? body.GetItem(body.handSlot) : null;
            if (held != null && held.id == Registration.GunId)
            {
                var state = DragonGun.For(held);
                row($"{state.State.Energy:0.##} / 1000 · {Mathf.FloorToInt(state.State.Energy / EnergyState.ShotCost)} 发 · {held.condition * 100:0.##}%", 25);
                if (state.Status.Length != 0) row(state.Status, 25);
            }
            else row(Registration.Text("notEquipped"), 25);
            var belt = GravityBelt.Worn(body);
            if (belt != null)
            {
                row(Registration.Text("beltName") + $" · {belt.State.Remaining:0.##} / 1000", 25);
                if (belt.Status.Length != 0) row(belt.Status, 25);
                if (DdenUI.Button(new Rect(area.x + 8, y, 220, 26), Registration.Text("ejectCell"))) belt.Request("eject", body);
                y += 34;
            }
            row(Registration.Text("instructions"), 65);
            row(Registration.Text("gunRecipe"), 60);
            row(Registration.Text("cellRecipe"), 60);
            row(Registration.Text("refillRecipe"), 60);
            row(Registration.Text("matterRecipe"), 60);
            row(Registration.Text("beltRecipe"), 60);
            row(Registration.Text("wrenchRecipe"), 80);
            row(Registration.Text("trade"), 45);
            row(Registration.Text("rarity"), 65);
            row(Registration.Text("validation"), 45);
        }
        public bool DrawSettings(Rect area) => false;

        private void OnGUI()
        {
            if (!_visible || DdenHub.ManagerLoaded) return;
            var window = new Rect(35, 35, Mathf.Min(700, Screen.width - 70), Mathf.Min(750, Screen.height - 70));
            GUI.Window(0x4c4e4f56, window, _ =>
                DdenUI.DrawStandalone(new Rect(0, 0, window.width, window.height), this, ref _scroll, false), GUIContent.none);
        }
        private void OnDisable()
        {
            _visible = false;
            DdenHub.SetPanelVisible(Id, false);
        }
        private void OnDestroy()
        {
            if (_key != null) _key.SettingChanged -= KeyChanged;
            _harmony?.UnpatchSelf();
            new Harmony(Guid + ".mp").UnpatchSelf();
            PulseVisual.Dispose();
            DdenHub.SetPanelVisible(Id, false);
        }
    }
}
