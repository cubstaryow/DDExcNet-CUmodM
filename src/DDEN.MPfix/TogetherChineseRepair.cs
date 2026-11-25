using System;
using System.Collections.Generic;
using System.Reflection;
using DDEN.Shared;
using HarmonyLib;
using UnityEngine;

namespace DDEN.MPfix
{
    internal static class TogetherChineseRepair
    {
        private static Func<string> _locale;
        private static Dictionary<string, Dictionary<string, string>> _dictionaries;
        internal static void Install(Harmony harmony)
        {
            var lang = Reflect.Type("Together.Lang");
            var locale = AccessTools.PropertyGetter(lang, "CurrentLocale");
            var get = AccessTools.Method(lang, "Get", new[] { typeof(string).MakeByRefType(), typeof(bool).MakeByRefType() });
            if (locale == null || get == null) throw new MissingMethodException("Together 5.5.5 language contract");
            _locale = (Func<string>)Delegate.CreateDelegate(typeof(Func<string>), locale);
            _dictionaries = AccessTools.Field(lang, "dict")?.GetValue(null) as Dictionary<string, Dictionary<string, string>>;
            harmony.Patch(get, postfix: new HarmonyMethod(typeof(TogetherChineseRepair), nameof(Translate)));
            var label = AccessTools.Method(Reflect.Type("Together.UIManager"), "SetUITooltip");
            if (label != null) harmony.Patch(label, postfix: new HarmonyMethod(typeof(TogetherChineseRepair), nameof(PrepareLabel)));
            var style = AccessTools.Method(Reflect.Type("Together.UIBullshit"), "DoFontThings", new[] { typeof(GUIStyle) });
            if (style != null) harmony.Patch(style, postfix: new HarmonyMethod(typeof(TogetherChineseRepair), nameof(PrepareStyle)));
        }
        private static void Translate(string __0, ref string __result)
        {
            if (!PlayerNameRepair.FontEnabled) return;
            try
            {
                if (string.Equals(_locale(), "zh-CN", StringComparison.OrdinalIgnoreCase))
                {
                    if (_dictionaries != null && _dictionaries.TryGetValue("zh-CN", out var chinese)
                        && chinese.TryGetValue(__0, out var value) && !string.IsNullOrWhiteSpace(value)) __result = value;
                    else if (__0 == "rule_name_RagdollImpact") __result = "布娃娃冲击";
                    else if (__0 == "rule_desc_RagdollImpact") __result = "倒下的玩家可撞伤下方玩家；按键锁定玩家可抵消此伤害。";
                }
                PlayerNameRepair.ObserveText(__result);
            }
            catch (Exception ex) { MPfixPlugin.Log.Debug("Together Chinese display: " + ex.GetType().Name); }
        }
        private static void PrepareLabel(TMPro.TMP_Text __0)
        { if (PlayerNameRepair.FontEnabled) PlayerNameFonts.Prepare(__0); }
        private static void PrepareStyle(GUIStyle style) => PlayerNameRepair.PrepareGuiStyle(style);
    }
}
