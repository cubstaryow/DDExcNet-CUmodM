using System;
using DDEN.Shared;
using HarmonyLib;
using UnityEngine;

namespace DDExcNet.CUmodM
{
    /// <summary>
    /// 主菜单的按钮不是 uGUI Button，而是 <c>AdaptiveButton.Update()</c> 里直接读
    /// <c>Input.GetMouseButtonDown(0)</c> 再调 <c>Clicked()</c> —— 这条路既不经过 EventSystem，
    /// 也不吃 uGUI 遮罩，所以之前两种拦截都拦不住。
    /// 这里在 <c>Clicked()</c> 上加前缀：DDEN 面板显示期间直接返回 false，点不到「开始 / 退出」。
    /// </summary>
    [HarmonyPatch(typeof(AdaptiveButton), "Clicked")]
    internal static class AdaptiveButton_ClickGuard
    {
        private static float _lastLogAt = -99f;

        private static bool Prefix(AdaptiveButton __instance)
        {
            try
            {
                if (!DdenHub.AnyPanelVisible) return true;
                if (!HubPlugin.CfgModalBlocker.Value) return true;

                if (Time.unscaledTime - _lastLogAt > 1f)
                {
                    _lastLogAt = Time.unscaledTime;
                    string action = "?";
                    try { action = __instance.action.ToString(); } catch { /* ignored */ }
                    HubPlugin.Log.Info($"[UIGuard] 面板打开，已拦截菜单按钮点击（{action}）");
                }
                return false;
            }
            catch (Exception ex)
            {
                HubPlugin.Log.Debug("UIGuard 前缀异常：" + ex.Message);
                return true;
            }
        }
    }
}
