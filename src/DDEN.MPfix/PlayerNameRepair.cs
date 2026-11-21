using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using DDEN.Shared;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace DDEN.MPfix
{
    /// <summary>Display aliases only. Never write ScavPlayer.playerName, SyncString, SteamID, or the join packet.</summary>
    internal static class PlayerNameRepair
    {
        private sealed class State
        {
            public State() { }
            internal string Raw, Native, Display;
            internal ulong Steam;
            internal float RetryAt;
            internal object Body;
        }
        private struct GuiFonts
        {
            internal GUISkin Skin;
            internal Font Label, Box, Button;
        }
        private static ConditionalWeakTable<object, State> _states = new ConditionalWeakTable<object, State>();
        private static Type _player;
        private static FieldInfo _nativeName, _steam, _body, _color, _gameFont;
        private static PropertyInfo _allPlayers;
        private static MethodInfo _persona, _netApply;
        private static float _nextTick;
        private static bool _wasEnabled, _installed, _needsUnicode;
        internal static bool Enabled => _installed && MPfixPlugin.Active && MPfixPlugin.CfgFixPlayerNames.Value && Mp.IsRunning;

        internal static bool Install(Harmony harmony)
        {
            try
            {
                _player = Reflect.Type("Together.ScavPlayer");
                _nativeName = AccessTools.Field(_player, "playerName");
                _steam = AccessTools.Field(_player, "SteamId");
                _body = AccessTools.Field(_player, "body");
                _color = AccessTools.Field(_player, "playerColor");
                _gameFont = AccessTools.Field(Reflect.Type("Together.CoopModAssets"), "gamefont");
                _allPlayers = AccessTools.Property(_player, "AllPlayers");
                _persona = AccessTools.Method(Reflect.Type("Together.KSteam"), "GetSteamUsername", new[] { typeof(ulong), typeof(bool) });
                var apply = AccessTools.Method(_player, "ApplyNameAndColor");
                _netApply = AccessTools.Method(Reflect.Type("Together.NetBody"), "ApplyNameAndColor", new[] { typeof(string), typeof(Color) });
                var visual = AccessTools.Method(Reflect.Type("Together.CharStatusVisuals"), "ApplyNameAndColor", Type.EmptyTypes);
                if (_nativeName == null || _steam == null || _body == null || _color == null || _allPlayers == null
                    || apply == null || _netApply == null || visual == null) return false;
                harmony.Patch(apply, prefix: new HarmonyMethod(typeof(PlayerNameRepair), nameof(Capture)));
                harmony.Patch(_netApply, prefix: new HarmonyMethod(typeof(PlayerNameRepair), nameof(BodyName)));
                harmony.Patch(visual, postfix: new HarmonyMethod(typeof(PlayerNameRepair), nameof(PrepareNametag)));
                var personaChanged = AccessTools.Method(Reflect.Type("Together.KSteam"), "OnPersonaStateChange");
                if (personaChanged != null) harmony.Patch(personaChanged, postfix: new HarmonyMethod(typeof(PlayerNameRepair), nameof(Invalidate)));

                // Only presentation reads. Identity matching, saves, bans and console lookup keep native names.
                foreach (var spec in new[] {
                    ("Together.UIMainMenu", "_GUI___DrawPlayerInList"),
                    ("Together.UIMainMenu", "_GUI__PlrListDrawPlr"),
                    ("Together.UIInGame", "_GUI__DrawPlayerDirection"),
                    ("Together.UIInGame", "_GUI_DoGeneralMPGui"),
                    ("Together.UIInGame", "_GUI_PlayerInteractionGui"),
                    ("Together.UI.TeammateList.Roster", "Touch"),
                    ("Together.UI.TeammateList.Roster", "ReadBlood"),
                    ("Together.MinigameMPManager+MinigameRemotePeerHand", "Start"),
                    ("Together.Chat", "Client_ChatMessageReceive"),
                    ("Together.Chat", "_GUI_DrawChat") })
                {
                    var method = AccessTools.Method(Reflect.Type(spec.Item1), spec.Item2);
                    if (method != null)
                    {
                        harmony.Patch(method, transpiler: new HarmonyMethod(typeof(PlayerNameRepair), nameof(DisplayReads)));
                        if (spec.Item1 == "Together.UIMainMenu")
                            harmony.Patch(method, prefix: new HarmonyMethod(typeof(PlayerNameRepair), nameof(BeginGui)),
                                finalizer: new HarmonyMethod(typeof(PlayerNameRepair), nameof(EndGui)));
                        if (spec.Item1.Contains("MinigameRemotePeerHand"))
                            harmony.Patch(method, postfix: new HarmonyMethod(typeof(PlayerNameRepair), nameof(PrepareHandName)));
                    }
                }
                Type message = Reflect.Type("Together.Chat+ChatMsgContainer");
                if (message != null)
                {
                    foreach (var ctor in message.GetConstructors()) harmony.Patch(ctor, transpiler: new HarmonyMethod(typeof(PlayerNameRepair), nameof(DisplayReads)));
                    var compile = AccessTools.Method(message, "Compile");
                    if (compile != null) harmony.Patch(compile, prefix: new HarmonyMethod(typeof(PlayerNameRepair), nameof(ChatName)));
                }
                var gameFont = AccessTools.Method(Reflect.Type("Together.UI.TeammateList.Roster"), "GameFont");
                if (gameFont != null) harmony.Patch(gameFont, postfix: new HarmonyMethod(typeof(PlayerNameRepair), nameof(GameFont)));
                var chatFont = AccessTools.Method(Reflect.Type("Together.UIBullshit"), "FocusedChatFont");
                if (chatFont != null) harmony.Patch(chatFont, postfix: new HarmonyMethod(typeof(PlayerNameRepair), nameof(GameFont)));
                _installed = true;
                return true;
            }
            catch (Exception ex) { MPfixPlugin.Log.Exception(ex, "安装名称显示修复"); return false; }
        }

        private static void Capture(object __instance, string name)
        {
            try
            {
                var state = _states.GetOrCreateValue(__instance);
                state.Raw = name; state.Display = null;
            }
            catch (Exception ex) { MPfixPlugin.Log.Debug("[名称显示] 记录名称失败：" + ex.Message); }
        }
        internal static string Display(object player)
        {
            if (player == null || player is UnityEngine.Object obj && obj == null) return "";
            string native = "";
            try
            {
                native = _nativeName?.GetValue(player) as string ?? "";
                if (!Enabled) return native;
                var state = _states.GetOrCreateValue(player);
                ulong steam = _steam.GetValue(player) is ulong value ? value : 0;
                float now = Time.unscaledTime;
                if (state.Display != null && state.Native == native && state.Steam == steam && now < state.RetryAt) return Track(state.Display);
                string profile = steam != 0 && _persona != null ? _persona.Invoke(null, new object[] { steam, false }) as string : null;
                state.Display = NameDisplayRules.Choose(state.Raw ?? native, profile, steam);
                state.Native = native; state.Steam = steam; state.RetryAt = now + 5f;
                return Track(state.Display);
            }
            catch (Exception ex) { MPfixPlugin.Log.Debug("[名称显示] 读取名称失败：" + ex.Message); return native; }
        }
        private static void BodyName(object __instance, ref string name)
        {
            try
            {
                if (!Enabled) return;
                object player = Reflect.GetInstance(__instance, "plr");
                if (player != null) name = Display(player);
            }
            catch (Exception ex) { MPfixPlugin.Log.Debug("[名称显示] 更新名牌失败：" + ex.Message); }
        }
        private static void PrepareNametag(object __instance)
        {
            try
            {
                if (!Enabled) return;
                var tag = Reflect.GetInstance(__instance, "nametag") as GameObject;
                if (tag != null) PlayerNameFonts.Prepare(tag.GetComponent<TMP_Text>());
            }
            catch (Exception ex) { MPfixPlugin.Log.Debug("[名称显示] 补充名称字体失败：" + ex.Message); }
        }
        private static void ChatName(object __instance)
        {
            try
            {
                if (!Enabled) return;
                object player = Reflect.GetInstance(__instance, "plr");
                if (player != null) Reflect.TrySetInstance(__instance, "name", GuiDisplay(player));
            }
            catch (Exception ex) { MPfixPlugin.Log.Debug("[名称显示] 更新聊天名称失败：" + ex.Message); }
        }
        private static void PrepareHandName(object __instance)
        {
            try
            {
                if (!Enabled || !(__instance is Component hand) || hand == null) return;
                foreach (var label in hand.GetComponentsInChildren<TMP_Text>(true))
                    if (label.gameObject.name == "RemoteHandPlayerName") PlayerNameFonts.Prepare(label);
            }
            catch (Exception ex) { MPfixPlugin.Log.Debug("[名称显示] 更新协作名称字体失败：" + ex.Message); }
        }
        private static void BeginGui(out GuiFonts __state)
        {
            __state = default;
            try
            {
                if (!Enabled || !_needsUnicode || GUI.skin == null) return;
                var skin = GUI.skin;
                var replacement = PlayerNameFonts.GuiFont(skin.label.font);
                if (replacement == null) return;
                __state = new GuiFonts { Skin = skin, Label = skin.label.font, Box = skin.box.font, Button = skin.button.font };
                skin.label.font = skin.box.font = skin.button.font = replacement;
            }
            catch (Exception ex) { MPfixPlugin.Log.Debug("[名称显示] 切换名单字体失败：" + ex.Message); }
        }
        private static Exception EndGui(Exception __exception, GuiFonts __state)
        {
            try
            {
                if (__state.Skin != null)
                { __state.Skin.label.font = __state.Label; __state.Skin.box.font = __state.Box; __state.Skin.button.font = __state.Button; }
            }
            catch (Exception ex) { MPfixPlugin.Log.Debug("[名称显示] 恢复名单字体失败：" + ex.Message); }
            return __exception;
        }
        private static string GuiDisplay(object player)
        {
            string name = Display(player);
            if (!Enabled) return name;
            try { return PlayerNameFonts.GuiName(name, _gameFont?.GetValue(null) as Font); }
            catch (Exception ex) { MPfixPlugin.Log.Debug("[名称显示] 检查名单字形失败：" + ex.Message); return name; }
        }
        private static string GuiBodyName(object body)
        {
            string name = Reflect.GetInstance(body, "playerName") as string ?? "";
            try
            {
                object player = Reflect.GetInstance(body, "plr");
                if (player != null) return GuiDisplay(player);
                return Enabled ? PlayerNameFonts.GuiName(name, _gameFont?.GetValue(null) as Font) : name;
            }
            catch (Exception ex) { MPfixPlugin.Log.Debug("[名称显示] 检查方向提示字形失败：" + ex.Message); return name; }
        }
        private static IEnumerable<CodeInstruction> DisplayReads(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            bool gui = !(__originalMethod.DeclaringType?.FullName?.Contains("MinigameRemotePeerHand") ?? false);
            var display = AccessTools.Method(typeof(PlayerNameRepair), gui ? nameof(GuiDisplay) : nameof(Display));
            var bodyName = AccessTools.Method(typeof(PlayerNameRepair), nameof(GuiBodyName));
            var guiFont = AccessTools.Method(typeof(PlayerNameRepair), nameof(GuiFont));
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldfld && Equals(instruction.operand, _nativeName))
                { instruction.opcode = OpCodes.Call; instruction.operand = display; }
                else if (gui && instruction.opcode == OpCodes.Ldfld && instruction.operand is FieldInfo f && f.Name == "playerName" && f.DeclaringType == _netApply.DeclaringType)
                { instruction.opcode = OpCodes.Call; instruction.operand = bodyName; }
                yield return instruction;
                if (instruction.opcode == OpCodes.Ldsfld && instruction.operand is FieldInfo field
                    && field.FieldType == typeof(Font) && field.Name == "gamefont") yield return new CodeInstruction(OpCodes.Call, guiFont);
            }
        }
        private static string Track(string name)
        {
            PlayerNameFonts.Observe(name);
            foreach (char c in name) if (c > 127) { _needsUnicode = true; break; }
            return name;
        }
        private static Font GuiFont(Font original)
        {
            try { return Enabled && _needsUnicode ? PlayerNameFonts.GuiFont(original) : original; }
            catch (Exception ex) { MPfixPlugin.Log.Debug("[名称显示] 名单字体不可用：" + ex.Message); return original; }
        }
        private static void GameFont(ref Font __result) { __result = GuiFont(__result); }
        internal static void Invalidate()
        {
            _nextTick = 0;
            try { foreach (var state in States()) state.Display = null; }
            catch (Exception ex) { MPfixPlugin.Log.Debug("[名称显示] 刷新名称缓存失败：" + ex.Message); }
        }
        private static IEnumerable<State> States()
        {
            if (_allPlayers?.GetValue(null) is IEnumerable players)
                foreach (object player in players) if (player != null) yield return _states.GetOrCreateValue(player);
        }
        internal static void Tick()
        {
            bool enabled = Enabled;
            if (!enabled && !_wasEnabled) return;
            if (Time.unscaledTime < _nextTick && enabled == _wasEnabled) return;
            _nextTick = Time.unscaledTime + 1f;
            bool toggled = enabled != _wasEnabled; _wasEnabled = enabled; _needsUnicode = false;
            try
            {
                if (_allPlayers?.GetValue(null) is IEnumerable players)
                foreach (object player in players)
                {
                    if (player == null || player is UnityEngine.Object obj && obj == null) continue;
                    var state = _states.GetOrCreateValue(player);
                    string previous = state.Display;
                    string display = Display(player);
                    object body = _body.GetValue(player);
                    if (body is Component component && component != null && (toggled || previous != display || !ReferenceEquals(state.Body, body)))
                    {
                        object netBody = component.GetComponent(_netApply.DeclaringType);
                        if (netBody != null)
                        {
                            // Calling the presentation layer never writes back the logical player name.
                            Color color = ReadColor(_color.GetValue(player));
                            _netApply.Invoke(netBody, new object[] { display, color });
                        }
                    }
                    state.Body = body;
                }
                if (enabled) PlayerNameFonts.Tick();
                else { PlayerNameFonts.Clear(); _states = new ConditionalWeakTable<object, State>(); }
            }
            catch (Exception ex) { MPfixPlugin.Log.Debug("[名称显示] 刷新失败：" + ex.Message); }
        }
        private static Color ReadColor(object color)
        {
            if (color is Color c) return c;
            if (color != null)
                foreach (var convert in color.GetType().GetMethods(BindingFlags.Public | BindingFlags.Static))
                    if (convert.Name == "op_Implicit" && convert.ReturnType == typeof(Color) && convert.GetParameters().Length == 1
                        && convert.GetParameters()[0].ParameterType == color.GetType()) return (Color)convert.Invoke(null, new[] { color });
            return Color.white;
        }
        internal static void Stop() { _wasEnabled = false; _states = new ConditionalWeakTable<object, State>(); PlayerNameFonts.Dispose(); }
    }
}
