using System;
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
        private sealed class LabelState
        {
            public LabelState() { }
            internal object Player;
            internal TMP_Text Label;
            internal string Native, Display, Rendered;
            internal float NextCheck;
            internal int Revision = -1;
        }
        private sealed class ChatState
        {
            public ChatState() { }
            internal NameDisplayCache.Snapshot Sender;
            internal string Native, Compiled;
            internal float NextCheck;
            internal int Revision = -1;
        }
        private struct GuiFonts
        {
            internal GUISkin Skin;
            internal Font Label, Box, Button;
        }
        private static readonly HashSet<string> ObservedText = new HashSet<string>(StringComparer.Ordinal);
        private static readonly NameDisplayCache Names = new NameDisplayCache(LookupPersona);
        private static ConditionalWeakTable<object, LabelState> _labels = new ConditionalWeakTable<object, LabelState>();
        private static readonly List<WeakReference<LabelState>> LabelStates = new List<WeakReference<LabelState>>();
        private static ConditionalWeakTable<object, ChatState> _chat = new ConditionalWeakTable<object, ChatState>();
        private static Type _player;
        private static FieldInfo _nativeName, _steam, _gameFont;
        private static MethodInfo _persona, _netApply, _bodyNameGetter, _tooltipNameGetter, _chatCompile;
        private static bool _wasEnabled, _wasRunning, _installed, _needsUnicode;
        internal static bool FontEnabled => _installed && MPfixPlugin.Active && MPfixPlugin.CfgFixPlayerNames.Value;
        internal static bool Enabled => FontEnabled && Mp.IsRunning;

        internal static bool Install(Harmony harmony)
        {
            try
            {
                _player = Reflect.Type("Together.ScavPlayer");
                _nativeName = AccessTools.Field(_player, "playerName");
                _steam = AccessTools.Field(_player, "SteamId");
                _gameFont = AccessTools.Field(Reflect.Type("Together.CoopModAssets"), "gamefont");
                _persona = AccessTools.Method(Reflect.Type("Together.KSteam"), "GetSteamUsername", new[] { typeof(ulong), typeof(bool) });
                var apply = AccessTools.Method(_player, "ApplyNameAndColor");
                _netApply = AccessTools.Method(Reflect.Type("Together.NetBody"), "ApplyNameAndColor", new[] { typeof(string), typeof(Color) });
                _bodyNameGetter = AccessTools.PropertyGetter(Reflect.Type("Together.NetBody"), "playerName");
                _tooltipNameGetter = AccessTools.PropertyGetter(Reflect.Type("Together.NetBody"), "bodyName");
                var visual = AccessTools.Method(Reflect.Type("Together.CharStatusVisuals"), "ApplyNameAndColor", Type.EmptyTypes);
                if (_nativeName == null || _steam == null
                    || apply == null || _netApply == null || _bodyNameGetter == null || visual == null) return false;
                harmony.Patch(apply, prefix: new HarmonyMethod(typeof(PlayerNameRepair), nameof(Capture)));
                harmony.Patch(visual, postfix: new HarmonyMethod(typeof(PlayerNameRepair), nameof(PrepareNametag)));
                var visualUpdate = AccessTools.Method(visual.DeclaringType, "LateUpdate");
                if (visualUpdate == null) return false;
                harmony.Patch(visualUpdate, postfix: new HarmonyMethod(typeof(PlayerNameRepair), nameof(RefreshNametag)));
                var personaChanged = AccessTools.Method(Reflect.Type("Together.KSteam"), "OnPersonaStateChange");
                if (personaChanged != null) harmony.Patch(personaChanged, postfix: new HarmonyMethod(typeof(PlayerNameRepair), nameof(Invalidate)));

                // Only presentation reads. Identity matching, saves, bans and console lookup keep native names.
                foreach (var spec in new[] {
                    ("Together.UIMainMenu", "_GUI___DrawPlayerInList"),
                    ("Together.UIMainMenu", "_GUI__PlrListDrawPlr"),
                    ("Together.UIInGame", "_GUI__DrawPlayerDirection"),
                    ("Together.UIInGame", "_GUI_DoGeneralMPGui"),
                    ("Together.UIInGame", "_GUI_PlayerInteractionGui"),
                    ("Together.UIInGame", "_UpdateCheckInteractKeybinds"),
                    ("Together.NetBody", "DoMouseHoverTooltip"),
                    ("Together.PlayerCamera_ToggleWoundView_MultiplayerPatch", "WoundView_SetTargetBody"),
                    ("Together.UI.TeammateList.Roster", "Touch"),
                    ("Together.UI.TeammateList.Roster", "ReadBlood"),
                    ("Together.MinigameMPManager+MinigameRemotePeerHand", "Start"),
                    ("Together.Chat", "Client_ChatMessageReceive"),
                    ("Together.Chat", "_GUI_DrawChat") })
                {
                    var method = AccessTools.Method(Reflect.Type(spec.Item1), spec.Item2);
                    if (method != null)
                    {
                        bool hand = spec.Item1.Contains("MinigameRemotePeerHand");
                        if (!hand && spec.Item2 != "WoundView_SetTargetBody") harmony.Patch(method, transpiler: new HarmonyMethod(typeof(PlayerNameRepair), nameof(DisplayReads)));
                        if (spec.Item2 == "WoundView_SetTargetBody")
                            harmony.Patch(method, postfix: new HarmonyMethod(typeof(PlayerNameRepair), nameof(PrepareMedicalTitle)));
                        if (spec.Item1 == "Together.UIMainMenu" || spec.Item2 == "_GUI_DrawChat" || spec.Item2 == "_GUI_DoGeneralMPGui" || spec.Item2 == "_GUI_PlayerInteractionGui")
                            harmony.Patch(method, prefix: new HarmonyMethod(typeof(PlayerNameRepair), nameof(BeginGui)),
                                finalizer: new HarmonyMethod(typeof(PlayerNameRepair), nameof(EndGui)));
                        if (spec.Item2 == "_GUI__DrawPlayerDirection")
                            harmony.Patch(method, prefix: new HarmonyMethod(typeof(PlayerNameRepair), nameof(BeginDirectionGui)),
                                finalizer: new HarmonyMethod(typeof(PlayerNameRepair), nameof(EndGui)));
                        if (hand)
                        {
                            harmony.Patch(method, postfix: new HarmonyMethod(typeof(PlayerNameRepair), nameof(PrepareHandName)));
                            var update = AccessTools.Method(method.DeclaringType, "Update");
                            if (update == null) return false;
                            harmony.Patch(update, postfix: new HarmonyMethod(typeof(PlayerNameRepair), nameof(RefreshNametag)));
                        }
                    }
                }
                // Announcement text is serialized independently of chat sender labels.
                // Resolve names where native MP formats them; never refresh a player/marker or rewrite identity.
                foreach (var spec in new[] {
                    ("Together.ScavPlayer", "Start"),
                    ("Together.ScavPlayer", "OnDestroy"),
                    ("Together.ScavPlayer", "Server_Kick"),
                    ("Together.ServerMain", "UpdateSyncClientsToClients"),
                    ("Together.Lang", "TryParseIndividualTag") })
                {
                    var producer = AccessTools.Method(Reflect.Type(spec.Item1), spec.Item2);
                    if (producer != null) harmony.Patch(producer,
                        transpiler: new HarmonyMethod(typeof(PlayerNameRepair), nameof(AnnouncementReads)));
                }
                Type message = Reflect.Type("Together.Chat+ChatMsgContainer");
                if (message != null)
                {
                    _chatCompile = AccessTools.Method(message, "Compile");
                    var measure = AccessTools.Method(Reflect.Type("Together.Chat"), "MeasureChatLine");
                    if (_chatCompile == null || measure == null) return false;
                    // Preserve the original sender text in constructors; only presentation caches are changed.
                    harmony.Patch(_chatCompile, prefix: new HarmonyMethod(typeof(PlayerNameRepair), nameof(ChatName)),
                        postfix: new HarmonyMethod(typeof(PlayerNameRepair), nameof(ChatCompiled)));
                    harmony.Patch(measure, prefix: new HarmonyMethod(typeof(PlayerNameRepair), nameof(RefreshChatLine)));
                }
                else return false;
                var gameFont = AccessTools.Method(Reflect.Type("Together.UI.TeammateList.Roster"), "GameFont");
                if (gameFont != null) harmony.Patch(gameFont, postfix: new HarmonyMethod(typeof(PlayerNameRepair), nameof(GameFont)));
                var chatFont = AccessTools.Method(Reflect.Type("Together.UIBullshit"), "FocusedChatFont");
                if (chatFont != null) harmony.Patch(chatFont, postfix: new HarmonyMethod(typeof(PlayerNameRepair), nameof(GameFont)));
                var fancy = AccessTools.Method(Reflect.Type("Together.CharStatusVisuals"), "TryMakeNametagFancy");
                if (fancy != null) harmony.Patch(fancy, postfix: new HarmonyMethod(typeof(PlayerNameRepair), nameof(PrepareViewerName)));
                TogetherChineseRepair.Install(harmony);
                _installed = true;
                return true;
            }
            catch (Exception ex) { MPfixPlugin.Log.Exception(ex, "安装名称显示修复"); return false; }
        }

        private static void Capture(object __instance, string name)
        {
            try
            {
                Names.Capture(__instance, name, Steam(__instance));
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
                return Track(Names.Resolve(player, native, Steam(player), Time.unscaledTime));
            }
            catch (Exception ex) { MPfixPlugin.Log.Debug("[名称显示] 读取名称失败：" + ex.Message); return native; }
        }
        private static ulong Steam(object player) => _steam?.GetValue(player) is ulong value ? value : 0;
        private static string LookupPersona(ulong steam) => _persona?.Invoke(null, new object[] { steam, false }) as string;
        private static bool Live(object player) => player != null && !(player is UnityEngine.Object obj && obj == null);
        private static void RefreshNametag(object __instance)
        {
            try
            {
                if (!_labels.TryGetValue(__instance, out var state)) return;
                RefreshLabel(state);
            }
            catch (Exception ex) { MPfixPlugin.Log.Debug("[名称显示] 更新名牌显示失败：" + ex.Message); }
        }
        private static void RefreshLabel(LabelState state)
        {
            try
            {
                if (state.Label == null) return;
                if (!Enabled)
                {
                    RestoreLabel(state);
                    return;
                }
                if (!Live(state.Player) || !state.Label.gameObject.activeInHierarchy) return;
                float now = Time.unscaledTime;
                if (now < state.NextCheck && state.Revision == Names.Revision) return;
                state.NextCheck = now + 0.25f;
                string display = Display(state.Player);
                state.Display = display;
                if (state.Label.text != display) state.Label.text = display;
                PlayerNameFonts.Prepare(state.Label);
                state.Rendered = state.Label.text; state.Revision = Names.Revision;
            }
            catch (Exception ex) { MPfixPlugin.Log.Debug("[名称显示] 更新名牌显示失败：" + ex.Message); }
        }
        private static void PrepareNametag(object __instance)
        {
            try
            {
                object player = Reflect.GetInstance(__instance, "plr");
                if (!Live(player)) return;
                var tag = Reflect.GetInstance(__instance, "nametag") as GameObject;
                var label = tag != null ? tag.GetComponent<TMP_Text>() : null;
                if (label == null) return;
                BindLabel(__instance, player, label);
            }
            catch (Exception ex) { MPfixPlugin.Log.Debug("[名称显示] 补充名称字体失败：" + ex.Message); }
        }
        private static void ChatName(object __instance)
        {
            try
            {
                if (!_chat.TryGetValue(__instance, out var state))
                {
                    if (!Enabled) return;
                    object player = Reflect.GetInstance(__instance, "plr");
                    // Plain server/system text is not an authenticated player identity.
                    string native = Reflect.GetInstance(__instance, "name") as string ?? "";
                    state = _chat.GetOrCreateValue(__instance);
                    state.Native = native; state.Compiled = native;
                    if (Live(player)) state.Sender = Names.CaptureMessage(player, native, _nativeName.GetValue(player) as string ?? native, Steam(player));
                }
                else if (state.Sender == null)
                {
                    // Only an explicit native player binding can turn an unresolved line into a player message.
                    object player = Reflect.GetInstance(__instance, "plr");
                    if (Live(player)) state.Sender = Names.CaptureSnapshot(player, _nativeName.GetValue(player) as string ?? state.Native, Steam(player));
                }
                if (state.Sender == null) return;
                string name = Enabled ? GuiName(Track(Names.Resolve(state.Sender, Time.unscaledTime))) : state.Native;
                Reflect.TrySetInstance(__instance, "name", name);
            }
            catch (Exception ex) { MPfixPlugin.Log.Debug("[名称显示] 更新聊天名称失败：" + ex.Message); }
        }
        private static void ChatCompiled(object __instance)
        {
            if (_chat.TryGetValue(__instance, out var state))
                state.Compiled = Reflect.GetInstance(__instance, "name") as string;
        }
        private static void RefreshChatLine(object text)
        {
            try
            {
                float now = Time.unscaledTime;
                if (_chat.TryGetValue(text, out var cached) && now < cached.NextCheck && cached.Revision == Names.Revision) return;
                ChatName(text);
                if (!_chat.TryGetValue(text, out var state)) return;
                state.NextCheck = now + 0.25f; state.Revision = Names.Revision;
                if (state.Sender == null) return;
                // Chat's outer GUI scope restores fonts; measure and draw use this same chosen font.
                if (Enabled && _needsUnicode && GUI.skin != null)
                {
                    var font = PlayerNameFonts.GuiFont(_gameFont?.GetValue(null) as Font);
                    if (font != null) GUI.skin.label.font = font;
                }
                string name = Reflect.GetInstance(text, "name") as string;
                // Refresh MP's cached name string before it measures the line, only when it changed.
                if (name != state.Compiled) _chatCompile.Invoke(text, null);
            }
            catch (Exception ex) { MPfixPlugin.Log.Debug("[名称显示] 更新聊天缓存失败：" + ex.GetBaseException().Message); }
        }
        private static void PrepareHandName(object __instance)
        {
            try
            {
                if (!(__instance is Component hand) || hand == null) return;
                object player = Reflect.GetInstance(__instance, "plr");
                if (!Live(player)) return;
                foreach (var label in hand.GetComponentsInChildren<TMP_Text>(true))
                    if (label.gameObject.name == "RemoteHandPlayerName") BindLabel(__instance, player, label);
            }
            catch (Exception ex) { MPfixPlugin.Log.Debug("[名称显示] 更新协作名称字体失败：" + ex.Message); }
        }
        private static void BindLabel(object owner, object player, TMP_Text label)
        {
            if (!_labels.TryGetValue(owner, out var state))
            {
                state = _labels.GetOrCreateValue(owner);
                LabelStates.RemoveAll(weak => !weak.TryGetTarget(out var old) || old.Label == null);
                LabelStates.Add(new WeakReference<LabelState>(state));
            }
            state.Player = player; state.Label = label; state.Native = label.text;
            state.NextCheck = 0; state.Revision = -1;
            RefreshNametag(owner);
        }
        private static void RestoreLabel(LabelState state)
        {
            if (state.Label != null && state.Rendered != null
                && (state.Label.text == state.Rendered || state.Label.text == state.Display)) state.Label.text = state.Native;
            state.Display = state.Rendered = null; state.Revision = -1;
        }
        private static void RestoreLabels()
        {
            foreach (var weak in LabelStates) if (weak.TryGetTarget(out var state)) RestoreLabel(state);
        }
        private static void BeginDirectionGui(object nb, out GuiFonts __state)
        {
            // Observe this label before choosing its font, within MP's original GUI call.
            if (Enabled) GuiBodyName(nb);
            BeginGui(out __state);
        }
        private static void BeginGui(out GuiFonts __state)
        {
            __state = default;
            try
            {
                if (!FontEnabled || GUI.skin == null) return;
                var skin = GUI.skin;
                __state = new GuiFonts { Skin = skin, Label = skin.label.font, Box = skin.box.font, Button = skin.button.font };
                if (!_needsUnicode) return;
                var replacement = PlayerNameFonts.GuiFont(skin.label.font);
                if (replacement == null) return;
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
            try { return GuiName(name); }
            catch (Exception ex) { MPfixPlugin.Log.Debug("[名称显示] 检查名单字形失败：" + ex.Message); return name; }
        }
        private static string GuiName(string name) => PlayerNameFonts.GuiName(name, _gameFont?.GetValue(null) as Font);
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
        private static string BodyDisplay(object body, bool tooltip, bool gui)
        {
            string native = Reflect.GetInstance(body, tooltip ? "bodyName" : "playerName") as string ?? "";
            if (!Enabled) return native;
            object player = Reflect.GetInstance(body, "plr");
            if (!Live(player)) return native;
            return gui ? GuiDisplay(player) : Display(player);
        }
        private static string TooltipDisplay(object body) => BodyDisplay(body, true, false);
        private static string TmpBodyDisplay(object body) => BodyDisplay(body, false, false);
        private static string GuiTooltipDisplay(object body) => BodyDisplay(body, true, true);
        internal static void ObserveText(string text)
        {
            if (!FontEnabled || string.IsNullOrEmpty(text) || ObservedText.Contains(text)) return;
            if (ObservedText.Count >= 512) ObservedText.Clear();
            ObservedText.Add(text); Track(text);
        }
        internal static void PrepareGuiStyle(GUIStyle style)
        {
            if (style != null && FontEnabled && _needsUnicode) style.font = PlayerNameFonts.GuiFont(style.font);
        }
        private static void PrepareViewerName(TMP_Text tmp, object plr)
        {
            if (Enabled && tmp != null && Live(plr)) BindLabel(tmp, plr, tmp);
        }
        private static void PrepareMedicalTitle(object b)
        {
            try
            {
                if (!Enabled || !(b is Component body)) return;
                var netBody = body.GetComponent(Reflect.Type("Together.NetBody"));
                object player = Reflect.GetInstance(netBody, "plr");
                bool local = Reflect.GetInstance(netBody, "IsLocal") is bool isLocal && isLocal;
                var view = AccessTools.Field(Reflect.Type("WoundView"), "view")?.GetValue(null);
                foreach (string name in new[] { "nameText", "bodyStatText" })
                    if (Reflect.GetInstance(view, name) is TMP_Text label)
                    {
                        if (!local && Live(player)) BindLabel(label, player, label);
                        else if (_labels.TryGetValue(label, out var old))
                        { old.Player = null; old.Label = null; _labels.Remove(label); }
                    }
            }
            catch (Exception ex) { MPfixPlugin.Log.Debug("[名称显示] 更新医疗名称失败：" + ex.Message); }
        }
        private static IEnumerable<CodeInstruction> DisplayReads(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            string type = __originalMethod.DeclaringType?.FullName ?? "";
            bool gui = type == "Together.UIMainMenu" || type == "Together.Chat" || type == "Together.UIInGame" && __originalMethod.Name.StartsWith("_GUI", StringComparison.Ordinal)
                || type == "Together.UI.TeammateList.Roster";
            const BindingFlags ownMethods = BindingFlags.Static | BindingFlags.NonPublic;
            var display = typeof(PlayerNameRepair).GetMethod(gui ? nameof(GuiDisplay) : nameof(Display), BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var bodyName = typeof(PlayerNameRepair).GetMethod(gui ? nameof(GuiBodyName) : nameof(TmpBodyDisplay), ownMethods);
            var tooltipName = typeof(PlayerNameRepair).GetMethod(gui ? nameof(GuiTooltipDisplay) : nameof(TooltipDisplay), ownMethods);
            var guiFont = typeof(PlayerNameRepair).GetMethod(nameof(GuiFont), ownMethods);
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldfld && Equals(instruction.operand, _nativeName))
                { instruction.opcode = OpCodes.Call; instruction.operand = display; }
                else if (gui && instruction.opcode == OpCodes.Ldfld && instruction.operand is FieldInfo f && f.Name == "playerName" && f.DeclaringType == _netApply.DeclaringType)
                { instruction.opcode = OpCodes.Call; instruction.operand = bodyName; }
                else if (_bodyNameGetter != null && instruction.Calls(_bodyNameGetter))
                { instruction.opcode = OpCodes.Call; instruction.operand = bodyName; }
                else if (_tooltipNameGetter != null && instruction.Calls(_tooltipNameGetter))
                { instruction.opcode = OpCodes.Call; instruction.operand = tooltipName; }
                yield return instruction;
                if (instruction.opcode == OpCodes.Ldsfld && instruction.operand is FieldInfo field
                    && field.FieldType == typeof(Font) && field.Name == "gamefont") yield return new CodeInstruction(OpCodes.Call, guiFont);
            }
        }
        private static IEnumerable<CodeInstruction> AnnouncementReads(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var code = new List<CodeInstruction>(instructions);
            bool languageTag = __originalMethod.DeclaringType?.FullName == "Together.Lang";
            var display = typeof(PlayerNameRepair).GetMethod(languageTag ? nameof(Display) : nameof(GuiDisplay), BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            var tooltip = typeof(PlayerNameRepair).GetMethod(nameof(TooltipDisplay), BindingFlags.NonPublic | BindingFlags.Static);
            var bodyName = typeof(PlayerNameRepair).GetMethod(nameof(TmpBodyDisplay), BindingFlags.NonPublic | BindingFlags.Static);
            bool join = __originalMethod.DeclaringType == _player
                && (__originalMethod.Name == "Start" || __originalMethod.Name == "OnDestroy");
            for (int i = 0; i < code.Count; i++)
            {
                var instruction = code[i];
                if (instruction.opcode == OpCodes.Ldfld && Equals(instruction.operand, _nativeName))
                {
                    // Start also uses the logical name to name the player GameObject. Only the
                    // name argument immediately passed to Chat.AddColorTags is presentation.
                    int next = i + 1;
                    while (next < code.Count && code[next].opcode == OpCodes.Nop) next++;
                    bool displaySite = !join || next < code.Count && code[next].operand is MethodInfo call
                        && call.DeclaringType?.FullName == "Together.Chat" && call.Name == "AddColorTags";
                    if (displaySite) { instruction.opcode = OpCodes.Call; instruction.operand = display; }
                }
                else if (languageTag && _tooltipNameGetter != null && instruction.Calls(_tooltipNameGetter))
                { instruction.opcode = OpCodes.Call; instruction.operand = tooltip; }
                else if (languageTag && _bodyNameGetter != null && instruction.Calls(_bodyNameGetter))
                { instruction.opcode = OpCodes.Call; instruction.operand = bodyName; }
                yield return instruction;
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
            try { return FontEnabled && _needsUnicode ? PlayerNameFonts.GuiFont(original) : original; }
            catch (Exception ex) { MPfixPlugin.Log.Debug("[名称显示] 名单字体不可用：" + ex.Message); return original; }
        }
        private static void GameFont(ref Font __result) { __result = GuiFont(__result); }
        internal static void Invalidate()
        {
            Names.Invalidate();
        }
        // This only releases local font/cache resources. Name updates belong to MP's own callbacks.
        internal static void UpdateLifecycle()
        {
            bool enabled = FontEnabled, running = Mp.IsRunning;
            if (!enabled && _wasEnabled || !running && _wasRunning)
            {
                PlayerNameFonts.Clear();
                RestoreLabels();
                Names.Clear(); ObservedText.Clear();
                _needsUnicode = false;
            }
            if (Enabled)
                for (int i = LabelStates.Count - 1; i >= 0; i--)
                {
                    if (!LabelStates[i].TryGetTarget(out var state) || state.Label == null) { LabelStates.RemoveAt(i); continue; }
                    RefreshLabel(state);
                }
            if (!running && _wasRunning)
            {
                LabelStates.Clear();
                _labels = new ConditionalWeakTable<object, LabelState>();
            }
            _wasEnabled = enabled; _wasRunning = running;
        }
        internal static void Stop()
        {
            _wasEnabled = _wasRunning = _needsUnicode = false; Names.Clear(); ObservedText.Clear();
            PlayerNameFonts.Dispose(); RestoreLabels(); LabelStates.Clear();
            _labels = new ConditionalWeakTable<object, LabelState>();
            _chat = new ConditionalWeakTable<object, ChatState>();
        }
    }
}
