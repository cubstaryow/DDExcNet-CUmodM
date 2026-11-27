using DDEN.LoongNova;
using Mono.Cecil;

internal static class EconomyChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var recipes=ArsenalRecipes.Create().ToDictionary(r=>r.result.id);
        void Physical(string id,params (string Id,int Count)[] expected)
        {
            var actual=recipes[id].items.Where(i=>i.specific&&!i.isLiquid).GroupBy(i=>i.specificId).ToDictionary(g=>g.Key,g=>g.Count());
            check(actual.Count==expected.Length&&expected.All(p=>actual.GetValueOrDefault(p.Id)==p.Count),"Exact physical crafting ingredients: "+id);
        }
        Physical(CannonAmmoState.DrillId,(Registration.PickaxeId,1),(CannonAmmoState.CarrierId,1));
        Physical(CannonAmmoState.DrainId,("spacedrain",1),(CannonAmmoState.CarrierId,1),("flammablepowder",1),("flexiglass",1),("titaniumsheet",1));
        Physical(CannonAmmoState.BreachId,(CannonAmmoState.CarrierId,1),("smallbattery",2),("flexiglass",1),("bundleofwires",2),("flammablepowder",1),("titaniumsheet",1));
        Physical(MedicalCatalog.EmptyId,("flexiglass",2),(Registration.MatterId,1));
        Physical(MedicalCatalog.MedicalId,(MedicalCatalog.EmptyId,1),("flammablepowder",1));
        Physical(MedicalCatalog.InjectorId,("pistol",1),(Registration.MatterId,1),("flexiglass",2),("lcdscreen",1),("titaniumsheet",1),("syringe",1));
        var reusable=recipes[CannonAmmoState.DrainId].items.Single(i=>i.specificId=="spacedrain");
        check(!reusable.destroyItem&&reusable.minimumCondition==0,"The spatial drainer must be retained without spending condition");
        var batteries=recipes[CannonAmmoState.BreachId].items.Where(i=>i.specificId=="smallbattery").ToArray();
        check(batteries.Length==2&&batteries.All(b=>b.destroyItem&&!b.noMaxCondition&&b.minimumCondition>0.9f
            &&MathF.BitDecrement(b.minimumCondition)==0.9f),"Both native batteries must require the smallest representable charge strictly above90%, including full batteries");
        foreach(float charge in new[]{0.89f,0.9f,0.9001f,0.95f,1})
            check((charge>=batteries[0].minimumCondition)==(charge>0.9f),"Native minimum-condition matching must reject exact90% and accept all higher battery charge");
        var liquids=recipes[MedicalCatalog.MedicalId].items.Where(i=>i.isLiquid&&i.specific).ToDictionary(i=>i.specificId,i=>i.minimumCondition);
        check(liquids.Count==5&&liquids["lrdserum"]==20&&liquids["antivenom"]==10&&liquids["painkillers"]==10
            &&liquids["biochem"]==10&&liquids["antirad"]==10,"Medical ampoules must consume the requested five exact native liquid amounts");
        var qualities=recipes[MedicalCatalog.MedicalId].items.Where(i=>i.isLiquid&&!i.specific).ToDictionary(i=>i.quality.id,i=>i.quality.amount);
        check(qualities.Count==3&&qualities["disinfectant"]==10&&qualities["opiate"]==10&&qualities["water"]==20,
            "Disinfection, opiate and fluid requirements must use native liquid qualities rather than physical material IDs");
        var emptyFluid=recipes[MedicalCatalog.EmptyId].items.Single(i=>i.isLiquid);
        check(emptyFluid.specific&&emptyFluid.specificId=="biochem"&&emptyFluid.minimumCondition==10,"Empty ampoule manufacture must consume10ml biochemical fluid");
        foreach(string id in new[]{CannonAmmoState.DrillId,CannonAmmoState.DrainId,CannonAmmoState.BreachId,MedicalCatalog.EmptyId,MedicalCatalog.MedicalId,MedicalCatalog.InjectorId})
        {
            var quality=recipes[id].items.Single(i=>!i.isLiquid&&!i.specific);
            float units=id==MedicalCatalog.InjectorId?200:100;
            check(!quality.destroyItem&&quality.quality.id==Registration.DragonQuality&&quality.quality.amount==units
                &&Math.Abs(quality.minimumCondition-DragonizationRules.Cost(units))<0.000001,"Each new recipe must debit its exact dragonization requirement");
        }
        check(!recipes.ContainsKey(MedicalCatalog.RegenerationId),"Emergency regeneration ampoules remain loot-only");
        var slot=new AmpouleState();check(MedicalMarket.InjectorValue(slot)==0,"An injector without an ampoule must be unsellable through zero native value");
        slot.Load(MedicalCatalog.MedicalId,1,false);check(MedicalMarket.InjectorValue(slot)==40,"A medically loaded injector must be worth40");
        slot.Consume(out _);check(MedicalMarket.InjectorValue(slot)==20,"An injector containing an empty ampoule must be worth20");
        var restored=new AmpouleState();restored.Restore(slot.Capture());check(MedicalMarket.InjectorValue(restored)==20,"Trading must honor saved spent-ampoule state");
        restored.Clear();check(MedicalMarket.InjectorValue(restored)==0,"Ejecting the empty ampoule must restore the no-sale state");
        restored.Load(MedicalCatalog.RegenerationId,1,false);check(MedicalMarket.InjectorValue(restored)==40,"A loaded injector's price must not depend on which live ampoule it contains");
        foreach(int source in new[]{2,8,16,32,64,112,128,256})
        {
            bool regen=source==2||LootRules.IsTrader(source);
            check(LootRules.Select(source,"medical",0.00125)==(regen?MedicalCatalog.RegenerationId:null),"Regeneration ampoules must appear only in medical crates or merchant stock");
            check(LootRules.Select(source,"medical",0.002)==MedicalCatalog.MedicalId,"Ordinary medical ampoules must remain available in medical slots of matching containers");
            check(LootRules.Select(source,"medical",0)==MedicalCatalog.InjectorId,"Matching medical slots must still supply full medical injectors");
        }
        foreach(var pair in new[]{(CannonAmmoState.DrillId,3),(CannonAmmoState.DrainId,4),(CannonAmmoState.BreachId,5)})
        foreach(int source in new[]{8,16,32,64,112,128,256})
        {
            int count=0;for(int i=0;i<10000;i++)if(LootRules.Select(source,"ammo",(i+0.5)/10000)==pair.Item1)count++;
            check(count==10,"Each functional ammunition type must share the ordinary0.1% matching-slot rate in containers and merchant stock");
        }
        for(int i=0;i<LootRules.RoundCount;i++)check(CannonSpawnWeights.Pick((i+0.5)/7)==LootRules.BonusRound(i),"Provisional weights must retain all seven spawn-bonus round types");
        var weights=new double[]{7,6,5,4,3,2,1};var counts=new int[7];
        for(int i=0;i<28000;i++){string id=CannonSpawnWeights.Pick((i+0.5)/28000,weights);counts[Enumerable.Range(0,7).Single(n=>LootRules.BonusRound(n)==id)]++;}
        check(Enumerable.Range(0,7).All(i=>counts[i]==weights[i]*1000),"Spawn selection must actually respect independently adjustable rarity weights");
        check(CannonSpawnWeights.Pick(0,new double[]{0,0,0,0,0,0,1})==CannonAmmoState.RevivalId
            &&CannonSpawnWeights.Pick(1,new double[]{0,0,0,0,0,0,1})==CannonAmmoState.RevivalId,"Zero weights must remove candidates without breaking endpoint rolls");
        foreach(var invalid in new[]{new double[7],new double[]{-1,1,1,1,1,1,1},new double[]{double.NaN,1,1,1,1,1,1},new double[6]})
            check(CannonSpawnWeights.Pick(0.5,invalid)==null,"Invalid or empty weighted pools must never silently select a round");
        int creates=0,rollbacks=0;
        object Create(){creates++;return new object();}void Rollback(object obj){rollbacks++;}
        check(DrillExchange.Prepare(1,3,Create,Rollback,out float remaining,out _)==DrillExchangeResult.MissingCases&&creates==0&&remaining==1,
            "A drill pod must report missing cases without creating ammunition or charging durability");
        check(DrillExchange.Prepare(0.039f,4,Create,Rollback,out _,out _)==DrillExchangeResult.InsufficientQuality&&creates==0,
            "The four-round drill exchange must require400 dragonization units");
        check(DrillExchange.Prepare(1,4,Create,Rollback,out remaining,out var made)==DrillExchangeResult.Success&&made.Count==4
            &&Math.Abs(remaining-0.96f)<0.000001,"One exchange must prepare four rounds for exactly4% wrench durability");
        int attempt=0;
        check(DrillExchange.Prepare(1,4,()=>++attempt==3?null:new object(),Rollback,out remaining,out made)==DrillExchangeResult.SpawnFailed
            &&made.Count==0&&rollbacks==2&&remaining==1,"Partial spawn failures must remove prepared rounds before any original resource is consumed");
    }
    internal static void Inspect(AssemblyDefinition assembly,Action<bool,string> check)
    {
        var types=assembly.MainModule.Types;
        var awake=types.Single(t=>t.Name=="LoongNovaPlugin").Methods.Single(m=>m.Name=="Awake").Body.Instructions;
        foreach(string name in new[]{"MedicalTradePatch","InjectorGrip","DrillPodSource"})
            check(awake.Any(i=>i.Operand is TypeReference t&&t.Name==name),"The plugin must install the actual trading/pose/drill-pod integration: "+name);
        var give=types.Single(t=>t.Name=="CannonSpawnAmmo").Methods.Single(m=>m.Name=="Give").Body.Instructions;
        check(give.Any(i=>i.Operand is MethodReference m&&m.DeclaringType.Name=="CannonSpawnWeights"&&m.Name=="Pick"),"Actual generated cannons must use weighted selection");
        var market=types.Single(t=>t.Name=="MedicalTradePatch");
        check(market.CustomAttributes.Any(a=>a.AttributeType.Name=="HarmonyPatch"&&a.ConstructorArguments.Any(x=>x.Value as string=="GetValue")),
            "Dynamic injector pricing must patch the original valuation method used to accept/reject sales");
        var pose=types.Single(t=>t.Name=="InjectorGrip").Methods.Single(m=>m.Name=="Pose").Body.Instructions;
        check(pose.Any(i=>i.Operand as string=="gun")&&pose.Any(i=>i.Operand is MethodReference m&&m.Name=="SetBool")
            &&pose.Any(i=>i.Operand is FieldReference f&&f.Name=="targetLookPos"),"Injector holding must use the pistol arm pose and native target direction");
        var medical=types.Single(t=>t.Name=="Registration").Methods.Single(m=>m.Name=="RegisterMedical").Body.Instructions;
        check(medical.Any(i=>i.Operand as string=="pistol")&&medical.Any(i=>i.Operand is FieldReference f&&f.Name=="slotRotation"),
            "The injector must copy native pistol grip/rotation data");
        // Exercise the real compiled registration call graph, excluding callbacks stored for later gameplay.
        var pending=new Queue<MethodDefinition>();var visited=new HashSet<MethodDefinition>();
        pending.Enqueue(types.Single(t=>t.Name=="Registration").Methods.Single(m=>m.Name=="Register"));
        foreach(var iterator in types.Single(t=>t.Name=="ArsenalRecipes").NestedTypes)
            foreach(var method in iterator.Methods.Where(m=>m.Name=="MoveNext"))pending.Enqueue(method);
        while(pending.TryDequeue(out var method))
        {
            if(!method.HasBody||!visited.Add(method))continue;
            check(!method.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName=="Item"&&(m.Name=="GetItem"||m.Name=="get_Stats"))
                &&!method.Body.Instructions.Any(i=>i.Operand is FieldReference f&&f.DeclaringType.FullName=="Item"&&f.Name=="GlobalItems"),
                "Early registration must not query the not-yet-initialized native item table: "+method.FullName);
            foreach(var instruction in method.Body.Instructions)
                if((instruction.OpCode==Mono.Cecil.Cil.OpCodes.Call||instruction.OpCode==Mono.Cecil.Cil.OpCodes.Callvirt||instruction.OpCode==Mono.Cecil.Cil.OpCodes.Newobj)
                    &&instruction.Operand is MethodReference called&&called.DeclaringType.FullName.StartsWith("DDEN.LoongNova."))
                {var callee=called.Resolve();if(callee!=null)pending.Enqueue(callee);}
        }
        check(visited.Any(m=>m.Name=="RegisterMedical")&&visited.Any(m=>m.Name=="RegisterRecipes"),
            "The early-startup dependency check must actually cover medical content and recipes");
        var mpInstall=types.Single(t=>t.Name=="MultiplayerCombat").Methods.Single(m=>m.Name=="InstallGuards").Body.Instructions;
        check(mpInstall.Any(i=>i.Operand as string=="Together.DrillPod_OnUse_MultiplayerPatch")&&mpInstall.Any(i=>i.Operand as string=="OnUseRewritten"),
            "Multiplayer drill exchanges must reuse the native authenticated drill-pod interaction");
        using var native=AssemblyDefinition.ReadAssembly(@"L:\SteamLibrary\steamapps\common\Casualties Unknown\CasualtiesUnknown_Data\Managed\Assembly-CSharp.dll");
        var item= native.MainModule.Types.Single(t=>t.Name=="Item").Methods.Single(m=>m.Name=="SetupItems");
        var nativeIds=item.Body.Instructions.Select(i=>i.Operand as string).Where(s=>s!=null).ToHashSet();
        var nativeInstructions=item.Body.Instructions.ToList();
        int pistolIndex=nativeInstructions.FindIndex(i=>i.Operand as string=="pistol");
        var pistolRotation=nativeInstructions.Skip(pistolIndex).TakeWhile(i=>!(i.Operand is MethodReference m&&m.Name=="Add"))
            .Single(i=>i.Operand is FieldReference f&&f.Name=="slotRotation");
        var injectorRotation=medical.Single(i=>i.Operand is FieldReference f&&f.Name=="slotRotation");
        check(pistolRotation.Previous.Operand is float nativeRotation&&nativeRotation==-90
            &&injectorRotation.Previous.Operand is float actualRotation&&actualRotation==nativeRotation,
            "The startup-safe injector rotation must still match the installed native pistol's actual rotation");
        foreach(string id in new[]{"spacedrain","smallbattery","pistol","lcdscreen","syringe"})check(nativeIds.Contains(id),"Crafting item ID must exist in the installed game: "+id);
        check(native.MainModule.Types.Single(t=>t.Name=="TraderScript").Methods.Single(m=>m.Name=="GiveItem").Body.Instructions
            .Any(i=>i.Operand is MethodReference m&&m.Name=="GetValue"),"Singleplayer selling must validate dynamic item value");
        using var mp=AssemblyDefinition.ReadAssembly(@"L:\SteamLibrary\steamapps\common\Casualties Unknown\BepInEx\plugins\Together\CasualtiesMP.dll");
        check(mp.MainModule.Types.Single(t=>t.FullName=="Together.TraderCommit").Methods.Single(m=>m.Name=="RunSell").Body.Instructions
            .Any(i=>i.Operand is MethodReference m&&m.Name=="GiveItem"),"MP selling must commit through the same native valuation path");
        check(mp.MainModule.Types.Single(t=>t.FullName=="Together.DrillPod_OnUse_MultiplayerPatch").Methods.Any(m=>m.Name=="OnUseRewritten"
            &&m.Parameters.Count==2&&m.Parameters[0].ParameterType.Name=="Body"&&m.Parameters[1].ParameterType.Name=="DrillPod"),
            "The installed drill-pod server callback must match the actor/target integration signature");
    }
}
