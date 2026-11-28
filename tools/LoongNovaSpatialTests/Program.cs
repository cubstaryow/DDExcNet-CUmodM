using System;
using System.Collections.Generic;
using System.Linq;
using DDEN.LoongNova;
using DDEN.Shared;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using CUCoreLib.Helpers;
using Newtonsoft.Json.Linq;
internal static class Program
{
    private static int _checks;
    private static void Check(bool ok,string note){if(!ok)throw new Exception(note);_checks++;}
    private static MicroSpaceCrate Device(string id)
    {var item=new Item{id=id,condition=0.75f};var c=item.gameObject.AddComponent<Container>();if(id==SpatialCatalog.ChargerId)c.maxWeight=c.maxWeightPerItem=1;return MicroSpaceCrate.For(item);}
    private static Item Cell(float energy)=>new Item{id=Registration.CellId,condition=energy/1000,favourited=true};
    private static BuildingEntity Pod(string id,out GameObject game)
    {game=new GameObject();var b=game.AddComponent<BuildingEntity>();b.id=id;Together.NetObjectRegistry.All.Add(new Together.SyncInfo{go=game,building=b,syncId=new Together.knetid((ushort)(Together.NetObjectRegistry.All.Count+1))});return b;}
    private static int _decayCalls;
    private static bool MpDecay(Item __instance){_decayCalls++;if(Mp.IsRunning){__instance.condition-=0.2f;return false;}return true;}
    private static void Main()
    {
        var harmony=new Harmony("fixture.spatial");var native=new Harmony("fixture.native");
        try
        {
            harmony.PatchAll(typeof(SpatialPatches));harmony.PatchAll(typeof(DrillPodSource));harmony.PatchAll(typeof(CapsuleMembraneSource));
            native.Patch(AccessTools.Method(typeof(Item),nameof(Item.HandleDecay)),prefix:new HarmonyMethod(typeof(Program),nameof(MpDecay)));
            SpatialDecayGuard.Install(AccessTools.Method(typeof(Program),nameof(MpDecay)));
            Run();Console.WriteLine("PASS "+_checks+" real Harmony spatial-device/pod checks on independent fixtures; no Unity/game execution");
        }
        catch(Exception error){Console.WriteLine(error);Environment.ExitCode=1;}
        finally{SpatialDecayGuard.Stop();harmony.UnpatchSelf();native.UnpatchSelf();MicroSpaceCrate.ClearRegistry();}
    }
    private static void Run()
    {
        Item.Registry[Registration.CellId]=new BatteryInfo{maxCharge=1000};Item.Registry["smallbattery"]=new BatteryInfo{maxCharge=50};
        var body=new Body();PlayerCamera.main=new PlayerCamera{body=body};
        var charger=Device(SpatialCatalog.ChargerId);var insufficient=Cell(199);
        Check(!charger.Execute("load",insufficient,body).Value<bool>("ok")&&insufficient!=null&&insufficient.condition==0.199f&&!charger.State.Cell.Loaded&&body.Notices==1,
            "Low energy cancels installation, preserves the original cell, and invokes the head notice");
        var source=Cell(1000);Check(charger.Execute("load",source,body).Value<bool>("ok")&&source==null&&charger.State.Energy==800,"Installation consumes the source object and deducts exactly 200 energy once");
        var stored=Cell(0);var button=new GameObject().AddComponent<InvButton>();button.Item=charger.Item;
        PlayerCamera.main.dragItem=stored;PlayerCamera.main.TryPerformInventoryAction(new RaycastResult{gameObject=button.gameObject},new List<RaycastResult>());
        Check(stored.transform.parent==charger.transform&&charger.State.Energy==800,"A second Dragonbreath cell enters storage rather than paying another activation fee");
        Time.timeAsDouble=10;charger.Settle();
        Check(Math.Abs(charger.State.Energy-810)<1e-8&&Math.Abs(stored.condition-0.054f)<1e-6&&charger.Item.condition==0.75f,"Source recovers 1/s, contents gain 5.4/s, and device durability is independent");
        Check(charger.Item.GetComponent<Container>().GetEncumberance()==0,"Recharger contents are weightless");
        Check(!charger.Item.GetComponent<Container>().CanHoldItem(new Item{id="scrap"}),"Non-batteries cannot enter the recharger");
        Check(charger.Execute("eject",null,body).Value<bool>("ok"),"Central-use ejection succeeds");
        var ejected=CustomInstantiate.Created.Last();Check(ejected.id==Registration.CellId&&Math.Abs(ejected.condition-0.81f)<1e-6&&ejected.favourited,"Ejected source retains its precise recovered energy and favourite state");
        Time.timeAsDouble=20;charger.Settle();Check(stored.condition==0.054f,"Removing the source stops contents charging");
        Item.Registry[Registration.WrenchId]=new ItemInfo{Tool=true};
        var tool=new Item{id=Registration.WrenchId};
        foreach(string deviceId in new[]{SpatialCatalog.ChargerId,SpatialCatalog.CrateId})
        {
            var device=Device(deviceId);device.Execute("load",Cell(1000),body);
            var target=new GameObject().AddComponent<InvButton>();target.Item=device.Item;
            PlayerCamera.main.dragItem=tool;int made=CustomInstantiate.Created.Count;
            Check(PlayerCamera.main.TryPerformInventoryAction(new RaycastResult{gameObject=target.gameObject},new List<RaycastResult>())
                &&!device.State.Cell.Loaded&&CustomInstantiate.Created.Count==made+1,
                "Dragging a wrench to either spatial device ejects its installed cell through the patched inventory action");
            Check(CustomInstantiate.Created.Last().favourited&&Math.Abs(CustomInstantiate.Created.Last().condition-(deviceId==SpatialCatalog.ChargerId?0.8f:1f))<0.000001
                &&tool.transform.parent!=device.transform&&device.Item.GetComponent<Container>().itemCount==0,
                "Wrench ejection preserves source energy/favourite and never stores or consumes the tool");
            made=CustomInstantiate.Created.Count;
            PlayerCamera.main.TryPerformInventoryAction(new RaycastResult{gameObject=target.gameObject},new List<RaycastResult>());
            Check(CustomInstantiate.Created.Count==made,"An empty source slot cannot produce duplicate cells from a wrench");
        }
        var box=Device(SpatialCatalog.CrateId);box.Execute("load",Cell(1000),body);var food=new Item{id="food"};box.Item.GetComponent<Container>().LoadItem(food);
        Check(food.TimeUntilDecayed()==TimeSpan.Zero,"Stored items do not display a running native decay countdown");
        food.HandleDecay();Check(food.condition==1,"Space contents do not decay even through a patched native item method");
        food.condition-=0.25f;food.HandleDecay();
        Check(food.condition==0.75f,"Skipping passive decay never reverses explicit item damage or usage wear");
        food.condition=1;
        box.Item.HandleDecay();Check(box.Item.condition<0.75f,"The box itself retains its own ordinary durability decay");
        Time.timeAsDouble=10020;box.Settle();Check(box.State.Energy==0&&box.State.Multiplier==0.9f,"A full space-crate cell drains in exactly 10000 occupied seconds");
        Mp.IsRunning=true;int decayBefore=_decayCalls;food.HandleDecay();Check(_decayCalls==decayBefore&&food.condition==1,"The first-priority stasis guard also blocks native MP's inlined decay");
        box.Item.GetComponent<Container>().UnloadItem(food);food.HandleDecay();Check(food.condition<1&&food.TimeUntilDecayed().TotalSeconds>0,"Leaving stasis resumes original multiplayer decay and its countdown");
        Mp.IsRunning=false;
        var freezer=Device(SpatialCatalog.CrateId);var nested=Device(SpatialCatalog.ChargerId);
        nested.Execute("load",Cell(1000),body);var nestedBattery=Cell(0);nested.Item.GetComponent<Container>().LoadItem(nestedBattery);
        freezer.Item.GetComponent<Container>().LoadItem(nested.Item);
        Time.timeAsDouble+=1;nested.Settle();
        Check(nestedBattery.condition>0,"Stasis suppresses passive decay only: stored devices still execute their charging logic");
        Mp.IsRunning=true;
        var normal=new Item{id=SupplyCrateRules.OpenId};var full=normal.gameObject.AddComponent<Container>();full.LoadItem(new Item{id="food"});
        var empty=new Item{id=SupplyCrateRules.OpenId};empty.gameObject.AddComponent<Container>();
        var ingredient=new RecipeItem{specific=true,specificId=SupplyCrateRules.OpenId,ignoredId=SpatialCatalog.CrateId};
        Check(ingredient.GetMatchingItem(new List<Item>{normal,empty},new List<Item>())==empty,"A spatial upgrade selects only an empty ordinary crate");
        var wrench=new Item{id=Registration.WrenchId};body.Held=wrench;body.Inventory.Add(wrench);
        for(int i=0;i<4;i++)body.Inventory.Add(new Item{id=CannonAmmoState.CarrierId});
        var drillBuilding=Pod("drillpod",out var drillGo);var drill=drillGo.AddComponent<DrillPod>();
        int before=CustomInstantiate.Created.Count;DrillPodSource.Multiplayer(body,drill);
        Check(drillGo==null&&Math.Abs(wrench.condition-0.96f)<1e-6&&CustomInstantiate.Created.Skip(before).Count(i=>i.id==CannonAmmoState.DrillId)==4,
            "A successful authenticated drill exchange destroys the pod and yields exactly four rounds");
        Check(drillBuilding.health==100&&drillBuilding.itemsDropOnDestroy.Length==0&&drillBuilding.alwaysDrop.Length==0&&drillBuilding.guaranteedDropAmount==0,
            "Pod removal suppresses every native drop route without invoking building death");
        DrillPodSource.Multiplayer(body,drill);Check(CustomInstantiate.Created.Count==before+4,"Deferred/repeated interactions cannot redeem a consumed drill pod again");
        var capsule=Pod("dropcapsule",out var capsuleGo);capsuleGo.AddComponent<Openable>();
        var result=CapsuleMembraneSource.Execute(body,capsule);
        Check(result.Value<bool>("ok")&&capsuleGo==null&&Math.Abs(wrench.condition-0.95f)<1e-6&&CustomInstantiate.Created.Last().id==SpatialCatalog.MembraneId,
            "A keypad drop capsule converts once into a membrane for exactly one-percent wrench wear");
        var other=Pod("safe",out var otherGo);otherGo.AddComponent<Openable>();
        Check(!CapsuleMembraneSource.Execute(body,other).Value<bool>("ok")&&otherGo!=null,"Other keypad buildings remain untouched");
        var retry=Pod("drillpod",out var retryGo);var retryDrill=retryGo.AddComponent<DrillPod>();
        for(int i=0;i<4;i++)body.Inventory.Add(new Item{id=CannonAmmoState.CarrierId});
        CustomInstantiate.FailAt=CustomInstantiate.Made+2;float old=wrench.condition;before=CustomInstantiate.Created.Count;
        DrillPodSource.Multiplayer(body,retryDrill);
        Check(retryGo!=null&&wrench.condition==old&&CustomInstantiate.Created.Skip(before).All(i=>i==null),"Partial spawn failure rolls back ammunition and preserves the pod and wrench");
        CustomInstantiate.FailAt=-1;Mp.IsServer=false;var client=Device(SpatialCatalog.ChargerId);var clientCell=Cell(1000);
        Check(!client.Execute("load",clientCell,body).Value<bool>("ok")&&clientCell!=null&&!client.State.Cell.Loaded,"A client cannot authoritatively consume or install a cell");
        Mp.IsRunning=Mp.IsServer=true;
        var events=Device(SpatialCatalog.CrateId);events.Item.NetId=321;
        int broadcasts=Mp.Broadcasts;
        events.Execute("load",Cell(1000),body);
        Check(Mp.Broadcasts==broadcasts+1,"Installing an idle source broadcasts its loaded state even when consumption mode stays zero");
        events.Execute("read",null,body);events.Execute("read",null,body);
        Check(Mp.Broadcasts==broadcasts+1,"Unchanged queries never rebroadcast the device state");
        events.Execute("eject",null,body);
        Check(Mp.Broadcasts==broadcasts+2,"Ejecting an idle source broadcasts the change exactly once");
        Mp.IsServer=false;
        NetworkAccess.Connection=new object();Time.unscaledTime=0;Mp.Sends=0;
        var many=Enumerable.Range(0,40).Select(_=>Device(SpatialCatalog.ChargerId)).ToArray();
        foreach(var device in many){device.Item.NetId=123;device.Request("read",body);}
        Check(Mp.Sends==1,"Forty spatial-device queries in one frame produce only the shared one-request frame budget");
        var action=Device(SpatialCatalog.ChargerId);action.Item.NetId=124;
        action.Request("eject",body);
        Check(Mp.Sends==2,"A gameplay command bypasses the exhausted optional-query budget");
        Time.unscaledTime=1;
        foreach(var device in many)device.Request("read",body);
        Check(Mp.Sends==3,"Failed sends leave no pending actions and recovery cannot burst beyond one query");

    }
}
