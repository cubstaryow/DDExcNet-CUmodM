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
    /// 只在主机生效 —— 联机里购买结算只在主机跑（客机 <c>TraderScript.TryPurchase</c> 的 prefix 只发包、
    /// 不本地生成），走到 <c>Together.TraderCommit.RunBuy</c> → <c>TryPurchase_rewritten</c> →
    /// <c>Utils.Create(titem.id, …)</c>。给 <c>RunBuy</c> 挂前后缀：窗口内由 <c>Utils.Create</c> 前缀
    /// 记下生成的 id（<see cref="Utils_Create_ModItemPatch"/> 里的 NoteId），窗口后再造一件给买家。
    ///
    /// 复制那件必须像原版一样“走完联机的登记流程”，否则**买家根本看不到它**：
    ///  ① <c>NetObjectRegistry.Server_EnsureItemIsNetworkRegistered</c>（原版 RunBuy:142460 的同一句）
    ///  ② 赠品弹药保持预制体默认满弹匣（用户指定）
    ///  ③ 拾取进买家背包；拾不进就登记好放地上（已登记，客机收得到）
    ///  ④ <c>SafeNewGO</c> 把状态推给客机
    ///
    /// 默认 **关闭**；开关名就叫「道生一 一生二」，放在「娱乐」分组里。
    /// 任何异常只记 Debug，绝不影响正常购买。
    /// </summary>
    internal static class DoublePurchase
    {
        /// <summary>购买窗口：从 RunBuy 前缀开到后缀关，另给 2 秒余量（RunBuy 抛异常时 postfix 不执行）。</summary>
        private const float WindowSeconds = 2f;

        private static float _windowUntil = -99f;
        private static string _lastId;

        internal static bool InPurchaseWindow => Time.unscaledTime <= _windowUntil;

        /// <summary>赠送那件自己创建时置真：避免被「20% 合成复制」再复制一次（否则偶尔出 3 件）。</summary>
        internal static bool SuppressCraftDuplicate;
        private sealed class BuyState { internal float Until; internal string Id; }

        internal static void NoteId(string id)
        {
            if (Time.unscaledTime > _windowUntil) return;
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
                    postfix: new HarmonyMethod(typeof(DoublePurchase), nameof(AfterBuy)),
                    finalizer: new HarmonyMethod(typeof(DoublePurchase), nameof(FinallyBuy)));
                MPfixPlugin.Log.Info("已启用补丁：商人购买结算观测（供「道生一 一生二」使用）");
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Warn("「道生一 一生二」补丁安装失败（不影响游戏）：" + ex.Message);
            }
        }

        /// <summary>参数名必须与 Together.TraderCommit.RunBuy(trader, actor, job) 一致，Harmony 按名字注入。</summary>
        private static void BeforeBuy(TraderScript trader, Body actor, out BuyState __state)
        {
            __state = new BuyState { Until = _windowUntil, Id = _lastId };
            _windowUntil = Time.unscaledTime + WindowSeconds;
            _lastId = null;
        }

        private static Exception FinallyBuy(Exception __exception, BuyState __state)
        { _windowUntil = __state?.Until ?? -99f; _lastId = __state?.Id; return __exception; }

        private static void AfterBuy(TraderScript trader, Body actor)
        {
            _windowUntil = -99f;

            try
            {
                if (!MPfixPlugin.Active) return;
                if (!MPfixPlugin.CfgDoublePurchase.Value) return;
                if (!Mp.IsRunning || !Mp.IsServer) return;
                if (actor == null || string.IsNullOrEmpty(_lastId)) return;

                string id = _lastId;
                _lastId = null;

                // 和原版同一个落点（TryPurchase_rewritten:144179 用的是商人位置；这里给买家脚下，观感更合理）
                SuppressCraftDuplicate = true;
                GameObject extra;
                try { extra = Utils.Create(id, actor.transform.position, 0f); }
                finally { SuppressCraftDuplicate = false; }
                if (extra == null)
                {
                    MPfixPlugin.Log.Debug($"[道生一] 额外那一件 '{id}' 生成失败（不影响正常购买；" +
                                          "模组物品需要「联机合成/FixModItemCraftSpawn」开着才能造出来）");
                    return;
                }

                Item extraItem = extra.GetComponent<Item>();
                if (extraItem == null)
                {
                    MPfixPlugin.Log.Debug($"[道生一] 额外那一件 '{id}' 没有 Item 组件，已放弃（不销毁任何对象）。");
                    return;
                }

                MirrorVanillaExtras(extra);

                // ★ 先登记网络身份，客机才看得到；否则主机多一件幽灵、买家什么都没有
                Utils_Create_ModItemPatch.RegisterOnHost(extra);

                try { actor.AutoPickUpItem(extraItem); }
                catch (Exception ex) { MPfixPlugin.Log.Debug("[道生一] 拾取失败：" + ex.Message); }

                if (!InBody(extraItem, actor))
                {
                    // 拾不进背包（满了/不可拿）→ 保持在地面，但已经是登记过的物品，客机会收到
                    extra.transform.SetParent(null, true);
                    ItemPhysicsGuard.Guard(extraItem, "double-purchase");
                    MPfixPlugin.Log.Info($"[道生一 一生二] 商人购买 '{id}'：额外奉送一份（背包满，已放在脚下）。");
                }
                else
                {
                    MPfixPlugin.Log.Info($"[道生一 一生二] 商人购买 '{id}'：额外奉送一份。");
                }

                Utils_Create_ModItemPatch.SyncNow(extra);
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Debug("「道生一 一生二」处理失败：" + ex.Message);
            }
        }

        /// <summary>
        /// 复制品的收尾：只补 <c>BoughtItem</c> 标记（原版 144180；它只是个 300 秒自毁的空壳组件，不影响玩法）。
        ///
        /// **弹药量故意不抄原版**：原版 <c>TryPurchase_rewritten:144184</c> 会把弹匣随机成 50%~100%，
        /// 但用户口径是“商人卖出来就是满的”，所以这里**保持预制体默认（满弹匣）**，不要改成随机量。
        /// </summary>
        private static void MirrorVanillaExtras(GameObject go)
        {
            try
            {
                go.AddComponent<BoughtItem>();
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Debug("[道生一] 补齐 BoughtItem 标记失败：" + ex.Message);
            }
        }

        private static bool InBody(Item item, Body body)
        {
            try
            {
                Transform t = item.transform;
                if (t == null || t.parent == null) return false;
                return t.parent.GetComponentInParent<Body>() == body;
            }
            catch
            {
                return false;
            }
        }
    }
}
