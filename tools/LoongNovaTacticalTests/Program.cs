using System;
using DDEN.LoongNova;
using HarmonyLib;
using UnityEngine;
internal static class Program
{
    private static int _checks;
    private static void Check(bool valid,string message){if(!valid)throw new Exception(message);_checks++;}
    private static void Main()
    {
        var harmony=new Harmony("fixture.tactical");
        try{harmony.PatchAll(typeof(TacticalGogglesPatches));harmony.PatchAll(typeof(CannonSightPatch));Run();Console.WriteLine("PASS "+_checks+" real Harmony local-goggles camera/mask checks on owned fixtures; no Unity/game execution");}
        catch(Exception error){Console.WriteLine(error);Environment.ExitCode=1;}
        finally{TacticalGogglesPatches.Reset();harmony.UnpatchSelf();}
    }
    private static void Run()
    {
        var camera=PlayerCamera.main=new PlayerCamera();
        camera.HandleCameraPosition();Check(camera.ObservedZoom==0.1f&&camera.zoomTime==0.1f,"Bare eyes retain native zoom");
        camera.body.Worn=new Item{id=Registration.TacticalGogglesId};Time.frameCount++;
        camera.HandleCameraPosition();Check(camera.ObservedZoom==0.5f&&camera.zoomTime==0.1f,"Goggles scope native zoom to the local camera calculation");
        camera.body.Held=new Item{id=Registration.ChargeGunId,Gun=new DragonGun()};
        camera.HandleCameraPosition();Check(camera.ObservedZoom==0.5f&&camera.zoomTime==0.1f,"Simultaneous cannon and goggles reuse one scope without leaking zoom state");
        camera.body.Held=null;
        camera.zoomTime=0.8f;camera.HandleCameraPosition();Check(camera.ObservedZoom==0.8f&&camera.zoomTime==0.8f,"A stronger existing zoom effect is preserved");
        camera.Fail=true;bool threw=false;try{camera.HandleCameraPosition();}catch(InvalidOperationException){threw=true;}
        Check(threw&&camera.zoomTime==0.8f,"A native exception still restores the camera's original zoom timer");camera.Fail=false;
        camera.HandleScreenShaders();Check(camera.mainScreenPass.GetFloat("_NightVisAmount")==0.2f&&camera.mainScreenPass.GetFloat("_Brightness")==0.8f&&camera.mainScreenPass.GetFloat("_BrightMult")==0.6f,"Only native visibility shader parameters gain night vision");
        camera.NativeNight=0;camera.HandleScreenShaders();
        Check(camera.mainScreenPass.GetFloat("_NightVisAmount")==0.04f&&camera.mainScreenPass.GetFloat("_Brightness")==0.8f&&camera.mainScreenPass.GetFloat("_BrightMult")==0.6f,"Dark scenes receive only a small native night-vision floor, never global brightness/bloom amplification");
        camera.NativeNight=0.2f;
        var remote=new PlayerCamera();remote.HandleScreenShaders();Check(remote.mainScreenPass.GetFloat("_NightVisAmount")==0.2f,"Another camera never inherits local goggles' night vision");
        WorldGeneration.unchipped=false;camera.HandleLineOfSight();Check(camera.MaskCalculations==1&&camera.LOSHole.Renderer.enabled,"Chipped-mode line of sight remains intact");
        WorldGeneration.unchipped=true;Time.frameCount++;camera.HandleLineOfSight();
        Check(camera.MaskCalculations==1&&!camera.LOSHole.Renderer.enabled&&camera.Warnings==2,"Unchipped goggles skip expensive mask construction while retaining native warning processing");
        Check(WorldGeneration.lineOfSightEnabled,"The local mask override never mutates global visibility settings");
        remote.HandleLineOfSight();Check(remote.MaskCalculations==1&&remote.LOSHole.Renderer.enabled&&!camera.LOSHole.Renderer.enabled,"A different camera retains its mask and cannot restore the local mask early");
        camera.WarnSoundCannon(new Vector2());remote.WarnSoundCannon(new Vector2());Check(TacticalGogglesOverlay.SoundWarnings==1,"Only the wearer receives persistent native sonic warning events");
        camera.body.Worn=null;Time.frameCount++;camera.HandleLineOfSight();camera.HandleScreenShaders();
        Check(camera.LOSHole.Renderer.enabled&&camera.MaskCalculations==2&&camera.mainScreenPass.GetFloat("_NightVisAmount")==0.2f,"Removing goggles restores both native visibility and shader behavior");
        new BuildingEntity().Start();Check(TacticalGogglesOverlay.Observed==1,"Building starts feed the existing-object cache without polling the scene");
    }
}
