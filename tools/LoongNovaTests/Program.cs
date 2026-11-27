using DDEN.LoongNova;
using Newtonsoft.Json.Linq;

int checks = 0;
void Check(bool valid, string message)
{
    checks++;
    if (!valid) throw new Exception(message);
}

float pickCondition = 1;
for (int swingIndex = 1; swingIndex <= 1200; swingIndex++)
{
    pickCondition = PickaxeWear.Spend(pickCondition);
    Check(swingIndex == 1200 ? pickCondition == 0 : pickCondition > 0, "The pickaxe must last precisely 1200 successful swings");
}
var refillIngredient = new RecipeItem { specific = true, specificId = Registration.CellId, noMaxCondition = true, ignoredId = Registration.CellId };
foreach (float charge in new[] { 0f, 0.001f, 0.5f, 0.991f, 0.99999f, 1f, -1f, float.NaN, float.PositiveInfinity })
{
    var candidate = new Item { id = Registration.CellId, condition = charge };
    Item match = null;
    Check(!CellRefillMatching.Match(refillIngredient, new List<Item> { candidate }, new List<Item>(), ref match)
        && (match == candidate) == (charge >= 0 && charge < 1), "Refill must accept all non-full charge levels and reject full/invalid values despite the native same-result exclusion");
    candidate.favourited = true; match = null;
    CellRefillMatching.Match(refillIngredient, new List<Item> { candidate }, new List<Item>(), ref match);
    Check(match == null, "Favourite cells must never be consumed by refill");
    candidate.favourited = false;
    CellRefillMatching.Match(refillIngredient, new List<Item> { candidate }, new List<Item> { candidate }, ref match);
    Check(match == null, "Excluded cells must never be reused by refill");
}
Item ordinaryMatch = null;
Check(CellRefillMatching.Match(new RecipeItem { specific = true, specificId = "largebattery", noMaxCondition = true }, new List<Item>(), new List<Item>(), ref ordinaryMatch), "Other batteries and recipes must keep their native matcher");
CannonRoundRegistry.Register(CannonAmmoState.BioId, new CannonRound(1, 750, 1, 0, 4, 1).Parameters());
CannonRoundRegistry.Register(CannonAmmoState.DemolitionId, new CannonRound(200, 1, 20, 20, 5, 1).Parameters());
CannonRoundRegistry.Register(CannonAmmoState.ExplosiveId, new CannonRound(500,400,1,0,1,1,12).Parameters());
CannonRoundRegistry.Register(CannonAmmoState.RevivalId, new CannonRound(0,0,1,0,1,effect:CannonEffect.Revive).Parameters());
UtilityAmmoChecks.Run(Check);
MedicalChecks.Run(Check);
RegrowthChecks.Run(Check);
EconomyChecks.Run(Check);
RevivalChecks.Run(Check);
var ammo = new CannonAmmoState();
Check(!ammo.Loaded && !ammo.Consume(out _), "A fresh cannon must have an empty one-round chamber");
Check(ammo.Load(CannonAmmoState.BioId, 0.75f, true), "A recognized single round must load");
Check(!ammo.Ready && !ammo.Consume(out _) && ammo.Loaded, "Loading alone must not chamber a cartridge or allow firing");
Check(ammo.Rack() && ammo.Ready && ammo.CanLoad && !ammo.CanUnload, "Chambering enables one magazine reload but blocks magazine unloading");
string loadedSnapshot = ammo.Capture().ToString();
Check(!ammo.Load(CannonAmmoState.DemolitionId, 0, false) && ammo.Capture().ToString() == loadedSnapshot, "An invalid reload must not replace or duplicate a chambered round");
var ammoRestored = new CannonAmmoState();
Check(ammoRestored.Restore(ammo.Capture()) && ammoRestored.Id == CannonAmmoState.BioId && ammoRestored.Favourite && ammoRestored.Condition == 0.75f && ammoRestored.Ready, "Loaded round type and return condition must survive saving");
foreach (string id in new[] { "rifleammo", "dden.loongnova.dragon-breath-cell", CannonAmmoState.CarrierId, "bad" })
{
    var empty = new CannonAmmoState();
    Check(!empty.Load(id, 1, false) && !empty.Loaded, "The cannon must reject ordinary ammo, cells and spent carriers");
}
foreach (float badCondition in new[] { 0f, -1f, 1.1f, float.NaN, float.PositiveInfinity })
    Check(!new CannonAmmoState().Load(CannonAmmoState.BioId, badCondition, false), "Invalid or destroyed rounds must not load");
Check(ammo.Consume(out string firedId) && firedId == CannonAmmoState.BioId && ammo.Spent && ammo.Chambered && !ammo.Ready && !ammo.Consume(out _), "One firing must consume precisely one loaded cartridge");
Check(ammo.Rack() && !ammo.Loaded && ammo.Load(CannonAmmoState.DemolitionId, 1, false), "Manual racking must eject the spent case before loading another round");
var bioProfile = CannonAmmoState.Profile(CannonAmmoState.BioId);
var demoProfile = CannonAmmoState.Profile(CannonAmmoState.DemolitionId);
Check(bioProfile.Structure == 1 && bioProfile.Creature == 750 && bioProfile.Pellets == 1 && bioProfile.Spread == 0, "Bio profile must be exactly 1/750 and one projectile");
Check(demoProfile.Structure == 200 && demoProfile.Creature == 1 && demoProfile.Pellets == 20 && demoProfile.Spread == 20, "Demolition must fire twenty 200/1 pellets in a 40-degree cone");
Check(CannonAmmoState.Profile("unknown").Pellets == 0, "Unknown projectile types must have no firing profile");
var legacyCell = new EnergyState(); legacyCell.Set(true, 123.375f, true);
var legacyAmmo = new CannonAmmoState(); var legacyRestored = new EnergyState();
Check(CannonSaveState.Restore(legacyAmmo, legacyRestored, legacyCell.Capture()) && legacyRestored.Loaded
    && legacyRestored.Energy == 123.375f && legacyRestored.Favourite && !legacyAmmo.Loaded, "An old experimental battery must remain explicitly recoverable rather than being deleted or turned into a round");
var wrappedAmmo = new CannonAmmoState(); var wrappedCell = new EnergyState();
Check(CannonSaveState.Restore(wrappedAmmo, wrappedCell, CannonSaveState.Capture(ammo, new EnergyState()))
    && wrappedAmmo.Id == CannonAmmoState.DemolitionId && !wrappedCell.Loaded, "New single-round payloads must round-trip with no cell");
var impossible = CannonSaveState.Capture(ammo, legacyCell);
string priorAmmo = wrappedAmmo.Capture().ToString();
Check(!CannonSaveState.Restore(wrappedAmmo, wrappedCell, impossible) && wrappedAmmo.Capture().ToString() == priorAmmo && !wrappedCell.Loaded, "A malformed payload cannot load both a cell and a round or partially mutate state");
var invalidAmmo = ammo.Capture(); invalidAmmo["id"] = "unknown";
Check(!wrappedAmmo.Restore(invalidAmmo) && wrappedAmmo.Capture().ToString() == priorAmmo, "Invalid saved ammunition must fail atomically");
var chargeOwner = new object();
var stranger = new object();
var cycle = new ChargeCycle();
Check(!cycle.Release(chargeOwner, 1, out _), "A charged shot must require an authenticated begin");
Check(cycle.Begin(chargeOwner, 10) && !cycle.Begin(chargeOwner, 11), "Repeated begins must not reset the authority clock");
Check(!cycle.Release(stranger, 12, out _) && cycle.Active, "Another owner must not release an existing charge");
Check(cycle.Release(chargeOwner, 13, out float fullCharge) && fullCharge == 1, "Three seconds on the authority clock must reach full charge");
Check(!cycle.Release(chargeOwner, 12, out _), "A release may produce at most one shot");
Check(fullCharge == 1, "Full charge must apply the complete per-round damage profile without an energy cost");
Check(cycle.Begin(chargeOwner, 20) && !cycle.Release(chargeOwner, 20.699, out _) && !cycle.Active, "Short taps must cancel without firing");
Check(cycle.Begin(chargeOwner, 20) && cycle.Release(chargeOwner, 20.7, out float minimumCharge)
    && Math.Abs(minimumCharge - (0.7f / 3)) < 0.000001f, "Minimum charge must return its partial damage fraction");
Check(cycle.Begin(chargeOwner, 30), "Cancellation fixture must begin");
cycle.Cancel();
Check(!cycle.Release(chargeOwner, 32, out _), "Early cancelled charges must not release later");
foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
{
    Check(!cycle.Begin(chargeOwner, invalid), "Invalid host time must reject charge starts");
    cycle.Begin(chargeOwner, 1);
    Check(!cycle.Release(chargeOwner, invalid, out _) && !cycle.Active, "Invalid release time must cancel the charge");
}
Check(cycle.Begin(chargeOwner, 1) && !cycle.Release(chargeOwner, 32, out _) && !cycle.Active, "Abandoned charges must expire");
foreach (var time in new[] { 0d, 0.7, 0.999, 1d })
    Check(bioProfile.DamageMultiplier(time) == 1 && demoProfile.DamageMultiplier(time) == 1, "No charge damage bonus may apply until after one second");
Check(bioProfile.DamageMultiplier(2) == 2.5f && bioProfile.DamageMultiplier(3) == 4
    && bioProfile.DamageMultiplier(10) == 4, "Registered ammunition must ramp linearly to its own maximum without overshooting");
Check(bioProfile.Creature * bioProfile.DamageMultiplier(3) == 3000 && demoProfile.Structure * demoProfile.DamageMultiplier(3) == 1000 && demoProfile.Creature * demoProfile.DamageMultiplier(3) == 5,
    "Full charge must use the ammo's base damage and multiplier");
const string thirdRound = "offline.third-round";
CannonRoundRegistry.Register(thirdRound, new CannonRound(17, 91, 3, 7, 6, 1.5f).Parameters());
var extensionAmmo = new CannonAmmoState();
Check(extensionAmmo.Load(thirdRound, 1, false) && extensionAmmo.Rack() && CannonAmmoState.Profile(thirdRound).Pellets == 3
    && CannonAmmoState.Profile(thirdRound).DamageMultiplier(3) == 6,
    "A newly registered round must load, chamber and use its own special parameters without modifying the gun");
var invalidMetadata = new CannonRound(1, 1, 21, 0).Parameters();
bool rejectedMetadata = false;
try { CannonRoundRegistry.Register("offline.invalid-round", invalidMetadata); } catch (ArgumentException) { rejectedMetadata = true; }
Check(rejectedMetadata && !CannonRoundRegistry.Contains("offline.invalid-round"), "Malformed round metadata must never enter the supported-ammo registry");
var commitCycle = new ChargeCycle(); commitCycle.Begin(chargeOwner, 100);
Check(!commitCycle.Committed(101) && commitCycle.Committed(101.001), "Forced-cancel firing must begin strictly after one second");
Check(commitCycle.Release(chargeOwner, 101.5, out float committedRelease) && committedRelease == 0.5f,
    "Manual firing after one second must remain allowed before the three-second limit");
commitCycle.Begin(chargeOwner, 200);
Check(commitCycle.Committed(203) && commitCycle.Release(chargeOwner, 203, out float forcedRelease) && forcedRelease == 1,
    "The authority's three-second automatic-fire deadline must produce one fully charged release");
foreach (string firstRound in new[] { CannonAmmoState.BioId, CannonAmmoState.DemolitionId })
foreach (string nextRound in new[] { CannonAmmoState.BioId, CannonAmmoState.DemolitionId })
{
    var magazineAndChamber = new CannonAmmoState();
    Check(magazineAndChamber.Load(firstRound, 0.23f, true) && !magazineAndChamber.CanLoad && magazineAndChamber.CanUnload && !magazineAndChamber.Ready,
        "An unchambered magazine holds exactly one removable cartridge");
    Check(magazineAndChamber.Rack() && magazineAndChamber.Ready && magazineAndChamber.CanLoad && !magazineAndChamber.CanUnload,
        "Manual chambering frees the magazine and disables magazine unloading");
    Check(magazineAndChamber.Load(nextRound, 0.67f, false) && magazineAndChamber.Id == firstRound && magazineAndChamber.Condition == 0.23f && magazineAndChamber.Favourite
        && magazineAndChamber.MagazineLoaded && !magazineAndChamber.CanLoad && !magazineAndChamber.Load(firstRound, 1, false),
        "A full chamber accepts exactly one independent reserve cartridge of either type");
    var restoredMixed = new CannonAmmoState();
    Check(restoredMixed.Restore(magazineAndChamber.Capture()) && restoredMixed.Capture().ToString() == magazineAndChamber.Capture().ToString(),
        "Mixed magazine and chamber types, conditions and favourite flags must round-trip independently");
    Check(restoredMixed.Consume(out var actualRound) && actualRound == firstRound && restoredMixed.Spent
        && restoredMixed.Id == CannonAmmoState.CarrierId && restoredMixed.Chambered && !restoredMixed.Ready
        && restoredMixed.MagazineLoaded && !restoredMixed.CanLoad && !restoredMixed.CanUnload,
        "A shot consumes only the chambered round and leaves one spent shell plus an untouched magazine");
    string spentSnapshot = restoredMixed.Capture().ToString();
    Check(!restoredMixed.Consume(out _) && restoredMixed.Capture().ToString() == spentSnapshot,
        "A spent shell cannot fire twice or automatically chamber the reserve");
    var savedSpent = new CannonAmmoState();
    Check(savedSpent.Restore(restoredMixed.Capture()) && savedSpent.Spent && savedSpent.MagazineLoaded,
        "Saving and reconnecting must retain the spent chamber and reserve round");
    Check(savedSpent.Rack() && savedSpent.Ready && !savedSpent.Spent && savedSpent.Id == nextRound
        && savedSpent.Condition == 0.67f && !savedSpent.Favourite && !savedSpent.MagazineLoaded,
        "Only manual racking ejects the shell and chambers the reserve with its own original metadata");
    Check(savedSpent.Rack() && !savedSpent.Loaded && !savedSpent.Chambered && !savedSpent.Rack(),
        "Racking a live chamber without a reserve ejects it and leaves the weapon empty");
}
var oldSingle = new JObject { ["id"] = CannonAmmoState.BioId, ["condition"] = 0.5f, ["favourite"] = true, ["chambered"] = true, ["revision"] = 9 };
var migratedSingle = new CannonAmmoState();
Check(migratedSingle.Restore(oldSingle) && migratedSingle.Ready && migratedSingle.CanLoad && !migratedSingle.MagazineLoaded,
    "Legacy single-round saves must migrate without inventing a reserve cartridge");
