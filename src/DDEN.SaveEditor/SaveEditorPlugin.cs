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
    [BepInPlugin(Guid, "DDEN Save Editor", "2.0.1")]
    [BepInDependency("net.cucorelib", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("org.bepinex.plugins.qol_unknown", BepInDependency.DependencyFlags.SoftDependency)]
    public class SaveEditorPlugin : BaseUnityPlugin, IDdenModule, IDdenModuleVersion
    {
        public const string Guid = "com.dden.saveeditor";
        private const string Title = "存档编辑器";

        internal static SaveEditorPlugin Instance;
        internal static ManualLogSource LogSource;
        internal static LogChannel Log;


        private readonly EditorView _view = new EditorView();

        // ===== IDdenModule =====
        public string Id => "saveeditor";
        public string DisplayName => Title;
        public string MinimumManagerVersion => "2.0.0";
        public string TestedManagerVersion => "2.1.0";
        public string MinimumSharedVersion => "2.0.1";
        public string TestedSharedVersion => "2.0.1";
        public int RequiredSharedApiVersion => DdenApi.Version;
        // The loader accepts numeric versions only; the release and module UI retain the alpha tag.
        public const string ReleaseVersion = "2.0.1-alpha";
        public string Version => ReleaseVersion;
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

            Log = DDEN.Shared.Log.For(LogSource, "SaveEditor");
            ConfigMigration.Retire(Config, "UI/StandaloneKey", "UI/Scale", "Debug/Verbose");
            DdenHub.Register(this);

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
                    DdenHub.OpenModule(Id);
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

    }
}
