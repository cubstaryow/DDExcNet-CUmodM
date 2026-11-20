using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using DDEN.Shared;
using HarmonyLib;
using UnityEngine;

namespace DDExcNet.CUmodM
{
    /// <summary>
    /// DDExcNet-CUmodM —— DDEN 系列的统一管理器：
    /// ① 一个窗口管全部功能界面与配置界面；② 兼容性总控（互斥检测、快捷键抢键、补丁栈、旧文件残留）；
    /// ③ 各模块仍可单独工作，管理器缺席时它们退化成独立小窗。
    /// </summary>
    [BepInPlugin(Guid, "DDExcNet CUmod Manager", "1.0.0")]
    [BepInDependency("net.cucorelib", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("CasualtiesMP", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("org.bepinex.plugins.qol_unknown", BepInDependency.DependencyFlags.SoftDependency)]
    public class HubPlugin : BaseUnityPlugin
    {
        public const string Guid = "com.dden.cumodm";

        internal static HubPlugin Instance;
        internal static ManualLogSource LogSource;
        internal static LogChannel Log;

        internal static ConfigEntry<KeyboardShortcut> CfgToggleKey;
        internal static ConfigEntry<float> CfgScale;
        internal static ConfigEntry<bool> CfgAutoFixHotkeys;
        internal static ConfigEntry<bool> CfgScanOnStart;
        internal static ConfigEntry<bool> CfgModalBlocker;
        internal static ConfigEntry<float> CfgBlockerDim;
        internal static ConfigEntry<bool> CfgVerbose;

        private Rect _rect = new Rect(80f, 70f, 1180f, 760f);
        private Vector2 _sideScroll;
        private Vector2 _compatScroll;
        private Vector2 _configScroll;
        private string _page = "overview";
        private bool _visible;
        private bool _modalActive;
        private float _lastToggleAt = -1f;
        private KeyCode _effectiveKey = KeyCode.F8;

        private const int WindowId = 0x4444454E; // 'DDEN'
        private const float SidebarWidth = 236f;

        private void Awake()
        {
            Instance = this;
            LogSource = base.Logger;

            CfgToggleKey = Config.Bind("UI", "ToggleKey", new KeyboardShortcut(KeyCode.F8),
                "打开/关闭 DDEN 管理器。若与其它模组抢键，会在启动时自动换一个空闲键（见 AutoFixHotkeyConflicts）。");
            CfgScale = Config.Bind("UI", "Scale", 1.1f,
                new ConfigDescription("界面缩放。", new AcceptableValueRange<float>(0.75f, 2f)));
            CfgAutoFixHotkeys = Config.Bind("Compat", "AutoFixHotkeyConflicts", true,
                "启动时检测快捷键冲突，冲突则自动改用空闲的 F 键并写入配置。");
            CfgScanOnStart = Config.Bind("Compat", "ScanOnStart", true,
                "启动时扫描已装模组/补丁栈/旧文件残留，结果显示在“兼容性”页。");
            CfgModalBlocker = Config.Bind("UI", "ModalBlocker", true,
                "面板打开时挂一张 uGUI 模态遮罩（和联机模组的做法一样）：吃掉鼠标点击，游戏按钮不会被点穿。\n" +
                "关闭后不拦截，点击会穿到游戏界面。");
            CfgBlockerDim = Config.Bind("UI", "BlockerDim", 0.15f,
                new ConfigDescription("遮罩暗化程度（0 = 全透明只吃点击）。",
                    new AcceptableValueRange<float>(0f, 0.8f)));
            CfgVerbose = Config.Bind("Debug", "Verbose", false, "输出调试日志。");

            Log = DDEN.Shared.Log.For(LogSource, "CUmodM", CfgVerbose.Value);
            DDEN.Shared.Log.Attach(LogSource, "DDEN", CfgVerbose.Value);   // 共享代码（Compat/Reflect/SaveCodec）的落点

            DdenHub.MarkManagerLoaded();
            DdenHub.OpenRequested += OnOpenRequested;
            try { new Harmony(Guid).PatchAll(typeof(AdaptiveButton_ClickGuard)); }
            catch (Exception ex) { Log.Exception(ex, "菜单按钮拦截补丁失败"); }
            _rect = LoadRect();

            _effectiveKey = Compat.ResolveHotkey(CfgToggleKey, Guid, CfgAutoFixHotkeys.Value);
            if (CfgScanOnStart.Value)
            {
                try { Compat.Rescan(); }
                catch (Exception ex) { Log.Exception(ex, "启动兼容性扫描"); }
            }

            TryRegisterConsoleCommands();
            Log.Info($"管理器就绪（{_effectiveKey} 开关，已注册模块 {DdenHub.Modules.Count} 个）。");
        }

        private void Start()
        {
            // 等所有插件 Awake 完再补扫一次：此时别的模组的配置/补丁都已就位
            try { DdenExternal.Scan(); }
            catch (Exception ex) { Log.Exception(ex, "扫描外部配置接入"); }
            // 等所有插件 Awake 完再补扫一次：此时别的模组的配置/补丁都已就位
            if (CfgScanOnStart.Value)
            {
                try { Compat.Rescan(); }
                catch (Exception ex) { Log.Exception(ex, "二次兼容性扫描"); }
            }

            foreach (CompatRow row in Compat.Rows.Where(r => r.Level >= CompatLevel.Warn))
            {
                Log.Warn($"兼容性：{row.Name} [{row.LevelText}] {row.Note}");
            }

            if (File.Exists(UiTestMarker)) StartCoroutine(UiSmokeTest());

            // 配置页截图/核对用：放 cumod.cfgtest 就直接停在“全部配置”页，不自动关闭
            string cfgTest = System.IO.Path.Combine(Paths.ConfigPath, "cumod.cfgtest");
            if (File.Exists(cfgTest))
            {
                _page = "config";
                _visible = true;
                try { File.Delete(cfgTest); } catch { /* ignored */ }
                Log.Info("[CFGTEST] 已停在“全部配置”页，供核对。");
            }
        }

        private static string UiTestMarker => System.IO.Path.Combine(Paths.ConfigPath, "cumod.uitest");

        /// <summary>界面冒烟测试：自动翻遍所有页面/模块页，任何绘制异常都会被 OnGUI 捕获并写日志。</summary>
        private System.Collections.IEnumerator UiSmokeTest()
        {
            Log.Info("[UITEST] 开始：自动打开窗口并轮播所有页面。");
            _visible = true;
            yield return new WaitForSecondsRealtime(0.5f);
            Log.Info($"[UITEST] 已注册模块 {DdenHub.Modules.Count} 个：{string.Join(", ", DdenHub.Modules.Select(m => m.Id))}");

            string[] pages = new[] { "overview", "compat", "config" }
                .Concat(DdenHub.Modules.Select(m => m.Id))
                .Concat(DdenHub.Modules.Select(m => "config:" + m.Id))
                .ToArray();

            foreach (string page in pages)
            {
                _page = page;

                // 存档编辑器页：先真的读一次档，让物品/肢体/身体各标签页都跑一遍绘制
                if (page.EndsWith("saveeditor", StringComparison.Ordinal))
                {
                    InvokeModuleMethod("saveeditor", "SmokeLoad");
                }

                Log.Info("[UITEST] 绘制页面：" + page);
                float hold = page.EndsWith("saveeditor", StringComparison.Ordinal) ? 4f
                    : (page == "config" ? 8f : 0.8f);
                yield return new WaitForSecondsRealtime(hold);

                if (!_visible)
                {
                    Log.Error("[UITEST] FAIL：绘制 " + page + " 时窗口被异常关闭（见上方 Error）");
                    yield break;
                }
            }

            // 最后一站：有存档编辑器就停在它（标签页自动轮播），没有就停在总览
            IDdenModule editor = DdenHub.Find("saveeditor");
            if (editor != null)
            {
                _page = "saveeditor";
                InvokeModuleMethod("saveeditor", "SmokeLoad");
                Log.Info("[UITEST] 停留在存档编辑页 6 秒（标签页自动轮播）");
                yield return new WaitForSecondsRealtime(6f);

                // 停在“肢体/人体图”页，方便核对图形化肢体（不自动关闭，按 F11 关）
                InvokeModuleMethod("saveeditor", "SmokeLimb");
                Log.Info("[UITEST] PASS：所有页面绘制完成，无异常；现停留在“肢体 · 人体图”，按热键关闭。");
            }
            else
            {
                _page = "overview";
                Log.Info("[UITEST] PASS：所有页面绘制完成，无异常（存档编辑器未部署，跳过其页面）；现停留在总览，按热键关闭。");
            }
            try { File.Delete(UiTestMarker); } catch { /* ignored */ }
            yield break;
        }

        /// <summary>跨程序集调用模块的可选方法（不硬引用模块 dll）。</summary>
        private void InvokeModuleMethod(string moduleId, string method)
        {
            try
            {
                IDdenModule module = DdenHub.Find(moduleId);
                if (module == null) return;
                System.Reflection.MethodInfo mi = module.GetType().GetMethod(method,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                mi?.Invoke(module, null);
            }
            catch (Exception ex)
            {
                Log.Debug($"调用 {moduleId}.{method} 失败：{ex.Message}");
            }
        }

        private void OnDestroy()
        {
            DdenHub.OpenRequested -= OnOpenRequested;
        }

        private void OnOpenRequested(string id)
        {
            _page = string.IsNullOrEmpty(id) ? "overview" : id;
            _visible = true;
        }

        private void TryRegisterConsoleCommands()
        {
            try
            {
                if (Reflect.Type("CUCoreLib.Registries.ConsoleCommandRegistry") == null) return;
                if (!Compat.ConsoleHasCommand("dden"))
                {
                    CUCoreLib.Registries.ConsoleCommandRegistry.Register("dden",
                        "打开 DDEN 管理器。", (string[] args) => Toggle());
                }
            }
            catch (Exception ex)
            {
                Log.Debug("注册控制台指令 dden 失败：" + ex.Message);
            }
        }

        public void Toggle()
        {
            _visible = !_visible;
            if (_visible && CfgScanOnStart.Value)
            {
                try { Compat.Rescan(); } catch { /* ignored */ }
            }
        }

        private void Update()
        {
            try
            {
                KeyCapture.Tick();
                float now = Time.unscaledTime;
                if (_effectiveKey != KeyCode.None && Input.GetKeyDown(_effectiveKey) && now - _lastToggleAt > 0.25f)
                {
                    _lastToggleAt = now;
                    Toggle();
                }
                if (_visible && Input.GetKeyDown(KeyCode.Escape)) _visible = false;
                UpdateBlocker();
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "Update");
            }
        }

        /// <summary>面板打开时挂 uGUI 模态遮罩（吃掉点击），关闭时撤掉。</summary>
        private void UpdateBlocker()
        {
            bool want = _visible && CfgModalBlocker.Value;
            DdenHub.SetPanelVisible("hub", _visible);
            if (want) DdenBlocker.Show(CfgBlockerDim.Value);
            else DdenBlocker.Hide();

            if (want != _modalActive)
            {
                _modalActive = want;
                Log.Debug(want ? "面板已显示：挂上模态遮罩（游戏 UI 点击被吃掉）" : "面板已关闭：撤掉模态遮罩");
            }
        }

        private void OnGUI()
        {
            if (!_visible) return;
            try
            {
                float scale = Mathf.Clamp(CfgScale.Value, 0.75f, 2f);
                Matrix4x4 old = GUI.matrix;
                GUI.depth = -1000;   // 画在别的模组窗口之上
                GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

                float w = Screen.width / scale;
                float h = Screen.height / scale;
                _rect.width = Mathf.Min(_rect.width, w - 20f);
                _rect.height = Mathf.Min(_rect.height, h - 20f);
                _rect.x = Mathf.Clamp(_rect.x, 0f, Mathf.Max(0f, w - _rect.width));
                _rect.y = Mathf.Clamp(_rect.y, 0f, Mathf.Max(0f, h - _rect.height));

                // 用无皮肤窗口：标题栏/边框全部自己画，避免 Unity 默认窗框压住第一行文字
                _rect = GUI.Window(WindowId, _rect, DrawWindow, GUIContent.none, GUIStyle.none);
                GUI.matrix = old;
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "OnGUI");
                _visible = false;
            }
        }

        private void DrawWindow(int id)
        {
            DdenUI.BeginFrame();
            DdenUI.Fill(new Rect(0, 0, _rect.width, _rect.height), DdenUI.Bg);

            // 顶部标题条
            Rect header = new Rect(0, 0, _rect.width, 36f);
            DdenUI.Fill(header, DdenUI.Panel);
            DdenUI.Fill(new Rect(0, header.yMax - 1f, header.width, 1f), DdenUI.AccentDim);
            GUI.Label(new Rect(12, 6, 320, 24), "DDEN 模组管理器", DdenUI.Title);
            GUI.Label(new Rect(340, 9, _rect.width - 360, 20), Compat.Headline(), DdenUI.Sub);
            if (DdenUI.Button(new Rect(_rect.width - 30f, 8f, 22f, 20f), "×", false)) _visible = false;

            // 侧边栏
            Rect side = new Rect(0, 36f, SidebarWidth, _rect.height - 36f - 24f);
            DdenUI.Fill(side, DdenUI.Panel);
            DrawSidebar(new Rect(side.x + 8f, side.y + 8f, side.width - 16f, side.height - 16f));

            // 内容区
            Rect content = new Rect(SidebarWidth + 10f, 44f, _rect.width - SidebarWidth - 20f, _rect.height - 44f - 28f);
            DrawContent(content);

            // 底栏
            DdenUI.Fill(new Rect(0, _rect.height - 24f, _rect.width, 24f), DdenUI.Panel);
            GUI.Label(new Rect(12, _rect.height - 23f, _rect.width - 24f, 20),
                $"快捷键 {_effectiveKey} 开关 · 缩放 {CfgScale.Value:0.00} · 模块 {DdenHub.Modules.Count} 个 · 拖动标题栏移动", DdenUI.Sub);

            GUI.DragWindow(new Rect(0, 0, _rect.width, 36f));
            DdenUI.DrawTooltip(new Rect(0, 0, _rect.width, _rect.height));
        }

        private void DrawSidebar(Rect area)
        {
            float y = area.y;

            y += NavButton(new Rect(area.x, y, area.width, 28f), "overview", "总览");
            y += NavButton(new Rect(area.x, y, area.width, 28f), "compat", "兼容性  " + Compat.Headline());
            y += NavButton(new Rect(area.x, y, area.width, 28f), "config", "全部配置");
            y += 10f;

            DdenUI.Label(new Rect(area.x, y, area.width, 18f), "模组", DdenUI.Sub);
            y += 20f;

            if (DdenHub.Modules.Count == 0)
            {
                GUI.Label(new Rect(area.x, y, area.width, 40f), "尚未发现 DDEN 模块\n（检查 dll 是否都在同一目录）", DdenUI.Wrap);
            }

            _sideScroll = GUI.BeginScrollView(new Rect(area.x, y, area.width, area.yMax - y), _sideScroll,
                new Rect(0, 0, area.width - 16f, Mathf.Max(DdenHub.Modules.Count * 32f, 40f)));
            float sy = 0f;
            foreach (IDdenModule module in DdenHub.Modules)
            {
                bool selected = _page == module.Id;
                var r = new Rect(0, sy, area.width - 16f, 30f);
                DdenUI.Fill(r, selected ? DdenUI.AccentDim : DdenUI.PanelAlt);
                DdenUI.Label(new Rect(r.x + 8f, r.y + 4f, r.width - 20f, 16f),
                    (module.Ready ? "<color=#8cd97c>●</color> " : "<color=#f0a>●</color> ") + module.DisplayName, DdenUI.Row);
                DdenUI.Label(new Rect(r.x + 8f, r.y + 17f, r.width - 20f, 12f), module.Summary ?? string.Empty, DdenUI.Small);
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) _page = module.Id;
                sy += 32f;
            }
            GUI.EndScrollView();
        }

        private float NavButton(Rect r, string page, string label)
        {
            bool selected = _page == page;
            DdenUI.Fill(r, selected ? DdenUI.AccentDim : DdenUI.PanelAlt);
            DdenUI.Label(new Rect(r.x + 8f, r.y + 5f, r.width - 12f, 18f), label, DdenUI.Row);
            if (GUI.Button(r, GUIContent.none, GUIStyle.none)) _page = page;
            return 30f;
        }

        private void DrawContent(Rect area)
        {
            DdenUI.Fill(area, DdenUI.PanelAlt);
            DdenUI.Frame(area, DdenUI.AccentDim);

            Rect inner = new Rect(area.x + 10f, area.y + 8f, area.width - 20f, area.height - 16f);
            switch (_page)
            {
                case "overview": DrawOverview(inner); break;
                case "compat": DrawCompat(inner); break;
                case "config": DrawAllConfig(inner); break;
                default:
                    if (_page != null && _page.StartsWith("ext:", StringComparison.Ordinal))
                    {
                        DrawExternalPage(inner, _page.Substring(4));
                        break;
                    }
                    IDdenModule module = DdenHub.Find(_page);
                    if (module == null) DrawOverview(inner);
                    else DrawModule(inner, module);
                    break;
            }
        }

        private void DrawOverview(Rect area)
        {
            GUI.Label(new Rect(area.x, area.y, area.width, 20f), "总览", DdenUI.Title);
            GUI.Label(new Rect(area.x, area.y + 22f, area.width, 18f),
                "本管理器统一承载功能界面与配置界面；模块缺失时各自降级为独立小窗。", DdenUI.Sub);

            float y = area.y + 48f;
            foreach (IDdenModule module in DdenHub.Modules)
            {
                var card = new Rect(area.x, y, area.width, 56f);
                DdenUI.Fill(card, DdenUI.Panel);
                DdenUI.Frame(card, module.Ready ? DdenUI.AccentDim : DdenUI.Warn);
                DdenUI.Label(new Rect(card.x + 10f, card.y + 6f, card.width - 200f, 18f),
                    module.DisplayName + $"  <size=10><color=#89a>{module.Version}</color></size>", DdenUI.Row);
                DdenUI.Label(new Rect(card.x + 10f, card.y + 26f, card.width - 200f, 16f),
                    (module.Ready ? "<color=#8cd97c>就绪</color> · " : "<color=#f0a>降级</color> · ") + (module.Summary ?? ""), DdenUI.Sub);
                if (DdenUI.Button(new Rect(card.xMax - 180f, card.y + 14f, 90f, 26f), "打开界面", true)) _page = module.Id;
                if (DdenUI.Button(new Rect(card.xMax - 84f, card.y + 14f, 74f, 26f), "配置")) _page = "config:" + module.Id;
                y += 62f;
            }

            if (y + 60f < area.yMax)
            {
                GUI.Label(new Rect(area.x, y + 6f, area.width, 18f), "兼容性摘要", DdenUI.Sub);
                float ry = y + 26f;
                foreach (CompatRow row in Compat.Rows.Take(8))
                {
                    GUI.Label(new Rect(area.x, ry, area.width, 16f),
                        $"{row.LevelText}  {row.Name}  <color=#89a>{row.Version}</color>  {row.Note}", DdenUI.Sub);
                    ry += 18f;
                }
            }
        }

        private void DrawCompat(Rect area)
        {
            GUI.Label(new Rect(area.x, area.y, area.width, 20f), "兼容性", DdenUI.Title);

            if (DdenUI.Button(new Rect(area.xMax - 100f, area.y, 100f, 24f), "重新扫描", true))
            {
                try { Compat.Rescan(); } catch (Exception ex) { Log.Exception(ex, "Rescan"); }
            }

            Rect list = new Rect(area.x, area.y + 30f, area.width, area.height - 30f);
            float rowH = 34f;
            float total = Mathf.Max(list.height, Compat.Rows.Count * rowH + 20f);
            _compatScroll = GUI.BeginScrollView(list, _compatScroll, new Rect(0, 0, list.width - 18f, total));

            float y = 0f;
            foreach (CompatRow row in Compat.Rows)
            {
                var r = new Rect(0, y, list.width - 18f, rowH - 4f);
                DdenUI.Fill(r, y % 68f < 34f ? DdenUI.Panel : DdenUI.PanelAlt);
                DdenUI.Label(new Rect(r.x + 6f, r.y + 2f, 250f, 16f), row.Name, DdenUI.Row);
                DdenUI.Label(new Rect(r.x + 6f, r.y + 17f, 250f, 12f), row.LevelText, DdenUI.Small);
                DdenUI.Label(new Rect(r.x + 262f, r.y + 2f, r.width - 268f, 14f), row.Version, DdenUI.Sub);
                DdenUI.Label(new Rect(r.x + 262f, r.y + 15f, r.width - 268f, 14f), row.Note, DdenUI.Sub);
                y += rowH;
            }
            GUI.EndScrollView();
        }

        /// <summary>外部模组交出的自定义页面（DdenDraw）。</summary>
        private void DrawExternalPage(Rect area, string indexText)
        {
            int index;
            if (!int.TryParse(indexText, out index) || index < 0 || index >= DdenExternal.Blocks.Count)
            {
                DrawOverview(area);
                return;
            }

            DdenExternal.Block block = DdenExternal.Blocks[index];
            GUI.Label(new Rect(area.x, area.y, area.width - 130f, 20f), block.Title, DdenUI.Title);
            if (DdenUI.Button(new Rect(area.xMax - 120f, area.y, 120f, 22f), "← 看全部配置"))
            {
                _page = "config";
                return;
            }

            Rect body = new Rect(area.x, area.y + 26f, area.width, area.height - 26f);
            try
            {
                block.Draw(body);
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "外部页面绘制失败：" + block.Title);
                DdenUI.Label(body, "该外部页面绘制失败：" + ex.Message, DdenUI.Wrap);
            }
        }