var malformedReserve = migratedSingle.Capture(); malformedReserve["chambered"] = false;
malformedReserve["reserve"] = new JObject { ["id"] = CannonAmmoState.DemolitionId, ["condition"] = 1f, ["favourite"] = false };
string unchangedMixed = migratedSingle.Capture().ToString();
Check(!migratedSingle.Restore(malformedReserve) && migratedSingle.Capture().ToString() == unchangedMixed,
    "Two magazine cartridges with no occupied chamber must be rejected atomically");
malformedReserve = migratedSingle.Capture(); malformedReserve["reserve"]["id"] = CannonAmmoState.CarrierId;
malformedReserve["reserve"]["condition"] = 1;
Check(!migratedSingle.Restore(malformedReserve), "Spent shells may never be stored as a loadable magazine round");
var casingOnly = new CannonAmmoState(); casingOnly.Load(CannonAmmoState.BioId, 1, false); casingOnly.Rack(); casingOnly.Consume(out _);
Check(casingOnly.Spent && casingOnly.CanLoad && !casingOnly.CanUnload && casingOnly.Load(CannonAmmoState.DemolitionId, 1, false),
    "An occupied spent chamber still permits a single new magazine cartridge but no magazine removal");

foreach(string operation in new[]{"load","read","eject","work","cancel"})
{
    var pendingAction=new PendingActions();pendingAction.Add("old",operation,0);
    Check(pendingAction.Count==1&&pendingAction.Busy,"Installing ammo/cells or reading state must own one finite request");
    Check(!pendingAction.Expire(4.999,out _)&&pendingAction.Expire(5,out bool chargeTimedOut)&&!chargeTimedOut
        &&pendingAction.Count==0&&!pendingAction.Busy,"A failed install/read/cancel must unlock without scheduling another cancel");
    pendingAction.Add("retry",operation,5);
    Check(!pendingAction.Finish("old")&&pendingAction.Count==1&&pendingAction.Busy,
        "A late response for a timed-out install must not unlock or overwrite its retry");
    Check(pendingAction.Finish("retry")&&!pendingAction.Busy&&!pendingAction.Finish("retry"),
        "A successful/rejected/malformed response releases its pending entry exactly once");
    pendingAction.Add("send-failed",operation,10);pendingAction.Finish("send-failed");
    Check(!pendingAction.Busy&&pendingAction.Count==0,"A failed send must not leave a loading lock");
    pendingAction.Add("disabled",operation,12);pendingAction.Clear();
    Check(!pendingAction.Finish("disabled")&&!pendingAction.Busy,"Disabling or reconnecting must discard pending work and late replies");
}
var chargeTimeouts=new PendingActions();chargeTimeouts.Add("charge","charge",0);
Check(chargeTimeouts.Expire(5,out bool needsCancel)&&needsCancel,"Only a lost charge/release can schedule a charge cancellation");
chargeTimeouts.Add("cancel","cancel",5);
Check(chargeTimeouts.Expire(10,out needsCancel)&&!needsCancel&&!chargeTimeouts.Busy,
    "A lost cancellation must terminate instead of creating a permanent cancel/read loading loop");
var parallelShots=new PendingActions();for(int shot=0;shot<8;shot++)parallelShots.Add(shot.ToString(),"shoot",0);
Check(parallelShots.Count==8&&!parallelShots.Busy&&parallelShots.Expire(5,out _)&&parallelShots.Count==0,
    "The beam rifle's bounded concurrent shots must remain independent of loading locks");

var chargeSamples = ChargeTone.Samples();
Check(chargeSamples.Length == ChargeTone.SampleRate / 2 && chargeSamples.All(float.IsFinite)
    && chargeSamples.Max(Math.Abs) < 0.9 && Math.Abs(chargeSamples.Average()) < 0.00001,
    "The original charging loop must have finite unclipped samples and no DC offset");
Check(Math.Abs(ChargeTone.Sample(0) - ChargeTone.Sample(0.5)) < 0.000001,
    "The charging tone must complete whole cycles at its loop seam");
float lastPitch = 0;
foreach (float progress in Enumerable.Range(0,101).Select(i => i/100f))
{
    Check(ChargeTone.Pitch(progress) >= lastPitch && ChargeTone.Gain(progress) <= 0.091,
        "Charge pitch must rise smoothly while remaining quiet");
    lastPitch = ChargeTone.Pitch(progress);
    float previousGain = float.MaxValue;
    foreach (float distance in new[] {0f,1,2,5,10,20,29,30,100})
    {
        float otherGain = ChargeTone.ListenerGain(progress, false, distance);
        Check(otherGain <= ChargeTone.ListenerGain(progress,true,distance) * 0.600001f
            && otherGain <= previousGain && otherGain >= 0,
            "Other players must hear at least forty percent quieter and attenuation must never rise with distance");
        previousGain = otherGain;
    }
}
Check(ChargeTone.ListenerGain(1,false,30) == 0 && ChargeTone.ListenerGain(1,false,float.NaN) == 0,
    "Distant or invalid listener positions must be silent");
var audioLease = new ChargeAudioLease();
Check(audioLease.Apply(1,true,0.25,10) && audioLease.Active(11) && Math.Abs(audioLease.Progress(11)-1.25f/3)<0.00001,
    "Remote charge progress must account for elapsed authoritative charge time");
Check(audioLease.Apply(3,false,0,11) && !audioLease.Apply(2,true,0,11) && !audioLease.Active(11),
    "An out-of-order start must never restart a stopped charging loop");
Check(audioLease.Apply(4,true,0,20) && !audioLease.Active(23.36), "Missing stop packets must not leave a permanent charging sound");
Check(!audioLease.Apply(5,true,4,30) && !audioLease.Apply(5,true,double.NaN,30), "Invalid charge sound timing must be rejected");
audioLease.Reset(); Check(audioLease.Apply(1,true,0,40), "A new connection must reset remote audio packet ordering");
var blastRound = CannonAmmoState.Profile(CannonAmmoState.ExplosiveId);
Check(blastRound.BlastRadius == 12 && blastRound.Creature == 400 && blastRound.Pellets == 1
    && blastRound.DamageMultiplier(0.7) == 1 && blastRound.DamageMultiplier(3) == 1,
    "Explosive rounds must retain a twelve-tile radius and fixed four-hundred creature damage");
foreach (bool multiplayer in new[] {false,true})
{
    DDEN.Shared.Mp.IsRunning = multiplayer; DDEN.Shared.Mp.IsServer = multiplayer; DDEN.Shared.Mp.CoreReady = true;
    WorldGeneration.Explosions.Clear();
    var blastOwner = new Body();
    var animal = new BuildingEntity {animal=true};
    var structure = new BuildingEntity();
    var protectedBuilding = new BuildingEntity {indestructible=true};
    var animalCollider = new UnityEngine.Collider2D(); animalCollider.gameObject.Building=animal;
    var structureCollider = new UnityEngine.Collider2D(); structureCollider.gameObject.Building=structure;
    var protectedCollider = new UnityEngine.Collider2D(); protectedCollider.gameObject.Building=protectedBuilding;
    var ownerCollider = new UnityEngine.Collider2D(); ownerCollider.gameObject.Body=blastOwner;
    var wallCollider = new UnityEngine.Collider2D(); wallCollider.gameObject.layer=6;
    UnityEngine.Physics2D.Nearby = new[] {animalCollider,animalCollider,structureCollider,protectedCollider};
    UnityEngine.Physics2D.Hits = new[] {
        new UnityEngine.RaycastHit2D {collider=structureCollider,distance=8,point=new UnityEngine.Vector2(8,0)},
        new UnityEngine.RaycastHit2D {collider=wallCollider,distance=0.001f,point=new UnityEngine.Vector2(0.001f,0)},
        new UnityEngine.RaycastHit2D {collider=ownerCollider,distance=0,point=new UnityEngine.Vector2(0,0)}};
    var impact = CannonBlast.Fire(blastOwner,new UnityEngine.Vector2(0,0),new UnityEngine.Vector2(1,0),blastRound);
    Check(impact.x == 0.001f && WorldGeneration.Explosions.Count == 1 && WorldGeneration.Explosions[0].position.x == impact.x,
        "Singleplayer and multiplayer host must explode once at the nearest real impact, with no minimum fuse distance");
    Check(animal.health == 1600 && structure.health == 1500 && protectedBuilding.health == 2000,
        "Overlapping colliders must deal creature damage only once and preserve indestructible buildings");
    var parameters = WorldGeneration.Explosions[0];
    Check(parameters.range == 12 && parameters.structuralDamage == 0 && parameters.muscleDamage.Max == 0
        && parameters.skinDamage.Max == 0 && parameters.skinDamageChance == 0 && parameters.shrapnelChance == 0
        && parameters.bleedChance == 0 && parameters.bleedAmount.Max == 0 && parameters.boneBreakChance == 0
        && parameters.dislocationChance == 0 && parameters.disfigureChance == 0 && parameters.dismemberChance == 0
        && parameters.velocity == 0,
        "The synchronized terrain explosion must not add duplicate creature damage or fatal player health damage");
    WorldGeneration.Explosions.Clear(); UnityEngine.Physics2D.Hits=Array.Empty<UnityEngine.RaycastHit2D>();
    Check(CannonBlast.Fire(blastOwner,new UnityEngine.Vector2(0,0),new UnityEngine.Vector2(1,0),blastRound).x == 200
        && WorldGeneration.Explosions.Count == 0, "A miss must not produce an arbitrary airburst at maximum range");
}
DDEN.Shared.Mp.IsRunning=true; DDEN.Shared.Mp.IsServer=false;
bool clientBlastRejected=false;
try { CannonBlast.Fire(new Body(),new UnityEngine.Vector2(),new UnityEngine.Vector2(1,0),blastRound); }
catch(InvalidOperationException) { clientBlastRejected=true; }
Check(clientBlastRejected && WorldGeneration.Explosions.Count == 0, "A multiplayer client must never perform authoritative blast damage");
DDEN.Shared.Mp.IsRunning=DDEN.Shared.Mp.IsServer=false;
var blastAmmo = new CannonAmmoState();
Check(blastAmmo.Load(CannonAmmoState.ExplosiveId,1,false) && blastAmmo.Rack()
    && blastAmmo.Load(CannonAmmoState.BioId,1,true) && blastAmmo.Consume(out var blastId)
    && blastId == CannonAmmoState.ExplosiveId && blastAmmo.Spent && blastAmmo.Rack() && blastAmmo.Id == CannonAmmoState.BioId,
    "New explosive ammo must use the same mixed magazine, retained shell and manual rack lifecycle");

var arsenalRecipes = ArsenalRecipes.Create().ToList();
Check(arsenalRecipes.Count == 11 && arsenalRecipes.Select(r=>r.result.id).Distinct().Count()==11,
    "Eleven distinct arsenal and medical recipes must be provided");
void CheckArsenalMaterials(string id,params (string Id,int Count)[] expected)
{
    var recipe = arsenalRecipes.Single(r=>r.result.id==id);
    Check(recipe.result.amount==1 && recipe.result.resultCondition==1 && recipe.INT==18,
        "Each arsenal recipe produces one full-condition item at the normal equipment intelligence requirement");
    var materials=recipe.items.Where(i=>i.specific&&!i.isLiquid).GroupBy(i=>i.specificId).ToDictionary(g=>g.Key,g=>g.Count());
    Check(materials.Count==expected.Length && expected.All(x=>materials.GetValueOrDefault(x.Id)==x.Count),
        "Arsenal recipe must have the exact requested physical ingredients: "+id);
    Check(recipe.items.Where(i=>i.specific&&!i.isLiquid).All(i=>i.destroyItem&&i.minimumCondition==0),
        "Physical materials must be consumed as whole items rather than matching a tool quality");
    var quality = recipe.items.Single(i=>!i.specific);
    float units=id==Registration.ChargeGunId?200:100;
    Check(!quality.destroyItem && quality.quality.id==Registration.DragonQuality && quality.quality.amount==units
        && Math.Abs(quality.minimumCondition-DragonizationRules.Cost(units))<0.000001,
        "Dragonization quality and required durability must scale from 100 units/1 percent to 200 units/2 percent");
}
CheckArsenalMaterials(CannonAmmoState.CarrierId,(Registration.MatterId,1),("casing",4),("titaniumsheet",1),("processedcopper",1));
CheckArsenalMaterials(CannonAmmoState.BioId,(CannonAmmoState.CarrierId,1),("flammablepowder",2),("titaniumrod",1));
CheckArsenalMaterials(CannonAmmoState.DemolitionId,(CannonAmmoState.CarrierId,1),("scrapcube",1),("nails",2),("flammablepowder",1));
CheckArsenalMaterials(CannonAmmoState.ExplosiveId,(CannonAmmoState.CarrierId,1),("dynamite",1),("scrappanel",1));
CheckArsenalMaterials(Registration.ChargeGunId,(Registration.MatterId,2),("titaniumsheet",2),("bundleofwires",3),
    (Registration.CellId,1),("scraptube",1),("flexiglass",2),("rifle",1));
var mindwipe = arsenalRecipes.Single(r=>r.result.id==CannonAmmoState.BioId).items.Single(i=>i.isLiquid);
Check(mindwipe.specific&&mindwipe.specificId=="mindwipe"&&mindwipe.minimumCondition==90,
    "Biological ammunition must consume exactly 90ml of the native mindwipe liquid, not 90 physical items");
