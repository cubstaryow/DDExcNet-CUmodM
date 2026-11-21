using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace HarmonyLib
{
    // The game's HarmonyX detour initializer targets Unity/Mono. Simulate reflection binding under the desktop test runtime.
    public static class AccessTools
    {
        private const BindingFlags Flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        public static Type TypeByName(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(t => t != null);
        public static PropertyInfo Property(Type type, string name) => type?.GetProperty(name, Flags);
        public static FieldInfo Field(Type type, string name) => type?.GetField(name, Flags);
        public static MethodInfo Method(Type type, string name, Type[] parameters = null) => parameters == null
            ? type?.GetMethods(Flags).FirstOrDefault(m => m.Name == name) : type?.GetMethod(name, Flags, null, parameters, null);
    }
}

// Simulate only Unity's object/component boundary. Production repair, matching, save and wire code is linked unchanged.
namespace UnityEngine
{
    public static class Time { public static float unscaledTime; }
    public class Object
    {
        public bool Destroyed;
        public static void Destroy(Object obj) { if (!ReferenceEquals(obj, null)) obj.Destroyed = true; }
        public static bool operator ==(Object a, Object b) => (ReferenceEquals(a, null) || a.Destroyed) ? ReferenceEquals(b, null) || b.Destroyed : ReferenceEquals(a, b);
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => base.GetHashCode();
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public T GetComponent<T>() where T : Component => (T)GetComponent(typeof(T));
        public Component GetComponent(Type type) => gameObject.GetComponent(type);
    }
    public class MonoBehaviour : Component { }
    public class GameObject : Object
    {
        private readonly List<Component> _components = new List<Component>();
        public Component GetComponent(Type type) => _components.FirstOrDefault(c => type.IsInstanceOfType(c) && c != null);
        public T AddComponent<T>() where T : Component => (T)AddComponent(typeof(T));
        public Component AddComponent(Type type)
        { var c = (Component)Activator.CreateInstance(type, true); c.gameObject = this; _components.Add(c); return c; }
    }
}
public sealed class Item : UnityEngine.MonoBehaviour
{
    public string id;
    public float condition = 1, decayMultiplier = 1;
}
public sealed class CraftingQuality { public string id; }
public sealed class WorldGeneration
{
    public static WorldGeneration world = new WorldGeneration();
    public int biomeDepth;
    public bool worldExists = true, generatingWorld;
}
public sealed class RecipeItem
{
    public bool specific, isLiquid;
    public CraftingQuality quality;
}
public sealed class FakeCageFire : UnityEngine.MonoBehaviour
{
    private bool active = true;
    private void SetActive(bool on)
    {
        active = on;
        typeof(HeatingCageFix.CageRuntime).GetMethod("AfterSetActive", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { this });
    }
}
public sealed class FakeSaveContext
{
    public readonly List<Action> Deferred = new List<Action>();
    public void Defer(Action action) => Deferred.Add(action);
    public void Finish() { foreach (var action in Deferred) action(); Deferred.Clear(); }
}
namespace HeatingCageFix
{
    internal sealed class Flag { public bool Value = true; }
    internal static class Plugin
    {
        public static Flag Master = new Flag(), Decay = new Flag(), Heat = new Flag(), Remember = new Flag(), Network = new Flag();
        public static bool NetworkReady = true;
        public static bool Active => Master.Value;
        public static void Error(Exception ex, string action) => throw new Exception(action, ex);
        public static void Debug(string text) { }
    }
}
namespace Together
{
    public static class Net
    {
        public static object ClientEntityManager { get; set; } = new object();
        public static object ServerEntityManager { get; set; } = new object();
    }
    public sealed class SyncInfo
    {
        public ushort Id;
        public byte Version;
        public UnityEngine.GameObject go;
    }
    public sealed class SyncInfoGameObjectTracker : UnityEngine.Component { public SyncInfo syncInfo; }
}
namespace CUCoreLib.Networking
{
    using Newtonsoft.Json.Linq;
    public static class MultiplayerApi
    {
        public static bool IsRunning { get; set; } = true;
        public static bool IsServer { get; set; }
        public static bool IsClient => IsRunning && !IsServer;
    }
    public static class MultiplayerBridge
    {
        public sealed class Pending { public JToken Payload; public Action<JToken> Callback; }
        public static readonly List<Pending> Requests = new List<Pending>();
        public static Func<JToken, JToken> ServerHandler;
        public static Action<JToken> ClientHandler;
        public static int Broadcasts;
        public static void RegisterServerHandler(string channel, Func<JToken, JToken> handler) => ServerHandler = handler;
        public static void RegisterClientHandler(string channel, Action<JToken> handler) => ClientHandler = handler;
        public static bool RequestServer(string channel, object payload, Action<JToken> reply, bool reliable)
        { Requests.Add(new Pending { Payload = (JToken)payload, Callback = reply }); return true; }
        public static bool Broadcast(string channel, object payload, bool host, bool reliable) { Broadcasts++; return true; }
    }
}