        private void DrawAllConfig(Rect area)
        {
            string moduleId = _page.StartsWith("config:") ? _page.Substring(7) : null;
            IDdenModule module = DdenHub.Find(moduleId);

            GUI.Label(new Rect(area.x, area.y, area.width - 130f, 20f),
                module == null ? "全部配置（所有模块 + 管理器）" : module.DisplayName + " · 配置", DdenUI.Title);

            if (module != null && DdenUI.Button(new Rect(area.xMax - 110f, area.y, 110f, 22f), "← 看全部"))
            {
                _page = "config";
                return;
            }

            // 单模块配置
            if (module != null)
            {
                Rect solo = new Rect(area.x, area.y + 26f, area.width, area.height - 26f);
                bool handled = false;
                try { handled = module.DrawSettings(solo); }
                catch (Exception ex) { DdenUI.Label(solo, "配置绘制失败：" + ex.Message, DdenUI.Wrap); handled = true; }
                if (handled) return;

                float soloH = DdenUI.ConfigListHeight(module.ConfigEntries) + 12f;
                _configScroll = GUI.BeginScrollView(solo, _configScroll,
                    new Rect(0, 0, solo.width - 18f, Mathf.Max(solo.height, soloH)));
                DdenUI.DrawConfigGrouped(new Rect(0, 0, solo.width - 18f, soloH), module.ConfigEntries);
                GUI.EndScrollView();
                return;
            }

            // 全部模块 + 管理器自身，一次滚到底
            Rect body = new Rect(area.x, area.y + 26f, area.width, area.height - 26f);
            float total = 40f;
            foreach (IDdenModule m in DdenHub.Modules) total += 28f + DdenUI.ConfigListHeight(m.ConfigEntries);
            total += 28f + DdenUI.ConfigListHeight(Config.Entries());
            foreach (DdenExternal.Block b in DdenExternal.Blocks) total += 28f + DdenUI.ConfigListHeight(b.Entries);

            float w = body.width - 18f;
            _configScroll = GUI.BeginScrollView(body, _configScroll,
                new Rect(0, 0, w, Mathf.Max(body.height, total)));

            float y = 0f;
            foreach (IDdenModule m in DdenHub.Modules)
            {
                DdenUI.Fill(new Rect(0, y, w, 22f), DdenUI.AccentDim);
                GUI.Label(new Rect(6f, y + 2f, w - 90f, 18f),
                    $"{m.DisplayName}  <size=10><color=#89a>{m.Version}</color></size>  ·  {m.Summary}", DdenUI.Row);
                if (DdenUI.Button(new Rect(w - 78f, y + 2f, 70f, 18f), "单独看"))
                {
                    _page = "config:" + m.Id;
                    GUI.EndScrollView();
                    return;
                }
                y += 26f;
                y += DdenUI.DrawConfigGrouped(new Rect(0, y, w, 4000f), m.ConfigEntries);
                y += 10f;
            }

            DdenUI.Fill(new Rect(0, y, w, 22f), DdenUI.AccentDim);
            GUI.Label(new Rect(6f, y + 2f, w - 12f, 18f), "管理器自身", DdenUI.Row);
            y += 26f;
            y += DdenUI.DrawConfigGrouped(new Rect(0, y, w, 4000f), Config.Entries());
            y += 10f;

            // 外部模组交出来的配置（DdenExternal 约定接入，无需引用 DDEN.Shared）
            for (int i = 0; i < DdenExternal.Blocks.Count; i++)
            {
                DdenExternal.Block b = DdenExternal.Blocks[i];
                DdenUI.Fill(new Rect(0, y, w, 22f), DdenUI.AccentDim);
                GUI.Label(new Rect(6f, y + 2f, w - 90f, 18f),
                    $"{b.Title}  <size=10><color=#89a>{b.Owner}</color></size>" +
                    (string.IsNullOrEmpty(b.Summary) ? "" : $"  ·  {b.Summary}"), DdenUI.Row);
                if (b.Draw != null && DdenUI.Button(new Rect(w - 78f, y + 2f, 70f, 18f), "单独看"))
                {
                    _page = "ext:" + i;
                    GUI.EndScrollView();
                    return;
                }
                y += 26f;
                y += DdenUI.DrawConfigGrouped(new Rect(0, y, w, 4000f), b.Entries);
                y += 10f;
            }

            GUI.EndScrollView();
        }