Check(DragonizationRules.TrySpend(1,200,out var cannonCraftWear)&&Math.Abs(cannonCraftWear-0.98f)<0.000001
    &&!DragonizationRules.TrySpend(0.019f,200,out _),"Cannon crafting must cost two percent durability and reject insufficient wear budget");
foreach(string roundId in new[]{CannonAmmoState.BioId,CannonAmmoState.DemolitionId,CannonAmmoState.ExplosiveId})
{
    var bonus = new CannonAmmoState();
    Check(bonus.TryGrantSpawnRound(roundId)&&bonus.Id==roundId&&bonus.MagazineLoaded&&!bonus.Chambered&&bonus.SpawnRoundGranted,
        "A merchant or crate cannon must receive exactly one random unchambered cartridge");
    Check(!bonus.TryGrantSpawnRound(roundId),"A duplicated spawn/pickup callback must not grant another round");
    bonus.Clear();
    var bonusRestored=new CannonAmmoState();
    Check(bonusRestored.Restore(bonus.Capture())&&bonusRestored.SpawnRoundGranted&&!bonusRestored.TryGrantSpawnRound(roundId),
        "Emptying, storing and restoring the cannon must not reset its one-time ammunition grant");
    var invalidGrant=bonusRestored.Capture();invalidGrant["spawnRoundGranted"]="true";
    string beforeInvalidGrant=bonusRestored.Capture().ToString();
    Check(!bonusRestored.Restore(invalidGrant)&&bonusRestored.Capture().ToString()==beforeInvalidGrant,
        "Malformed grant flags must be rejected without modifying ammunition");
}
var existingGun = new CannonAmmoState();existingGun.Load(CannonAmmoState.BioId,0.4f,true);
Check(!existingGun.TryGrantSpawnRound(CannonAmmoState.ExplosiveId)&&existingGun.Id==CannonAmmoState.BioId&&existingGun.Condition==0.4f,
    "Spawn bonus must not replace or fill a gun that already carries ammunition");
var oldGrantSave=new CannonAmmoState().Capture();oldGrantSave.Remove("spawnRoundGranted");
Check(new CannonAmmoState().Restore(oldGrantSave),"Old saves without the bonus flag must remain valid");
Check(Enumerable.Range(0,LootRules.RoundCount).Select(LootRules.BonusRound).Distinct().Count()==7&&LootRules.BonusRound(-1)==null&&LootRules.BonusRound(7)==null,
    "The bundled round must be uniformly selectable from all seven live ammunition types");
var accessBody = new Body(); var groundSource = new Item {id=Registration.CellId};
DDEN.Shared.Mp.IsRunning=false; DDEN.Shared.Mp.CoreReady=false;
accessBody.PickupAllowed=true;
Check(NetworkAccess.CanAccess(accessBody,groundSource)&&accessBody.PickupChecks==1&&accessBody.NoAlerts,
    "Singleplayer can load a reachable ground/container item without multiplayer availability or an inventory-slot requirement");
accessBody.PickupAllowed=false;
Check(!NetworkAccess.CanAccess(accessBody,groundSource),"Singleplayer must retain native pickup distance and obstruction rejection");
accessBody.CarriedItems.Add(groundSource);
Check(NetworkAccess.CanAccess(accessBody,groundSource),"Carried-container contents must remain usable without a ground-distance check");
accessBody.CarriedItems.Clear();
DDEN.Shared.Mp.IsRunning=true;DDEN.Shared.Mp.IsServer=true;DDEN.Shared.Mp.CoreReady=true;
accessBody.PickupAllowed=true;OfflineReachCheck.Allowed=false;
Check(!NetworkAccess.CanAccess(accessBody,groundSource)&&OfflineReachCheck.Calls==1&&OfflineReachCheck.Radius==20&&!OfflineReachCheck.Obstruction,
    "The host must use the original MP combine defaults, without an extra ten-unit or obstruction requirement");
OfflineReachCheck.Items[groundSource]=new OfflineItemSyncInfo {syncId=new OfflineItemSyncId {id=601}};
Check(NetworkAccess.Id(groundSource)==601,"Ground/container material IDs must resolve through the native MP item registry even without a per-object tracker");
OfflineReachCheck.Items.Clear();
OfflineReachCheck.Allowed=true;
Check(NetworkAccess.CanAccess(accessBody,groundSource),"A host-approved nearby ground or external-container material must be loadable");
groundSource.gameObject.activeInHierarchy=false;
Check(NetworkAccess.CanAccess(accessBody,groundSource),"Hidden container contents must follow the native MP result rather than an added active-hierarchy requirement");
groundSource.gameObject.activeInHierarchy=true;
DDEN.Shared.Mp.CoreReady=false;
Check(!NetworkAccess.CanAccess(accessBody,groundSource),"Unavailable multiplayer validation cannot accept an unowned material");
DDEN.Shared.Mp.IsRunning=DDEN.Shared.Mp.IsServer=false;DDEN.Shared.Mp.CoreReady=true;

// Execute the production camera/button bridge with UI and network-boundary doubles.
var rackBody = new Body();
var rackCamera = new PlayerCamera { body = rackBody, gunRackImage = new UnityEngine.UI.Image() };
var rackButton = new UnityEngine.UI.Button { enabled = false, interactable = false };
rackCamera.gunRackImage.ParentButton = rackButton;
PlayerCamera.main = rackCamera;
var rackItem = new Item { id = Registration.ChargeGunId };
rackBody.Held = rackItem;
var rackGun = DragonGun.For(rackItem);
var rackControls = new CannonRackControls { TestComponent = rackCamera };
object RackMethod(string name, params object[] arguments) => typeof(CannonRackControls).GetMethod(name,
    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance)
    .Invoke(name == "Rack" ? null : rackControls, arguments);
RackMethod("LateUpdate");
Check(rackButton.enabled && !rackButton.interactable, "An empty cannon cannot rack even if its native button is enabled");
rackGun.Ammo.Load(CannonAmmoState.BioId, 1, false);
RackMethod("LateUpdate");
Check(rackButton.enabled && rackButton.interactable, "A loaded unchambered cannon must enable the disabled native rack button, including a parent button");
Check(!(bool)RackMethod("Rack", rackCamera) && rackGun.Requests == 1 && rackGun.Operation == "rack" && !rackGun.Ammo.Ready,
    "Clicking rack must issue one authoritative request instead of changing client ammunition or running the native rack action");
DDEN.Shared.Mp.IsRunning = true;
RackMethod("LateUpdate");
Check(!rackButton.interactable, "Unknown client ammunition state cannot enable rack");
rackGun.Known = true; RackMethod("LateUpdate");
Check(rackButton.interactable, "An acknowledged loaded client cartridge must enable rack");
Check(rackCamera.gunMagButton.interactable, "Loaded magazine can be unloaded before chambering");
rackGun.Ammo.Rack(); RackMethod("LateUpdate");
Check(rackButton.interactable && !rackCamera.gunMagButton.interactable, "A chambered round must allow manual ejection while disabling magazine unload");
rackGun.Ammo.Consume(out _); RackMethod("LateUpdate");
Check(rackButton.interactable && !rackCamera.gunMagButton.interactable, "A spent chamber enables manual shell ejection while blocking magazine unloading");
rackGun.Ammo.Clear(); rackGun.Ammo.Load(CannonAmmoState.DemolitionId, 1, false);
rackBody.conscious = false; RackMethod("LateUpdate"); RackMethod("Rack", rackCamera);
Check(!rackButton.interactable && rackGun.Requests == 1, "An unconscious owner cannot rack by button or key");
rackBody.conscious = true;
DDEN.Shared.DdenHub.AnyPanelVisible = true; RackMethod("LateUpdate"); RackMethod("Rack", rackCamera);
Check(!rackButton.interactable && rackGun.Requests == 1, "A modal panel must block local rack requests");
DDEN.Shared.DdenHub.AnyPanelVisible = false;
rackBody.Held = new Item { id = "rifle" }; RackMethod("LateUpdate");
Check(!rackButton.enabled && !rackButton.interactable && (bool)RackMethod("Rack", rackCamera),
    "Leaving the cannon must restore the original disabled button and pass ordinary guns to the native action");
rackButton.enabled = true; rackButton.interactable = true;
rackBody.Held = rackItem; RackMethod("LateUpdate");
rackCamera.gunMenu.activeInHierarchy = false; RackMethod("LateUpdate");
Check(rackButton.enabled && rackButton.interactable, "Closing the native HUD must restore a normally enabled button");
rackCamera.gunMenu.activeInHierarchy = true; RackMethod("LateUpdate");
rackButton.enabled = false; RackMethod("OnDisable");
Check(!rackButton.enabled && rackButton.interactable, "Cleanup must not overwrite a subsequent external change to button enabled state");
DDEN.Shared.Mp.IsRunning = DDEN.Shared.Mp.IsServer = false;
PlayerCamera.main = null;

var chargeCell = new EnergyState();
chargeCell.Set(true, 49.5f, true);
Check(!chargeCell.TrySpend(50) && chargeCell.Energy == 49.5f, "Insufficient full-charge energy must not debit anything");
Check(chargeCell.TrySpend(15) && chargeCell.Energy == 34.5f && chargeCell.Favourite, "Charged shots must preserve fractional remainder and favourite marker");
Check(!chargeCell.TrySpend(float.NaN) && !chargeCell.TrySpend(-1) && chargeCell.Energy == 34.5f, "Invalid variable shot costs must be rejected");
var state = new EnergyState();
state.Set(true, 1000, true);
for (int shot = 0; shot < 100; shot++) Check(state.TrySpendShot(), "Full cell must support one hundred shots");
Check(state.Loaded && state.Energy == 0 && state.Favourite, "Empty cell keeps its shell and favourite flag");
Check(!state.TrySpendShot() && state.Energy == 0, "Hundred-and-first shot must fail without mutating energy");
var restored = new EnergyState();
Check(restored.Restore(JObject.Parse(state.Capture().ToString())), "Empty loaded cell must survive save round-trip");
Check(restored.Loaded && restored.Energy == 0 && restored.Favourite, "Empty cell state must be retained");
state.Set(true, 12.375f, false);
Check(state.TrySpendShot() && state.Energy == 2.375f, "Fractional remainders must survive a shot");
Check(!state.TrySpendShot(), "Insufficient fractional remainder cannot fire");
Check(restored.Restore(JObject.Parse(state.Capture().ToString())) && restored.Energy == 2.375f, "Fractional charge must survive save round-trip");
var bad = state.Capture();
foreach (double invalid in new[] { -1d, 1001d, double.NaN, double.PositiveInfinity })
{
    bad["energy"] = invalid;
    Check(!restored.Restore(bad) && restored.Energy == 2.375f, "Invalid saved charge must leave current state intact");
}
bad = state.Capture(); bad["loaded"] = "true";
Check(!restored.Restore(bad), "Invalid loaded flag must be rejected");
restored.Set(false, 400, true);
Check(!restored.Loaded && restored.Energy == 0 && !restored.Favourite, "Ejection must clear embedded cell state");
Check(!restored.TrySpendShot() && !restored.Restore(null), "Unloaded gun and missing data must fail safely");
var belt = new BeltPowerState();
Check(!belt.Cell.Loaded && belt.Remaining == 0 && belt.Advance(2) == 0 && belt.EncumbranceMultiplier == 0.6f, "New purchased/crafted belts must have empty sockets and 40 percent storage reduction");
belt.Load(1000, true);
Check(belt.Advance(1) == 1 && belt.Cell.Energy == 1000 && belt.Remaining == 999.5f && belt.EncumbranceMultiplier == 0, "First active second must retain its pending half-unit debt and powered storage reduction");
var checkpoint = belt.Capture().ToString();
for (int idle = 0; idle < 500; idle++) belt.Advance(0);
Check(belt.Capture().ToString() == checkpoint, "Idle time must not consume energy or reset the billing remainder");
var savedBelt = new BeltPowerState();
Check(savedBelt.Restore(JObject.Parse(checkpoint)) && savedBelt.Remaining == 999.5f && savedBelt.EncumbranceMultiplier == 0, "Sub-two-second remainder and powered reduction must survive saving");
savedBelt.Advance(1);
Check(savedBelt.Cell.Energy == 999 && savedBelt.PartialSeconds == 0 && savedBelt.WorkTotal == 2, "Resumed work must complete the same two-second billing interval");
Check(savedBelt.Cell.Favourite && savedBelt.Epoch == belt.Epoch, "Saved cell identity and favourite marker must survive work");
foreach (double invalid in new[] { -1d, double.NaN, double.PositiveInfinity })
{
    var prior = savedBelt.Capture().ToString();
    Check(savedBelt.Advance(invalid) == 0 && savedBelt.Capture().ToString() == prior, "Invalid work must not mutate power state");
}
belt.Load(1, false);
for (int tick = 0; tick < 100; tick++) belt.Advance(0.02);
Check(belt.Remaining == 0 && belt.Cell.Loaded && belt.Cell.Energy == 0 && belt.EncumbranceMultiplier == 0.6f, "An exhausted cell must stop assistance after exactly two seconds, keep its shell and revert storage reduction to 40 percent");
Check(belt.Advance(1) == 0, "An exhausted belt must never grant more work");
belt.Load(0.125f, true);
Check(belt.Advance(10) == 0.25 && belt.Remaining == 0, "Fractional cells may pay only for the affordable assistance time");
belt.Load(1000, true);
for (int tick = 0; tick < 100000; tick++) belt.Advance(0.02);
Check(belt.Remaining == 0 && Math.Abs(belt.WorkTotal - 2000) < 0.001, "A full cell must pay for 2000 seconds without drift");
belt.Load(32.375f, true); belt.Advance(1.5);
float ejectedCharge = belt.Remaining;
Check(ejectedCharge == 31.625f, "Ejection must settle fractional pending work instead of resetting it");
belt.Clear(); belt.Load(ejectedCharge, true);
Check(belt.Remaining == 31.625f && belt.PartialSeconds == 0, "Reloading must preserve the settled charge");
checkpoint = belt.Capture().ToString();
foreach (string key in new[] { "partial", "work" })
{
    var badBelt = JObject.Parse(checkpoint); badBelt[key] = double.NaN;
    Check(!belt.Restore(badBelt) && belt.Capture().ToString() == checkpoint, "Invalid saved belt counters must be rejected atomically");
}
var invalidEpoch = JObject.Parse(checkpoint); invalidEpoch["epoch"] = "old-cell";
Check(!belt.Restore(invalidEpoch) && belt.Capture().ToString() == checkpoint, "Malformed cell identity must be rejected");
belt.Clear();
Check(savedBelt.Restore(belt.Capture()) && !savedBelt.Cell.Loaded && savedBelt.Remaining == 0 && savedBelt.EncumbranceMultiplier == 0.6f, "Ejected state must round-trip as an empty socket with 40 percent storage reduction");
var reports = new WorkReportBudget();
belt.Load(1000, false);
string oldEpoch = belt.Epoch;
Check(reports.Apply(belt, oldEpoch, 1, 10) && belt.Remaining == 999.5f, "Authenticated active-time report must charge the host state");
checkpoint = belt.Capture().ToString();
Check(reports.Apply(belt, oldEpoch, 1, 10) && belt.Capture().ToString() == checkpoint, "Retried cumulative reports must never double-charge");
Check(!reports.Apply(belt, oldEpoch, 0.5, 10) && belt.Capture().ToString() == checkpoint, "Reordered older work reports must not refund energy");
Check(!reports.Apply(belt, oldEpoch, 33, 10) && belt.Capture().ToString() == checkpoint, "Work above the real-time burst budget must be rejected");
Check(reports.Apply(belt, oldEpoch, 2, 11) && belt.Remaining == 999, "Budget must replenish as time advances");
belt.Clear(); belt.Load(100, true);
checkpoint = belt.Capture().ToString();
Check(!reports.Apply(belt, oldEpoch, 2.5, 12) && belt.Capture().ToString() == checkpoint, "Delayed reports for an ejected cell must not charge a newly loaded cell");
Check(reports.Apply(belt, belt.Epoch, 0.5, 12) && belt.Remaining == 99.75f, "A newly loaded cell starts a separate cumulative report epoch");
Check(!reports.Apply(belt, belt.Epoch, double.NaN, 12), "Non-finite client work must be rejected");
// Exercise the actual production UI prefix, before the native container path can return.
var actor = new Body();
var beltItem = new Item { id = Registration.BeltId };
var cellItem = new Item { id = Registration.CellId };
var toolItem = new Item { id = "wrench", Stats = new ItemInfo { Tool = true } };
var camera = new PlayerCamera { body = actor, dragItem = cellItem };
var button = new InvButton { Item = beltItem };
var hit = new UnityEngine.EventSystems.RaycastResult { gameObject = new UnityEngine.EventSystems.FakeHit { Button = button } };
var hits = new List<UnityEngine.EventSystems.RaycastResult> { hit };
bool result = false;
Check(!BeltInteractionPatches.InventoryAction(camera, hit, hits, ref result) && result
    && beltItem.Belt.Operation == "load" && beltItem.Belt.Cell == cellItem && beltItem.Belt.Body == actor,
    "Dragging a cell onto the belt must dispatch installation before native container handling");
