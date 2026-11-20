using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace DdenExternalProbe
{
    /// <summary>仅用于端到端验证 DDExcNet CUmodM 的「交出配置」约定（L1）。验证后会删除这个插件。</summary>
    [BepInPlugin("com.dden.probe.externalconfig", "DDEN 外部配置探针", "0.0.1")]
    public class ProbePlugin : BaseUnityPlugin
    {
        public static ProbePlugin Instance;

        private void Awake()
        {
            Instance = this;
            Config.Bind("探针", "示例开关", true, "验证外部模组的配置能被管理器收集并渲染。");
            Config.Bind("探针", "示例数值", 12.5f, "浮点示例。");
            Config.Bind("高级", "示例热键", new KeyboardShortcut(KeyCode.F7), "热键示例。");
            Logger.LogInfo("[Probe] 已加载（外部配置探针）");
        }
    }

    /// <summary>约定类型：不需要引用 DDEN.Shared，管理器靠反射认识它。</summary>
    public static class DdenBridge
    {
        public static ConfigFile DdenConfig => ProbePlugin.Instance.Config;

        public static string DdenTitle => "外部探针模组";

        public static string DdenSummary => "验证 OK";

        public static void DdenDraw(Rect area)
        {
            GUI.Label(area, "这是外部模组自己画的页面（DdenDraw）。");
        }
    }
}
