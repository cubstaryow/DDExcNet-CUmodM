using System.Reflection;
using System.Reflection.Emit;

// Simulate native callbacks and GUI objects, not the production resolver/adapters linked by the project.
namespace UnityEngine
{
    public partial class Object
    {
        public static bool operator ==(Object a, Object b) =>
            (ReferenceEquals(a, null) || a.Destroyed) && (ReferenceEquals(b, null) || b.Destroyed) || ReferenceEquals(a, b);
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object o) => ReferenceEquals(this, o);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Component GetComponent(Type type) => gameObject.Components.OfType<Component>().FirstOrDefault(type.IsInstanceOfType);
        public T[] GetComponentsInChildren<T>(bool inactive) where T : class => gameObject.GetComponentsInChildren<T>();
    }
    public class GameObject : Object
    {
        public bool activeInHierarchy = true;
        public readonly List<object> Components = new();
        public T GetComponent<T>() where T : class => Components.OfType<T>().FirstOrDefault();
        public T[] GetComponentsInChildren<T>() where T : class => Components.OfType<T>().ToArray();
    }
    public struct Color { }
    public class GUIStyle { public Font font; }
    public class GUISkin { public GUIStyle label = new(), box = new(), button = new(); }
    public static class GUI { public static GUISkin skin = new(); }
}
namespace TMPro
{
    public partial class TMP_Text { public UnityEngine.GameObject gameObject = new(); }
}
namespace HarmonyLib
{
    public class Harmony
    {
        public readonly List<(MethodBase Target, HarmonyMethod Prefix, HarmonyMethod Postfix, HarmonyMethod Finalizer)> Hooks = new();
        public void Patch(MethodBase target, HarmonyMethod prefix = null, HarmonyMethod postfix = null, HarmonyMethod transpiler = null, HarmonyMethod finalizer = null) =>
            Hooks.Add((target, prefix, postfix, finalizer));
    }
    public class HarmonyMethod { public string Name; public HarmonyMethod(Type type, string name) { Name = name; } }
    public class CodeInstruction
    {
        public OpCode opcode;
        public object operand;
        public CodeInstruction(OpCode op, object arg = null) { opcode = op; operand = arg; }
        public bool Calls(MethodInfo method) => (opcode == OpCodes.Call || opcode == OpCodes.Callvirt) && Equals(operand, method);
    }
    public static class AccessTools
    {
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        public static FieldInfo Field(Type t, string n) => t?.GetField(n, Flags);
        public static PropertyInfo Property(Type t, string n) => t?.GetProperty(n, Flags);
        public static MethodInfo PropertyGetter(Type t, string n) => Property(t, n)?.GetMethod;
        public static MethodInfo Method(Type t, string n, Type[] args = null) => args == null ? t?.GetMethod(n, Flags) : t?.GetMethod(n, Flags, null, args, null);
    }
}
namespace DDEN.Shared
{
    public static class Reflect
    {
        public static Type Type(string name) => typeof(Reflect).Assembly.GetType(name);
        public static object GetInstance(object obj, string name)
        {
            if (obj == null) return null;
            var t = obj.GetType();
            return HarmonyLib.AccessTools.Field(t, name)?.GetValue(obj) ?? HarmonyLib.AccessTools.Property(t, name)?.GetValue(obj);
        }
        public static bool TrySetInstance(object obj, string name, object value)
        { HarmonyLib.AccessTools.Field(obj.GetType(), name).SetValue(obj, value); return true; }
    }
}
namespace DDEN.MPfix
{
    public class Setting { public bool Value = true; }
    public static class Mp { public static bool IsRunning = true; }
}
namespace Together
{
    public class ScavPlayer : UnityEngine.Object
    {
        public void Start() { }
        public string playerName;
        public ulong SteamId;
        public static ScavPlayer[] AllPlayers { get; set; } = Array.Empty<ScavPlayer>();
        public void ApplyNameAndColor(string name, object color) { playerName = name; }
    }
    public static class KSteam
    {
        public static readonly Dictionary<ulong, string> Names = new();
        public static int Calls;
        public static string GetSteamUsername(ulong id, bool force) { Calls++; return Names.TryGetValue(id, out var name) ? name : id.ToString(); }
        public static void OnPersonaStateChange() { }
    }
    public static class CoopModAssets { public static UnityEngine.Font gamefont = new(); }
    public class NetBody : UnityEngine.Component
    {
        public ScavPlayer plr;
        public bool IsLocal;
        public string NativeBodyName = "";
        public string bodyName => NativeBodyName;
        public string playerName => plr?.playerName ?? bodyName;
        public void DoMouseHoverTooltip(ref string tooltipName_text, ref string tooltipDescription_text) { tooltipName_text = bodyName; }
        public static int NameRefreshCalls;
        public static float ArrowAlpha = 0.2f;
        public void ApplyNameAndColor(string name, UnityEngine.Color color) { NameRefreshCalls++; ArrowAlpha = 1; }
    }
    public class CharStatusVisuals
    {
        public ScavPlayer plr;
        public UnityEngine.GameObject nametag;
        public void ApplyNameAndColor() { NetBody.NameRefreshCalls++; NetBody.ArrowAlpha = 1; }
        public void LateUpdate() { }
        public static void TryMakeNametagFancy(TMPro.TMP_Text tmp, ScavPlayer plr) { }
    }
    public class UIInGame { public static void _GUI__DrawPlayerDirection(NetBody nb, float alpha, bool shiftHeld) { } }
    public static class Chat
    {
        public static string AddColorTags(object color, string name) => name;
        public static Action<object> BeforeCompile, AfterCompile;
        public static void MeasureChatLine(ChatMsgContainer text, float width) { }
        public static void _GUI_DrawChat() { }
        public class ChatMsgContainer
        {
            public ScavPlayer plr;
            public string name, result_nametag;
            public int Compiles;
            public ChatMsgContainer(ScavPlayer player) { plr = player; name = player.playerName; Compile(); }
            public ChatMsgContainer(string text) { name = text; Compile(); }
            public void Compile()
            {
                BeforeCompile?.Invoke(this);
                Compiles++; result_nametag = "[" + name + "]: ";
                AfterCompile?.Invoke(this);
            }
        }
    }
    public static class UIBullshit { public static UnityEngine.Font FocusedChatFont() => CoopModAssets.gamefont; }
}

namespace Together { public class ServerMain { public void UpdateSyncClientsToClients() { } } public static class Lang {
public static string CurrentLocale { get; set; } = "zh-CN";
public static Dictionary<string, Dictionary<string, string>> dict = new() { ["zh-CN"] = new() { ["wv_viewers"] = "查看者" } };
public static string Get(in string key, in bool allow_null_result = false) => key;
public static bool TryParseIndividualTag(ref string tag) => true; } }

public class WoundView { public static WoundView view = new(); public TMPro.TMP_Text nameText, bodyStatText; }