beltItem.Belt.State.Load(100, true);
camera.dragItem = toolItem;
Check(!BeltInteractionPatches.InventoryAction(camera, hit, hits, ref result) && result
    && beltItem.Belt.Operation == "eject" && beltItem.Belt.Cell == null,
    "Dragging a tool onto a loaded belt must eject instead of storing the tool");
beltItem.Belt.State.Clear();
int priorRequests = beltItem.Belt.Requests;
Check(BeltInteractionPatches.InventoryAction(camera, hit, hits, ref result) && beltItem.Belt.Requests == priorRequests,
    "An empty belt must still allow native tool storage");
camera.dragItem = new Item { id = "cloth" };
Check(BeltInteractionPatches.InventoryAction(camera, hit, hits, ref result), "Ordinary items must retain native inventory behavior");
var uiCannon=new Item {id=Registration.ChargeGunId};
var uiBio=new Item {id=CannonAmmoState.BioId};
button.Item=uiCannon;camera.dragItem=uiBio;result=false;
Check(!BeltInteractionPatches.InventoryAction(camera,hit,hits,ref result)&&result
    &&DragonGun.For(uiCannon).Operation=="load"&&DragonGun.For(uiCannon).RequestedCell==uiBio,
    "Dedicated inventory drag path must send loose/container ammo to the cannon without requiring a player inventory slot");
var uiScorch=new Item{id=Registration.GunId};button.Item=uiScorch;camera.dragItem=cellItem;result=false;
Check(!BeltInteractionPatches.InventoryAction(camera,hit,hits,ref result)&&result&&DragonGun.For(uiScorch).RequestedCell==cellItem,
    "Loose/container energy cells must use the same early drag path for the beam rifle");
button.Item=uiCannon;camera.dragItem=cellItem;
Check(BeltInteractionPatches.InventoryAction(camera,hit,hits,ref result),"The cannon must still reject energy cells while preserving unrelated native UI actions");

button.Item=beltItem;
camera.dragItem = cellItem;
button.Overlap = false;
Check(BeltInteractionPatches.InventoryAction(camera, hit, hits, ref result), "Obscured buttons must not intercept a drag");
button.Overlap = true;
button.Item = cellItem;
Check(BeltInteractionPatches.InventoryAction(camera, hit, hits, ref result), "Dragging onto itself must keep native behavior");
button.Item = new Item { id = "belt" };
Check(BeltInteractionPatches.InventoryAction(camera, hit, hits, ref result), "Vanilla and other mod containers must not be intercepted");
button.Item = beltItem;
camera.body = null;
Check(BeltInteractionPatches.InventoryAction(camera, hit, hits, ref result), "No player body must not dispatch an installation");
camera.body = actor;
Check(BeltInteractionPatches.InventoryAction(camera, default, hits, ref result), "Non-inventory raycast hits must retain native behavior");
Check(beltItem.Belt.Requests == priorRequests, "Ignored inventory actions must never send requests");

var displayState = new BeltPowerState();
Check(displayState.ChargeFraction == 0, "Empty belt must display zero battery charge");
displayState.Load(1000, false);
Check(displayState.ChargeFraction == 1, "Full cell must display 100 percent");
displayState.Advance(1);
Check(Math.Abs(displayState.ChargeFraction - 0.9995f) < 0.000001f, "Battery display must include pending fractional consumption");
var displayRestore = new BeltPowerState();
Check(displayRestore.Restore(displayState.Capture()) && displayRestore.ChargeFraction == displayState.ChargeFraction,
    "Restored battery display must match the embedded cell rather than old item durability");
displayState.Advance(2000);
Check(displayState.ChargeFraction == 0 && displayState.Cell.Loaded, "An exhausted cell displays zero and retains its shell");
displayState.Load(12.375f, true);
Check(Math.Abs(displayState.ChargeFraction - 0.012375f) < 0.000001f, "Partially charged cells must retain their displayed precision");
displayState.Clear();
Check(displayState.ChargeFraction == 0, "Ejection must clear the charge display");
// Both modes share the legacy work units, preserving old save payloads and fractional debt.
var flight = new BeltPowerState();
flight.Load(1000, true);
Check(flight.AdvanceActivity(1, true) == 1 && flight.Remaining == 992 && flight.WorkTotal == 16,
    "One second of flight must cost exactly eight energy");
flight.AdvanceActivity(1, false);
Check(flight.Remaining == 991.5f, "Slow fall must cost half an energy without flight-rate stacking");
var mixedSaved = flight.Capture();
var mixed = new BeltPowerState();
Check(mixed.Restore(mixedSaved) && mixed.Remaining == flight.Remaining, "Mixed-mode fractional debt must survive saving");
mixed.AdvanceActivity(0.5, true);
Check(mixed.Remaining == 987.5f && mixed.Cell.Favourite, "Resumed flight must retain old fractional debt and favourite flag");
flight.Load(1, false);
Check(flight.AdvanceActivity(1, true) == 0.125 && flight.Remaining == 0 && flight.Cell.Loaded,
    "The last partial flight tick grants only affordable thrust time and retains the cell shell");
flight.Load(1000, false);
for (int tick = 0; tick < 6249; tick++) flight.AdvanceActivity(0.02, true);
Check(Math.Abs(flight.Remaining - 0.16f) < 0.00001f, "A full cell must still power the tick immediately before 125 seconds");
flight.AdvanceActivity(0.02, true);
Check(flight.Remaining == 0 && Math.Abs(flight.WorkTotal - 2000) < 0.001,
    "A full cell must support exactly 125 seconds of flight without drift");
flight.Load(1000, false);
Check(flight.AdvanceActivity(3, true, BeltPowerState.MaxUnconfirmedWork) == 2 && flight.Remaining == 984,
    "Client speculation must stop after two seconds of unconfirmed flight");
var flightReports = new WorkReportBudget();
var authority = new BeltPowerState();
authority.Load(1000, true);
Check(flightReports.Apply(authority, authority.Epoch, 32, 10) && authority.Remaining == 984,
    "Host must accept the full two-second flight burst");
Check(!flightReports.Apply(authority, authority.Epoch, 48, 10), "Host must reject excess instantaneous flight work");
Check(flightReports.Apply(authority, authority.Epoch, 48, 11) && authority.Remaining == 976,
    "Flight budget must replenish at the flight work rate rather than the old slow-fall rate");
var acknowledged = authority.Capture().ToString();
Check(flightReports.Apply(authority, authority.Epoch, 48, 11) && authority.Capture().ToString() == acknowledged,
    "Repeated flight reports must not double-charge");
foreach (double invalid in new[] { -1d, double.NaN, double.PositiveInfinity })
{
    Check(authority.AdvanceActivity(invalid, true) == 0, "Invalid flight durations must be ignored");
    Check(authority.AdvanceActivity(1, true, invalid) == (double.IsPositiveInfinity(invalid) ? 1 : 0),
        "Only a positive infinite work limit denotes an unlimited local budget");
}
foreach (float width in new[] { 240f, 320f, 549f, 550f, 700f, 929f, 930f, 1200f })
{
    int columns = ItemCardLayout.Columns(width);
    float cardWidth = ItemCardLayout.CardWidth(width);
    float usedWidth = columns * cardWidth + (columns - 1) * ItemCardLayout.Gap;
    Check(usedWidth <= width + 0.001f && cardWidth > 200, "Item cards must fit small and wide windows without horizontal overflow");
    Check(ItemCardLayout.GridHeight(width, 6) == ((6 + columns - 1) / columns) * (ItemCardLayout.Height + ItemCardLayout.Gap) - ItemCardLayout.Gap,
        "Catalog height must include every card row and gap");
    for (int i = 0; i < 6; i++)
    {
        float x = (i % columns) * (cardWidth + ItemCardLayout.Gap);
        float y = (i / columns) * (ItemCardLayout.Height + ItemCardLayout.Gap);
        Check(x >= 0 && x + cardWidth <= width + 0.001f && y + ItemCardLayout.Height <= ItemCardLayout.GridHeight(width, 6) + 0.001f,
            "Every information card must remain inside the computed viewport content");
    }
}
var notice = new LowEnergyNotice();
Check(!notice.ShouldWarn("cellA", false, 0, 0), "An empty socket must never emit low-energy warnings");
Check(!notice.ShouldWarn("cellA", true, 100.1f, 0), "Energy above ten percent must not warn");
Check(notice.ShouldWarn("cellA", true, 100, 0), "Crossing ten percent must warn immediately");
Check(!notice.ShouldWarn("cellA", true, 99, 0) && !notice.ShouldWarn("cellA", true, 0, 29.99),
    "Low-energy feedback must not repeat every frame or when charge reaches zero");
Check(notice.ShouldWarn("cellA", true, 0, 30), "Continued low charge may remind the player after thirty seconds");
Check(notice.ShouldWarn("cellB", true, 50, 31), "A newly installed low-charge cell starts its own warning state");
Check(!notice.ShouldWarn("cellB", true, 1000, 32) && notice.ShouldWarn("cellB", true, 100, 33),
    "Charge recovery must re-arm the next low-energy transition");
Check(!notice.ShouldWarn("", true, 50, 40) && !notice.ShouldWarn("cellB", true, float.NaN, 40)
    && !notice.ShouldWarn("cellB", true, 50, double.NaN), "Invalid cell state or clocks must never trigger a warning");
var visual = new EnergyState();
Check(GunVisualState.Select(true, visual.Loaded, true, "installed", "empty") == "empty", "New empty gun must select the no-magazine image");
visual.Set(true, 1000, false);
Check(GunVisualState.Select(true, visual.Loaded, false, "installed", "empty") == "installed", "Confirmed cell insertion must change to the installed image");
visual.Set(true, 0, true);
Check(GunVisualState.Select(true, visual.Loaded, false, "installed", "empty") == "installed", "An exhausted cell remains visually installed until ejected");
var visualSaved = new EnergyState();
Check(visualSaved.Restore(visual.Capture()) && GunVisualState.Select(true, visualSaved.Loaded, false, "installed", "empty") == "installed", "Restored empty installed cells must retain their installed image");
visual.Set(false, 0, false);
Check(GunVisualState.Select(true, visual.Loaded, true, "installed", "empty") == "empty", "Ejection must override stale native flags with the no-magazine image");
Check(GunVisualState.Select(false, false, true, "installed", "empty") == "installed"
    && GunVisualState.Select(false, true, false, "installed", "empty") == "empty", "Pending custom-state reads must use the native replicated magazine flag");

