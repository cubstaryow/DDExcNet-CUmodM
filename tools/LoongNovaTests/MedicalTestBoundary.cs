// Minimal engine/CCL boundary; the medical effect and timer code is linked from production.
namespace CUCoreLib.Data
{
    public abstract class BodyStatus { }
    public abstract class LimbStatus { }
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public sealed class StatusOptionsAttribute : System.Attribute
    {
        public string Key {get;set;}
        public bool SaveEnabled {get;set;}
        public bool ClientAuthoritative {get;set;}
    }
}
namespace CUCoreLib.Helpers
{
    public static class StatusExtensions
    {
        public static T GetStatus<T>(this Body body) where T : CUCoreLib.Data.BodyStatus,new()
        {
            if(!body.Statuses.TryGetValue(typeof(T),out var status)) body.Statuses[typeof(T)]=status=new T();
            return (T)status;
        }
        public static T GetStatus<T>(this Limb limb) where T : CUCoreLib.Data.LimbStatus,new()
        {
            if(!limb.Statuses.TryGetValue(typeof(T),out var status)) limb.Statuses[typeof(T)]=status=new T();
            return (T)status;
        }
    }
}
public sealed partial class Body
{
    public bool alive {get=>brainHealth>0;set=>brainHealth=value?100:0;}
    public float brainHealth=100,bloodOxygen,bloodPressure,heartRate,respiratoryRate,bloodVesselSize,bloodViscosity,heartRatePressureOffset;
    public float fibrillationProgress,strokeAmount,internalBleeding,hemothorax,shock,painShock,traumaAmount,averagePain,totalBleedSpeed;
    public float temperature,septicShock,venomCurrent,venomTotal,radiationSickness,sicknessAmount,hunger,thirst,energy,stamina;
    public bool breathing,fibrillationForced,hasPulmonaryEmbolism,sleeping;
    public Painkillers Opioids;
    public SleepingPills Sedatives;
    public bool TryGetComponent<T>(out T value) where T:class {value=Opioids as T ?? Sedatives as T;return value!=null;}
    public float bloodVolume=100,consciousness=100;
    public Limb[] limbs=System.Array.Empty<Limb>();
    public readonly UnityEngine.Transform transform=new UnityEngine.Transform();
    public readonly System.Collections.Generic.Dictionary<System.Type,object> Statuses=new();
    public int Ragdolls;
    public void Ragdoll(){Ragdolls++;}
    public OfflineNetBody NetworkBody;
}
public sealed class Limb
{
    public struct ProstheticInfo {public bool hasProsthetic;}
    public Body body;
    public bool dismembered,isVital,isHead,isAbdomen,infected;
    public ProstheticInfo prostheticInfo;
    public int distanceToHeart,Reattachments;
    public Limb[] connectedLimbs=System.Array.Empty<Limb>();
    public readonly UnityEngine.Transform transform=new UnityEngine.Transform();
    public readonly UnityEngine.GameObject gameObject=new UnityEngine.GameObject();
    public readonly UnityEngine.Collider2D Collider=new UnityEngine.Collider2D();
    public T GetComponent<T>() where T:class=>Collider as T;
    public void Heal(){muscleHealth=skinHealth=100;bleedAmount=infectionAmount=pain=0;infected=false;}
    public float muscleHealth=100,skinHealth=100,infectionAmount,bleedAmount,pain,disinfectionTime;
    public System.Action OnReattach;
    public readonly System.Collections.Generic.Dictionary<System.Type,object> Statuses=new();
    public void Reattach(){Reattachments++;OnReattach?.Invoke();}
    public void SetDisinfect(float time){disinfectionTime=System.Math.Max(disinfectionTime,time);}
    public bool broken,dislocated;
    public int BoneRepairs,Relocations;
    public void MendBone(){broken=false;BoneRepairs++;}
    public void UnDislocate(){dislocated=false;Relocations++;}
}
public sealed class WoundView
{
    public static WoundView view;
    public Body body;
    public int Marks;
    public void AddImageToLimb(Limb limb,UnityEngine.Sprite sprite,bool flip){Marks++;}
}
public static class Sound {public static void Play(string id,UnityEngine.Vector2 pos){}}
public sealed class Painkillers { }
public sealed class SleepingPills { }
public sealed class OfflinePlayer {public Body body;}
public sealed class OfflineNetBody
{
    public bool IsPlayer=>plr!=null;
    public OfflinePlayer plr;
    public readonly System.Collections.Generic.List<OfflineReviveEvent> Events=new();
    public void OnDamageEvent(OfflineReviveEvent marker){Events.Add(marker);}
}
public sealed class OfflineReviveEvent
{
    public enum DamageEventType {Revive}
    public DamageEventType type;
    public OfflineNetBody target;
    public OfflineReviveEvent(DamageEventType kind,OfflineNetBody body){type=kind;target=body;}
}
public static class OfflinePlayerLists
{
    public static int Updates;
    public static void ForceUpdatePlayerLists(){Updates++;}
}
public static class OfflineMedicalSync
{
    public static int Pushes;
    public static void ForceHealth(OfflineNetBody body){Pushes++;}
}
namespace UnityEngine
{
    public static class Random { public static int NextInt=2; public static int Range(int min,int max)=>System.Math.Clamp(NextInt,min,max-1); }
    public struct Vector3 {public static Vector3 zero=>default;}
    public static class Mathf
    {
        public static float Max(float a,float b)=>System.Math.Max(a,b);
        public static float Min(float a,float b)=>System.Math.Min(a,b);
        public static float Clamp(float x,float min,float max)=>System.Math.Clamp(x,min,max);
        public static float MoveTowards(float x,float target,float maxDelta)=>x<target?System.Math.Min(target,x+maxDelta):System.Math.Max(target,x-maxDelta);
    }
    public static class Object
    {
        public static readonly System.Collections.Generic.List<object> Destroyed=new();
        public static void Destroy(object target){Destroyed.Add(target);}
    }
    public static class Time {public static float deltaTime;}
    public static class Resources {public static T Load<T>(string name) where T:class=>null;}
}
namespace HarmonyLib {public sealed class HarmonyPostfix:System.Attribute { }}
namespace DDEN.LoongNova
{
    internal static class HealingFields
    {
        internal static readonly HealingFieldState State=new();
        internal static int Creates;
        internal static UnityEngine.Vector2 LastPoint;
        internal static void Create(UnityEngine.Vector2 point){Creates++;LastPoint=point;State.Add("offline-"+Creates,point.x,point.y,HealingFieldState.Duration);}
        internal static float RemainingAt(UnityEngine.Vector2 point)=>State.RemainingAt(point.x,point.y);
    }
    internal sealed class MedicalInjector
    {
        internal System.Action OnRefresh;
        internal void RefreshSprite()=>OnRefresh?.Invoke();
        internal readonly AmpouleState State=new AmpouleState();
        internal int Requests;
        internal string Operation;
        internal Item RequestedAmpoule;
        private static readonly System.Collections.Generic.Dictionary<Item,MedicalInjector> States=new();
        internal static MedicalInjector For(Item item)
        {if(!States.TryGetValue(item,out var value))States[item]=value=new MedicalInjector();return value;}
        internal void Request(string op,Body body,Item ampoule=null){Requests++;Operation=op;RequestedAmpoule=ampoule;}
    }
}

namespace DDEN.LoongNova
{
    internal sealed class MicroSpaceCrate
    {
        internal static MicroSpaceCrate For(Item item)=>new MicroSpaceCrate();
        internal void RefreshSprite(){}
    }
}
