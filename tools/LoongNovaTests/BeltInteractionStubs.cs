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
    public static class Priority { public const int First = 800, Last = 0; }
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
public sealed partial class Body
{
    public bool conscious = true, allowUseItem = true;
    public int handSlot;
    public Item Held;
    public Item GetItem(int slot) => Held;
    public readonly System.Collections.Generic.List<Item> CarriedItems = new System.Collections.Generic.List<Item>();
    public bool PickupAllowed=true, NoAlerts;
    public int PickupChecks;
    public bool HoldingItem(Item item) => Held==item;
    public System.Collections.Generic.List<Item> GetAllItemsThorough() => CarriedItems;
    public bool DoPickupCheck(Item item,bool noAlerts=false) { PickupChecks++; NoAlerts=noAlerts; return HoldingItem(item) || CarriedItems.Contains(item) || PickupAllowed; }
    public object GetComponent(System.Type type) => this is Body body && type == typeof(OfflineNetBody) ? body.NetworkBody : null;
}
public sealed class Item
{
    public string id;
    public float condition = 1;
    public bool favourited;
    public ItemInfo Stats = new ItemInfo();
    public DDEN.LoongNova.GravityBelt Belt = new DDEN.LoongNova.GravityBelt();
    internal DDEN.LoongNova.DragonGun Cannon;
    public UnityEngine.GameObject gameObject = new UnityEngine.GameObject();
    public object GetComponent(System.Type type) => null;
    public readonly UnityEngine.SpriteRenderer Renderer=new UnityEngine.SpriteRenderer();
    public T GetComponent<T>() where T:class=>Renderer as T;
}
public sealed class ItemInfo
{
    public bool Tool;
    public bool HasTag(string tag) => tag == "tool" && Tool;
}
public sealed class InvButton
{
    public UnityEngine.UI.Image itemImg=new UnityEngine.UI.Image();
    public UnityEngine.Vector2 maxImageSize;
    public void UpdateGraphic(){}
    public Item Item;
    public bool Overlap = true;
    public Item GetItem() => Item;
    public bool Overlaps(System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult> hits) => Overlap;
}
public sealed class PlayerCamera
{
    public static UnityEngine.Vector2 ImageSizeDelta(object texture,float scale,UnityEngine.Vector2 max)=>new UnityEngine.Vector2(12,20);
    public static PlayerCamera main;
    public Body body;
    public Item dragItem;
    public UnityEngine.UI.Image gunRackImage;
    public UnityEngine.UI.Button gunMagButton = new UnityEngine.UI.Button();
    public UnityEngine.GameObject gunMenu = new UnityEngine.GameObject();
    public void GunRack() { }
}
public static class PauseHandler { public static bool paused; }
namespace DDEN.Shared
{
    public static class Mp { public static bool IsRunning, IsServer, CoreReady = true; }
    public static class DdenHub { public static bool AnyPanelVisible; }
}
namespace DDEN.LoongNova
{
    internal static class Registration
    {
        internal static UnityEngine.Sprite WingFoldedSprite = new UnityEngine.Sprite("folded");
        internal static UnityEngine.Sprite WingOpenSprite = new UnityEngine.Sprite("open");
        internal const string GunId = "dden.loongnova.dragon-scorch";
        internal const string ChargeGunId = "dden.loongnova.charge-prototype";
        internal static bool IsGun(string id) => id == GunId || id == ChargeGunId;
        internal const string SmelterId="dden.loongnova.portable-dragonflame-smelter";
        internal const string TacticalGogglesId = "dden.loongnova.tactical-goggles";
        internal const string DragonId = "dden.loongnova.dragon";
        internal const string MatterId = "dden.loongnova.dragon-breath-matter";
        internal const string DragonQuality = "loongnova.dragonization";
        internal const string PickaxeId = "dden.loongnova.rock-dragon-pickaxe";
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
    internal sealed class DragonGun
    {
        internal System.Action OnRefresh;
        internal void RefreshSprite()=>OnRefresh?.Invoke();
        internal readonly CannonAmmoState Ammo = new CannonAmmoState();
        internal readonly EnergyState State = new EnergyState();
        internal bool Known;
        internal string Operation;
        internal int Requests;
        internal static DragonGun For(Item item) => item.Cannon ??= new DragonGun();
        internal Item RequestedCell;
        internal void Request(string operation, Body body, Item cell=null) { Operation = operation; RequestedCell=cell; Requests++; }
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
    public bool specific, isLiquid, destroyItem, noMaxCondition;
    public string specificId, ignoredId;
    public float minimumCondition;
    public RecipeItem(float minimumCondition=0) { this.minimumCondition=minimumCondition; }
    public Item GetMatchingItem(System.Collections.Generic.List<Item> items, System.Collections.Generic.List<Item> exclusions) => null;
    public CraftingQuality quality;
    public void UseItem(Item item) { }
}
namespace UnityEngine
{
    public sealed class Sprite
    {
        public object texture=new object();
        public string Name;
        public Sprite(string name) { Name = name; }
    }
    public sealed class SpriteRenderer { public Sprite sprite; }
    public struct Quaternion
    {
        public float Z;
        public static Quaternion Euler(float x, float y, float z) => new Quaternion { Z = z };
    }
    public sealed class Transform { public Quaternion localRotation; public Vector2 position; public Vector3 localPosition; }
    public class MonoBehaviour
    {
        public object TestComponent;
        public readonly SpriteRenderer TestRenderer = new SpriteRenderer();
        public readonly Transform transform = new Transform();
        public T GetComponent<T>() where T : class => TestComponent as T ?? TestRenderer as T;
    }
    public sealed class GameObject
    {
        public void SetActive(bool active){activeInHierarchy=active;}
        public bool activeInHierarchy = true;
        public int layer;
        public Body Body;
        public BuildingEntity Building;
        public void SendMessage(string name,float value,SendMessageOptions options) { }
    }
}
namespace UnityEngine.UI
{
    public sealed class Button { public bool enabled = true, interactable = true; }
    public sealed class Image
    {
        public Sprite sprite;
        public RectTransform rectTransform=new RectTransform();
        public Button OwnButton, ParentButton;
        public T GetComponent<T>() where T : class => OwnButton as T;
        public T GetComponentInParent<T>() where T : class => ParentButton as T;
    }
    public sealed class RectTransform {public Vector2 sizeDelta;}
}
