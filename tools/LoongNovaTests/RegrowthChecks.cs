using DDEN.LoongNova;
using DDEN.Shared;
using CUCoreLib.Helpers;
using Mono.Cecil;
using Newtonsoft.Json.Linq;
using UnityEngine;

internal static class RegrowthChecks
{
    private static (Body Body,Limb Root,Limb[] Parts) Arm()
    {
        var body=new Body();var root=new Limb{body=body,isVital=true,distanceToHeart=0};
        var parts=Enumerable.Range(1,3).Select(n=>new Limb{body=body,dismembered=true,distanceToHeart=n,muscleHealth=0,skinHealth=0}).ToArray();
        root.connectedLimbs=new[]{parts[0]};parts[0].connectedLimbs=new[]{root,parts[1]};
        parts[1].connectedLimbs=new[]{parts[0],parts[2]};parts[2].connectedLimbs=new[]{parts[1]};
        foreach(var part in parts)part.gameObject.SetActive(false);
        body.limbs=new[]{root}.Concat(parts).ToArray();return(body,root,parts);
    }
    internal static void Run(Action<bool,string> check)
    {
        foreach(int count in new[]{1,2})foreach(float frame in new[]{0.02f,0.1f,1f,5f})
        {
            Mp.IsRunning=Mp.IsServer=false;
            var (body,root,parts)=Arm();
            check(DelayedRegrowth.Schedule(root,count),"An adjacent severed limb must support a delayed growth plan");
            var status=root.GetStatus<LimbGrowth>();
            check(status.Remaining==45&&status.Targets.Length==count&&parts.All(l=>l.dismembered),"Planning must preserve all missing segments until its 45-second timer ends");
            check(!DelayedRegrowth.Schedule(root,2)&&!MedicalEffects.CanInject(root,MedicalCatalog.RegenerationId),"An active injection site must reject a duplicate regeneration dose");
            var saved=JObject.FromObject(status);var loaded=saved.ToObject<LimbGrowth>();
            status.Remaining=loaded.Remaining;status.Targets=loaded.Targets;
            float elapsed=0;
            while(elapsed<44){float step=Math.Min(frame,44-elapsed);Time.deltaTime=step;LimbGrowthPatch.Grow(body);elapsed+=step;}
            check(parts.All(l=>l.dismembered),"Saved growth must not produce limbs before its deadline at any frame rate");
            Time.deltaTime=1.01f;LimbGrowthPatch.Grow(body);
            check(parts.Count(l=>!l.dismembered)==count&&parts[2].dismembered&&!parts[2].gameObject.activeInHierarchy,
                "One dose must restore only one or two proximal segments, never an entire three-segment arm");
            check(parts.Take(count).All(l=>l.gameObject.activeInHierarchy&&l.muscleHealth==10&&l.skinHealth==10&&l.Reattachments==0),
                "Selected segments must activate at ten percent health without invoking full-branch reattachment");
            parts[0].muscleHealth=6;Time.deltaTime=60;LimbGrowthPatch.Grow(body);
            check(parts[0].muscleHealth==6&&parts[2].dismembered&&status.Targets.Length==0,"Completed growth must never repeat or restore an unplanned third segment");
        }
        foreach(bool death in new[]{true,false})
        {
            var (body,root,parts)=Arm();DelayedRegrowth.Schedule(root,2);
            if(death)body.alive=false;else root.dismembered=true;
            Time.deltaTime=50;LimbGrowthPatch.Grow(body);
            check(root.GetStatus<LimbGrowth>().Remaining==0&&parts.All(p=>p.dismembered),"Death or amputation of the injection site must cancel pending growth");
        }
        var fork=Arm();fork.Parts[0].prostheticInfo=new Limb.ProstheticInfo{hasProsthetic=true};
        check(!DelayedRegrowth.Schedule(fork.Root,2),"Growth must never replace prosthetic branches");
        var bad=Arm();DelayedRegrowth.Schedule(bad.Root,2);bad.Root.GetStatus<LimbGrowth>().Targets=new[]{999,-1,3};
        Time.deltaTime=45;LimbGrowthPatch.Grow(bad.Body);
        check(bad.Parts.All(p=>p.dismembered),"Malformed persisted targets cannot bypass the two-segment cap or grow an unattached distal part");
        var host=Arm();Mp.IsRunning=Mp.IsServer=Mp.CoreReady=true;host.Body.NetworkBody=new OfflineNetBody();
        DelayedRegrowth.Schedule(host.Root,2);int pushes=OfflineMedicalSync.Pushes;int repairs=OfflineLimbRepair.Restored.Count;
        Time.deltaTime=45;LimbGrowthPatch.Grow(host.Body);
        check(OfflineMedicalSync.Pushes==pushes+1&&OfflineLimbRepair.Restored.Count==repairs+2&&host.Parts[2].dismembered,
            "Host completion must repair exactly the planned segments and send one original health snapshot");
        var client=Arm();DelayedRegrowth.Schedule(client.Root,2);Mp.IsServer=false;Time.deltaTime=45;LimbGrowthPatch.Grow(client.Body);
        check(client.Parts.All(p=>p.dismembered)&&OfflineMedicalSync.Pushes==pushes+1,"Clients may count down but must never grow a second copy of limbs");

        Mp.IsRunning=Mp.IsServer=false;
        foreach(string id in new[]{MedicalCatalog.MedicalId,MedicalCatalog.RegenerationId})
        {
            var slot=new AmpouleState();slot.Load(id,0.7f,true);long before=slot.Revision;
            check(slot.Consume(out var dose)&&dose==id&&slot.Spent&&slot.Id==MedicalCatalog.EmptyId&&slot.Revision==before+1,
                "Injecting either ampoule must retain exactly one empty ampoule in the same slot");
            var saved=slot.Capture();var loaded=new AmpouleState();
            check(loaded.Restore(saved)&&loaded.Spent&&!loaded.Ready&&!loaded.Consume(out _)&&!loaded.Load(id,1,false),
                "A retained empty ampoule must save correctly and block reinjection or loading before ejection");
            loaded.Clear();check(loaded.Load(id,1,false),"Ejecting the empty ampoule must allow the next full dose");
            check(!new AmpouleState().Load(MedicalCatalog.EmptyId,1,false)&&!new CannonAmmoState().Load(MedicalCatalog.EmptyId,1,false),
                "Loose empty ampoules must not be accepted as live injector or cannon ammunition");
        }
        foreach(float frame in new[]{0.02f,0.1f,1f,5f})
        {
            var body=new Body{bloodVolume=37,septicShock=50,venomCurrent=50,venomTotal=50,bloodViscosity=-30,radiationSickness=50};
            var limb=new Limb{body=body};body.limbs=new[]{limb};MedicalEffects.Apply(limb,MedicalCatalog.MedicalId);
            float elapsed=0;while(elapsed<10){float step=Math.Min(frame,10-elapsed);Time.deltaTime=step;DragonLifeBlessingPatch.Bless(body);elapsed+=step;}
            check(Math.Abs(body.venomCurrent-25)<0.01&&Math.Abs(body.venomTotal-25)<0.01&&Math.Abs(body.septicShock-25)<0.01
                &&Math.Abs(body.bloodViscosity+20)<0.01&&Math.Abs(body.radiationSickness-40)<0.01&&body.bloodVolume==37,
                "Rapid treatment must remove venom reservoirs/sepsis, normalize blood viscosity and reduce radiation at 1/s without adding blood");
            Time.deltaTime=5;DragonLifeBlessingPatch.Bless(body);
            check(Math.Abs(body.radiationSickness-35)<0.01&&body.venomTotal<13,"The aftereffect must continue blood detoxification and moderate radiation treatment");
            PlayerCamera.main=new PlayerCamera{body=body};body.venomCurrent=body.venomTotal=50;body.radiationSickness=50;
            HealingFieldEffects.Apply(body,5);
            check(body.venomCurrent==37.5f&&body.venomTotal==37.5f&&body.radiationSickness==45,
                "Mysterious-round fields must provide the same antivenom mechanism and moderate radiation recovery");
            body.venomCurrent=body.venomTotal=body.septicShock=body.radiationSickness=0;body.bloodViscosity=0;
            MedicalEffects.RecoverBlood(body,100);
            check(body.venomCurrent==0&&body.venomTotal==0&&body.radiationSickness==0&&body.bloodViscosity==0,"Detoxification must never overshoot into negative poison/radiation or abnormal viscosity");
        }
        var remote=new Body{venomCurrent=50,venomTotal=50,radiationSickness=50};Mp.IsRunning=true;Mp.IsServer=false;
        MedicalEffects.RecoverBlood(remote,5);check(remote.venomCurrent==50&&remote.venomTotal==50&&remote.radiationSickness==50,
            "Clients must not apply additional detoxification on top of host health snapshots");
        Mp.IsRunning=Mp.IsServer=false;PlayerCamera.main=null;
    }
    internal static void Inspect(AssemblyDefinition assembly,Action<bool,string> check)
    {
        var types=assembly.MainModule.Types;
        var awake=types.Single(t=>t.Name=="LoongNovaPlugin").Methods.Single(m=>m.Name=="Awake").Body.Instructions;
        check(awake.Any(i=>i.Operand is TypeReference t&&t.Name=="LimbGrowthPatch"),"The actual plugin must install delayed growth updates");
        var restore=types.Single(t=>t.Name=="PartialRegrowth").Methods.Single(m=>m.Name=="Restore").Body.Instructions;
        check(!restore.Any(i=>i.Operand is MethodReference m&&m.Name=="Reattach")&&restore.Any(i=>i.Operand as string=="RestoreDetachedLimb")
            &&restore.Any(i=>i.Operand as string=="RebindLimbHinges"),"Partial growth must avoid whole-arm recovery and reuse native MP physics repairs");
        var hint=types.Single(t=>t.Name=="MedicalPatches").Methods.Single(m=>m.Name=="SlotHint").Body.Instructions;
        check(hint.Any(i=>i.Operand is string text&&text.Contains("<color=#5eff00><sprite index=")),"Injector ammunition hints must use the same green battery-icon style as guns");
        using var stream=((EmbeddedResource)assembly.MainModule.Resources.Single(r=>r.Name.EndsWith("Content.json"))).GetResourceStream();
        using var reader=new StreamReader(stream);var text=JObject.Parse(reader.ReadToEnd());
        foreach(var pair in new[]{("growthBuffName","安瓿-肢体生长"),("comaBuffName","安瓿-再生过程"),
            ("treatmentBuffName","安瓿-快速治疗"),("blessingBuffName","安瓿-治疗后效")})
            check(text.Value<string>(pair.Item1)==pair.Item2,"Medical status display names must match the requested labels");
        using var mp=AssemblyDefinition.ReadAssembly(@"L:\SteamLibrary\steamapps\common\Casualties Unknown\BepInEx\plugins\Together\CasualtiesMP.dll");
        var util=mp.MainModule.Types.Single(t=>t.FullName=="CasualtiesTogetherUtils.Util");
        foreach(string method in new[]{"RestoreDetachedLimb","IgnoreOwnLimbCollisions","RebindLimbHinges"})
            check(util.Methods.Any(m=>m.Name==method&&m.IsPublic&&m.IsStatic),"Partial-growth compatibility must match the installed MP API: "+method);
    }
}
