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
    public sealed class UnitListPlugin : BaseUnityPlugin, IDdenModule, IDdenModuleVersion, IDdenModuleActivity, IDdenConfigPolicy, IDdenConfigPresentation, IDdenConfigAvailability
    {
        public const string Guid = "com.dden.unitlistmgr";
        internal static LogChannel Log;
        internal static string T(string chinese, string english) => DdenLocalization.Language == DdenLanguage.English ? english : chinese;
        private ConfigEntry<KeyboardShortcut> _openKey, _radialKey;
        private ConfigEntry<string> _favorites;
        private static ConfigEntry<bool> _allowGuests, _disableAchievements;
        internal static bool AllowGuestSpawn => _allowGuests?.Value ?? true;
        internal static bool DisableAchievementsOnSpawn => _disableAchievements?.Value ?? true;
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
        public bool CanEditConfig(ConfigEntryBase entry, bool normallyInteractive) => normallyInteractive && ConfigUnavailableReason(entry) == null;
        public object ConfigDisplayValue(ConfigEntryBase entry) => entry.BoxedValue;
        public string ConfigNotice => !Mp.TogetherReady ? T("Together 未就绪 · 物品工具以单机模式运行，成就选项仍生效。", "Together is not ready. Item tools use single-player mode; achievement policy remains active.")
            : Mp.IsRunning && !Mp.IsServer && !Mp.CoreReady ? T("CCL MP 未就绪 · 客机生成不可用。", "CCL multiplayer bridge is not ready; guest spawning is unavailable.") : "";
        public string ConfigUnavailableReason(ConfigEntryBase entry)
        {
            if (!ReferenceEquals(entry, _allowGuests)) return null;
            if (!Mp.TogetherReady) return T("Together 未就绪", "Together is not ready");
            if (!Mp.CoreReady) return T("CCL MP 未就绪", "CCL multiplayer bridge is not ready");
            return Mp.IsRunning && !Mp.IsServer ? T("此项由主机决定。", "The host controls this setting.") : null;
        }
        public string Localize(string original, string language) => original;
        public DdenConfigMetadata Metadata(ConfigEntryBase entry)
        {
            string key = entry.Definition.Key;
            if (key == "AllowGuestSpawn" || key == "DisableAchievements")
                return new DdenConfigMetadata {
                    DisplayName = key == "AllowGuestSpawn" ? T("是否允许客机生成", "Allow guest spawning") : T("是否禁用成就", "Disable achievements"),
                    Description = key == "AllowGuestSpawn" ? T("默认开启。仅主机设置有效；关闭后拒绝客机请求，仍需满足 MP 原作弊权限。", "On by default. Host controls this extra restriction; MP cheat permissions still apply.")
                        : T("默认开启。实际生成后标记已作弊；多人由主机决定。关闭不清除既有标记，也不绕过游戏或其他模组的限制。", "On by default. Created items mark the save cheated. Host controls MP. Turning off does not clear existing flags or override other restrictions."),
                    IsExperimental = false, PerformanceImpact = DdenPerformanceImpact.Low
                };
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
            _allowGuests = Config.Bind("Host", "AllowGuestSpawn", true, "是否允许客机生成物品；仅主机设置生效，仍须满足 MP 作弊规则。 / Allow guest spawning, in addition to MP cheat permissions. Host setting only.");
            _disableAchievements = Config.Bind("Generation", "DisableAchievements", true, "生成物品时标记存档已作弊并禁用成就。多人由主机设置决定。关闭不会清除已有标记。 / Mark saves cheated and disable achievements on creation. Host controls multiplayer; disabling never clears an existing flag.");
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
                    else _service.ShowFailure(T("当前没有可用的游戏镜头。", "Game camera is unavailable."), true);
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
            if (_service.ShowToast)
            {
                int depth = GUI.depth;
                GUI.depth = -10000;
                try
                {
                    float width = Mathf.Min(_service.QuickFeedback ? 380 : 560, Screen.width - 32);
                    ResultNotice.Draw(new Rect((Screen.width - width) / 2, 56, width, 30), _service.ToastText, _service.Notice, true);
                }
                finally { GUI.depth = depth; }
            }
        }
        private void DrawRadial()
        {
            // Keep the eight cells inside the screen as one group, preserving their spacing.
            // Selection still uses the original anchor so moving the visual group never changes a gesture.
            var layout = new RadialLayout(Screen.width, Screen.height, _radialOrigin.x, _radialOrigin.y);
            float scale = layout.Scale, halfWidth = layout.HalfWidth, halfHeight = layout.HalfHeight;
            var center = new Vector2(layout.X, layout.Y);
            Vector2 mouse = Event.current.mousePosition;
            int active = SpawnRules.Sector(mouse.x - _radialOrigin.x, mouse.y - _radialOrigin.y);
            var centered = new GUIStyle(DdenUI.Sub) { alignment = TextAnchor.MiddleCenter };
            for (int i = 0; i < 8; i++)
            {
                var box = new Rect(layout.CellX(i), layout.CellY(i), halfWidth * 2, halfHeight * 2);
                bool filled = !string.IsNullOrWhiteSpace(_slots[i]);
                bool chosen = active == i;
                DdenUI.Fill(box, chosen ? new Color(0.16f, 0.29f, 0.37f, 0.76f) : new Color(0.05f, 0.07f, 0.1f, 0.62f));
                DdenUI.Frame(box, chosen ? DdenUI.Accent : DdenUI.AccentDim, chosen ? 2 : 1);
                if (chosen) DdenUI.Fill(new Rect(box.x, box.y, 4, box.height), filled ? DdenUI.Accent : DdenUI.Warn);
                DdenUI.Label(new Rect(box.x + 5, box.y + 3, 18, 16), (i + 1).ToString(), DdenUI.Sub);
                if (filled)
                {
                    float icon = 28 * scale;
                    DdenUI.DrawItemIcon(new Rect(box.center.x - icon / 2, box.y + 5, icon, icon), _slots[i]);
                    DdenUI.Label(new Rect(box.x + 5, box.yMax - 25, box.width - 10, 20), DdenUI.ElideToWidth(Catalog.Name(_slots[i]), box.width - 10, centered), centered);
                }
                else DdenUI.Label(new Rect(box.x + 6, box.y + halfHeight - 10, box.width - 12, 20), T("未分配", "Unassigned"), centered);
            }
            var middle = new Rect(center.x - halfWidth, center.y - halfHeight, halfWidth * 2, halfHeight * 2);
            DdenUI.Fill(middle, new Color(0.05f, 0.07f, 0.1f, 0.65f));
            DdenUI.Frame(middle, DdenUI.AccentDim);
            string text = active < 0 ? T("滑动选择\n中心取消", "Select\nCenter: cancel")
                : string.IsNullOrWhiteSpace(_slots[active]) ? T("空槽\n不生成", "Empty\nNo action") : T("松开生成\n右键取消", "Release\nRMB: cancel");
            var centerStyle = new GUIStyle(centered) { wordWrap = true };
            DdenUI.Label(new Rect(middle.x + 6, middle.y + 7, middle.width - 12, middle.height - 14), text, centerStyle);
        }
    }
}
