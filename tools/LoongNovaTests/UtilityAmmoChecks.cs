using DDEN.LoongNova;
using Mono.Cecil;
using Newtonsoft.Json.Linq;

internal static class UtilityAmmoChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var profiles = new[]
        {
            (CannonAmmoState.DrillId, new CannonRound(0,0,1,0,1,effect:CannonEffect.Drill,range:150,size:4)),
            (CannonAmmoState.DrainId, new CannonRound(0,0,1,0,1,effect:CannonEffect.Drain)),
            (CannonAmmoState.BreachId, new CannonRound(0,0,1,0,1,effect:CannonEffect.Breach,size:25))
        };
        foreach (var (id, profile) in profiles)
        {
            CannonRoundRegistry.Register(id, profile.Parameters());
            var round = CannonRoundRegistry.Get(id);
            check(round.Effect==profile.Effect && round.Range==profile.Range && round.Size==profile.Size
                && round.Structure==0 && round.Creature==0 && round.DamageMultiplier(3)==1,
                "Utility parameters must round-trip without charge scaling or creature damage");
            var ammo = new CannonAmmoState();
            check(ammo.Load(id,0.6f,true)&&ammo.Rack()&&ammo.Load(CannonAmmoState.BioId,1,false),"New utility rounds must support mixed magazine/chamber loading");
            var restored=new CannonAmmoState();
            check(restored.Restore(ammo.Capture())&&restored.Id==id&&restored.Condition==0.6f&&restored.Favourite,
                "Utility chamber identity and condition must survive the existing save schema");
            check(restored.Consume(out var fired)&&fired==id&&restored.Spent&&restored.Rack()&&restored.Id==CannonAmmoState.BioId,
                "A utility shot must leave one case and never automatically chamber the reserve");
            foreach(var key in new[]{"size","range","effect","structureDamage","pellets","spreadDegrees","blastRadius","chargeMultiplierMax"})
            {
                var invalid=profile.Parameters(); invalid[key]=key=="structureDamage"?1:key=="effect"?99:key=="range"?1000:key=="size"?99:2;
                bool rejected=false;
                try { CannonRoundRegistry.Register("invalid.utility",invalid); }catch(ArgumentException){rejected=true;}
                // Drain does not use a brush size, so nonzero sizes are invalid as well.
                check(rejected&&!CannonRoundRegistry.Contains("invalid.utility"),"Utility metadata must reject unsupported/mixed effects: "+key);
            }
        }
        var grid=new ushort[200,30];
        for(int x=0;x<200;x++)for(int y=0;y<30;y++)grid[x,y]=(ushort)(x%2==0?42:1);
        var corridor=UtilityGeometry.Drill(grid,10.5,10.5,1,0,150,4).ToArray();
        check(corridor.Length==600&&corridor.Select(c=>(c.X,c.Y)).Distinct().Count()==600
            &&corridor.Min(c=>c.X)==10&&corridor.Max(c=>c.X)==159&&corridor.Min(c=>c.Y)==8&&corridor.Max(c=>c.Y)==11,
            "Horizontal drilling must clear exactly 150 columns by four rows, including alloy, without writing behind the muzzle");
        var reverse=UtilityGeometry.Drill(grid,170.5,10.5,-1,0,150,4).ToArray();
        check(reverse.Length==600&&reverse.Min(c=>c.X)==21&&reverse.Max(c=>c.X)==170,"Reverse drilling must preserve length and width");
        grid[40,8]=14;
        var stopped=UtilityGeometry.Drill(grid,10.5,10.5,1,0,150,4).ToArray();
        check(stopped.Length>0&&stopped.All(c=>c.X<40)&&!stopped.Any(c=>c.X==40&&c.Y==8),
            "Infinite rock at the brush edge must stop the whole drill, not let its other rows tunnel past");
        grid[10,10]=14;
        check(!UtilityGeometry.Drill(grid,10.5,10.5,1,0,150,4).Any(),"Initial infinite rock must prevent all drilling writes");
        check(!UtilityGeometry.Drill(grid,10.5,10.5,0,0,150,4).Any(),"Degenerate directions must terminate without writes");
        foreach(var dir in new[]{(1d,1d),(-1d,1d),(1d,-1d),(-1d,-1d),(0d,1d),(0d,-1d)})
        {
            var diagonal=UtilityGeometry.Drill(grid,100.5,15.5,dir.Item1,dir.Item2,150,4).ToArray();
            check(diagonal.All(c=>c.X>=0&&c.Y>=0&&c.X<199&&c.Y<29&&grid[c.X,c.Y]!=14)
                &&diagonal.Select(c=>(c.X,c.Y)).Distinct().Count()==diagonal.Length,"Drilling directions must terminate at map edges and never alias the native clamped edge cells");
        }
        int square=0;
        for(int x=-14;x<=14;x++)for(int y=-14;y<=14;y++)if(UtilityGeometry.InSquare(x,y,0,0,25))square++;
        check(square==625&&UtilityGeometry.InSquare(-12,-12,0,0,25)&&UtilityGeometry.InSquare(12,12,0,0,25)
            &&!UtilityGeometry.InSquare(13,0,0,0,25)&&!UtilityGeometry.InSquare(0,-13,0,0,25),"Breach must be a 25x25 square, not radius25 or a circle");
        var fluid=new byte[20,20];fluid[4,4]=1;fluid[5,4]=3;fluid[5,5]=6;fluid[6,6]=7;fluid[12,12]=1;
        var flood=new ConnectedFluid(fluid,4,4);
        check(!flood.Step(1),"Connected-region discovery must support bounded per-frame work");
        while(!flood.Step(1)){}
        check(flood.Cells.Select(c=>(c.X,c.Y)).ToHashSet().SetEquals(new[]{(4,4),(5,4),(5,5)}),
            "Mixed adjacent liquids must be one region; diagonals and disconnected pools must remain separate");
        check(fluid[4,4]==1&&fluid[5,4]==3&&fluid[5,5]==6,"Discovery must not mutate the live simulation before atomic commit");
        fluid[7,5]=2;
        check(!flood.Cells.Any(c=>c.X==7&&c.Y==5),"Discovery must use a consistent shot-time fluid snapshot");
        check(new ConnectedFluid(fluid,-1,0).Step(1)&&new ConnectedFluid(fluid,0,0).Cells.Count==0,"Empty/out-of-map impacts must be harmless");
        var ocean=new byte[320,320];for(int x=0;x<320;x++)for(int y=0;y<320;y++)ocean[x,y]=(byte)(1+(x+y)%7);
        var large=new ConnectedFluid(ocean,0,0);int frames=0;
        while(!large.Step(8192))check(++frames<20,"Large drainage must make bounded progress without recursion");
        check(large.Cells.Count==102400&&large.Cells.Select(c=>(c.X,c.Y)).Distinct().Count()==102400,
            "Drainage must cross former radius/volume limits and include boundary cells exactly once");
        foreach(int index in Enumerable.Range(0,LootRules.RoundCount))
        {
            var id=LootRules.BonusRound(index);
            check(CannonAmmoState.IsRound(id)&&LootRules.Select(16,"ammo",(index+0.5)*LootRules.AmmoChance)==id,
                "All seven rounds must be available through random spawn bonuses and rare merchant ammo slots");
        }
    }

    internal static void Inspect(AssemblyDefinition assembly, Action<bool,string> check)
    {
        var types=assembly.MainModule.Types;
        var utility=types.Single(t=>t.Name=="CannonUtility");
        var tick=utility.Methods.Single(m=>m.Name=="Tick");
        check(tick.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="get_IsServer")
            &&tick.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="SetBlockNoUpdate")
            &&!tick.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="DamageBlock"),"Only the authority may clear drill tiles, without the native ore-drop damage path");
        check(tick.Body.Instructions.Count(i=>i.Operand is MethodReference m&&m.Name=="FlushBlocks")==2,
            "Drill batching must flush the original MP change queue before and after writing");
        var fire=types.Single(t=>t.Name=="CannonService").Methods.Single(m=>m.Name=="Fire");
        check(fire.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.DeclaringType.Name=="CannonUtility"&&m.Name=="Fire"),"The production firing path must dispatch registered utility ammo");
        var salvage=types.Single(t=>t.Name=="CannonBreach").Methods.Single(m=>m.Name=="Salvage");
        check(!salvage.Body.Instructions.Any(i=>i.Operand is MethodReference m&&(m.Name=="set_health"||m.Name=="CreateExplosion"))
            &&salvage.Body.Instructions.Any(i=>i.Operand as string=="exploded")
            &&salvage.Body.Instructions.Any(i=>i.Operand as string=="Destroy"),"Disarming must neutralize mines and use native network destruction while retaining safe nonzero health");
        var apply=types.Single(t=>t.Name=="CannonBreach").Methods.Single(m=>m.Name=="Apply");
        check(apply.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="get_animal")==false
            &&apply.Body.Instructions.Any(i=>i.Operand is FieldReference f&&f.Name=="animal"),"Breach target selection must exclude creatures");
        var native=types.Single(t=>t.Name=="UtilityNativeSync");
        check(native.Methods.Single(m=>m.Name=="Tick").Body.Instructions.Any(i=>i.Operand as string=="Server_SendFluidChunk"),
            "Drain commits must enqueue original reliable fluid chunks rather than client-side flood fills");
        using var contentStream=((EmbeddedResource)assembly.MainModule.Resources.Single(r=>r.Name.EndsWith("Content.json"))).GetResourceStream();
        using var reader=new StreamReader(contentStream);var content=JObject.Parse(reader.ReadToEnd());
        check(content.Value<string>("gunName")=="龙域武库-龙灼线束"&&content.Value<string>("beltName")=="龙域武库-龙翼腰带",
            "Equipment renames must be embedded with the requested arsenal names");
        foreach(var name in new[]{"drill-round.png","spatial-drain-round.png","emp-breach-round.png"})
            check(assembly.MainModule.Resources.Any(r=>r.Name.EndsWith(name)),"Utility cartridge art must be embedded: "+name);
        using var game=AssemblyDefinition.ReadAssembly(@"L:\SteamLibrary\steamapps\common\Casualties Unknown\CasualtiesUnknown_Data\Managed\Assembly-CSharp.dll");
        var blocks=game.MainModule.Types.Single(t=>t.Name=="WorldGeneration").Fields.Single(f=>f.Name=="worldBlocks");
        check(blocks.IsPrivate&&blocks.FieldType is ArrayType terrainArray&&terrainArray.Rank==2&&terrainArray.ElementType.FullName=="System.UInt16",
            "Drill reflection must match the installed private terrain storage");
        using var mp=AssemblyDefinition.ReadAssembly(@"L:\SteamLibrary\steamapps\common\Casualties Unknown\BepInEx\plugins\Together\CasualtiesMP.dll");
        var chunk=mp.MainModule.Types.Single(t=>t.FullName=="Together.WorldChunkSync");
        var send=chunk.Methods.Single(m=>m.Name=="Server_SendFluidChunk");
        check(send.IsPublic&&send.IsStatic&&send.Parameters.Select(p=>p.ParameterType.Name).SequenceEqual(new[]{"Vector2UInt8","ScavPlayer","Boolean"}),
            "Reliable fluid reflection must match the installed native packet API");
        check(mp.MainModule.Types.Single(t=>t.FullName=="Together.NetObjectRegistry").Methods.Any(m=>m.Name=="TryGetSyncInfo"
            &&m.Parameters.Count==2&&m.Parameters[0].ParameterType.Name=="GameObject"&&m.Parameters[1].ParameterType.Name=="SyncInfo&"),
            "Hazard removal must use the installed original registry's GameObject lookup");
        using var entities=AssemblyDefinition.ReadAssembly(@"L:\SteamLibrary\steamapps\common\Casualties Unknown\BepInEx\plugins\Together\LiteEntitySystem.dll");
        check(entities.MainModule.Types.Single(t=>t.FullName=="LiteEntitySystem.Internal.InternalEntity").Methods.Any(m=>m.Name=="Destroy"&&m.IsPublic&&m.Parameters.Count==0),
            "Explicit native network destruction must resolve to the installed inherited zero-argument Destroy method");
    }
}