var craftTool = new Item { id = Registration.WrenchId, condition = 1 };
var dragonRequirement = new RecipeItem { quality = new CraftingQuality(Registration.DragonQuality, 100) };
Check(DragonizationRules.Cost(100) == 0.01f, "100 dragonization units must debit one percent durability");
for (int craft = 0; craft < 100; craft++)
{
    Check(!DragonizationPatches.Spend(dragonRequirement, craftTool), "Dragonization must replace the native debit rather than pay twice");
    Check(Math.Abs(craftTool.condition - (99 - craft) * 0.01f) < 0.00001f, "Every craft must consume the requested one percent");
}
Check(craftTool.condition == 0 && !DragonizationRules.TrySpend(craftTool.condition, 100, out _), "Full durability must support exactly one hundred dragonization crafts");
craftTool.condition = 0.009f;
Check(!DragonizationRules.TrySpend(craftTool.condition, 100, out var tooLow) && tooLow == craftTool.condition, "Less than one percent must be rejected without changing durability");
craftTool.condition = 0.01f;
Check(!DragonizationPatches.Spend(dragonRequirement, craftTool) && craftTool.condition == 0, "Exactly one percent may pay for the final craft");
craftTool.condition = 0.5f;
Check(DragonizationPatches.Spend(new RecipeItem { quality = new CraftingQuality("cutting", 100) }, craftTool) && craftTool.condition == 0.5f,
    "Cutting, hammering and modification must keep native consumption behavior");
Check(DragonizationPatches.Spend(dragonRequirement, new Item { id = "other-mod-tool" }), "Other mods' tools must retain native quality accounting");
Check(DragonizationPatches.Spend(new RecipeItem { specific = true, quality = dragonRequirement.quality }, craftTool), "Consumed material ingredients must not be mistaken for retained tools");
foreach (float badCondition in new[] { -1f, float.NaN, float.PositiveInfinity })
    Check(!DragonizationRules.TrySpend(badCondition, 100, out _), "Invalid tool durability must fail safely");
var wingLease = new WingFlightLease();
Check(!wingLease.IsFlying(10), "Remote wings must start folded");
Check(wingLease.Apply(1, true, 10) && wingLease.IsFlying(10.5), "An authenticated fresh flight update must unfold remote wings");
Check(!wingLease.Apply(1, false, 10.5) && wingLease.IsFlying(11), "Duplicate/reordered wing packets must not override the current pose");
Check(!wingLease.IsFlying(12.01), "Lost flight updates must expire to the folded pose");
Check(wingLease.Apply(2, true, 13) && wingLease.IsFlying(14), "A newer heartbeat may reopen the wings");
Check(wingLease.Apply(3, false, 14) && !wingLease.IsFlying(14), "Stopping flight must close the wings immediately on receipt");
Check(!wingLease.Apply(2, true, 14.1) && !wingLease.IsFlying(14.1), "A delayed open packet must never reopen wings after a newer stop");
Check(!wingLease.Apply(4, true, double.NaN) && !wingLease.IsFlying(14.1), "Invalid timestamps must not change the remote pose");
wingLease.Reset();
Check(!wingLease.IsFlying(0) && wingLease.Apply(0, true, 0), "Changing connections must reset the old wing sequence");
var wingBelt = new GravityBelt();
var wingRenderer = new BeltWingVisual();
wingRenderer.Initialize(wingBelt);
Check(wingRenderer.TestRenderer.sprite == Registration.WingFoldedSprite && wingRenderer.transform.localRotation.Z == 0,
    "Creating wearable wing sprites must show the compact folded image");
