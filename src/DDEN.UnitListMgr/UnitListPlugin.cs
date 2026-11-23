using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using DDEN.Shared;
using UnityEngine;

namespace DDEN.UnitListMgr
{
    [BepInPlugin(Guid, "DDEN Unit List Manager", "0.1.0")]
    [BepInDependency("net.cucorelib", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class UnitListPlugin : BaseUnityPlugin, IDdenModule, IDdenModuleVersion, IDdenModuleActivity, IDdenConfigPolicy, IDdenConfigPresentation
    {
        public const string Guid = "com.dden.unitlistmgr";
        internal static LogChannel Log;
        internal static string T(string chinese, string english) => DdenLocalization.Language == DdenLanguage.English ? english : chinese;
        private ConfigEntry<KeyboardShortcut> _openKey, _radialKey;
        private ConfigEntry<string> _favorites;
        private readonly SpawnService _service = new SpawnService();
        private ItemView _view;
        private bool _standalone, _radial;
        private bool _ownsBlocker;
        private Vector2 _radialOrigin;
        private readonly string[] _slots = new string[8];
        private KeyCode _key, _quick;
        private bool _resolvingKeys;
        internal static string QuickKeyLabel { get; private set; } = "F8";

        public string Id => "unitlistmgr";
        public string DisplayName => T("物品工具", "Item tools");
        public string Version => "0.1.0-alpha";
        public string Summary => T("物品列表、属性与生成 · 实验性", "Item catalog, properties and spawning · Experimental");
        public bool Ready => true;
        public bool IsRunning => _radial || _standalone || _service.Busy;
        public string MinimumManagerVersion => "2.0.0";
        public string TestedManagerVersion => "2.0.0";
        public string MinimumSharedVersion => "2.0.0";
        public string TestedSharedVersion => "2.0.0";
        public int RequiredSharedApiVersion => DdenApi.Version;
        public IEnumerable<ConfigEntryBase> ConfigEntries => Config.Entries();
        public bool CanEditConfig(ConfigEntryBase entry, bool normallyInteractive) => normallyInteractive;
        public object ConfigDisplayValue(ConfigEntryBase entry) => entry.BoxedValue;
        public string ConfigNotice => "";
        public string Localize(string original, string language) => original;
        public DdenConfigMetadata Metadata(ConfigEntryBase entry)
        {
            string key = entry.Definition.Key;
            return new DdenConfigMetadata {
                DisplayName = key == "OpenKey" ? T("打开物品工具", "Open item tools") : key == "QuickMenuKey" ? T("快捷轮盘", "Quick menu") : T("轮盘槽位", "Quick slots"),
                Description = key == "OpenKey" ? T("打开管理器中的物品页；没有管理器时打开独立窗口。", "Open the item page, or a standalone window without the manager.")
                    : key == "QuickMenuKey" ? T("按住滑动，松开在鼠标处生成；中心、右键或 Esc 取消。只使用默认属性。", "Hold and slide; release to spawn a default item at the cursor. Center, right-click or Esc cancels.")
                    : T("八个物品 ID，以 | 分隔；也可在物品页分配。", "Eight item IDs separated by |; assign them on the item page."),
                IsExperimental = true, PerformanceImpact = DdenPerformanceImpact.Low,
                ExperimentalNote = T("尚未进行真实联机验收。", "Live multiplayer validation is pending.")
            };
        }

        private void Awake()
        {
            Log = DDEN.Shared.Log.For(Logger, "UnitListMgr", false);
            _openKey = Config.Bind("UI", "OpenKey", new KeyboardShortcut(KeyCode.F7), "打开物品工具 / Open item tools");
            _radialKey = Config.Bind("UI", "QuickMenuKey", new KeyboardShortcut(KeyCode.F8), "按住并滑动鼠标，松开后在鼠标处生成默认物品；中心或 Esc 取消。 / Hold and slide; release to spawn a default item at the cursor. Center or Esc cancels.");
            _favorites = Config.Bind("UI", "QuickSlots", "", "八个快捷物品 ID，以 | 分隔；只记录种类，不记录主面板属性。 / Eight item IDs separated by |. Property overrides are never stored.");
            LoadSlots();
            _view = new ItemView(_service, _slots, SaveSlots);
            DdenHub.Register(this);
            KeysChanged(null, EventArgs.Empty);
            _openKey.SettingChanged += KeysChanged;
            _radialKey.SettingChanged += KeysChanged;
            _favorites.SettingChanged += SlotsChanged;
        }
        private void KeysChanged(object sender, EventArgs args)
        {
            if (_resolvingKeys) return;
            _resolvingKeys = true;
            try
            {
                _key = Compat.ResolveHotkey(_openKey, Guid, true);
                _quick = Compat.ResolveHotkey(_radialKey, Guid, true);
                if (_quick == _key && _quick != KeyCode.None)
                {
                    _quick = KeyCode.None;
                    Log.Warn("OpenKey and QuickMenuKey must be different; quick menu disabled until changed.");
                }
                QuickKeyLabel = _quick == KeyCode.None ? T("未绑定", "Unbound") : _radialKey.Value.ToString();
            }
            finally { _resolvingKeys = false; }
        }
        private void SlotsChanged(object sender, EventArgs args) => LoadSlots();
        private void LoadSlots()
        {
            Array.Clear(_slots, 0, _slots.Length);
            string[] ids = _favorites.Value.Split('|');
            for (int i = 0; i < Math.Min(8, ids.Length); i++) _slots[i] = ids[i].Trim();
        }
        private void SaveSlots() => _favorites.Value = string.Join("|", _slots);
        public void DrawPage(Rect area) => _view.Draw(area);
        public bool DrawSettings(Rect area) => false;

        private void Update()
        {
            _service.Tick();
            if (!Application.isFocused)
            {
                _radial = false; _standalone = false; UpdateBlocker(); return;
            }
            if (_key != KeyCode.None && _openKey.Value.IsDown())
            {
                if (DdenHub.ManagerLoaded) DdenHub.OpenModule(Id);
                else _standalone = !_standalone;
            }
            if (_quick != KeyCode.None && _radialKey.Value.IsDown() && !DdenHub.AnyPanelVisible && SpawnService.LocalBody != null)
            {
                _radialOrigin = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                _radial = true;
            }
            if (_radial && (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1) || SpawnService.LocalBody == null)) _radial = false;
            else if (_radial && !Input.GetKey(_quick))
            {
                _radial = false;
                Vector2 mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                int sector = SpawnRules.Sector(mouse.x - _radialOrigin.x, mouse.y - _radialOrigin.y);
                if (sector >= 0 && !string.IsNullOrWhiteSpace(_slots[sector]))
                {
                    var camera = Camera.main;
                    if (camera != null && camera.orthographic)
                    {
                        Vector3 world = camera.ScreenToWorldPoint(Input.mousePosition);
                        // This request is constructed afresh: it cannot inherit edited properties.
                        _service.Submit(new SpawnRequest { RequestId = System.Guid.NewGuid().ToString("N"), ItemId = _slots[sector],
                            AtCursor = true, DefaultOnly = true, X = world.x, Y = world.y });
                    }
                    else _service.Status = T("当前没有可用的游戏镜头。", "Game camera is unavailable.");
                }
            }
            UpdateBlocker();
        }
        private void UpdateBlocker()
        {
            bool visible = _standalone || _radial;
            DdenHub.SetPanelVisible(Id, visible);
            if (visible) { DdenBlocker.Show(0.06f); _ownsBlocker = true; }
            else if (_ownsBlocker)
            {
                if (!DdenHub.AnyPanelVisible) DdenBlocker.Hide();
                _ownsBlocker = false;
            }
        }
        private void OnEnable() { _service.Enabled = true; }
        private void OnDisable() { _service.Enabled = false; _standalone = _radial = false; UpdateBlocker(); }
        private void OnDestroy()
        {
            _openKey.SettingChanged -= KeysChanged; _radialKey.SettingChanged -= KeysChanged; _favorites.SettingChanged -= SlotsChanged;
            DdenHub.SetPanelVisible(Id, false);
        }
        private void OnGUI()
        {
            if (_standalone && !DdenHub.ManagerLoaded)
            {
                var rect = new Rect(35, 35, Mathf.Max(450, Screen.width - 70), Mathf.Max(300, Screen.height - 70));
                GUI.Window(0x554e4954, rect, _ =>
                {
                    DdenUI.Fill(new Rect(0, 0, rect.width, rect.height), DdenUI.Bg);
                    DdenUI.Label(new Rect(12, 7, rect.width - 64, 24), "DDEN · " + DisplayName, DdenUI.Title);
                    if (DdenUI.Button(new Rect(rect.width - 35, 7, 24, 22), "×")) _standalone = false;
                    _view.Draw(new Rect(12, 38, rect.width - 24, rect.height - 50));
                }, GUIContent.none, GUIStyle.none);
            }
            if (_radial) DrawRadial();
        }
        private void DrawRadial()
        {
            Vector2 mouse = Event.current.mousePosition;
            int active = SpawnRules.Sector(mouse.x - _radialOrigin.x, mouse.y - _radialOrigin.y);
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI / 4;
                var at = _radialOrigin + new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle)) * 112;
                var box = new Rect(at.x - 61, at.y - 26, 122, 52);
                box.x = Mathf.Clamp(box.x, 2, Screen.width - box.width - 2);
                box.y = Mathf.Clamp(box.y, 2, Screen.height - box.height - 2);
                DdenUI.Fill(box, active == i ? DdenUI.PanelAlt : DdenUI.Panel);
                DdenUI.Frame(box, active == i ? DdenUI.Accent : DdenUI.AccentDim);
                if (!string.IsNullOrWhiteSpace(_slots[i]))
                {
                    DdenUI.DrawItemIcon(new Rect(box.x + 4, box.y + 7, 34, 34), _slots[i]);
                    DdenUI.Label(new Rect(box.x + 42, box.y + 6, 76, 40), Catalog.Name(_slots[i]), DdenUI.Sub);
                }
                else DdenUI.Label(new Rect(box.x + 6, box.y + 14, 110, 22), T("空槽 ", "Empty ") + (i + 1), DdenUI.Sub);
            }
            var center = new Rect(_radialOrigin.x - 45, _radialOrigin.y - 20, 90, 40);
            DdenUI.Fill(center, DdenUI.Bg);
            DdenUI.Label(center, T("松开生成\n中心取消", "Release: spawn\nCenter: cancel"), DdenUI.Sub);
        }
    }
}
