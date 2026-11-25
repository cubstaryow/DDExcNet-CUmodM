// Offline doubles for the UI boundary only; the production prefix is linked verbatim.
namespace HarmonyLib
{
    [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Method)]
    public sealed class HarmonyPatch : System.Attribute
    {
        public HarmonyPatch() { }
        public HarmonyPatch(System.Type type, string method) { }
    }
    public sealed class HarmonyPrefix : System.Attribute { }
    public sealed class HarmonyPriority : System.Attribute { public HarmonyPriority(int value) { } }
    public static class Priority { public const int First = 800; }
}
namespace UnityEngine.EventSystems
{
    public struct RaycastResult { public FakeHit gameObject; }
    public sealed class FakeHit
    {
        public InvButton Button;
        public bool TryGetComponent<T>(out T component) where T : class
        { component = Button as T; return component != null; }
    }
}
public sealed class Body { }
public sealed class Item
{
    public string id;
    public float condition = 1;
    public ItemInfo Stats = new ItemInfo();
    public DDEN.LoongNova.GravityBelt Belt = new DDEN.LoongNova.GravityBelt();
}
public sealed class ItemInfo
{
    public bool Tool;
    public bool HasTag(string tag) => tag == "tool" && Tool;
}
public sealed class InvButton
{
    public Item Item;
    public bool Overlap = true;
    public Item GetItem() => Item;
    public bool Overlaps(System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult> hits) => Overlap;
}
public sealed class PlayerCamera { public Body body; public Item dragItem; }
namespace DDEN.LoongNova
{
    internal static class Registration
    {
        internal static UnityEngine.Sprite WingFoldedSprite = new UnityEngine.Sprite("folded");
        internal static UnityEngine.Sprite WingOpenSprite = new UnityEngine.Sprite("open");
        internal const string GunId = "dden.loongnova.dragon-scorch";
        internal const string DragonId = "dden.loongnova.dragon";
        internal const string MatterId = "dden.loongnova.dragon-breath-matter";
        internal const string DragonQuality = "loongnova.dragonization";
        internal const string WrenchId = "dden.loongnova.dragon-claw-wrench";
        internal const string BeltId = "dden.loongnova.gravity-belt";
        internal const string CellId = "dden.loongnova.dragon-breath-cell";
    }
    public sealed class GravityBelt
    {
        internal readonly BeltPowerState State = new BeltPowerState();
        internal bool WingsOpen;
        internal string Operation;
        internal Item Cell;
        internal Body Body;
        internal int Requests;
        internal static GravityBelt For(Item item) => item.Belt;
        internal void Request(string operation, Body body, Item cell = null)
        { Operation = operation; Body = body; Cell = cell; Requests++; }
    }
}

public sealed class CraftingQuality
{
    public string id;
    public float amount;
    public CraftingQuality(string id, float amount) { this.id = id; this.amount = amount; }
}
public sealed class RecipeItem
{
    public bool specific, isLiquid, destroyItem;
    public CraftingQuality quality;
    public void UseItem(Item item) { }
}
namespace UnityEngine
{
    public sealed class Sprite
    {
        public string Name;
        public Sprite(string name) { Name = name; }
    }
    public sealed class SpriteRenderer { public Sprite sprite; }
    public struct Quaternion
    {
        public float Z;
        public static Quaternion Euler(float x, float y, float z) => new Quaternion { Z = z };
    }
    public sealed class Transform { public Quaternion localRotation; }
    public class MonoBehaviour
    {
        public readonly SpriteRenderer TestRenderer = new SpriteRenderer();
        public readonly Transform transform = new Transform();
        public T GetComponent<T>() where T : class => TestRenderer as T;
    }
}