using System;
using System.Collections.Generic;
using BepInEx.Configuration;

namespace DDEN.Shared
{
    /// <summary>
    /// DDEN 系列模组的统一模块契约：每个插件实现一份，注册进 <see cref="DdenHub"/>，
    /// 由 DDExcNet-CUmodM 统一渲染“功能界面 + 配置界面”。
    /// </summary>
    /// <summary>模块可选实现：返回 false 时管理器把它的配置项整体置灰、不可编辑（例：总开关关闭 / 环境版本闸门拦住）。
    /// <summary>模块健康度：管理器据此给侧边栏/总览上色（黄=有错误、红=严重错误/已终止）。</summary>
    public enum DdenHealth
    {
        Ok = 0,
        Warn = 1,
        Severe = 2
    }

    /// <summary>模块可选实现：报告自己的健康度与原因（例：功能崩溃、版本核对未通过）。</summary>
    public interface IDdenModuleHealth
    {
        DdenHealth Health { get; }
        string HealthNote { get; }
    }

    public interface IDdenModuleGate
    {
        bool ModuleEnabled { get; }

        /// <summary>主开关的完整键名（`分区/键名`）。模块关闭时这一行**仍然可点** —— 否则关掉就再也打不开。</summary>
        string MasterKey { get; }
    }

    /// <summary>Optional per-entry policy, e.g. temporary host-authoritative values without overwriting local preferences.</summary>
    public interface IDdenConfigPolicy
    {
        bool CanEditConfig(ConfigEntryBase entry, bool normallyInteractive);
        object ConfigDisplayValue(ConfigEntryBase entry);
        string ConfigNotice { get; }
    }

    public interface IDdenModule
    {
        /// <summary>短 id（小写，唯一），用于导航与 URL 式跳转。</summary>
        string Id { get; }

        /// <summary>侧边栏显示名。</summary>
        string DisplayName { get; }

        string Version { get; }

        /// <summary>一行状态（显示在总览列表里）。</summary>
        string Summary { get; }

        /// <summary>依赖是否满足（例如 CUCoreLib 缺失）。</summary>
        bool Ready { get; }

        /// <summary>功能界面：hub 把内容区交给模块自己画。</summary>
        void DrawPage(UnityEngine.Rect area);

        /// <summary>配置界面：模块可以自绘；返回 false 时 hub 用通用渲染器列出 ConfigEntries。</summary>
        bool DrawSettings(UnityEngine.Rect area);

        /// <summary>参与统一配置页的条目（一般是 BaseUnityPlugin.Config 的值集合）。</summary>
        IEnumerable<ConfigEntryBase> ConfigEntries { get; }
    }

    /// <summary>模块注册中心（放在 Shared 里，插件与管理器共享同一份静态状态）。</summary>
    public static class DdenHub
    {
        private static readonly List<IDdenModule> _modules = new List<IDdenModule>();

        public static IReadOnlyList<IDdenModule> Modules => _modules;

        /// <summary>管理器插件加载后置 true；为 false 时模块自己退化成独立小窗。</summary>
        public static bool ManagerLoaded { get; private set; }

        public static event Action Changed;

        /// <summary>管理器请求跳到某个模块页（模块可广播，管理器监听）。</summary>
        public static event Action<string> OpenRequested;

        public static void Register(IDdenModule module)
        {
            if (module == null) return;
            foreach (IDdenModule existing in _modules)
            {
                if (existing.Id == module.Id) return;
            }
            _modules.Add(module);
            Safe(Changed);
        }

        public static void MarkManagerLoaded()
        {
            ManagerLoaded = true;
            Safe(Changed);
        }

        public static void OpenModule(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            Action<string> handler = OpenRequested;
            if (handler != null) handler(id);
        }

        public static IDdenModule Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (IDdenModule module in _modules)
            {
                if (string.Equals(module.Id, id, StringComparison.OrdinalIgnoreCase)) return module;
            }
            return null;
        }

        private static void Safe(Action action)
        {
            try { action?.Invoke(); }
            catch (Exception ex) { Log.Warn("DdenHub 事件回调异常：" + ex.Message); }
        }

        // ===== 面板可见性（供 Unity 侧补丁判断“现在能不能点游戏”）=====

        private static readonly HashSet<string> OpenPanels = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>是否有任意 DDEN 面板正在显示。</summary>
        public static bool AnyPanelVisible
        {
            get { return OpenPanels.Count > 0; }
        }

        public static void SetPanelVisible(string owner, bool visible)
        {
            if (string.IsNullOrEmpty(owner)) return;
            if (visible) OpenPanels.Add(owner);
            else OpenPanels.Remove(owner);
        }
    }

    public static class ConfigExt
    {
        /// <summary>把 ConfigFile 摊平成条目序列（BepInEx 的 ConfigFile 本身是 KeyValuePair 序列）。</summary>
        public static IEnumerable<ConfigEntryBase> Entries(this ConfigFile file)
        {
            if (file == null) yield break;
            foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> pair in file)
            {
                yield return pair.Value;
            }
        }
    }
}
