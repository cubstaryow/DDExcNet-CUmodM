using DDEN.LoongNova;
using CUCoreLib.Helpers;
using Newtonsoft.Json.Linq;
using Mono.Cecil;

internal static class MedicalChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var manufactured=new AmpouleState();
        check(!manufactured.Loaded&&!manufactured.SpawnDoseGranted,"Manufactured injectors must begin empty before any explicit purchase/drop grant");
        var factorySlot=new AmpouleState();
        check(factorySlot.TryGrantSpawnDose()&&factorySlot.Id==MedicalCatalog.MedicalId&&factorySlot.Condition==1&&factorySlot.SpawnDoseGranted,
            "A new injector must receive exactly one full medical ampoule");
        check(!factorySlot.TryGrantSpawnDose(),"Duplicate initialization must not replace or add a dose");
        factorySlot.Consume(out _);var emptyFactorySave=factorySlot.Capture();var emptyFactoryRestored=new AmpouleState();
        check(emptyFactoryRestored.Restore(emptyFactorySave)&&emptyFactoryRestored.SpawnDoseGranted&&!emptyFactoryRestored.TryGrantSpawnDose(),
            "An emptied injector must stay empty after saving instead of receiving another free ampoule");
        var badGrant=emptyFactorySave.DeepClone();badGrant["spawnDoseGranted"]="true";
        check(!emptyFactoryRestored.Restore(badGrant)&&emptyFactoryRestored.Spent,"Malformed one-time spawn markers must preserve the retained empty ampoule atomically");
        var legacyMedical=emptyFactorySave.DeepClone();((JObject)legacyMedical).Remove("spawnDoseGranted");
        check(new AmpouleState().Restore(legacyMedical),"Pre-grant medical saves must remain readable without a refill");
        foreach(string id in new[]{MedicalCatalog.RegenerationId,MedicalCatalog.MedicalId})
        {
            check(!CannonAmmoState.IsRound(id)&&!new CannonAmmoState().Load(id,1,false),"Medical ampoules must never enter a cannon magazine");
            var slot=new AmpouleState();check(slot.Load(id,0.75f,true)&&!slot.Load(id,1,false),"A medical injector must hold exactly one ampoule");
            var saved=slot.Capture();var restored=new AmpouleState();
            check(restored.Restore(saved)&&restored.Id==id&&restored.Condition==0.75f&&restored.Favourite,"Ampoule identity/condition/favourite must persist");
            check(restored.Consume(out var used)&&used==id&&restored.Spent&&restored.Loaded&&!restored.Ready&&!restored.Consume(out _),"One injection must consume exactly one dose and retain a non-injectable empty ampoule");
            foreach(string weaponId in Enumerable.Range(0,LootRules.RoundCount).Select(LootRules.BonusRound).Append(Registration.CellId).Append(CannonAmmoState.CarrierId))
                check(!new AmpouleState().Load(weaponId,1,false),"Medical sockets must reject every weapon round, spent case and battery");
            foreach(float bad in new[]{0,-1,1.1f,float.NaN,float.PositiveInfinity})check(!new AmpouleState().Load(id,bad,false),"Invalid ampoule charge/condition must fail without consumption");
            foreach(string key in new[]{"schema","id","condition","favourite","revision"})
            {
                var malformed=(JObject)saved.DeepClone();malformed.Remove(key);string before=slot.Capture().ToString();
                check(!slot.Restore(malformed)&&slot.Capture().ToString()==before,"Incomplete medical saves must fail atomically: "+key);
            }
            var wrong=saved.DeepClone();wrong["id"]=CannonAmmoState.BioId;
            check(!slot.Restore(wrong)&&slot.Id==id,"A weapon ID in an ampoule save must not cross ammunition families");
        }
        foreach(float field in new[]{-99f,-80,0,40,100,180})
        {
            float before=0.5f+field/200;
            float after=0.5f+MedicalRules.RegenerationBlood(field)/200;
            check(Math.Abs(after-before*0.8f)<0.000001,"Regeneration must consume 20 percent of actual volume, including the vanilla offset");
        }
        check(MedicalRules.RegenerationBlood(100)==60,"Normal blood volume must become 80 percent, which is native field value 60");
        var owner=new Body();var stump=new Limb{body=owner,muscleHealth=80,skinHealth=40,distanceToHeart=0};
        var missing=new Limb{body=owner,dismembered=true,distanceToHeart=1};
        var distal=new Limb{body=owner,dismembered=true,distanceToHeart=2};
        var unrelated=new Limb{body=owner,dismembered=true,distanceToHeart=1};
        stump.connectedLimbs=new[]{missing};missing.connectedLimbs=new[]{stump,distal};distal.connectedLimbs=new[]{missing};
        missing.GetStatus<MedicalTreatment>().Remaining=2;
        owner.limbs=new[]{stump,missing,distal,unrelated};
        check(MedicalEffects.CanInject(stump,MedicalCatalog.RegenerationId),"A valid stump must recognize its adjacent missing branch");
        DDEN.Shared.Mp.IsRunning=false;
        MedicalEffects.Apply(stump,MedicalCatalog.RegenerationId);
        check(missing.dismembered&&distal.dismembered&&stump.GetStatus<LimbGrowth>().Remaining==45,
            "Regeneration must start a saved 45-second growth timer without immediately restoring limbs");
        UnityEngine.Time.deltaTime=44;LimbGrowthPatch.Grow(owner);
        check(missing.dismembered&&distal.dismembered&&stump.GetStatus<LimbGrowth>().Remaining==1,"Growth must wait for the entire countdown");
        UnityEngine.Time.deltaTime=1;LimbGrowthPatch.Grow(owner);
        check(!missing.dismembered&&!distal.dismembered&&unrelated.dismembered&&missing.Reattachments==0,
            "Delayed growth must restore only planned segments without using whole-extremity reattachment");
        check(missing.muscleHealth==10&&missing.skinHealth==10&&distal.muscleHealth==10&&distal.skinHealth==10,"Every newly restored segment must finish at ten percent health");
        check(missing.GetStatus<MedicalTreatment>().Remaining==0,"Newly grown limbs must not inherit a stale local treatment from the amputated limb");
        check(stump.muscleHealth==20&&stump.skinHealth==10&&owner.bloodVolume==60&&owner.consciousness==0&&owner.Ragdolls==1,
            "Regeneration must quarter both stump health values, consume actual blood and begin unconsciousness");
        check(!MedicalEffects.CanInject(stump,MedicalCatalog.RegenerationId),"A restored branch must not allow wasting another regeneration dose");
        owner.consciousness=70;UnityEngine.Time.deltaTime=59;RegenerationComaPatch.Hold(owner);
        check(owner.GetStatus<RegenerationComa>().Remaining==1&&owner.consciousness==0,"The regeneration coma must continuously hold for the full minute");
        UnityEngine.Time.deltaTime=1;RegenerationComaPatch.Hold(owner);owner.consciousness=3;RegenerationComaPatch.Hold(owner);
        check(owner.GetStatus<RegenerationComa>().Remaining==0&&owner.consciousness==3,"Expiration must stop holding consciousness without forcing recovery or healing");
        foreach(float frame in new[]{0.02f,0.1f,1f,5f})
        {
            var body=new Body{bloodVolume=37};
            var local=new Limb{body=body,muscleHealth=5,skinHealth=10,infectionAmount=100,infected=true,bleedAmount=100,pain=100};
            var distant=new Limb{body=body,muscleHealth=20,skinHealth=25,infectionAmount=90,infected=true,pain=80};
            var prosthetic=new Limb{body=body,muscleHealth=20,skinHealth=30,prostheticInfo=new Limb.ProstheticInfo{hasProsthetic=true}};
            var severed=new Limb{body=body,dismembered=true,muscleHealth=0,skinHealth=0};body.limbs=new[]{local,distant,prosthetic,severed};
            MedicalEffects.Apply(local,MedicalCatalog.MedicalId);
            check(local.muscleHealth==5&&local.disinfectionTime==600&&body.bloodVolume==37,"Medical doses must start local treatment and disinfection without an instant heal or blood refill");
            float elapsed=0;while(elapsed<3){float step=Math.Min(frame,3-elapsed);UnityEngine.Time.deltaTime=step;MedicalTreatmentPatch.Treat(local);DragonLifeBlessingPatch.Bless(body);elapsed+=step;}
            check(Math.Abs(local.muscleHealth-65)<0.002&&Math.Abs(local.skinHealth-70)<0.002&&local.infectionAmount<0.002&&local.bleedAmount<10.002&&local.pain<0.002,
                "The three-second burst must restore sixty limb-health points and strongly reduce infection, bleeding and pain at any frame rate");
            check(distant.muscleHealth==20&&body.GetStatus<DragonLifeBlessing>().Remaining>119.99
                &&Math.Abs(local.GetStatus<MedicalTreatment>().Remaining-7)<0.002,
                "Rapid treatment must continue beyond three seconds and defer its aftereffect for ten seconds");
            while(elapsed<10){float step=Math.Min(frame,10-elapsed);UnityEngine.Time.deltaTime=step;MedicalTreatmentPatch.Treat(local);DragonLifeBlessingPatch.Bless(body);elapsed+=step;}
            check(local.muscleHealth==100&&local.skinHealth==100&&local.bleedAmount<0.002&&local.GetStatus<MedicalTreatment>().Remaining<0.002,
                "Ten seconds of rapid treatment must finish at capped limb health and complete hemostasis");
            check(Math.Abs(distant.muscleHealth-20)<0.002&&body.GetStatus<DragonLifeBlessing>().Remaining>119.99,
                "The full 120-second aftereffect must begin only after the ten-second local treatment");
            elapsed=0;while(elapsed<120){float step=Math.Min(frame,120-elapsed);UnityEngine.Time.deltaTime=step;DragonLifeBlessingPatch.Bless(body);elapsed+=step;}
            check(Math.Abs(distant.muscleHealth-80)<0.02&&Math.Abs(distant.skinHealth-85)<0.02&&Math.Abs(distant.infectionAmount-30)<0.02&&distant.pain<0.001,
                "Dragon Life Blessing must heal all present organic limbs at 0.5/s and provide analgesia for 120 seconds");
            check(local.muscleHealth==100&&local.skinHealth==100&&prosthetic.muscleHealth==20&&severed.muscleHealth==0&&body.bloodVolume==37,
                "Healing must cap at full limb health, skip prostheses/missing limbs, and never alter blood volume");
            float ended=distant.muscleHealth;UnityEngine.Time.deltaTime=5;DragonLifeBlessingPatch.Bless(body);
            check(Math.Abs(distant.muscleHealth-ended)<0.02,"Blessing must expire without an extra tick beyond its duration");
        }
        var clientBody=new Body{NetworkBody=new OfflineNetBody(),bloodVolume=50};
        var clientLimb=new Limb{body=clientBody,muscleHealth=10,skinHealth=10,infectionAmount=90,pain=50};clientBody.limbs=new[]{clientLimb};
        DDEN.Shared.Mp.IsRunning=true;DDEN.Shared.Mp.IsServer=false;bool rejected=false;
        try{MedicalEffects.Apply(clientLimb,MedicalCatalog.MedicalId);}catch(InvalidOperationException){rejected=true;}
        check(rejected&&clientBody.bloodVolume==50&&clientLimb.muscleHealth==10,"Clients must never apply or spend a second medical dose locally");
        clientLimb.GetStatus<MedicalTreatment>().Remaining=3;clientBody.GetStatus<DragonLifeBlessing>().Remaining=120;
        UnityEngine.Time.deltaTime=1;MedicalTreatmentPatch.Treat(clientLimb);DragonLifeBlessingPatch.Bless(clientBody);
        check(clientLimb.muscleHealth==10&&clientLimb.infectionAmount==90&&clientLimb.pain==50,"Client BUFF countdowns must leave health to the native authoritative packet");
        DDEN.Shared.Mp.IsServer=true;MedicalEffects.Apply(clientLimb,MedicalCatalog.MedicalId);
        check(OfflineMedicalSync.Pushes==1,"Host medical actions must force a reliable original health snapshot");
        DDEN.Shared.Mp.IsRunning=false;DDEN.Shared.Mp.IsServer=false;
        foreach(var snapshot in new[]{JObject.FromObject(new RegenerationComa{Remaining=42}),JObject.FromObject(new DragonLifeBlessing{Delay=1,Remaining=111})})
            check(snapshot.Value<float>("Remaining")>0,"Persistent CCL statuses must expose their remaining duration in serializable data");
        var tool=new Item{id=MedicalCatalog.InjectorId};var vial=new Item{id=MedicalCatalog.MedicalId};
        var button=new InvButton{Item=tool};var hit=new UnityEngine.EventSystems.RaycastResult{gameObject=new UnityEngine.EventSystems.FakeHit{Button=button}};
        var hits=new List<UnityEngine.EventSystems.RaycastResult>{hit};var camera=new PlayerCamera{body=new Body(),dragItem=vial};bool result=false;
        check(!BeltInteractionPatches.InventoryAction(camera,hit,hits,ref result)&&result&&MedicalInjector.For(tool).RequestedAmpoule==vial,
            "Ground/container ampoule dragging must use the same early inventory loading path as existing equipment");
        camera.dragItem=new Item{id=CannonAmmoState.BioId};int requests=MedicalInjector.For(tool).Requests;
        check(BeltInteractionPatches.InventoryAction(camera,hit,hits,ref result)&&MedicalInjector.For(tool).Requests==requests,"Weapon rounds must never dispatch an injector load");
        check(LootRules.Select(2,"medical",0)==MedicalCatalog.InjectorId&&LootRules.Select(2,"medical",0.00125)==MedicalCatalog.RegenerationId
            &&LootRules.Select(2,"medical",0.002)==MedicalCatalog.MedicalId&&LootRules.Select(2,"ammo",0)==null,"Medical equipment must enter medical-crate slots without adding weapon ammo to those crates");
        foreach(string id in new[]{"grabbershroom","mushroomdropper","bouncecap","fungalibungali","mushroomplatform","sidestabber","sidestabberflip","skullcrusher","jumppad","soundcannon","coil","piston","pistonleft","pistonright","turret","ceilingturret","sawblade","landmine"})
            check(BreachTargets.IsTarget("BuildingEntity",id,true),"Explicit stationary traps must be included even if generic/organic: "+id);
        foreach(string type in new[]{"GrabberPlant","BounceShroom","SoundCannon","JumpPadScript","PistonScript","BarbedFence","SpikeStabberScript","BearTrap","SawbladeScript"})
            check(BreachTargets.IsTarget(type,null,false),"Native trap behavior must be included: "+type);
        foreach(string type in new[]{"GrabberPlant","BounceShroom","SpikeStabberScript"})
            check(BreachTargets.IsTarget(type,null,true),"Organic trap components must remain removable even without an explicit prefab ID: "+type);
        foreach(string id in new[]{"thornbackelder","shadecrawler","laserdrone","caveticks","overgrowntick","guard","trader"})
            check(!BreachTargets.IsTarget("BuildingEntity",id,true),"Extending stationary hazards must not include roaming creatures or NPCs: "+id);
    }

    internal static void Inspect(AssemblyDefinition assembly,Action<bool,string> check)
    {
        var types=assembly.MainModule.Types;
        var effects=types.Single(t=>t.Name=="MedicalEffects");
        var apply=effects.Methods.Single(m=>m.Name=="Apply");
        check(apply.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="Schedule")
            &&apply.Body.Instructions.Count(i=>i.Operand is FieldReference f&&f.Name=="bloodVolume"&&i.OpCode==Mono.Cecil.Cil.OpCodes.Stfld)==1,
            "The real regeneration path must schedule delayed reconstruction; only regeneration may change blood volume");
        foreach(var name in new[]{"RegenerationComa","MedicalTreatment","DragonLifeBlessing","LimbGrowth"})
        {
            var status=types.Single(t=>t.Name==name);
            var options=status.CustomAttributes.Single(a=>a.AttributeType.Name=="StatusOptionsAttribute");
            check(options.Properties.Any(p=>p.Name=="SaveEnabled"&&(bool)p.Argument.Value)
                &&options.Properties.Any(p=>p.Name=="ClientAuthoritative"&&!(bool)p.Argument.Value),"Medical statuses must use CCL persistence and host-authoritative ownership: "+name);
        }
        foreach(var name in new[]{"MedicalTreatmentPatch","DragonLifeBlessingPatch"})
            check(!types.Single(t=>t.Name==name).Methods.Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Any(i=>i.Operand is FieldReference f&&f.Name=="bloodVolume"),
                "Medical healing BUFFs must never refill or drain blood: "+name);
        var register=types.Single(t=>t.Name=="Registration");
        var medicalRegister=register.Methods.Single(m=>m.Name=="RegisterMedical");
        var spriteCalls=medicalRegister.Body.Instructions.ToList();
        check(spriteCalls.Where((i,index)=>i.Operand is MethodReference m&&m.Name=="LoadSprite"
            &&index>0&&spriteCalls[index-1].OpCode==Mono.Cecil.Cil.OpCodes.Ldc_I4_1).Count()==3,
            "Both injector sprites must retain their common frame so the gun body stays still during loading");
        check(medicalRegister.Body.Instructions.Count(i=>i.Operand is MethodReference m&&m.DeclaringType.Name=="MoodleRegistry")==5,
            "Coma, local treatment, Dragon Life Blessing and healing fields must all have native-style status displays");
        check(register.Methods.Single(m=>m.Name=="RegisterAmpoule").Body.Instructions.Any(i=>i.Operand as string==MedicalCatalog.DataKey)
            &&!register.Methods.Single(m=>m.Name=="RegisterAmpoule").Body.Instructions.Any(i=>i.Operand as string==CannonRoundRegistry.DataKey),
            "Medical doses must use separate metadata and must not register as cannon ammunition");
        var injector=types.Single(t=>t.Name=="MedicalInjector");
        var inject=injector.Methods.Single(m=>m.Name=="Inject").Body.Instructions;
        int can=inject.ToList().FindIndex(i=>i.Operand is MethodReference m&&m.Name=="CanInject");
        int consume=inject.ToList().FindIndex(i=>i.Operand is MethodReference m&&m.Name=="Consume");
        check(can>=0&&consume>can&&inject.Any(i=>i.Operand is MethodReference m&&m.Name=="get_IsServer"),
            "Medical use must check effect eligibility before consuming and execute on the authority only");
        check(types.Single(t=>t.Name=="GunSaveProvider").Methods.Single(m=>m.Name=="Restore").Body.Instructions.Any(i=>i.Operand as string==MedicalCatalog.InjectorId),
            "Loaded medical injectors must be restored by the production item-save provider");
        check(!injector.Methods.Single(m=>m.Name=="Awake").Body.Instructions.Any(i=>i.Operand is MethodReference m&&(m.Name=="TryGrantSpawnDose"||m.Name=="GrantSpawnDose")),
            "General initialization, including crafting, must not grant a free medical dose");
        check(injector.Methods.Single(m=>m.Name=="GrantSpawnDose").Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="TryGrantSpawnDose")
            &&injector.Methods.Single(m=>m.Name=="GrantSpawnDose").Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="get_IsServer"),
            "Explicit spawn doses must still be granted only by the authority and at most once");
        foreach(string source in new[]{"CannonPurchaseAmmoPatch","CannonDropAmmoPatch"})
            check(types.Single(t=>t.Name==source).Methods.Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions)
                .Any(i=>i.Operand is MethodReference m&&m.DeclaringType.Name=="MedicalInjector"&&m.Name=="GrantSpawnDose"),
                "Only recognized purchase/container-drop sources must supply the injector's initial dose: "+source);
        var visual=injector.Methods.Single(m=>m.Name=="RefreshSprite").Body.Instructions;
        check(visual.Any(i=>i.Operand is FieldReference f&&f.Name=="InjectorLoadedSprite")&&visual.Any(i=>i.Operand is FieldReference f&&f.Name=="InjectorEmptySprite")
            &&visual.Any(i=>i.Operand is MethodReference m&&m.Name=="set_sprite"),"The live renderer must select separate loaded/empty sprites");
        var central=register.Methods.Single(m=>m.Name=="RegisterMedical").Body.Instructions;
        check(central.Any(i=>i.Operand is MethodReference m&&m.DeclaringType.Name=="MedicalInjector"&&m.Name=="Unload")
            &&types.Single(t=>t.Name=="MedicalPatches").Methods.Single(m=>m.Name=="RepairDelegate").Body.Instructions.Any(i=>i.Operand is FieldReference f&&f.Name=="useAction"),
            "The native central use action must unload ampoules and survive CCL delegate snapshots");
        var nativeUse=types.Single(t=>t.Name=="MultiplayerCombat").Methods.Single(m=>m.Name=="SkipNativeInjectorPrefix");
        check(nativeUse.Parameters.Any(p=>p.Name=="__result"&&p.ParameterType.IsByReference)
            &&nativeUse.Body.Instructions.Any(i=>i.OpCode==Mono.Cecil.Cil.OpCodes.Stind_I1),
            "Skipping the duplicate native MP prefix must explicitly allow the original Body.UseItem delegate");
        check(assembly.MainModule.Resources.Any(r=>r.Name.EndsWith("medical-injector-empty.png")),"The empty injector variant must be embedded");
        byte[] SpriteBytes(string name)
        {
            using var stream=((EmbeddedResource)assembly.MainModule.Resources.Single(r=>r.Name.EndsWith("."+name))).GetResourceStream();
            using var memory=new MemoryStream();stream.CopyTo(memory);return memory.ToArray();
        }
        (int Width,int Height) Size(byte[] bytes)=>((bytes[16]<<24)|(bytes[17]<<16)|(bytes[18]<<8)|bytes[19],
            (bytes[20]<<24)|(bytes[21]<<16)|(bytes[22]<<8)|bytes[23]);
        var loadedArt=SpriteBytes("medical-injector.png");var emptyArt=SpriteBytes("medical-injector-empty.png");
        check(Size(loadedArt)==Size(emptyArt)&&Size(loadedArt).Width==36&&!loadedArt.SequenceEqual(emptyArt),
            "Loaded/empty injector artwork must be distinct while retaining identical small sprite frames");
        check(Size(SpriteBytes("medical-ampoule.png"))==(10,20)&&Size(SpriteBytes("regeneration-ampoule.png"))==(10,20),
            "The two medical ampoule sprites must share their final caliber and length");
        using var mp=AssemblyDefinition.ReadAssembly(@"L:\SteamLibrary\steamapps\common\Casualties Unknown\BepInEx\plugins\Together\CasualtiesMP.dll");
        check(mp.MainModule.Types.Single(t=>t.FullName=="Together.MedicalSync").Methods.Any(m=>m.Name=="ForceHealth"&&m.IsPublic&&m.IsStatic),
            "Medical health synchronization must match the installed original ForceHealth API");
        var health=mp.MainModule.Types.Single(t=>t.FullName=="Together.CharacterHealthStateSyncPacket").Methods.Single(m=>m.Name=="Apply"&&m.Parameters[0].ParameterType.Name=="Body");
        check(health.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="RestoreDetachedLimb")
            &&health.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="RebindLimbHinges"),
            "The installed native health packet must restore regrown limb transforms and hinges on clients");
        using var game=AssemblyDefinition.ReadAssembly(@"L:\SteamLibrary\steamapps\common\Casualties Unknown\CasualtiesUnknown_Data\Managed\Assembly-CSharp.dll");
        foreach(string script in new[]{"GrabberPlant","BounceShroom","SoundCannon","JumpPadScript","PistonScript","BarbedFence"})
            check(game.MainModule.Types.Any(t=>t.Name==script),"Extended trap names must exist in the installed game: "+script);
    }
}
