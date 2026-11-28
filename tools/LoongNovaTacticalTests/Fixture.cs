using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
namespace UnityEngine
{
    public class Renderer { public bool enabled=true; }
    public sealed class GameObject
    {
        public Renderer Renderer=new Renderer();
        public T GetComponent<T>() where T:class => Renderer as T;
    }
    public struct Vector2 { public float x,y; }
    public sealed class Material
    {
        private readonly Dictionary<string,float> Values=new Dictionary<string,float>();
        public float GetFloat(string name)=>Values.TryGetValue(name,out var value)?value:0;
        public void SetFloat(string name,float value)=>Values[name]=value;
    }
    public static class Mathf { public static float Max(float a,float b)=>Math.Max(a,b); }
    public static class Time { public static int frameCount; }
}
public sealed class Item{public string id;public float condition=1;public DDEN.LoongNova.DragonGun Gun;public T GetComponent<T>() where T:class=>Gun as T;}
public sealed class Limb{public bool dismembered;}
public sealed class Body
{
    public bool conscious=true;public int handSlot;public Item Held;public Item GetItem(int slot)=>Held;
    public Limb Head=new Limb();public Item Worn;
    public Limb LimbByName(string name)=>name=="Head"?Head:null;
    public Item GetWearableBySlotID(string slot,Limb limb)=>slot=="eyes"&&limb==Head?Worn:null;
}
public sealed class WorldGeneration
{
    public static WorldGeneration world=new WorldGeneration();
    public bool worldExists=true,generatingWorld;
    public static bool unchipped,Enabled=true;
    public static bool lineOfSightEnabled=>Enabled;
}
public sealed class BuildingEntity
{
    [MethodImpl(MethodImplOptions.NoInlining)]public void Start(){}
}
public sealed class PlayerCamera
{
    public static PlayerCamera main;public void WarnSoundCannon(UnityEngine.Vector2 pos){}public bool radialOpen;public Window woundView=new Window(),tradeMenu=new Window();
    public Body body=new Body();public float zoomTime=0.1f,ObservedZoom;
    public bool Fail;public float NativeNight=0.2f;
    public int Warnings,MaskCalculations;
    public UnityEngine.Material mainScreenPass=new UnityEngine.Material();
    public UnityEngine.GameObject LOSHole=new UnityEngine.GameObject();
    [MethodImpl(MethodImplOptions.NoInlining)]public void HandleCameraPosition(){ObservedZoom=zoomTime;if(Fail)throw new InvalidOperationException("fixture");}
    [MethodImpl(MethodImplOptions.NoInlining)]public void HandleScreenShaders()
    {mainScreenPass.SetFloat("_NightVisAmount",NativeNight);mainScreenPass.SetFloat("_Brightness",0.8f);mainScreenPass.SetFloat("_BrightMult",0.6f);}
    [MethodImpl(MethodImplOptions.NoInlining)]public void HandleLineOfSight(){Warnings++;if(!WorldGeneration.lineOfSightEnabled)return;MaskCalculations++;}
}
namespace DDEN.LoongNova
{
    internal static class Registration{internal const string ChargeGunId="dden.loongnova.charge-prototype";internal const string TacticalGogglesId="dden.loongnova.tactical-goggles";}
    internal static class TacticalGogglesOverlay{internal static int SoundWarnings;internal static void SoundWarning(UnityEngine.Vector2 point){SoundWarnings++;}internal static int Observed;internal static void Observe(BuildingEntity entity){Observed++;}}
}

public sealed class Window{public bool activeSelf;}
public static class PauseHandler{public static bool paused;}
namespace DDEN.Shared
{
    public static class DdenHub{public static bool AnyPanelVisible;}
    public static class Mp{public static bool IsRunning,IsServer;}
}
namespace DDEN.LoongNova
{
    internal sealed class FixtureAmmo{internal bool Ready=true;}
    public sealed class DragonGun{internal bool Known=true;internal FixtureAmmo Ammo=new FixtureAmmo();}
}

public sealed class TraderScript{[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]public void Start(){}public T GetComponentInParent<T>() where T:class=>null;}
