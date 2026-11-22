using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using DDEN.Shared;
using UnityEngine;

namespace DDEN.SaveEditor
{
    [BepInPlugin(Guid, "DDEN Save Editor", "1.7.2")]
    [BepInDependency("net.cucorelib", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("org.bepinex.plugins.qol_unknown", BepInDependency.DependencyFlags.SoftDependency)]
    public class SaveEditorPlugin : BaseUnityPlugin, IDdenModule
    {
        public const string Guid = "com.dden.saveeditor";
        private const string Title = "存档编辑器";

        internal static SaveEditorPlugin Instance;
        internal static ManualLogSource LogSource;
        internal static LogChannel Log;

        internal static ConfigEntry<KeyboardShortcut> CfgStandaloneKey;
        internal static ConfigEntry<float> CfgScale;
        internal static ConfigEntry<bool> CfgVerbose;

        private readonly EditorView _view = new EditorView();
        private bool _standaloneVisible;
        private Rect _standaloneRect;
        private KeyCode _standaloneKey = KeyCode.F9;

        // ===== IDdenModule =====
        public string Id => "saveeditor";
        public string DisplayName => Title;
        public string Version => Info.Metadata.Version.ToString();
        public bool Ready => true;
        public string Summary
        {
            get
            {
                try
                {
                    int slots = Qol.Slots().Count;
                    return $"{slots} 个存档位 · 当前 {Qol.CurrentSaveFile}";
                }
                catch
                {
                    return "存档位读取失败";
                }
            }
        }
        public IEnumerable<ConfigEntryBase> ConfigEntries => Config.Entries();

        private void Awake()
        {
            Instance = this;
            LogSource = base.Logger;

            CfgStandaloneKey = Config.Bind("UI", "StandaloneKey", new KeyboardShortcut(KeyCode.F9),
                "没有管理器时，用这个键打开存档编辑器自己的小窗口。");
            CfgScale = Config.Bind("UI", "Scale", 1.1f,
                new ConfigDescription("小窗口的界面缩放（从管理器里打开时跟随管理器）。", new AcceptableValueRange<float>(0.75f, 2f)));
            CfgVerbose = Config.Bind("Debug", "Verbose", false, "输出更详细的日志（排查问题用）。");

            Log = DDEN.Shared.Log.For(LogSource, "SaveEditor", CfgVerbose.Value);

            DdenHub.Register(this);
            _standaloneKey = Compat.ResolveHotkey(CfgStandaloneKey, Guid, !DdenHub.ManagerLoaded);

            TryRegisterConsoleCommand();
            Log.Info("存档编辑器就绪：只改你点的字段，不会自动改动“禁用成就”标记。");

            if (SelfTest.Requested) StartCoroutine(RunSelfTestDelayed());
        }

        private System.Collections.IEnumerator RunSelfTestDelayed()
        {
            yield return new WaitForSecondsRealtime(3f);
            SelfTest.Run(_view, Log);
        }

        private void TryRegisterConsoleCommand()
        {
            try
            {
                Type registry = Reflect.Type("CUCoreLib.Registries.ConsoleCommandRegistry");
                if (registry == null) return;
                if (Compat.ConsoleHasCommand("saveedit")) return;
                Action<string[]> command = args =>
                {
                    if (DdenHub.ManagerLoaded) DdenHub.OpenModule(Id);
                    else _standaloneVisible = true;
                };
                foreach (var method in registry.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
                {
                    var parameters = method.GetParameters();
                    if (method.Name != "Register" || parameters.Length < 3 || parameters[0].ParameterType != typeof(string)
                        || parameters[1].ParameterType != typeof(string) || !typeof(Delegate).IsAssignableFrom(parameters[2].ParameterType)) continue;
                    var invoke = parameters[2].ParameterType.GetMethod("Invoke");
                    if (invoke == null || invoke.ReturnType != typeof(void) || invoke.GetParameters().Length != 1
                        || invoke.GetParameters()[0].ParameterType != typeof(string[])) continue;
                    var arguments = new object[parameters.Length];
                    arguments[0] = "saveedit"; arguments[1] = "打开存档编辑器。";
                    arguments[2] = Delegate.CreateDelegate(parameters[2].ParameterType, command.Target, command.Method);
                    for (int i = 3; i < parameters.Length; i++)
                    {
                        var parameter = parameters[i];
                        arguments[i] = parameter.HasDefaultValue ? parameter.DefaultValue
                            : parameter.ParameterType.IsArray ? Array.CreateInstance(parameter.ParameterType.GetElementType(), 0) : null;
                    }
                    method.Invoke(null, arguments);
                    break;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("注册控制台指令 saveedit 失败：" + ex.Message);
            }
        }

        public void DrawPage(Rect area)
        {
            _view.Draw(area);
        }

        /// <summary>给管理器的界面冒烟测试调用。</summary>
        public void SmokeLoad() => _view.SmokeLoad();

        public void SmokeStop() => _view.SmokeStop();

        public void SmokeLimb() => _view.SmokeLimb();

        public bool DrawSettings(Rect area)
        {
            return false;
        }

        private void Update()
        {
            try
            {
                if (DdenHub.ManagerLoaded)
                {
                    DdenBlocker.Hide();
                    return;
                }

                if (_standaloneKey != KeyCode.None && Input.GetKeyDown(_standaloneKey)) _standaloneVisible = !_standaloneVisible;

                if (_standaloneVisible) DdenBlocker.Show(0.12f);
                else DdenBlocker.Hide();
                DdenHub.SetPanelVisible("saveeditor", _standaloneVisible);
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "Update");
            }
        }

        private void OnGUI()
        {
            if (DdenHub.ManagerLoaded || !_standaloneVisible) return;
            try
            {
                float scale = Mathf.Clamp(CfgScale.Value, 0.75f, 2f);
                Matrix4x4 old = GUI.matrix;
                GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

                var rect = new Rect(40f, 40f, Screen.width / scale - 80f, Screen.height / scale - 80f);
                _standaloneRect = new Rect(rect.x * scale, rect.y * scale, rect.width * scale, rect.height * scale);
                GUI.Window(0x53415645, rect, _ =>
                {
                    DdenUI.Fill(new Rect(0, 0, rect.width, rect.height), DdenUI.Bg);
                    DdenUI.Fill(new Rect(0, 0, rect.width, 30f), DdenUI.Panel);
                    DdenUI.Fill(new Rect(0, 29f, rect.width, 1f), DdenUI.AccentDim);
                    DdenUI.Label(new Rect(10, 5, rect.width - 70f, 22), "DDEN · 存档编辑器", DdenUI.Title);
                    if (DdenUI.Button(new Rect(rect.width - 28f, 6f, 20f, 18f), "×")) _standaloneVisible = false;
                    _view.Draw(new Rect(8, 34, rect.width - 16, rect.height - 42));
                    GUI.DragWindow(new Rect(0, 0, rect.width, 30f));
                }, GUIContent.none, GUIStyle.none);

                GUI.matrix = old;
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "OnGUI");
                _standaloneVisible = false;
            }
        }
    }
}