        private void DrawManagerConfig(Rect area)
        {
            DdenUI.Section(new Rect(area.x, area.y, area.width, 18f), "管理器自身");
            float y = area.y + 24f;
            foreach (ConfigEntryBase entry in Config.Entries())
            {
                DdenUI.ConfigRow(new Rect(area.x, y, area.width, 22f), entry);
                y += 26f;
                if (y > area.yMax - 24f) break;
            }
        }

        private void DrawModule(Rect area, IDdenModule module)
        {
            GUI.Label(new Rect(area.x, area.y, area.width - 160f, 20f), module.DisplayName, DdenUI.Title);
            if (DdenUI.Button(new Rect(area.xMax - 150f, area.y, 70f, 22f), "配置"))
            {
                _page = "config:" + module.Id;
            }
            if (DdenUI.Button(new Rect(area.xMax - 74f, area.y, 74f, 22f), "总览"))
            {
                _page = "overview";
            }

            Rect page = new Rect(area.x, area.y + 26f, area.width, area.height - 26f);
            try
            {
                module.DrawPage(page);
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "DrawPage(" + module.Id + ")");
                DdenUI.Label(page, "界面绘制失败：" + ex.Message, DdenUI.Wrap);
            }
        }

        // ===== 窗口位置记忆 =====

        private static Rect LoadRect()
        {
            var r = new Rect(80f, 70f, 1180f, 760f);
            try
            {
                r.x = PlayerPrefs.GetFloat("DDEN.Hub.X", r.x);
                r.y = PlayerPrefs.GetFloat("DDEN.Hub.Y", r.y);
                r.width = PlayerPrefs.GetFloat("DDEN.Hub.W", r.width);
                r.height = PlayerPrefs.GetFloat("DDEN.Hub.H", r.height);
            }
            catch { /* ignored */ }
            return r;
        }

        private void OnDisable()
        {
            try
            {
                DdenBlocker.Hide();
                PlayerPrefs.SetFloat("DDEN.Hub.X", _rect.x);
                PlayerPrefs.SetFloat("DDEN.Hub.Y", _rect.y);
                PlayerPrefs.SetFloat("DDEN.Hub.W", _rect.width);
                PlayerPrefs.SetFloat("DDEN.Hub.H", _rect.height);
                PlayerPrefs.Save();
            }
            catch { /* ignored */ }
        }
    }
}