var wingTick = typeof(BeltWingVisual).GetMethod("LateUpdate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
wingBelt.WingsOpen = true;
wingTick.Invoke(wingRenderer, null);
Check(wingRenderer.TestRenderer.sprite == Registration.WingOpenSprite && wingRenderer.transform.localRotation.Z == 20,
    "Actual flight must switch to the flatter open sprite");
wingBelt.WingsOpen = false;
wingTick.Invoke(wingRenderer, null);
Check(wingRenderer.TestRenderer.sprite == Registration.WingFoldedSprite && wingRenderer.transform.localRotation.Z == 0,
    "Stopping, exhausting the cell or losing confirmation must restore folded wings");
foreach (int source in new[] { 0, 1, 2, 4, 8, 16, 32, 64, 112, 128, 256, 511 })
foreach (string category in new[] { "tool", "utility", "container", "food", "medical", "custom", "ammo", "", null! })
foreach (double roll in new[] { 0d, 0.0005, 0.001, 0.0015, 0.005, 0.006, 0.5, 1 })
{
    string chosen = LootRules.Select(source, category, roll);
    Check(chosen != Registration.MatterId && chosen != CannonAmmoState.CarrierId, "Manufactured matter and crafted shells must never be selected from any natural pool");
    Check(chosen != Registration.DragonId || (LootRules.IsTrader(source) || source == 8) && category == "tool", "Dragon must appear only in merchant or supply-crate tool slots outside its boss reward");
    Check(chosen == null || LootRules.Eligible(source, category), "Unsupported sources and mismatched categories must preserve the original draw");
    if (chosen == Registration.WrenchId) Check(category == "tool", "Wrench must match a tool slot");
    if (chosen == Registration.BeltId) Check(category == "container", "Belt must match a container slot");
    if (chosen == Registration.GunId || chosen == Registration.ChargeGunId || chosen == Registration.CellId) Check(category == "utility", "Energy equipment must match utility slots");
}
foreach (double invalidRoll in new[] { -1d, double.NaN, double.PositiveInfinity })
    Check(LootRules.Select(16, "tool", invalidRoll) == null, "Invalid probability inputs must never generate loot");
var counts = new Dictionary<string, int>();
void CountLoot(string id) { if (id != null) counts[id] = counts.GetValueOrDefault(id) + 1; }
for (int i = 0; i < 1000000; i++)
{
    double roll = (i + 0.5) / 1000000;
    CountLoot(LootRules.Select(16, "tool", roll));
    CountLoot(LootRules.Select(8, "container", roll));
    CountLoot(LootRules.Select(128, "utility", roll));
}
Check(counts[Registration.DragonId] == 5000 && counts[Registration.WrenchId] == 1000 && counts[Registration.PickaxeId] == 1000, "Merchant tool-slot rates must be dragon 0.5 percent and wrench 0.1 percent");
Check(counts[Registration.BeltId] == 1000 && counts[Registration.GunId] == 500 && counts[Registration.ChargeGunId] == 500 && counts[Registration.CellId] == 1000,
    "Matching slots must produce belt/cell 0.1 percent and gun 0.05 percent");
Check(counts[CannonAmmoState.BioId]==1000 && counts[CannonAmmoState.DemolitionId]==1000 && counts[CannonAmmoState.ExplosiveId]==1000,
    "Each ammunition type must occupy a 0.1 percent interval of a compatible utility draw");
int crateDragons=0;for(int rollIndex=0;rollIndex<100000;rollIndex++)if(LootRules.Select(8,"tool",(rollIndex+0.5)/100000)==Registration.DragonId)crateDragons++;
Check(crateDragons==100,"Supply-crate dragon chance must be exactly 0.1 percent per tool-category draw");
var bossWon = new BossDropGate();
Check(bossWon.TryRoll(0.249999) && !bossWon.TryRoll(0), "A winning boss may drop only once");
var bossLost = new BossDropGate();
Check(!bossLost.TryRoll(0.25) && !bossLost.TryRoll(0), "A failed boss roll must never be rerolled on repeated death callbacks");
int bossWins = 0;
for (int i = 0; i < 10000; i++) if (new BossDropGate().TryRoll((i + 0.5) / 10000)) bossWins++;
Check(bossWins == 2500, "Elder dragon reward must have exactly a 25 percent roll window");
string root = args.Length == 0 ? Path.GetFullPath(".") : args[0];
using (var assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly(Path.Combine(root, "src/DDEN.LoongNova/bin/Release/DDEN.LoongNova.dll")))
{
    UtilityAmmoChecks.Inspect(assembly,Check);
    MedicalChecks.Inspect(assembly,Check);
    RegrowthChecks.Inspect(assembly,Check);
    EconomyChecks.Inspect(assembly,Check);
    ContentResourceChecks.Inspect(assembly,Check,root);
    RevivalChecks.Inspect(assembly,Check,root);
    var types = assembly.MainModule.Types;
    var pluginType = types.Single(type => type.Name == "LoongNovaPlugin");
    Check(assembly.Name.Version == new Version(1, 1, 0, 0), "LoongNova assembly must be version 1.1.0.0");
    Check(pluginType.CustomAttributes.Single(a => a.AttributeType.Name == "BepInPlugin").ConstructorArguments[2].Value as string == "1.1.0"
        && pluginType.Methods.Single(m => m.Name == "get_Version").Body.Instructions.Any(i => i.Operand is string s && s == "1.1.0"),
        "Plugin loader and manager must agree on LoongNova 1.1.0");
    var controller = types.Single(type => type.Name == "DragonGun");
    foreach (string entry in new[] { "Request", "Update" })
        Check(controller.Methods.Single(m => m.Name == entry).Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "IsGun"), "Shared gun controller must actually accept the prototype at entry: " + entry);
    var registerType = types.Single(t => t.Name == "Registration");
    var chargeBuilder = registerType.Methods.Single(m => m.Name == "RegisterChargeGun");
    Check(chargeBuilder.Body.Instructions.Any(i => i.Operand is string s && s == "dden.loongnova.charge-prototype")
        && registerType.Methods.Single(m => m.Name == "Register").Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "RegisterChargeGun"), "Functional prototype must be registered in the live plugin path");
    var gunPredicate = registerType.Methods.Single(m => m.Name == "IsGun");
    foreach (string id in new[] { "dden.loongnova.dragon-scorch", "dden.loongnova.charge-prototype" })
        Check(gunPredicate.Body.Instructions.Any(i => i.Operand is string s && s == id), "Gun predicate must explicitly include each supported ID");
    foreach (string typeName in new[] { "GunSaveProvider", "MultiplayerCombat", "WeaponPatches", "WeaponService" })
        Check(types.Single(t => t.Name == typeName).Methods.Where(m => m.HasBody).Any(m => m.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference call && call.Name == "IsGun")), "Prototype must share save/authority/native suppression: " + typeName);
    var chargeHud = types.Single(t => t.Name == "ChargeHud");
    Check(types.Single(t => t.Name == "ChargeHudPatch").CustomAttributes.Any(a => a.ConstructorArguments.Any(arg => arg.Value as string == "HandleGunMenu")), "Charge HUD must attach to the actual native gun HUD update");
    Check(chargeHud.Methods.Single(m => m.Name == "Create").Body.Instructions.Any(i => i.Operand is Mono.Cecil.FieldReference f && f.Name == "gunBindText")
        && !chargeHud.Methods.Single(m => m.Name == "Create").Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "Instantiate"), "HUD must use a custom white frame without cloning the original layout");
    Check(chargeHud.Methods.Single(m => m.Name == "OnDestroy").HasBody, "Native HUD child must be cleaned up with the camera");
    Check(!chargeHud.Methods.Where(m => m.HasBody).Any(m => m.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference call && call.Name == "set_text")),
        "The custom charge HUD must contain no text writes, time labels, status captions or prompts");
    Check(chargeHud.Methods.Single(m => m.Name == "Draw").Body.Instructions.Any(i => i.Operand is float alpha && alpha == 0.35f),
        "A loaded but unchambered cartridge must be represented by a faded icon");
    var rackBridge = types.Single(t => t.Name == "CannonRackControls");
    Check(rackBridge.CustomAttributes.Any(a => a.ConstructorArguments.Any(arg => arg.Value as string == "GunRack"))
        && rackBridge.Methods.Single(m => m.Name == "Rack").Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "Request"),
        "The native camera rack action must reach the authenticated cannon request channel");
    Check(types.Single(t => t.Name == "LoongNovaPlugin").Methods.Single(m => m.Name == "Awake").Body.Instructions.Any(i => i.Operand is Mono.Cecil.TypeReference t && t.Name == "CannonRackControls")
        && types.Single(t => t.Name == "ChargeHudPatch").Methods.Single(m => m.Name == "Postfix").Body.Instructions.Any(i => i.Operand is Mono.Cecil.GenericInstanceMethod m && m.Name == "AddComponent" && m.GenericArguments.Any(t => t.Name == "CannonRackControls")),
        "The production plugin must install the rack patch and attach its button-state lifecycle");
    Check(chargeBuilder.Body.Instructions.Any(i => i.Operand is string s && s == "universal-ammo-carrier.png")
        && chargeBuilder.Body.Instructions.Any(i => i.Operand is string s && s == "demolition-round-v2.png")
        ,
        "Both compact demolition ammo and its independent matching spent casing must be used by live registration");

    Check(!controller.Fields.Any(f=>f.Name=="_transaction") && controller.Fields.Any(f=>f.FieldType.Name=="PendingActions"),
        "Gun loading must derive its lock from live requests instead of retaining an independent transaction flag");
    foreach(string ownerType in new[]{"DragonGun","GravityBelt"})
    {
        var owner=types.Single(t=>t.Name==ownerType);
        Check(owner.Methods.Single(m=>m.Name=="OnDisable").Body.Instructions.Any(i=>i.Operand is Mono.Cecil.MethodReference m&&m.DeclaringType.Name=="PendingActions"&&m.Name=="Clear"),
            "Disabled gun/belt must release all pending requests: "+ownerType);
        Check(owner.Methods.Single(m=>m.Name=="Update").Body.Instructions.Any(i=>i.Operand is Mono.Cecil.MethodReference m&&m.DeclaringType.Name=="PendingActions"&&m.Name=="Expire"),
            "Unanswered gun/belt operations must expire: "+ownerType);
        var callbacks=owner.NestedTypes.SelectMany(t=>t.Methods).Where(m=>m.HasBody&&m.Name.Contains("Request"));
        Check(callbacks.Any(m=>m.Body.Instructions.Any(i=>i.Operand is Mono.Cecil.MethodReference call&&call.DeclaringType.Name=="PendingActions"&&call.Name=="Finish")),
            "The actual response callback must release its request before parsing the reply: "+ownerType);
    }
    foreach(string ownerType in new[]{"WeaponService","CannonService","GravityBelt"})
    {
        var install=types.Single(t=>t.Name==ownerType).Methods.Single(m=>m.Name=="Execute");
        Check(!install.Body.Instructions.Any(i=>i.Operand is Mono.Cecil.MethodReference m
            &&(m.Name=="UnloadItem"||m.Name=="DropItem"||m.Name=="get_parent"||m.Name=="SetActive")),
            "Ammo/cell installation must match native in-place consumption without detachment, root-parent or activation checks: "+ownerType);
        Check(install.Body.Instructions.Any(i=>i.Operand is Mono.Cecil.MethodReference m&&m.Name=="Destroy"),
            "Ammo/cell installation must consume the source through the native destruction lifecycle: "+ownerType);
    }

    var cannonService = types.Single(t => t.Name == "CannonService");
    var cannonFire = cannonService.Methods.Single(m => m.Name == "Fire");
    Check(!cannonService.Methods.Where(m => m.HasBody).Any(m => m.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference call
        && call.DeclaringType.Name == "EnergyState" && (call.Name == "TrySpend" || call.Name == "TrySpendShot"))), "Cannon firing must never consume battery energy");
    Check(cannonFire.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "DamageMultiplier")
        && cannonFire.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "Profile"), "Weapon damage must be calculated from registered ammo metadata");
    Check(cannonFire.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "Consume")
        && !cannonFire.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "InstantiateReturn")
        && cannonService.Methods.Single(m => m.Name == "Execute").Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "CreateReturnedRound"),
        "Firing must keep one spent shell inside; only manual rack/unload may instantiate returned ammunition");
    Check(controller.Methods.Single(m => m.Name == "UpdateCharge").Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "ForceCommitted"),
        "Host update must force full-charge and interrupted committed shots without waiting for a client release packet");
    Check(chargeHud.Methods.Single(m => m.Name == "Draw").Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "RoundSprite"),
        "HUD ammunition icon must follow the actual chambered round rather than a fixed image");
    var roundRegistration = registerType.Methods.Single(m => m.Name == "RegisterRound");
    Check(roundRegistration.Body.Instructions.Any(i => i.Operand is Mono.Cecil.FieldReference f && f.Name == "CustomData")
        && roundRegistration.Body.Instructions.Any(i => i.Operand is string s && s == CannonRoundRegistry.DataKey),
        "Special parameters must be attached to the ammunition's own CCL registration data");
    var blastType = types.Single(t => t.Name == "CannonBlast");
    Check(blastType.Methods.Single(m => m.Name == "Fire").Body.Instructions.Count(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "RaycastAll") == 1,
        "Explosive ammo must use exactly one impact ray instead of adding a second ray to native bullet damage");
    Check(blastType.Methods.Single(m => m.Name == "Explode").Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "CreateExplosion")
        && cannonFire.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.DeclaringType.Name == "CannonBlast" && m.Name == "Fire"),
        "The live firing path must invoke the native explosion API for multiplayer terrain/effect synchronization");
    Check(chargeBuilder.Body.Instructions.Any(i => i.Operand is string s && s == CannonAmmoState.ExplosiveId),
        "Explosive ammunition must be registered by the actual plugin initialization");
    var chargeAudio = types.Single(t => t.Name == "CannonChargeAudio");
    Check(chargeBuilder.Body.Instructions.Any(i => i.Operand is Mono.Cecil.GenericInstanceMethod m && m.GenericArguments.Any(t => t.Name == "CannonChargeAudio"))
        && chargeAudio.Methods.Single(m => m.Name == "Update").Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "ListenerGain"),
        "The actual cannon must attach charging audio and use the tested owner/other-player attenuation");
    Check(chargeAudio.Methods.Single(m => m.Name == "Broadcast").Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "Broadcast")
        && types.Single(t => t.Name == "PulseVisual").Methods.Single(m => m.Name == "Play").Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.DeclaringType.Name == "CannonChargeAudio" && m.Name == "Receive"),
        "Remote charging events must travel over the registered server broadcast channel");

    var sightPatch = types.Single(t => t.Name == "CannonSightPatch");
    Check(sightPatch.Methods.Single(m => m.Name == "Active").Body.Instructions.Any(i => i.Operand is Mono.Cecil.FieldReference f && f.Name == "main")
        && sightPatch.Methods.Single(m => m.Name == "Active").Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "get_Ready"),
        "Sight must require the local main camera and a usable chambered round");
    Check(sightPatch.Methods.Single(m => m.Name == "Finalizer").Body.Instructions.Any(i => i.Operand is Mono.Cecil.FieldReference f && f.Name == "zoomTime"),
        "Native zoom effect must be scoped and restore the original goggles timer");
    Check((float)controller.Fields.Single(field => field.Name == "ShotInterval").Constant == 0.15f, "Compiled shot interval must be 0.15 seconds");
    var energy = types.Single(type => type.Name == "EnergyState");
    Check((float)energy.Fields.Single(field => field.Name == "ShotCost").Constant == 10, "Compiled shot cost must be ten");
    var execution = types.Single(type => type.Name == "WeaponService").Methods.Single(method => method.Name == "Execute");
    float DamageBeforeStore(Mono.Cecil.MethodDefinition method, string fieldName)
    {
        var code = method.Body.Instructions;
        int store = code.ToList().FindIndex(instruction => instruction.OpCode == Mono.Cecil.Cil.OpCodes.Stfld
            && instruction.Operand is Mono.Cecil.FieldReference field && field.Name == fieldName);
        for (int i = store - 1; i >= Math.Max(0, store - 4); i--)
        {
            if (code[i].Operand is float value) return value;
            if (code[i].OpCode == Mono.Cecil.Cil.OpCodes.Ldc_I4 && code[i].Operand is int integer) return integer;
        }
        throw new Exception("Missing compiled damage field " + fieldName);
    }
    var plugin = types.Single(type => type.Name == "LoongNovaPlugin").Methods.Single(method => method.Name == "Awake");
    Check(plugin.Body.Instructions.Any(i => i.Operand is Mono.Cecil.TypeReference t && t.Name == "BeltInteractionPatches"),
        "The drag handler must actually be registered during plugin initialization");
    var refresh = types.Single(type => type.Name == "GravityBelt").Methods.Single(method => method.Name == "RefreshEncumbrance");
    Check(refresh.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "get_ChargeFraction")
        && refresh.Body.Instructions.Any(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Stfld && i.Operand is Mono.Cecil.FieldReference f && f.Name == "condition"),
        "Native item percentage must derive from authoritative battery charge");
    var fixedUpdate = types.Single(type => type.Name == "GravityBelt").Methods.Single(method => method.Name == "FixedUpdate");
    Check(!fixedUpdate.Body.Instructions.Any(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Stfld && i.Operand is Mono.Cecil.FieldReference f && f.Name == "condition"),
        "FixedUpdate must never reset the battery percentage to full durability");
    var tooltip = types.Single(type => type.Name == "EquipmentPatches").Methods.Single(method => method.Name == "ItemEnergyHint");
    foreach (string status in new[] { "beltSocketEmpty", "beltSocketLoaded" })
        Check(tooltip.Body.Instructions.Any(i => i.Operand is string s && s == status), "Tooltip must distinguish each socket state: " + status);

    Check(tooltip.Body.Instructions.Any(i => i.Operand is string s && s == "cellEnergy")
        && tooltip.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "CellHint")
        && tooltip.Body.Instructions.Any(i => i.Operand is Mono.Cecil.FieldReference f && f.Name == "condition"),
        "Native hover descriptions must include both loose-cell charge and the gun's actual installed-cell hint");
    var gunHint = controller.Methods.Single(m => m.Name == "CellHint");
    Check(gunHint.Body.Instructions.Any(i => i.Operand is Mono.Cecil.FieldReference f && f.Name == "Energy")
        && !gunHint.Body.Instructions.Any(i => i.Operand is Mono.Cecil.FieldReference f && f.Name == "condition")
        && !gunHint.Body.Instructions.Any(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Stfld && i.Operand is Mono.Cecil.FieldReference f && f.Name == "Energy"),
        "Gun information must read embedded-cell energy without equating it with gun wear or changing charge");
    Check(gunHint.Body.Instructions.Any(i => i.Operand is Mono.Cecil.FieldReference f && f.Name == "Known")
        && gunHint.Body.Instructions.Any(i => i.Operand is string s && s == "waiting")
        && gunHint.Body.Instructions.Any(i => i.Operand is Mono.Cecil.FieldReference f && f.Name == "_nextRead")
        && gunHint.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "Carries"),
        "Client gun information must wait for known state and throttle reads through the existing carried-item boundary");
    foreach (string key in new[] { "beltSocketEmpty", "beltSocketLoaded" })
        Check(gunHint.Body.Instructions.Any(i => i.Operand is string s && s == key), "Gun hint must distinguish empty sockets and installed cells including depleted shells");

    // Inspect this machine's actual game API and the native container-before-combine order.
    using var game = Mono.Cecil.AssemblyDefinition.ReadAssembly(@"L:\SteamLibrary\steamapps\common\Casualties Unknown\CasualtiesUnknown_Data\Managed\Assembly-CSharp.dll");
    var nativeDrag = game.MainModule.Types.Single(t => t.Name == "PlayerCamera").Methods.Single(m => m.Name == "TryPerformInventoryAction");
    Check(nativeDrag.Parameters.Count == 2 && nativeDrag.Parameters[0].ParameterType.Name == "RaycastResult"
        && nativeDrag.ReturnType.FullName == "System.Boolean", "UI interception must match the installed game's real method signature");
    var nativeCalls = nativeDrag.Body.Instructions.Where(i => i.Operand is Mono.Cecil.MethodReference)
        .Select(i => ((Mono.Cecil.MethodReference)i.Operand).Name).ToList();
    Check(nativeCalls.IndexOf("LoadItem") >= 0 && nativeCalls.IndexOf("LoadItem") < nativeCalls.IndexOf("CanCombine"),
        "Regression fixture must cover the real native container branch that swallows installation");
    var equipmentPatches = types.Single(t => t.Name == "EquipmentPatches");
    var jumping = equipmentPatches.Methods.Single(m => m.Name == "AfterJump");
    Check(jumping.Parameters.Any(p => p.Name == "___firstWallJump" && p.ParameterType.IsByReference)
        && jumping.Body.Instructions.Any(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Stind_I1),
        "Climbing-claw behavior must re-arm native wall jumps");
    Check(!jumping.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "get_Powered"),
        "Passive climbing-claw behavior must not be gated by battery charge");
    var flying = types.Single(t => t.Name == "GravityBelt").Methods.Single(m => m.Name == "ApplyFlight");
    Check(flying.Body.Instructions.Any(i => i.Operand is float f && f == 198000f)
        && flying.Body.Instructions.Any(i => i.Operand is float f && f == 60000f)
        && flying.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "MoveRotation"),
        "Dragon-wing flight must preserve native jetpack vertical thrust, steering and ragdoll righting");
    var wrench = types.Single(t => t.Name == "Registration").Methods.Single(m => m.Name == "UseWrench");
    Check(DamageBeforeStore(wrench, "structuralDamage") == 75 && DamageBeforeStore(wrench, "damage") == 50,
        "Wrench must send the requested base mining and creature damage into native Attack");
    Check(DamageBeforeStore(wrench, "cooldown") == 0.5f, "Wrench must have a fast base swing interval");
    Check(wrench.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "Attack"),
        "Wrench must use native melee/mining and multiplayer damage paths");
    var pickUse = types.Single(t => t.Name == "Registration").Methods.Single(m => m.Name == "UsePickaxe");
    Check(DamageBeforeStore(pickUse, "structuralDamage") == 375 && DamageBeforeStore(pickUse, "damage") == 10
        && DamageBeforeStore(pickUse, "cooldown") == 0.1f, "Pickaxe must use 375/10 damage and 0.1s base swing");
    Check(pickUse.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.DeclaringType.Name == "PickaxeWear"), "Real successful swings must debit the tested 1200-use wear policy");
    var deferred = types.Single(t => t.Name == "DeferredEtWrench");
    Check(!assembly.MainModule.AssemblyReferences.Any(r => r.Name == "EnhancedTools"), "Dormant ET support must not create a hard dependency");
    foreach (var type in types.Where(t => t.Name != "DeferredEtWrench" && t.Name != "DeferredEtWrenchBehavior"))
    foreach (var method in type.Methods.Where(m => m.HasBody))
        Check(!method.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.DeclaringType.Name == "DeferredEtWrench"),
            "Deferred wrench builders must never be called from active runtime code");
    foreach (var method in deferred.Methods.Where(m => m.HasBody))
        Check(!method.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "Register"),
            "Deferred wrench must never register its item or recipe");
    var deferredRecipe = deferred.Methods.Single(m => m.Name == "CreateRecipe");
    Check(deferredRecipe.Body.Instructions.Any(i => i.Operand is string s && s == "engineering"),
        "Reserved recipe must retain the requested engineering ingredient");
    var page = types.Single(t => t.Name == "ModuleItemPage");
    var cards = page.Methods.Single(m => m.Name == ".cctor");
    var catalogIds = cards.Body.Instructions.Where(i => i.Operand is string s && s.StartsWith("dden.loongnova.")).Select(i => (string)i.Operand).ToList();
    Check(catalogIds.Count == 20 && catalogIds.Contains(CannonAmmoState.RevivalId) && catalogIds.Contains(MedicalCatalog.EmptyId) && !catalogIds.Contains("dden.loongnova.dragon-claw-refitted-wrench"),
        "Catalog must include the empty ampoule among twenty active items and exclude the deferred wrench");
    var drawing = page.Methods.Single(m => m.Name == "Draw");
    Check(drawing.Body.ExceptionHandlers.Any(h => h.HandlerType == Mono.Cecil.Cil.ExceptionHandlerType.Finally),
        "Module page must always close its scroll view even if drawing fails");
    var feedback = types.Single(t => t.Name == "GravityBelt").Methods.Single(m => m.Name == "UpdateFeedback");
    Check(feedback.Body.Instructions.Any(i => i.Operand is string s && s == "jetpack")
        && feedback.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "get_clip")
        && feedback.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "set_clip"),
        "Flight audio must reuse the actual native jetpack clip");
    var lowWarning = types.Single(t => t.Name == "GravityBelt").Methods.Single(m => m.Name == "ShowLowEnergy");
    Check(lowWarning.Body.Instructions.Any(i => i.Operand is string s && s == "watch")
        && lowWarning.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "TalkDelayed"),
        "Low-energy warning must use the native electronic watch text and voice template");
    Check(lowWarning.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "SetParent"),
        "Electronic warning must follow an explicit head anchor rather than the waist or ground position");
    var beltUpdate = types.Single(t => t.Name == "GravityBelt").Methods.Single(m => m.Name == "Update");
    Check(beltUpdate.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "UpdateFeedback"),
        "Belt feedback must actually be called during updates");
    var disable = types.Single(t => t.Name == "GravityBelt").Methods.Single(m => m.Name == "OnDisable");
    Check(disable.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "Stop"),
        "Disabling or removing the belt must stop its looping flight audio");
    var definition = types.Single(type => type.Name == "Registration").Methods.Single(method => method.Name == "Register");
    Check(DamageBeforeStore(definition, "StructureDamage") == 625, "Registered structural damage must be 625");
    Check(DamageBeforeStore(definition, "AnimalDamage") == 225, "Registered animal damage must be 225");
    using var nativeLoadMp=Mono.Cecil.AssemblyDefinition.ReadAssembly(@"L:\SteamLibrary\steamapps\common\Casualties Unknown\BepInEx\plugins\Together\CasualtiesMP.dll");
    var nativeReach=nativeLoadMp.MainModule.Types.Single(t=>t.FullName=="Together.ItemSync").Methods.Single(m=>m.Name=="CheckIfBodyReachThisItem"&&m.Parameters.Count==4&&m.Parameters[0].ParameterType.Name=="Item");
    Check((float)nativeReach.Parameters[2].Constant==20f&&(bool)nativeReach.Parameters[3].Constant==false,
        "Loading must match the installed MP combine/battery method's defaults exactly");
    var normalization = controller.Methods.Single(method => method.Name == "Normalize");
    Check(DamageBeforeStore(normalization, "structureDamage") == 625, "Runtime structural damage must be 625");
    Check(DamageBeforeStore(normalization, "animalDamage") == 225, "Runtime animal damage must be 225");
    Check(DamageBeforeStore(execution, "structureDamage") == 625, "Actual shot structural damage must be 625");
    Check(DamageBeforeStore(execution, "animalDamage") == 225, "Actual shot animal damage must be 225");
    var instructions = execution.Body.Instructions;
    int tinnitus = instructions.ToList().FindIndex(instruction => instruction.Operand is Mono.Cecil.FieldReference field && field.Name == "doTinnitus");
    Check(tinnitus > 0 && instructions[tinnitus - 1].OpCode == Mono.Cecil.Cil.OpCodes.Ldc_I4_0, "Actual firing entry must disable tinnitus");
    Check(instructions.Any(instruction => instruction.Operand is float value && value == 0.002f), "Actual shot must deduct 0.2 percent durability");
    foreach (var type in types)
    foreach (var method in type.Methods.Where(method => method.HasBody))
    {
        Check(!method.Body.Instructions.Any(instruction => instruction.OpCode == Mono.Cecil.Cil.OpCodes.Stfld
            && instruction.Operand is Mono.Cecil.FieldReference field && field.Name == "hearingLoss"), "Weapon must never write hearing damage");
        Check(type.Name == "CannonBlast" || type.Name == "CannonBreach" || type.Name == "CannonRevival" || !method.Body.Instructions.Any(instruction => instruction.Operand is Mono.Cecil.MethodReference call
            && call.DeclaringType.FullName == "UnityEngine.Physics2D" && call.Name.Contains("Raycast")), "Only dedicated explosive/breach/revival impact paths may perform their own raycasts; ordinary weapons and sights reuse the native query");
    }
    var register = types.Single(type => type.Name == "Registration").Methods.Single(method => method.Name == "RegisterRecipes");
    var fluids = new List<float>();
    for (int i = 1; i < register.Body.Instructions.Count; i++)
        if (register.Body.Instructions[i].Operand is Mono.Cecil.MethodReference call && call.Name == "Fluid")
            fluids.Add((float)register.Body.Instructions[i - 1].Operand);
    Check(fluids.SequenceEqual(new[] { 50f, 100f, 160f, 100f }), "Compiled matter/cell/refill/wrench fluid amounts must match the requested recipes");
    Check(register.Body.Instructions.Count(instruction => instruction.Operand is Mono.Cecil.MethodReference call && call.Name == "AddRecipe") == 7, "Exactly seven content recipes must be registered");
    int IntConstant(Mono.Cecil.Cil.Instruction instruction) => instruction.OpCode.Code switch
    {
        Mono.Cecil.Cil.Code.Ldc_I4 => (int)instruction.Operand,
        Mono.Cecil.Cil.Code.Ldc_I4_S => (sbyte)instruction.Operand,
        Mono.Cecil.Cil.Code.Ldc_I4_0 => 0,
        Mono.Cecil.Cil.Code.Ldc_I4_1 => 1,
        Mono.Cecil.Cil.Code.Ldc_I4_2 => 2,
        Mono.Cecil.Cil.Code.Ldc_I4_3 => 3,
        Mono.Cecil.Cil.Code.Ldc_I4_4 => 4,
        Mono.Cecil.Cil.Code.Ldc_I4_5 => 5,
        Mono.Cecil.Cil.Code.Ldc_I4_6 => 6,
        Mono.Cecil.Cil.Code.Ldc_I4_7 => 7,
        Mono.Cecil.Cil.Code.Ldc_I4_8 => 8,
        _ => throw new Exception("Expected compiled integer constant")
    };
    Check(register.Body.Instructions.Count(i => i.Operand is string s && s == "loongnova.dragonization") == 6, "All six manufacture/refill dragonization requirements must use the shared 100-unit tool builder");
    var intelligence = new List<int>();
    for (int i = 1; i < register.Body.Instructions.Count; i++)
        if (register.Body.Instructions[i].Operand is Mono.Cecil.MethodReference call && call.Name == "AddRecipe")
            intelligence.Add(IntConstant(register.Body.Instructions[i - 2]));
    Check(intelligence.SequenceEqual(new[] { 16, 18, 18, 18, 18, 18, 14 }), "Recipe intelligence must be matter16, equipment18 and wrench14");
    var equipment = types.Single(type => type.Name == "Registration").Methods.Single(method => method.Name == "RegisterEquipment");
    var values = new List<int>();
    for (int i = 1; i < equipment.Body.Instructions.Count; i++)
        if (equipment.Body.Instructions[i].OpCode == Mono.Cecil.Cil.OpCodes.Stfld
            && equipment.Body.Instructions[i].Operand is Mono.Cecil.FieldReference field && field.Name == "value")
            values.Add(IntConstant(equipment.Body.Instructions[i - 1]));
    Check(!equipment.Body.Instructions.Any(i => i.Operand is string s && s == "modification"), "Active wrench registration must not grant modification quality");
    Check(equipment.Body.Instructions.Any(i => i.Operand is float f && f == 3f), "Wrench must request three world units independently of native sprite width and PNG resolution");
    Check(register.Body.Instructions.Any(i => i.Operand is string s && s == "dden.loongnova.rock-dragon-pickaxe"), "Pickaxe recipe must actually be registered");
    Check(register.Body.Instructions.Where((i, index) => i.Operand is Mono.Cecil.MethodReference m && m.Name == "AddRecipe"
        && register.Body.Instructions[index-1].OpCode == Mono.Cecil.Cil.OpCodes.Ldc_I4_1).Count() == 1,
        "Exactly the refill recipe must opt into native same-ID repair matching");
    var recipeBuilder = types.Single(t => t.Name == "Registration").Methods.Single(m => m.Name == "AddRecipe");
    Check(recipeBuilder.Body.Instructions.Any(i => i.Operand is Mono.Cecil.FieldReference f && f.Name == "isRepair"), "Recipe builder must preserve the refill repair flag for CCL same-ID matching");
    foreach (var resource in assembly.MainModule.Resources.OfType<Mono.Cecil.EmbeddedResource>().Where(r => r.Name.EndsWith(".png")))
    {
        byte[] png = resource.GetResourceData();
        int ReadDimension(int offset) => (png[offset] << 24) | (png[offset+1] << 16) | (png[offset+2] << 8) | png[offset+3];
        Check(ReadDimension(16) <= 64 && ReadDimension(20) <= 64 && png.Length < 30000, "Embedded art must be real small pixel sprites, not high-resolution design originals: " + resource.Name);
    }
    Check(register.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.DeclaringType.Name == "ArsenalRecipes" && m.Name == "Create"),
        "The live recipe registration must include the eleven arsenal/medical recipes in addition to the original seven");
    int roundValueStore=roundRegistration.Body.Instructions.ToList().FindIndex(i=>i.Operand is Mono.Cecil.FieldReference f&&f.Name=="value");
    Check(roundRegistration.Body.Instructions[roundValueStore-1].Operand is Mono.Cecil.ParameterDefinition valueParameter && valueParameter.Name=="value"
        && (int)roundRegistration.Parameters.Single(p=>p.Name=="value").Constant==10
        && (float)roundRegistration.Parameters.Single(p=>p.Name=="weight").Constant==0.3f,
        "Ordinary rounds must default to value ten and 0.3u, while specialized registrations may supply their requested price");
    var chargeValues=new List<int>();
    for(int index=1;index<chargeBuilder.Body.Instructions.Count;index++)
        if(chargeBuilder.Body.Instructions[index].Operand is Mono.Cecil.FieldReference f && f.Name=="value")
            chargeValues.Add(IntConstant(chargeBuilder.Body.Instructions[index-1]));
    Check(chargeValues.SequenceEqual(new[]{40,0}) && chargeBuilder.Body.Instructions.Any(i=>i.Operand is Mono.Cecil.FieldReference f&&f.Name=="untradeable")
        && chargeBuilder.Body.Instructions.Any(i=>i.Operand is float f&&f==0.5f),
        "Live cannon/shell values must be 40/0; shells cannot trade and explosive rounds must weigh 0.5u");
    var spawnBonus=types.Single(t=>t.Name=="CannonSpawnAmmo");
    Check(spawnBonus.Methods.Single(m=>m.Name=="Give").Body.Instructions.Any(i=>i.Operand is Mono.Cecil.MethodReference m&&m.Name=="get_IsServer")
        && spawnBonus.Methods.Single(m=>m.Name=="Give").Body.Instructions.Any(i=>i.Operand is Mono.Cecil.MethodReference m&&m.Name=="TryGrantSpawnRound"),
        "Random ammunition grants must use the one-time save-backed transition on the authority only");
    Check(types.Single(t=>t.Name=="CannonPurchaseAmmoPatch").CustomAttributes.Any(a=>a.ConstructorArguments.Any(arg=>arg.Value as string=="AutoPickUpItem"))
        && types.Single(t=>t.Name=="CannonDropAmmoPatch").Methods.Single(m=>m.Name=="TargetMethod").Body.Instructions.Any(i=>i.Operand as string=="SpawnDrop"),
        "Purchase and physical crate-drop origins must both invoke the ammunition grant without altering crafting or inventory storage");
    var pluginAwake=types.Single(t=>t.Name=="LoongNovaPlugin").Methods.Single(m=>m.Name=="Awake");
    foreach(string patchName in new[]{"CannonPurchaseAmmoPatch","CannonDropAmmoPatch"})
        Check(pluginAwake.Body.Instructions.Any(i=>i.Operand is Mono.Cecil.TypeReference t&&t.Name==patchName),"Spawn ammunition hook must be installed: "+patchName);
    foreach(string serviceName in new[]{"WeaponService","CannonService","GravityBelt"})
        Check(types.Single(t=>t.Name==serviceName).Methods.Single(m=>m.Name=="Execute").Body.Instructions.Any(i=>i.Operand is Mono.Cecil.MethodReference m&&m.DeclaringType.Name=="NetworkAccess"&&m.Name=="CanAccess"),
            "Every gun/belt installation backend must accept validated reachable materials: "+serviceName);

    Check(values.SequenceEqual(new[] { 45, 8, 35, 35, 20 }), "Belt/dragon/wrench base prices must stay within their requested ranges");
    Check(!equipment.Body.Instructions.Any(instruction => instruction.Operand is Mono.Cecil.FieldReference field && field.Name == "Battery"), "Belt definition must never create a native battery or spawn with one");
    Check(DamageBeforeStore(equipment, "Capacity") == 6, "Belt storage capacity must remain six units");
    Check(equipment.Body.Instructions.Count(instruction => instruction.Operand is float value && value == 0.02f) == 0, "Legacy fractional pool weights must no longer inject the belt");
    Check(!assembly.MainModule.AssemblyReferences.Any(reference => reference.Name == "CasualtiesMP" || reference.Name == "Together"), "Optional multiplayer integration must not become a hard assembly reference");
    Check(equipment.Body.Instructions.Count(instruction => instruction.Operand is float value && value == 100000f) == 3, "Wrench must provide only three qualities with extremely low crafting wear");
    Check(equipment.Body.Instructions.Any(instruction => instruction.Operand is string text && text == "cutting")
        && equipment.Body.Instructions.Any(instruction => instruction.Operand is string text && text == "hammering")
        && equipment.Body.Instructions.Any(instruction => instruction.Operand is string text && text == "loongnova.dragonization"), "Wrench must provide cutting, hammering and dragonization");
    Check(assembly.MainModule.Resources.Count(resource => resource.Name.EndsWith(".png")) == 25, "Twenty-five small sprites must include the user's empty ampoule and new mysterious round while excluding superseded art");
    Check(assembly.MainModule.Resources.Any(r => r.Name.EndsWith("dragon-scorch-rifle-v2.png"))
        && definition.Body.Instructions.Any(i => i.Operand is string s && s == "dragon-scorch-rifle-v2.png"),
        "Gun registration must use the new sprite without an external magazine");
    Check(assembly.MainModule.Resources.Any(r => r.Name.EndsWith("dragon-scorch-rifle.png")) && !assembly.MainModule.Resources.Any(r => r.Name.EndsWith("gravity-belt.png")),
        "Installed gun image must be restored while superseded belt image stays excluded");
    foreach (string spriteState in new[] { "NormalSprite", "RackedSprite", "NormalSpriteNoMag", "RackedSpriteNoMag" })
        Check(definition.Body.Instructions.Any(i => i.Operand is Mono.Cecil.FieldReference f && f.Name == spriteState),
            "Every native gun display state must receive the new sprite: " + spriteState);
    foreach (string asset in new[] { "dragon-breath-matter.png", "dragon-claw-wrench.png", "gravity-belt-v2.png" })
    {
        Check(assembly.MainModule.Resources.Any(r => r.Name.EndsWith(asset)), "New sprite must be embedded: " + asset);
        Check(equipment.Body.Instructions.Any(i => i.Operand is string s && s == asset), "Equipment must use its new sprite: " + asset);
    }
    string SpriteGetterBeforeStore(string fieldName)
    {
        int store = definition.Body.Instructions.ToList().FindIndex(i => i.Operand is Mono.Cecil.FieldReference f && f.Name == fieldName);
        return definition.Body.Instructions.Take(store).Where(i => i.Operand is Mono.Cecil.MethodReference m
            && (m.Name == "get_GunSprite" || m.Name == "get_GunLoadedSprite")).Select(i => ((Mono.Cecil.MethodReference)i.Operand).Name).Last();
    }
    Check(SpriteGetterBeforeStore("NormalSprite") == "get_GunLoadedSprite" && SpriteGetterBeforeStore("RackedSprite") == "get_GunLoadedSprite",
        "Installed gun display states must receive the installed sprite");
    Check(SpriteGetterBeforeStore("NormalSpriteNoMag") == "get_GunSprite" && SpriteGetterBeforeStore("RackedSpriteNoMag") == "get_GunSprite",
        "Empty gun display states must receive the no-magazine sprite");
    var spriteRefresh = controller.Methods.Single(m => m.Name == "RefreshSprite");
    Check(spriteRefresh.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.DeclaringType.Name == "GunVisualState" && m.Name == "Select"),
        "Renderer must actually call the tested state selector instead of forcing one image");
    Check(spriteRefresh.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "SetItemSprite"),
        "Gun state changes must update CCL inventory icon overrides as well as world sprites");
    var toolBuilder = types.Single(t => t.Name == "Registration").Methods.Single(m => m.Name == "Tool");
    Check(toolBuilder.Body.Instructions.Any(i => i.Operand is float f && f == 100)
        && toolBuilder.Body.Instructions.Any(i => i.Operand is float f && f == 0.01f), "Recipe builder must encode 100 units and the one-percent matching threshold");
    Check(plugin.Body.Instructions.Any(i => i.Operand is Mono.Cecil.TypeReference tr && tr.Name == "DragonizationPatches"),
        "The exact crafting-wear patch must be installed");
    var wearDefinition = equipment.Body.Instructions;
    Check(wearDefinition.Any(i => i.Operand is string s && s == "UpTorso")
        && wearDefinition.Any(i => i.Operand is Mono.Cecil.FieldReference f && f.Name == "MultiWornSprites"),
        "Belt must register an independent back wing layer on the upper torso");
    Check(plugin.Body.Instructions.Any(i => i.Operand is Mono.Cecil.TypeReference tr && tr.Name == "BeltWearPatches"), "Wing layering patch must be installed");
    var backLayer = types.Single(t => t.Name == "BeltWearPatches").Methods.Single(m => m.Name == "BackLayer");
    Check(backLayer.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "set_sortingOrder"),
        "Wing renderer must be placed behind the body rather than using the waist's front layer");
    Check(assembly.MainModule.Resources.Any(r => r.Name.EndsWith("dragon-wing-waist-side.png"))
        && assembly.MainModule.Resources.Any(r => r.Name.EndsWith("dragon-wing-open-side.png")), "Both worn layers must use their side-profile art");
    Check(assembly.MainModule.Resources.Any(r => r.Name.EndsWith("dragon-wing-folded-side.png")), "Folded side-wing image must be embedded");
    Check(equipment.Body.Instructions.Any(i => i.Operand is float f && f == 1.05f)
        && equipment.Body.Instructions.Any(i => i.Operand is float f && f == 2.1f)
        && equipment.Body.Instructions.Any(i => i.Operand is float f && f == 0.25f),
        "Folded and open wings must be smaller than the old three-width sprite and restored to the earlier torso position");
    Check(backLayer.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.DeclaringType.Name == "BeltWingVisual" && m.Name == "Initialize"),
        "Native wearable recreation must initialize the runtime pose switcher");
    var beltController = types.Single(t => t.Name == "GravityBelt");
    var poseGetter = beltController.Methods.Single(m => m.Name == "get_WingsOpen");
    Check(poseGetter.Body.Instructions.Any(i => i.Operand is Mono.Cecil.FieldReference f && f.Name == "_flying")
        && poseGetter.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "get_Powered"),
        "Owned wings must open from actual powered flight rather than W alone or upward velocity");
    var action = beltController.Methods.Single(m => m.Name == "Execute");
    Check(action.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.DeclaringType.Name == "WorkReportBudget" && m.Name == "Apply")
        && action.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.DeclaringType.Name == "WingVisualSync" && m.Name == "Publish"),
        "Remote wing broadcasts must share authenticated accepted work requests");
    var sync = types.Single(t => t.Name == "WingVisualSync");
    Check(sync.Methods.Single(m => m.Name == "Publish").Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "get_IsServer"),
        "Only the server may broadcast other players' wing poses");
    var tick = types.Single(t => t.Name == "WeaponService").Methods.Single(m => m.Name == "Tick");
    Check(tick.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.DeclaringType.Name == "WingVisualSync" && m.Name == "Receive"),
        "Client cosmetic receiver must actually be registered");
    foreach (var setup in new[] { definition, equipment })
    {
        var code = setup.Body.Instructions;
        for (int i = 0; i < code.Count; i++)
            if (code[i].OpCode == Mono.Cecil.Cil.OpCodes.Stfld && code[i].Operand is Mono.Cecil.FieldReference f && f.Name == "DropPool")
                Check(code.Skip(Math.Max(0, i - 4)).Take(Math.Min(4, i)).Any(ins => ins.OpCode == Mono.Cecil.Cil.OpCodes.Ldc_I4_0),
                    "Every LoongNova item must opt out of CCL fractional/global fallback pools");
    }
    Check(!types.Any(t => t.Name == "MerchantPatches") && types.Any(t => t.Name == "LootPoolPatches"),
        "Old vanilla-wrench-equivalent merchant weighting must be removed");
    Check(plugin.Body.Instructions.Any(i => i.Operand is Mono.Cecil.TypeReference tr && tr.Name == "ElderDragonDrop"), "Boss reward patch must be installed");
    var bossDrop = types.Single(t => t.Name == "ElderDragonDrop");
    Check(!bossDrop.Methods.Any(m => m.Name == "OnDestroy"), "World unload/despawn must not count as boss defeat");
    var beforeDeath = bossDrop.Methods.Single(m => m.Name == "BeforeDeath");
    Check(beforeDeath.Body.Instructions.Any(i => i.Operand is Mono.Cecil.GenericInstanceMethod g && g.GenericArguments.Any(a => a.Name == "ElderThornbackBehaviour")),
        "Boss reward must identify the actual Elder Thornback component rather than names or coordinates");
    Check(beforeDeath.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "get_IsServer")
        && bossDrop.Methods.Single(m => m.Name == "AfterDeath").Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "get_IsServer"),
        "Both death observation and spawning must reject multiplayer clients");
    Check(bossDrop.Methods.Single(m => m.Name == "AfterDeath").Body.Instructions.Count(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "InstantiateReturn") == 1,
        "Boss reward must use one normal CCL factory call");
    Check(!assembly.MainModule.Resources.Any(r => r.Name.EndsWith("dragon-wing-back-side.png"))
        && assembly.MainModule.Resources.Any(r => r.Name.EndsWith("dragon-wing-open-side.png")), "Only the newly redrawn single visible open wing must ship");
    using var ccl = Mono.Cecil.AssemblyDefinition.ReadAssembly(@"L:\SteamLibrary\steamapps\common\Casualties Unknown\BepInEx\plugins\CUCoreLib.dll");
    var poolEnum = ccl.MainModule.Types.Single(t => t.FullName == "CUCoreLib.Data.DropPool");
    foreach (var expected in new[] { ("Trader1", 16), ("Trader2", 32), ("Trader3", 64), ("AllTraders", 112), ("ContainerCrate", 8), ("DropCapsule", 128), ("CapsuleContainer", 256) })
        Check(Convert.ToInt32(poolEnum.Fields.Single(f => f.Name == expected.Item1).Constant) == expected.Item2, "Loot source mapping must match the installed CCL API");
    using var contentStream = ((Mono.Cecil.EmbeddedResource)assembly.MainModule.Resources.Single(resource => resource.Name.EndsWith("Content.json"))).GetResourceStream();
    using var contentReader = new StreamReader(contentStream);
    var content = JObject.Parse(contentReader.ReadToEnd());
    Check(content.Value<string>("beltSocketEmpty") == "可安装电池：实验性龙息单元", "Empty socket wording must match the requested compact hint");
    Check(string.Format(content.Value<string>("beltSocketLoaded")!, 1000, 100) == "龙息单元就绪：1000/1000（100%）", "Loaded wording must exactly match the requested compact hint");
    Check(string.Format(content.Value<string>("beltSocketLoaded")!, 0, 0) == "龙息单元就绪：0/1000（0%）", "An exhausted installed cell must retain the compact status");
    Check(content.Value<string>("beltDescription")!.Contains("每秒消耗0.5") && content.Value<string>("beltDescription")!.Contains("每秒消耗8"),
        "Compact belt description must disclose both actual energy rates");
    Check(content.Value<string>("cellDescription")!.Contains("发射100次") && content.Value<string>("gunDescription")!.Contains("0.15秒"),
        "Gun and cell descriptions must match the updated shot cost and interval");
    Check(string.Format(System.Globalization.CultureInfo.InvariantCulture, content.Value<string>("cellEnergy")!, 12.375, 1.2375)
        == "能源：12.375/1000（1.2375%）", "Loose cells must show fractional energy rather than a rounded whole percent");
    Check(string.Format(System.Globalization.CultureInfo.InvariantCulture, content.Value<string>("beltSocketLoaded")!, 12.375, 1.2375)
        == "龙息单元就绪：12.375/1000（1.2375%）", "Installed cells must retain the same fractional display precision");
    foreach (int charge in new[] { 0, 1000 })
        Check(string.Format(content.Value<string>("cellEnergy")!, charge, charge / 10) == $"能源：{charge}/1000（{charge / 10}%）",
            "Loose-cell empty and full display must agree with the exact capacity");
    Check(content.Value<string>("beltCard")!.Contains("飞行 8/s"), "Belt card must disclose the new flight cost");
    Check(content.Value<string>("beltLowEnergy") == "龙翼单元：能源储备低", "Low-energy warning must match the requested text exactly");
    foreach (string key in new[] { "matterRecipe", "cellRecipe", "gunRecipe", "beltRecipe", "refillRecipe", "pickaxeRecipe" })
        Check(content.Value<string>(key)!.Contains("可龙化100单位") && content.Value<string>(key)!.Contains("1%耐久"), "Every dragonization recipe must disclose the exact requirement and cost");
    Check(content.Value<string>("dragonRecipe")!.Contains("25%") && content.Value<string>("matterDescription")!.Contains("只能"), "Catalog must disclose the exclusive dragon sources and crafted-only matter");
    Check(content.Value<string>("refillName") == "重装填实验性龙息单元", "Refill recipe must retain the requested display name");
    Check(content.Value<string>("beltName") == "龙域武库-龙翼腰带", "Belt display name must use the new dragon-wing name");
}
Console.WriteLine($"PASS {checks} LoongNova gun/belt energy/save/recipes/prices/hearing/performance checks");

