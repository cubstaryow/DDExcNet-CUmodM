using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;
namespace UnityEngine
{
    public class Transform { public Transform parent; }
    public class Component
    {
        private GameObject _game;
        public GameObject gameObject => _game ?? (_game = new GameObject(this));
        public readonly Transform transform = new Transform();
        public Component Parent;
        public readonly List<Component> Children = new List<Component>();
        internal void Attach(GameObject game) { _game = game; }
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
        public T GetComponentInParent<T>() where T : Component => this as T ?? Parent as T;
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component
        { var items = new List<T>(); if (this is T value) items.Add(value); foreach (var child in Children) items.AddRange(child.GetComponentsInChildren<T>(includeInactive)); return items.ToArray(); }
    }
    public class MonoBehaviour : Component { }
    public class GameObject
    {
        private readonly List<Component> _components = new List<Component>();
        public bool activeSelf;
        public GameObject(Component component = null) { if (component != null) { _components.Add(component); component.Attach(this); } }
        public T GetComponent<T>() where T : Component => _components.OfType<T>().FirstOrDefault();
        public T AddComponent<T>() where T : Component, new() { var value = new T(); value.Attach(this); _components.Add(value); return value; }
    }
    public static class Time { public static float unscaledTime; public static int frameCount; }
}
public sealed class Item : UnityEngine.Component { public string id; public Item(string value) { id = value; } }
public sealed class Body : UnityEngine.Component
{
    public readonly List<Item> Inventory = new List<Item>();
    public int Intelligence = 30;
    public Together.NetBody Net;
    [MethodImpl(MethodImplOptions.NoInlining)] public List<Item> GetAllItemsThorough() => new List<Item>(Inventory);
    public bool HoldingItem(Item item) => Inventory.Contains(item);
    [MethodImpl(MethodImplOptions.NoInlining)] public void PickUpItem(Item item, int slot, bool force = false) { Inventory.Add(item); }
}
public sealed class Container : UnityEngine.Component
{
    [MethodImpl(MethodImplOptions.NoInlining)] public void LoadItem(Item item)
    { item.transform.parent = transform; var owner = Parent as Body; if (owner != null) owner.Inventory.Add(item); }
}
public sealed class RecipeResult { public string id; }
public sealed class Recipe
{
    public int INT = 18;
    public RecipeResult result;
    public bool visible { [MethodImpl(MethodImplOptions.NoInlining)] get { return PlayerCamera.main.body.Intelligence >= INT - 3; } }
    [MethodImpl(MethodImplOptions.NoInlining)] public List<Item> GetItemsForRecipe() => new List<Item>();
}
public static class Recipes { public static List<Recipe> recipes = new List<Recipe>(); }
public sealed class PlayerCamera
{
    public static PlayerCamera main;
    public Body body;
    public int selectedRecipe, Crafted, Refreshed;
    public UnityEngine.GameObject craftingPanel = new UnityEngine.GameObject();
    public string Alert;
    public void DoAlert(string message) { Alert = message; }
    public void RefreshRecipeList() { Refreshed++; }
    [MethodImpl(MethodImplOptions.NoInlining)] public void TryCraft() { Crafted++; }
}
namespace Together
{
    public sealed class ScavPlayer : UnityEngine.Component { public Body body; }
    public sealed class NetBody { public Body body; public ScavPlayer plr; }
    public static class CraftCommit
    {
        public static int Spent;
        [MethodImpl(MethodImplOptions.NoInlining)] public static bool Commit(NetBody pb, uint session, uint attempt, Recipe recipe, ushort[] ids)
        {
            if (PlayerCamera_TryCraft_MultiplayerPatch.CheckIfTheseItemsAreEnoughForTheRecipe(recipe, pb.body.GetAllItemsThorough()) == null) return false;
            Spent++; return true;
        }
    }
    public static class PlayerCamera_TryCraft_MultiplayerPatch
    {
        public static bool Throw;
        [MethodImpl(MethodImplOptions.NoInlining)] public static List<Item> CheckIfTheseItemsAreEnoughForTheRecipe(Recipe recipe, List<Item> items)
        { if (Throw) throw new InvalidOperationException("original matcher failure"); return items; }
    }
}
namespace CUCoreLib.Saving
{
    public sealed class SaveRestoreContext { }
    public interface IBodySaveProvider
    { int GetVersion(); JToken Capture(Body body); void Restore(Body body, JToken payload, int version, SaveRestoreContext context); }
}
namespace DDEN.Shared
{
    public static class Reflect
    {
        public static Type Type(string name) => Assembly.GetExecutingAssembly().GetType(name);
        public static object GetInstance(object value, string name) => value?.GetType().GetField(name)?.GetValue(value);
    }
    public static class Mp
    {
        public static bool IsRunning, IsServer = true, TogetherReady;
        public static Action<JToken> Response;
        public static bool RequestServer(string channel, object packet, Action<JToken> response) { Response = response; return true; }
    }
}
namespace DDEN.LoongNova
{
    internal static class Registration
    {
        internal const string WrenchId = "dden.loongnova.dragon-claw-wrench", DragonId = "dden.loongnova.dragon", CellId = "dden.loongnova.dragon-breath-cell";
        internal static string Text(string key) => key;
    }
    internal sealed class OfflineLog { internal void Warn(string message) { Console.WriteLine(message); } }
    internal sealed class WeaponService { internal const string Channel = "test.recipes"; internal bool Ready = true; }
    internal static class LoongNovaPlugin
    {
        internal const string Guid = "test.loongnova";
        internal static readonly OfflineLog Log = new OfflineLog();
        internal static readonly WeaponService Service = new WeaponService();
    }
    internal static class NetworkAccess
    {
        internal static object Connection = new object();
        internal static object NetBody(Body body) => body?.Net;
    }
}
