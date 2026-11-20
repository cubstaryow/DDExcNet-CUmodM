using System;
using System.Reflection;
using DDEN.Shared;
using HarmonyLib;
using UnityEngine;

namespace DDEN.MPfix
{
    /// <summary>
    /// 娱乐开关「道生一 一生二」：**商人购买时多给一份**。
    ///
    /// 只在主机生效（购买结算在主机做）。做法：
    ///  - 给 Together 的 <c>TraderCommit.RunBuy</c> 挂前后缀：前缀开“购买窗口”，
    ///  - 这个窗口里 <c>Utils.Create</c> 被调用时记下生成的物品 id（见 <see cref="Utils_Create_ModItemPatch"/> 里的 NoteId），
    ///  - 后缀在窗口关闭后，用同一个 id 在买家脚下再生成一件并自动拾取。
    ///
    /// 默认 **关闭**；开关名就叫「道生一 一生二」，放在「娱乐」分组里。
    /// 任何异常只记 Debug，绝不影响正常购买。
    /// </summary>
    internal static class DoublePurchase
    {
        private static bool _inBuy;
        private static string _lastId;
        private static float _lastAt = -99f;

        internal static bool IsBuying => _inBuy;

        internal static void NoteId(string id)
        {
            if (!_inBuy) return;
            if (string.IsNullOrEmpty(id)) return;
            _lastId = id;
        }

        internal static void Install(Harmony harmony)
        {
            try
            {
                Type tc = AccessTools.TypeByName("Together.TraderCommit");
                MethodInfo runBuy = tc == null ? null : AccessTools.Method(tc, "RunBuy");
                if (runBuy == null)
                {
                    MPfixPlugin.Log.Debug("未找到 Together.TraderCommit.RunBuy，「道生一 一生二」不可用。");
                    return;
                }

                harmony.Patch(runBuy,
                    prefix: new HarmonyMethod(typeof(DoublePurchase), nameof(BeforeBuy)),
                    postfix: new HarmonyMethod(typeof(DoublePurchase), nameof(AfterBuy)));
                MPfixPlugin.Log.Info("已启用补丁：商人购买结算观测（供「道生一 一生二」使用）");
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Warn("「道生一 一生二」补丁安装失败（不影响游戏）：" + ex.Message);
            }
        }

        private static void BeforeBuy()
        {
            _inBuy = true;
            _lastId = null;
        }

        private static void AfterBuy(TraderScript trader, Body actor)
        {
            _inBuy = false;

            try
            {
                if (!MPfixPlugin.CfgDoublePurchase.Value) return;
                if (!Mp.IsRunning || !Mp.IsServer) return;
                if (actor == null || string.IsNullOrEmpty(_lastId)) return;

                float now = Time.unscaledTime;
                if (now - _lastAt < 0.05f) return;      // 同一帧重复回调保护
                _lastAt = now;

                string id = _lastId;
                _lastId = null;

                GameObject extra = Utils.Create(id, actor.transform.position, 0f);
                if (extra == null)
                {
                    MPfixPlugin.Log.Debug($"[道生一] 额外那一件 '{id}' 生成失败（不影响正常购买）");
                    return;
                }

                Item extraItem = extra.GetComponent<Item>();
                if (extraItem == null) return;

                actor.AutoPickUpItem(extraItem);
                MPfixPlugin.Log.Info($"[道生一 一生二] 商人购买 '{id}'：额外奉送一份。");
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Debug("「道生一 一生二」处理失败：" + ex.Message);
            }
        }
    }
}
