using DDEN.LoongNova;
using DDEN.Shared;
using CUCoreLib.Helpers;
using Mono.Cecil;
using Newtonsoft.Json.Linq;
using UnityEngine;

internal static class RevivalChecks
{
    private static Body Patient(bool dead)
    {
        var body=new Body {alive=!dead,bloodVolume=-90,bloodOxygen=0,bloodPressure=0,heartRate=0,temperature=20,
            fibrillationProgress=100,fibrillationForced=true,internalBleeding=90,hemothorax=90,septicShock=90,
            hasPulmonaryEmbolism=true,strokeAmount=100,hunger=-50,thirst=250};
        body.limbs=Enumerable.Range(0,15).Select(i=>new Limb {body=body,isHead=i==0,isVital=i==1,isAbdomen=i==2,
            dismembered=i>=3,broken=true,dislocated=true,muscleHealth=0,skinHealth=0,bleedAmount=100,infectionAmount=100,infected=true}).ToArray();
        foreach(var limb in body.limbs) limb.OnReattach=()=>{limb.dismembered=false;limb.muscleHealth=limb.skinHealth=100;};
        return body;
    }
    internal static void Run(Action<bool,string> check)
    {
        var profile=CannonAmmoState.Profile(CannonAmmoState.RevivalId);
        check(profile.Effect==CannonEffect.Revive&&profile.Structure==0&&profile.Creature==0&&profile.Pellets==1
            &&profile.BlastRadius==0&&profile.DamageMultiplier(3)==1,"The mysterious round must be non-damaging and independent of charge scaling");
        var ammo=new CannonAmmoState();
        check(ammo.Load(CannonAmmoState.RevivalId,0.7f,true)&&ammo.Rack()&&ammo.Load(CannonAmmoState.BioId,1,false),
            "Revival ammunition must accept the existing mixed chamber/magazine flow");
        var restored=new CannonAmmoState();
        check(restored.Restore(ammo.Capture())&&restored.Ready&&restored.Id==CannonAmmoState.RevivalId
            &&restored.Condition==0.7f&&restored.Favourite,"Revival round identity must survive existing save and network snapshots");
        check(restored.Consume(out var fired)&&fired==CannonAmmoState.RevivalId&&restored.Spent&&restored.Rack()&&restored.Id==CannonAmmoState.BioId,
            "A revival shot must leave a casing and require manual racking to chamber the reserve");
        check(!new AmpouleState().Load(CannonAmmoState.RevivalId,1,false),"Mysterious cannon rounds must never load into medical injectors");
        foreach(string key in new[]{"structureDamage","creatureDamage","spreadDegrees","blastRadius","size","chargeMultiplierMax"})
        {
            var invalid=profile.Parameters();invalid[key]=key=="chargeMultiplierMax"?2:1;
            bool rejected=false;try{CannonRoundRegistry.Register("invalid.revival",invalid);}catch(ArgumentException){rejected=true;}
            check(rejected&&!CannonRoundRegistry.Contains("invalid.revival"),"Revival metadata must reject mixed damage/area modes: "+key);
        }
        Mp.IsRunning=false;Mp.IsServer=false;Mp.CoreReady=true;
        var target=Patient(true);PlayerCamera.main=new PlayerCamera{body=target};
        target.GetStatus<RegenerationComa>().Remaining=60;
        target.Opioids=new Painkillers();target.Sedatives=new SleepingPills();
        check(RevivalEffects.TryRevive(target)&&target.alive&&target.brainHealth==80&&Math.Abs(0.5+target.bloodVolume/200.0-0.7)<0.00001,
            "The actual singleplayer body must revive with brain 80 and displayed blood volume 70 percent");
        check(target.limbs.All(l=>!l.dismembered&&l.skinHealth==25&&l.muscleHealth==25&&l.bleedAmount==0),
            "Every extremity must be reattached and all limbs must retain low health with bleeding stopped");
        check(!target.limbs[0].broken&&!target.limbs[1].broken&&!target.limbs[0].dislocated&&!target.limbs[1].dislocated,
            "Head and chest fractures/dislocations must be repaired during resuscitation");
        check(target.heartRate==70&&target.bloodOxygen==85&&target.bloodPressure==90&&target.breathing
            &&target.respiratoryRate==100&&target.fibrillationProgress==0&&!target.fibrillationForced
            &&target.internalBleeding==0&&target.hemothorax==0&&!target.hasPulmonaryEmbolism&&target.strokeAmount==0,
            "Restarted circulation must not retain immediate fatal fibrillation, hypoxia or internal bleeding");
        check(target.consciousness==40&&target.stamina==25&&target.GetStatus<RegenerationComa>().Remaining==0
            &&UnityEngine.Object.Destroyed.Contains(target.Opioids)&&UnityEngine.Object.Destroyed.Contains(target.Sedatives),
            "Revival must clear stale coma/sedatives while retaining weak consciousness and stamina");
        target.limbs[0].muscleHealth=15;target.bloodVolume=10;
        check(!RevivalEffects.TryRevive(target)&&target.limbs[0].muscleHealth==15&&target.bloodVolume==10,
            "A second hit must not resuscitate/heal an already living player directly");
        var stranger=Patient(true);
        check(!RevivalEffects.TryRevive(stranger)&&!stranger.alive,"Generated or unrelated bodies must never replace the singleplayer character");

        foreach(bool multiplayer in new[]{false,true})
        {
            Mp.IsRunning=multiplayer;Mp.IsServer=multiplayer;Mp.CoreReady=true;
            var shooter=new Body();target=Patient(true);PlayerCamera.main=new PlayerCamera{body=target};
            if(multiplayer)target.NetworkBody=new OfflineNetBody{plr=new OfflinePlayer{body=target}};
            var own=new Collider2D{gameObject=new GameObject{Body=shooter}};
            var corpse=new Collider2D{gameObject=new GameObject{Body=target}};
            var wall=new Collider2D{gameObject=new GameObject{layer=6}};
            Physics2D.Hits=new[]{new RaycastHit2D{collider=corpse,distance=8,point=new Vector2(8,0)},
                new RaycastHit2D{collider=own,distance=0},new RaycastHit2D{collider=wall,distance=3,point=new Vector2(3,0)}};
            int fields=HealingFields.Creates;
            var end=CannonRevival.Fire(shooter,new Vector2(0,0),new Vector2(1,0),profile);
            check(!target.alive&&end.x==3&&HealingFields.Creates==fields+1&&HealingFields.LastPoint.x==3,
                "A nearer wall must stop revival and create exactly one field at the actual impact point");
            Physics2D.Hits=new[]{new RaycastHit2D{collider=corpse,distance=8,point=new Vector2(8,0)}};
            int snapshots=OfflineMedicalSync.Pushes;
            CannonRevival.Fire(shooter,new Vector2(0,0),new Vector2(1,0),profile);
            check(target.alive&&HealingFields.Creates==fields+2,"A player corpse impact must revive once and create its green field");
            if(multiplayer)check(OfflineMedicalSync.Pushes==snapshots+1&&target.NetworkBody.Events.Count==1&&OfflinePlayerLists.Updates>0,
                "Host revival must issue the native revive marker, player-list refresh and reliable health snapshot");
            Physics2D.Hits=Array.Empty<RaycastHit2D>();fields=HealingFields.Creates;
            end=CannonRevival.Fire(shooter,new Vector2(0,0),new Vector2(1,0),profile);
            check(end.x==200&&HealingFields.Creates==fields,"A miss must never create an arbitrary distant healing field");
        }
        target=Patient(true);target.NetworkBody=new OfflineNetBody{plr=new OfflinePlayer{body=new Body()}};
        check(!RevivalEffects.TryRevive(target)&&!target.alive,"A stale corpse cannot revive a player whose current body is different");
        target.NetworkBody=new OfflineNetBody();
        check(!RevivalEffects.TryRevive(target)&&!target.alive,"A multiplayer NPC body must remain ineligible");
        Mp.IsServer=false;int pushes=OfflineMedicalSync.Pushes;
        bool blocked=false;try{RevivalEffects.TryRevive(target);}catch(InvalidOperationException){blocked=true;}
        check(blocked&&!target.alive&&pushes==OfflineMedicalSync.Pushes,"Clients must never apply a second resurrection");
        blocked=false;try{CannonRevival.Fire(new Body(),new Vector2(0,0),new Vector2(1,0),profile);}catch(InvalidOperationException){blocked=true;}
        check(blocked,"Client raycasts cannot spawn independent authoritative healing fields");

        foreach(int source in new[]{0,1,2,4,8,16,32,64,112,128,256,511})
        {
            check(LootRules.Select(source,"medical",0.0225)==(LootRules.Eligible(source,"medical")?CannonAmmoState.RevivalId:null),
                "Only eligible medical supplies/merchants may naturally select the mysterious round");
            check(LootRules.Select(source,"ammo",0.0365)==(LootRules.Eligible(source,"ammo")?CannonAmmoState.RevivalId:null),
                "Eligible ammo containers and merchants may now yield a standalone mysterious round");
            check(LootRules.Select(source,"utility",0.0465)==(LootRules.Eligible(source,"utility")?CannonAmmoState.RevivalId:null),
                "Eligible utility containers and merchants must share the new mysterious-round interval");
        }
        check(LootRules.BonusRound(6)==CannonAmmoState.RevivalId&&!ArsenalRecipes.Create().Any(r=>r.result.id==CannonAmmoState.RevivalId),
            "Generated cannons may bundle a revival round, which has no crafting recipe");
        FieldChecks(check);
        foreach(string id in new[]{Registration.GunId,Registration.ChargeGunId,MedicalCatalog.InjectorId})
        {
            var item=new Item{id=id};var other=new Item{id=id};var button=new InvButton{Item=item};
            var empty=new Sprite("empty");var loaded=new Sprite("loaded");
            other.Renderer.sprite=empty;
            void SetRefresh(Sprite sprite)
            {
                if(id==MedicalCatalog.InjectorId)MedicalInjector.For(item).OnRefresh=()=>item.Renderer.sprite=sprite;
                else DragonGun.For(item).OnRefresh=()=>item.Renderer.sprite=sprite;
            }
            SetRefresh(loaded);button.itemImg.sprite=empty;InventoryStateVisuals.Refresh(button);
            check(button.itemImg.sprite==loaded&&other.Renderer.sprite==empty,"Inventory state rendering must update the selected instance after a stale registered icon");
            SetRefresh(empty);button.itemImg.sprite=loaded;InventoryStateVisuals.Refresh(button);
            check(button.itemImg.sprite==empty,"Ejecting ammunition must restore the empty icon in the existing slot");
        }
        Mp.IsRunning=Mp.IsServer=false;Mp.CoreReady=true;PlayerCamera.main=null;
        Physics2D.Hits=Array.Empty<RaycastHit2D>();HealingFields.State.Active.Clear();
    }
    private static void FieldChecks(Action<bool,string> check)
    {
        var map=new HealingFieldState();
        check(map.Add("a",0,0,30)&&map.RemainingAt(6,0)==30&&map.RemainingAt(6.01f,0)==0,
            "Healing fields must have a six-tile radius and thirty-second lifetime");
        map.Advance(10);check(!map.Add("a",0,0,30)&&map.RemainingAt(0,0)==20,"Repeated field packets must never extend the lifetime");
        check(map.Add("b",0,0,30)&&map.RemainingAt(0,0)==30,"Overlapping fields use the longest remaining coverage, not stacked potency");
        map.Advance(30);check(map.Active.Count==0&&map.RemainingAt(0,0)==0,"Expired fields must stop all treatment");
        foreach(float invalid in new[]{float.NaN,float.PositiveInfinity,-1,0,31})check(!map.Add("bad",0,0,invalid),"Invalid field durations must not create persistent healing");
        foreach(float frame in new[]{0.02f,0.1f,1f,5f})
        {
            Mp.IsRunning=Mp.IsServer=false;
            var body=Patient(false);body.bloodVolume=40;body.bloodOxygen=70;body.temperature=32;
            PlayerCamera.main=new PlayerCamera{body=body};
            float elapsed=0;while(elapsed<5){float step=Math.Min(frame,5-elapsed);HealingFieldEffects.Apply(body,step);elapsed+=step;}
            check(body.limbs.All(l=>!l.dismembered&&!l.broken&&!l.dislocated&&l.skinHealth>99.99f&&l.muscleHealth>99.99f
                &&l.bleedAmount<0.001f&&l.infectionAmount<0.001f&&!l.infected),"Rapid physical healing/regrowth must work independently of frame rate");
            check(Math.Abs(body.bloodVolume-50)<0.002&&Math.Abs(body.bloodOxygen-80)<0.002&&Math.Abs(body.temperature-37)<0.002,
                "Blood and oxygen must recover slowly while body temperature normalizes without overshooting");
            body.bloodVolume=130;body.bloodOxygen=105;HealingFieldEffects.Apply(body,1);
            check(body.bloodVolume==130&&body.bloodOxygen==105,"Healing must not reduce blood/oxygen that already exceed the target");
        }
        var dead=Patient(true);PlayerCamera.main=new PlayerCamera{body=dead};
        HealingFieldEffects.Apply(dead,5);check(!dead.alive&&dead.bloodVolume==-90,"The field heals living players; remote corpses require a direct revival hit");
        Mp.IsRunning=Mp.IsServer=false;
        var walker=Patient(false);PlayerCamera.main=new PlayerCamera{body=walker};
        HealingFields.State.Active.Clear();HealingFields.State.Add("walk",0,0,30);Time.deltaTime=1;
        walker.transform.position=new Vector2(7,0);HealingFieldTreatmentPatch.Treat(walker);
        check(walker.limbs[0].muscleHealth==0&&walker.GetStatus<HealingFieldTreatment>().Remaining==0,
            "Players outside the actual field radius must not receive healing or its buff");
        walker.transform.position=new Vector2(5,0);HealingFieldTreatmentPatch.Treat(walker);
        check(walker.limbs[0].muscleHealth==20&&walker.GetStatus<HealingFieldTreatment>().Remaining==30,
            "Entering the field must activate the production healing patch and its status display");
        walker.transform.position=new Vector2(7,0);HealingFieldTreatmentPatch.Treat(walker);
        check(walker.limbs[0].muscleHealth==20&&walker.GetStatus<HealingFieldTreatment>().Remaining==0,
            "Leaving the field must stop its treatment without leaving a persistent healing buff");
        walker.transform.position=new Vector2(0,0);HealingFields.State.Advance(30);HealingFieldTreatmentPatch.Treat(walker);
        check(walker.limbs[0].muscleHealth==20,"An expired field must stop the actual body-update treatment");
        Mp.IsRunning=true;Mp.IsServer=false;bool blocked=false;
        try{HealingFieldEffects.Apply(dead,1);}catch(InvalidOperationException){blocked=true;}
        check(blocked,"Clients must never add local health on top of authoritative healing packets");
    }
    internal static void Inspect(AssemblyDefinition assembly,Action<bool,string> check,string root)
    {
        var types=assembly.MainModule.Types;
        var awake=types.Single(t=>t.Name=="LoongNovaPlugin").Methods.Single(m=>m.Name=="Awake").Body.Instructions;
        foreach(string patch in new[]{"InventoryStateVisuals","HealingFieldTreatmentPatch"})
            check(awake.Any(i=>i.Operand is TypeReference type&&type.Name==patch),"The plugin must actually install its gameplay/UI patch: "+patch);
        var registration=types.Single(t=>t.Name=="Registration");
        var calls=registration.Methods.Single(m=>m.Name=="RegisterChargeGun").Body.Instructions.ToList();
        int revival=calls.FindIndex(i=>i.Operand is MethodReference m&&m.Name=="RegisterRound"
            &&i.Previous?.Operand is sbyte n&&n==25);
        check(revival>=0&&calls[revival-2].Operand is sbyte price&&price==60,
            "The real item registration must pass recognition INT25 and base value60");
        {
            var content=LocaleChecks.Chinese(assembly);
            check(content.Value<string>("revivalName")=="龙域武库-????弹"&&!content.Value<string>("revivalDescription").Contains("复活"),
                "The name and item description must retain their mystery after recognition");
            check(content.Value<string>("fieldBuffName")=="????弹"&&content.Value<string>("fieldBuffDescription")=="你猜的没错，这是代表生命的亮绿色。",
                "The healing-field buff must display the exact requested name and description");
        }
        foreach(string file in new[]{"revival-round.png","bio-erasure-round.png","demolition-round.png","drill-round.png","spatial-drain-round.png",
            "emp-breach-round.png","explosive-round.png","universal-ammo-carrier.png","regeneration-ampoule.png","medical-ampoule.png","medical-injector.png","medical-injector-empty.png","ampoule.png"})
        {
            using var stream=((EmbeddedResource)assembly.MainModule.Resources.Single(r=>r.Name.EndsWith("."+file))).GetResourceStream();
            using var memory=new MemoryStream();stream.CopyTo(memory);
            check(memory.ToArray().SequenceEqual(File.ReadAllBytes(Path.Combine(root,"src/DDEN.LoongNova/images",file))),
                "The build must embed the user's exact untouched artwork: "+file);
        }
        var fire=types.Single(t=>t.Name=="CannonRevival").Methods.Single(m=>m.Name=="Fire").Body.Instructions;
        check(fire.Any(i=>i.Operand is MethodReference m&&m.DeclaringType.Name=="HealingFields"&&m.Name=="Create")
            &&!fire.Any(i=>i.Operand is MethodReference m&&(m.Name=="Shoot"||m.Name=="CreateExplosion")),"Actual revival impacts must create healing fields without normal shot damage");
        var effects=types.Single(t=>t.Name=="RevivalEffects");
        check(effects.Methods.Single(m=>m.Name=="TryRevive").Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="PushHealth"),
            "Actual resurrection must use the original medical health synchronization");
        var inventory=types.Single(t=>t.Name=="InventoryStateVisuals").Methods.Single(m=>m.Name=="Refresh");
        check(inventory.CustomAttributes.Any(a=>a.AttributeType.Name=="HarmonyPriority"&&(int)a.ConstructorArguments[0].Value==0)
            &&inventory.Body.Instructions.Count(i=>i.Operand is MethodReference m&&m.Name=="RefreshSprite")==3,
            "Inventory state icons must refresh guns/injectors/rechargers after ordinary icon patches");
        check(types.Single(t=>t.Name=="MedicalInjector").Methods.Single(m=>m.Name=="RefreshSprite").Body.Instructions
            .Any(i=>i.Operand is MethodReference m&&m.Name=="SetItemSprite"),"Injector load state must also update CCL's per-instance icon override");
        check(types.Single(t=>t.Name=="DragonGun").Methods.Single(m=>m.Name=="LateUpdate").Body.Instructions
            .Any(i=>i.Operand is MethodReference m&&m.Name=="RefreshSprite"),"Held gun sprites must be reapplied after native gun Update");
        var status=types.Single(t=>t.Name=="HealingFieldTreatment").CustomAttributes.Single(a=>a.AttributeType.Name=="StatusOptionsAttribute");
        check(status.Properties.Any(p=>p.Name=="SaveEnabled"&&!(bool)p.Argument.Value)
            &&status.Properties.Any(p=>p.Name=="ClientAuthoritative"&&!(bool)p.Argument.Value),"Field treatment must be transient and host authoritative");
        var registerSync=types.Single(t=>t.Name=="WeaponService").Methods.Single(m=>m.Name=="Tick").Body.Instructions;
        check(registerSync.Any(i=>i.Operand as string=="dden.loongnova.healing-fields.v1"),"Late join snapshots must include still-active healing fields");
        using var mp=AssemblyDefinition.ReadAssembly(@"L:\SteamLibrary\steamapps\common\Casualties Unknown\BepInEx\plugins\Together\CasualtiesMP.dll");
        var nb=mp.MainModule.Types.Single(t=>t.FullName=="Together.NetBody");
        check(nb.Methods.Any(m=>m.Name=="OnDamageEvent"&&m.IsPublic)&&mp.MainModule.Types.Single(t=>t.FullName=="Together.SharedMain").Methods
            .Any(m=>m.Name=="ForceUpdatePlayerLists"&&m.IsPublic&&m.IsStatic),"Revive markers/list refresh must match the installed MP API");
        check(nb.Methods.Any(m=>m.HasBody&&m.Body.Instructions.Any(i=>i.Operand is MethodReference called&&called.Name=="StopSpectatorMode")),
            "The installed MP must support control recovery when a dead body becomes alive");
    }
}