if (args.Length >= 3 && args[1] == "--audio-preview")
{
    const int durationSeconds = 3;
    int count = ChargeTone.SampleRate * durationSeconds;
    using var wave = new BinaryWriter(File.Create(args[2]));
    wave.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); wave.Write(36 + count * 2);
    wave.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); wave.Write(16);
    wave.Write((short)1); wave.Write((short)1); wave.Write(ChargeTone.SampleRate);
    wave.Write(ChargeTone.SampleRate * 2); wave.Write((short)2); wave.Write((short)16);
    wave.Write(System.Text.Encoding.ASCII.GetBytes("data")); wave.Write(count * 2);
    double phaseSeconds = 0;
    for (int sampleIndex = 0; sampleIndex < count; sampleIndex++)
    {
        float progress = (float)sampleIndex / count;
        phaseSeconds += ChargeTone.Pitch(progress) / ChargeTone.SampleRate;
        float edge = Math.Min(1, Math.Min(sampleIndex, count - 1 - sampleIndex) / 120f);
        float value = ChargeTone.Sample(phaseSeconds) * ChargeTone.Gain(progress) * edge;
        wave.Write((short)Math.Clamp(Math.Round(value * short.MaxValue), short.MinValue, short.MaxValue));
    }
    Console.WriteLine("Wrote charge audio preview: " + args[2]);
}
