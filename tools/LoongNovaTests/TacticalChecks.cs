using DDEN.LoongNova;
using Mono.Cecil;
internal static class TacticalChecks
{
    internal static void Run(Action<bool,string> check)
    {
        foreach(var v in new[]{(100f,0f),(0f,100f),(-100f,0f),(0f,-100f),(100f,100f),(-100f,-100f)})
        {
            TacticalScreenGeometry.Edge(400,300,400+v.Item1*20,300+v.Item2*20,60,60,740,540,out float x,out float y);
            check(Math.Abs((x-400)*v.Item2-(y-300)*v.Item1)<0.05f,"Edge arrows stay on the true player-to-source bearing");
            check(x>=60&&x<=740&&y>=60&&y<=540,"Direction markers remain in the safe screen rectangle");
        }
        check(TacticalScreenGeometry.Span(0,5,10,5,4,4,6,6,out float enter,out float exit)&&Math.Abs(enter-0.4f)<0.001&&Math.Abs(exit-0.6f)<0.001,"HUD clipping removes only the intersecting part of a terrain edge");
        check(!TacticalScreenGeometry.Span(0,0,10,0,4,4,6,6,out _,out _),"Terrain outside protected native UI remains unchanged");
        check(TacticalRules.LocatorAvailable(true,true)&&!TacticalRules.LocatorAvailable(false,true)&&!TacticalRules.LocatorAvailable(true,false),"Locator data requires a live actor in a loaded scene, not enabled simulation components");
        check(TacticalRules.TraderSignal(500*500)&&!TacticalRules.TraderSignal(500*500+1)
            &&!TacticalRules.TraderSignal(float.NaN),"Trader guidance stops at exactly 500m and rejects invalid distances");
        foreach(var kind in Enum.GetValues<TacticalKind>())
        {
            check(!TacticalRules.ExtraWarning(kind,float.NaN,0,100),"Invalid coordinates never create warnings");
            if(kind==TacticalKind.Creature)check(!TacticalRules.ExtraWarning(kind,1,-1,100),"Nearby creatures do not gain off-screen detection");
        }
        check(TacticalRules.ExtraWarning(TacticalKind.Trap,4,-32,100)&&!TacticalRules.ExtraWarning(TacticalKind.Trap,4.01f,-20,100)
            &&!TacticalRules.ExtraWarning(TacticalKind.Trap,0,1,100)&&!TacticalRules.ExtraWarning(TacticalKind.Trap,0,-33,100),
            "Off-screen traps are limited to the narrow column directly below the player");
        foreach(var kind in new[]{TacticalKind.Turret,TacticalKind.Sound})
            check(TacticalRules.ExtraWarning(kind,60,80,100)&&!TacticalRules.ExtraWarning(kind,61,80,100),"Ranged warnings obey each source's maximum radius");
        foreach(string harmless in new[]{"LampScript","BatteryRecharger","ScrapEaterScript","TraderScript"})
            check(!TacticalRules.HazardScript(harmless),"Benign facilities do not become hazard markers: "+harmless);
        foreach(string danger in new[]{"BearTrap","SawbladeScript","SpikeStabberScript","SoundCannon","CoilScript","GrabberPlant","BounceShroom"})
            check(TacticalRules.HazardScript(danger),"Stationary hazard components remain detectable: "+danger);
        check(TacticalRules.ElderSignal(4,true,true,1000000)&&!TacticalRules.ElderSignal(4,true,true,1000001)
            &&!TacticalRules.ElderSignal(3,true,true,100)&&!TacticalRules.ElderSignal(4,false,true,100)
            &&!TacticalRules.ElderSignal(4,true,false,100),"Elder signals require the native layer, a living active entity and a bounded range");
    }
    internal static void Inspect(AssemblyDefinition assembly,Action<bool,string> check)
    {
        var types=assembly.MainModule.Types;
        var overlay=types.Single(t=>t.Name=="TacticalGogglesOverlay");
        check(!overlay.Methods.Any(m=>m.Name=="OnGUI"),"Tactical graphics no longer draw above native UI through IMGUI");
        var surface=types.Single(t=>t.Name=="TacticalCanvasSurface");
        check(surface.Methods.Single(m=>m.Name=="Root").Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="SetAsFirstSibling"),"Tactical graphics are placed below all native canvas children");
        check(overlay.Methods.Single(m=>m.Name=="VisibleOverlay").Body.Instructions.Any(i=>i.Operand is FieldReference f&&f.Name=="craftingPanel"),"Crafting explicitly hides the tactical overlay");
        check(overlay.Methods.Single(m=>m.Name=="LateUpdate").Body.Instructions.Count(i=>i.Operand is MethodReference m&&m.Name.StartsWith("FindObjects"))>=2,"Discovery reconciles newly loaded and re-enabled regions after the initial snapshot");
        check(!overlay.Methods.Single(m=>m.Name=="ScanTargets").Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="SetActive"),"Locating sleeping actors must not wake them or expand simulation");
        var instructions=overlay.Methods.Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions);
        check(!instructions.Any(i=>i.Operand is MethodReference m&&(m.Name=="Broadcast"||m.Name=="RequestServer")),"Local goggles overlay must never transmit player/terrain/target data");
        foreach(string name in new[]{"ScanTargets","ScanTerrain"})
            check(!overlay.Methods.Single(m=>m.Name==name).Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name.StartsWith("FindObjects")),"Steady scans must use cached objects/world tiles rather than scene searches: "+name);
        var registration=types.Single(t=>t.Name=="Registration").Methods.Single(m=>m.Name=="RegisterTacticalGoggles").Body.Instructions;
        check(registration.Any(i=>i.Operand as string=="nospawn")&&registration.Any(i=>i.Operand as string=="eyes"),"Direct-spawn-only goggles use the native eyes wear slot");
        check(!types.Single(t=>t.Name=="ArsenalRecipes").Methods.Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Any(i=>i.Operand as string==Registration.TacticalGogglesId),"Tactical goggles must not acquire a crafting recipe");
    }
}
