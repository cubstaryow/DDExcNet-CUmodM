using System.Reflection;

// Only Unity objects and CCL storage are simulated. The production recovery hook is linked unchanged.
namespace UnityEngine
{
    public class GameObject
    {
        public bool Destroyed;
        public Item Item = new Item();
        public T GetComponent<T>() where T : class => Item as T;
        public static bool operator ==(GameObject a, GameObject b) =>
            (ReferenceEquals(a, null) || a.Destroyed) && (ReferenceEquals(b, null) || b.Destroyed) || ReferenceEquals(a, b);
        public static bool operator !=(GameObject a, GameObject b) => !(a == b);
        public override bool Equals(object o) => ReferenceEquals(this, o);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
    public static class Time { public static float unscaledTime; }
}
public class Item { }
namespace DDEN.Shared
{
    public static class Reflect { public static Type Type(string name) => typeof(Reflect).Assembly.GetType(name); }
}
namespace HarmonyLib
{
    public class Harmony
    {
        public void Patch(MethodInfo target, HarmonyMethod prefix = null, HarmonyMethod finalizer = null) { }
    }
    public class HarmonyMethod { public HarmonyMethod(Type type, string name) { } }
    public static class AccessTools
    {
        private const BindingFlags Flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        public static FieldInfo Field(Type type, string name) => type?.GetField(name, Flags);
        public static MethodInfo Method(Type type, string name, Type[] args) => type?.GetMethod(name, Flags, null, args, null);
    }
}
namespace DDEN.MPfix
{
    public class Setting { public bool Value = true; }
    public class TestLog
    {
        public readonly List<string> Entries = new();
        public void Info(string s) => Entries.Add(s);
        public void Warn(string s) => Entries.Add(s);
        public void Exception(Exception e, string s) => Entries.Add(s);
    }
    public static class MPfixPlugin
    {
        public static bool Active = true;
        public static Setting CfgFixModItemCraftSpawn = new();
        public static TestLog Log = new();
    }
    public static class Mp { public static bool IsRunning = true; }
}
namespace CUCoreLib.Registries
{
    public static class ItemRegistry
    {
        internal static Dictionary<string, object> RegisteredItems = new(StringComparer.OrdinalIgnoreCase);
    }
}
namespace CUCoreLib.Helpers
{
    public static class CustomInstantiate
    {
        internal static Dictionary<string, UnityEngine.GameObject> _templateCache = new(StringComparer.OrdinalIgnoreCase);
        public static int Builds, Clears;
        public static bool ReturnNull;
        public static Exception Failure;
        internal static void ClearTemplateCache(string id) { Clears++; _templateCache.Remove(id); }
        public static UnityEngine.GameObject GetOrCreateTemplate(string id)
        {
            id = id.Trim();
            if (_templateCache.TryGetValue(id, out var cached)) return cached;
            if (!CUCoreLib.Registries.ItemRegistry.RegisteredItems.ContainsKey(id)) return null;
            Builds++;
            if (Failure != null) throw Failure;
            if (ReturnNull) return null;
            return _templateCache[id] = new UnityEngine.GameObject();
        }
    }
}
