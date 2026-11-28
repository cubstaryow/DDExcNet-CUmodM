using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;
namespace UnityEngine
{
    public class Object
    {
        public bool Dead;
        public static bool operator ==(Object a,Object b) => ReferenceEquals(a,b) || (ReferenceEquals(a,null)||a.Dead)&&(ReferenceEquals(b,null)||b.Dead);
        public static bool operator !=(Object a,Object b)=>!(a==b);
        public override bool Equals(object obj)=>ReferenceEquals(this,obj);
        public override int GetHashCode()=>RuntimeHelpers.GetHashCode(this);
        public static void Destroy(Object value)
        {
            if(value==null)return;
            if(value is GameObject game){game.Dead=true;game.transform.SetParent(null);foreach(var part in game.Parts)part.Dead=true;}
            else value.Dead=true;
        }
    }
    public class Component:Object
    {
        private GameObject _game;
        public GameObject gameObject=>_game??(_game=new GameObject(this));
        public Transform transform=>gameObject.transform;
        internal void Attach(GameObject game){_game=game;}
        public T GetComponent<T>() where T:Component=>gameObject.GetComponent<T>();
        public bool TryGetComponent<T>(out T value) where T:Component{value=GetComponent<T>();return value!=null;}
        public T GetComponentInParent<T>() where T:Component=>transform.GetComponentInParent<T>(true);
    }
    public class MonoBehaviour:Component{}
    public sealed class GameObject:Object
    {
        internal readonly List<Component> Parts=new List<Component>();
        public readonly Transform transform;
        public Scene scene=>new Scene();
        public GameObject(Component initial=null){transform=new Transform(this);if(initial!=null){Parts.Add(initial);initial.Attach(this);}}
        public T GetComponent<T>()where T:Component=>Parts.OfType<T>().FirstOrDefault(p=>!p.Dead);
        public bool TryGetComponent<T>(out T value)where T:Component{value=GetComponent<T>();return value!=null;}
        public T AddComponent<T>()where T:Component,new(){var value=new T();value.Attach(this);Parts.Add(value);typeof(T).GetMethod("Awake",BindingFlags.Instance|BindingFlags.NonPublic)?.Invoke(value,null);return value;}
    }
    public struct Scene{public bool IsValid()=>true;}
    public sealed class Transform:IEnumerable<Transform>
    {
        public readonly GameObject gameObject;
        public Transform parent;
        private readonly List<Transform> _children=new List<Transform>();
        public Vector3 position;
        internal Transform(GameObject game){gameObject=game;}
        public int childCount=>_children.Count;
        public void SetParent(Transform next){parent?._children.Remove(this);parent=next;next?._children.Add(this);}
        public bool TryGetComponent<T>(out T value)where T:Component=>gameObject.TryGetComponent(out value);
        public T GetComponentInParent<T>(bool inactive)where T:Component{for(var t=this;t!=null;t=t.parent){var c=t.gameObject.GetComponent<T>();if(c!=null)return c;}return null;}
        public IEnumerator<Transform> GetEnumerator()=>_children.ToList().GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
    }
    public struct Vector3{public float x,y,z;}
    public struct Vector2
    {
        public float x,y;
        public static implicit operator Vector2(Vector3 value)=>new Vector2{x=value.x,y=value.y};
        public static float Distance(Vector2 a,Vector2 b)=>(float)Math.Sqrt((a.x-b.x)*(a.x-b.x)+(a.y-b.y)*(a.y-b.y));
    }
    public struct Quaternion{public static Quaternion identity=>new Quaternion();}
    public static class Time{public static double timeAsDouble;public static float unscaledTime;}
    public static class Mathf{public static float Clamp01(float value)=>Math.Max(0,Math.Min(1,value));}
}
namespace UnityEngine.EventSystems{public struct RaycastResult{public UnityEngine.GameObject gameObject;}}
public sealed class Recognition{public bool recognizable=true;}
public class ItemInfo{public bool Tool;public bool HasTag(string tag)=>tag=="tool"&&Tool;public float weight=0.1f;public Recognition rec=new Recognition();public Action<Body,Item> useAction;}
public sealed class BatteryInfo:ItemInfo{public float maxCharge;}
public sealed class Item:UnityEngine.Component
{
    public string id;public float condition=1;public bool favourited;public int NetId;
    public ItemInfo Stats=>GetItem(id);
    public float totalWeight=>Stats.weight;
    public static readonly Dictionary<string,ItemInfo> Registry=new Dictionary<string,ItemInfo>();
    public static ItemInfo GetItem(string id)=>Registry.TryGetValue(id,out var info)?info:new ItemInfo();
    [MethodImpl(MethodImplOptions.NoInlining)] public void HandleDecay(){condition-=0.05f;}
    [MethodImpl(MethodImplOptions.NoInlining)] public TimeSpan TimeUntilDecayed()=>TimeSpan.FromHours(1);
    private void Start(){}
}
public sealed class Body:UnityEngine.Component
{
    public bool conscious=true;public int handSlot;public Item Held;public int Notices;
    public readonly List<Item> Inventory=new List<Item>();
    public Item GetItem(int slot)=>Held;
    public List<Item> GetAllItemsThorough()=>Inventory.Where(item=>item!=null).ToList();
    public void AutoPickUpItem(Item item){Inventory.Add(item);item.transform.SetParent(transform);}
}
public sealed class Container:UnityEngine.Component
{
    public float maxWeight=20,maxWeightPerItem=5,encumberanceMult=0.9f;
    public int itemCount=>transform.childCount;
    public float GetHoldingWeight(){float total=0;foreach(var t in transform)if(t.TryGetComponent<Item>(out var item))total+=item.totalWeight;return total;}
    [MethodImpl(MethodImplOptions.NoInlining)] public float GetEncumberance()=>GetHoldingWeight()*encumberanceMult;
    [MethodImpl(MethodImplOptions.NoInlining)] public bool CanHoldItem(Item item)=>item!=null&&GetHoldingWeight()+item.totalWeight<=maxWeight&&item.totalWeight<=maxWeightPerItem;
    [MethodImpl(MethodImplOptions.NoInlining)] public void LoadItem(Item item){item.transform.SetParent(transform);}
    [MethodImpl(MethodImplOptions.NoInlining)] public void UnloadItem(Item item){item.transform.SetParent(null);}
}
public sealed class FreshItemDrop:UnityEngine.Component{}
public sealed class ItemDrop{}
public sealed class BuildingEntity:UnityEngine.Component
{
    public string id;public float health=100,dropChanceMultiplier=1;public int guaranteedDropAmount=2;
    public ItemDrop[] itemsDropOnDestroy={new ItemDrop()},alwaysDrop={new ItemDrop()};
}
public sealed class DrillPod:UnityEngine.Component{[MethodImpl(MethodImplOptions.NoInlining)]public void OnUse(){throw new Exception("Native drill use should be intercepted");}}
public sealed class Openable:UnityEngine.Component{public bool isKeypad=true;[MethodImpl(MethodImplOptions.NoInlining)]public void OnUse(){}}
public sealed class InvButton:UnityEngine.Component
{public Item Item;public Item GetItem()=>Item;public bool Overlaps(List<UnityEngine.EventSystems.RaycastResult> hits)=>true;}
public sealed class PlayerCamera
{
    public static PlayerCamera main;public Body body;public Item dragItem;public string Alert;
    public void DoAlert(string text){Alert=text;}
    [MethodImpl(MethodImplOptions.NoInlining)]public bool TryPerformInventoryAction(UnityEngine.EventSystems.RaycastResult hit,List<UnityEngine.EventSystems.RaycastResult> uiCasts)
    {var box=hit.gameObject.GetComponent<InvButton>().Item.GetComponent<Container>();if(box.CanHoldItem(dragItem))box.LoadItem(dragItem);return true;}
    [MethodImpl(MethodImplOptions.NoInlining)]public (string,string) ItemHoverDescription(Item item)=>("","");
}
public sealed class RecipeItem
{
    public bool specific;public string specificId,ignoredId;
    [MethodImpl(MethodImplOptions.NoInlining)]public Item GetMatchingItem(List<Item> items,List<Item> exclusions)=>items.FirstOrDefault(item=>item.id==specificId&&!item.favourited&&!exclusions.Contains(item));
}
public static class Sound{public static void Play(string id,UnityEngine.Vector3 position){}}
namespace Together
{
    public struct knetid{public ushort id;public knetid(ushort value){id=value;}}
    public sealed class SyncInfo
    {public knetid syncId;public UnityEngine.GameObject go;public BuildingEntity building;public int Removed;public void Destroy(){Removed++;UnityEngine.Object.Destroy(go);}}
    public static class NetObjectRegistry
    {
        public static readonly List<SyncInfo> All=new List<SyncInfo>();
        public static bool TryGetSyncInfo(UnityEngine.GameObject go,out SyncInfo sync){sync=All.FirstOrDefault(s=>s.go==go);return sync!=null;}
        public static bool TryGetSyncInfo(knetid id,out SyncInfo sync){sync=All.FirstOrDefault(s=>s.syncId.id==id.id);return sync!=null;}
    }
}
namespace CUCoreLib.Helpers
{
    public static class CustomInstantiate
    {
        public static int FailAt=-1,Made;
        public static readonly List<Item> Created=new List<Item>();
        public static UnityEngine.GameObject InstantiateReturn(string id,UnityEngine.Vector3 position,UnityEngine.Quaternion rotation,float condition)
        {if(++Made==FailAt)return null;var item=new Item{id=id,condition=condition};item.transform.position=position;Created.Add(item);return item.gameObject;}
    }
}
namespace DDEN.Shared
{
    public static class Mp
    {public static bool IsRunning,IsServer=true,CoreReady=true;public static int Sends;public static int Broadcasts;public static bool Broadcast(string c,object d){Broadcasts++;return true;}public static bool RequestServer(string channel,object data,Action<JToken> reply){Sends++;return false;}}
    public static class Reflect
    {
        public static Type Type(string name)=>Assembly.GetExecutingAssembly().GetType(name);
        public static object GetInstance(object value,string name)=>value?.GetType().GetField(name)?.GetValue(value);
    }
}
namespace DDEN.LoongNova
{
    internal static class Registration
    {internal const string CellId="dden.loongnova.dragon-breath-cell",WrenchId="dden.loongnova.dragon-claw-wrench";internal static UnityEngine.Sprite RechargerActiveSprite=new UnityEngine.Sprite(),RechargerInactiveSprite=new UnityEngine.Sprite();internal static string Text(string key)=>key;}
    internal static class CannonAmmoState{internal const string CarrierId="dden.loongnova.universal-ammo-carrier",DrillId="dden.loongnova.drill-round";}
    internal static class SupplyCrateRules{internal const string OpenId="dden.loongnova.supply-crate";}
    internal static class BeltPowerState{internal static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);}
    internal sealed class OfflineLog{internal void Warn(string message){Console.WriteLine(message);}}
    internal sealed class WeaponService{internal const string Channel="fixture.actions",VisualChannel="fixture.visual";internal bool Ready=true;}
    internal static class LoongNovaPlugin{internal static readonly OfflineLog Log=new OfflineLog();internal static readonly WeaponService Service=new WeaponService();}
    internal static class NetworkAccess
    {
        internal static object Connection=new object();
        internal static int Id(Item item)=>item.NetId;
        internal static Item Item(int id)=>null;
        internal static bool CanAccess(Body body,Item item)=>item!=null;
        internal static bool Carries(Body body,Item item)=>body.Inventory.Contains(item);
        internal static object NetBody(Body body)=>null;
    }
    internal static class EnergyCellNotice{internal static void Show(Body body){body.Notices++;}}
}

namespace UnityEngine
{
    public sealed class Sprite:Object{}
    public sealed class SpriteRenderer:Component{public Sprite sprite;}
}
namespace CUCoreLib.Helpers
{
    public static class CUCoreUtils{public static bool SetItemSprite(Item item,UnityEngine.Sprite sprite)=>true;}
}
