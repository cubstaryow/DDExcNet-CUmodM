using DDEN.LoongNova;
using Newtonsoft.Json.Linq;

int checks = 0;
void Check(bool valid, string message)
{
    checks++;
    if (!valid) throw new Exception(message);
}

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
foreach (string category in new[] { "tool", "utility", "container", "food", "medical", "custom", "", null! })
foreach (double roll in new[] { 0d, 0.0005, 0.001, 0.0015, 0.005, 0.006, 0.5, 1 })
{
    string chosen = LootRules.Select(source, category, roll);
    Check(chosen != Registration.MatterId, "Manufactured matter must never be selected from any natural pool");
    Check(chosen != Registration.DragonId || LootRules.IsTrader(source) && category == "tool", "Dragon must only appear in merchant tool slots outside its boss reward");
    Check(chosen == null || LootRules.Eligible(source, category), "Unsupported sources and mismatched categories must preserve the original draw");
    if (chosen == Registration.WrenchId) Check(category == "tool", "Wrench must match a tool slot");
    if (chosen == Registration.BeltId) Check(category == "container", "Belt must match a container slot");
    if (chosen == Registration.GunId || chosen == Registration.CellId) Check(category == "utility", "Energy equipment must match utility slots");
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
Check(counts[Registration.DragonId] == 5000 && counts[Registration.WrenchId] == 1000, "Merchant tool-slot rates must be dragon 0.5 percent and wrench 0.1 percent");
Check(counts[Registration.BeltId] == 1000 && counts[Registration.GunId] == 500 && counts[Registration.CellId] == 1000,
    "Matching slots must produce belt/cell 0.1 percent and gun 0.05 percent");
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
    var types = assembly.MainModule.Types;
    var pluginType = types.Single(type => type.Name == "LoongNovaPlugin");
    Check(assembly.Name.Version == new Version(1, 0, 1, 0), "LoongNova assembly must be version 1.0.1.0");
    Check(pluginType.CustomAttributes.Single(a => a.AttributeType.Name == "BepInPlugin").ConstructorArguments[2].Value as string == "1.0.1"
        && pluginType.Methods.Single(m => m.Name == "get_Version").Body.Instructions.Any(i => i.Operand is string s && s == "1.0.1"),
        "Plugin loader and manager must agree on LoongNova 1.0.1");
    var controller = types.Single(type => type.Name == "DragonGun");
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
    Check(DamageBeforeStore(wrench, "structuralDamage") == 375 && DamageBeforeStore(wrench, "damage") == 350,
        "Wrench must send the requested base mining and creature damage into native Attack");
    Check(DamageBeforeStore(wrench, "cooldown") == 0.1f, "Wrench must have a fast base swing interval");
    Check(wrench.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "Attack"),
        "Wrench must use native melee/mining and multiplayer damage paths");
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
    Check(catalogIds.Count == 6 && !catalogIds.Contains("dden.loongnova.dragon-claw-refitted-wrench"),
        "Catalog must show six active items and exclude the deferred wrench");
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
        Check(!method.Body.Instructions.Any(instruction => instruction.Operand is Mono.Cecil.MethodReference call
            && call.DeclaringType.FullName == "UnityEngine.Physics2D" && call.Name.Contains("Raycast")), "Mod must reuse the original raycast without adding another query");
    }
    var register = types.Single(type => type.Name == "Registration").Methods.Single(method => method.Name == "RegisterRecipes");
    var fluids = new List<float>();
    for (int i = 1; i < register.Body.Instructions.Count; i++)
        if (register.Body.Instructions[i].Operand is Mono.Cecil.MethodReference call && call.Name == "Fluid")
            fluids.Add((float)register.Body.Instructions[i - 1].Operand);
    Check(fluids.SequenceEqual(new[] { 50f, 100f, 160f, 100f }), "Compiled matter/cell/refill/wrench fluid amounts must match the requested recipes");
    Check(register.Body.Instructions.Count(instruction => instruction.Operand is Mono.Cecil.MethodReference call && call.Name == "AddRecipe") == 6, "Exactly six content recipes must be registered");
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
    Check(register.Body.Instructions.Count(i => i.Operand is string s && s == "loongnova.dragonization") == 5, "All five manufacture/refill dragonization requirements must use the shared 100-unit tool builder");
    var intelligence = new List<int>();
    for (int i = 1; i < register.Body.Instructions.Count; i++)
        if (register.Body.Instructions[i].Operand is Mono.Cecil.MethodReference call && call.Name == "AddRecipe")
            intelligence.Add(IntConstant(register.Body.Instructions[i - 1]));
    Check(intelligence.SequenceEqual(new[] { 16, 18, 18, 18, 18, 14 }), "Recipe intelligence must be matter16, equipment18 and wrench14");
    var equipment = types.Single(type => type.Name == "Registration").Methods.Single(method => method.Name == "RegisterEquipment");
    var values = new List<int>();
    for (int i = 1; i < equipment.Body.Instructions.Count; i++)
        if (equipment.Body.Instructions[i].OpCode == Mono.Cecil.Cil.OpCodes.Stfld
            && equipment.Body.Instructions[i].Operand is Mono.Cecil.FieldReference field && field.Name == "value")
            values.Add(IntConstant(equipment.Body.Instructions[i - 1]));
    Check(values.SequenceEqual(new[] { 45, 8, 35, 35 }), "Belt/dragon/wrench base prices must stay within their requested ranges");
    Check(!equipment.Body.Instructions.Any(instruction => instruction.Operand is Mono.Cecil.FieldReference field && field.Name == "Battery"), "Belt definition must never create a native battery or spawn with one");
    Check(DamageBeforeStore(equipment, "Capacity") == 6, "Belt storage capacity must remain six units");
    Check(equipment.Body.Instructions.Count(instruction => instruction.Operand is float value && value == 0.02f) == 0, "Legacy fractional pool weights must no longer inject the belt");
    Check(!assembly.MainModule.AssemblyReferences.Any(reference => reference.Name == "CasualtiesMP" || reference.Name == "Together"), "Optional multiplayer integration must not become a hard assembly reference");
    Check(equipment.Body.Instructions.Count(instruction => instruction.Operand is float value && value == 100000f) == 4, "Wrench must provide all four qualities with extremely low crafting wear");
    Check(equipment.Body.Instructions.Any(instruction => instruction.Operand is string text && text == "cutting")
        && equipment.Body.Instructions.Any(instruction => instruction.Operand is string text && text == "hammering")
        && equipment.Body.Instructions.Any(instruction => instruction.Operand is string text && text == "loongnova.dragonization"), "Wrench must provide cutting, hammering and dragonization");
    Check(assembly.MainModule.Resources.Count(resource => resource.Name.EndsWith(".png")) == 10, "Ten active item and folded/open worn-state sprites must be embedded");
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
    foreach (string key in new[] { "matterRecipe", "cellRecipe", "gunRecipe", "beltRecipe", "refillRecipe" })
        Check(content.Value<string>(key)!.Contains("可龙化100单位") && content.Value<string>(key)!.Contains("1%耐久"), "Every dragonization recipe must disclose the exact requirement and cost");
    Check(content.Value<string>("dragonRecipe")!.Contains("25%") && content.Value<string>("matterDescription")!.Contains("只能"), "Catalog must disclose the exclusive dragon sources and crafted-only matter");
    Check(content.Value<string>("refillName") == "重装填实验性龙息单元", "Refill recipe must retain the requested display name");
    Check(content.Value<string>("beltName") == "实验性龙翼腰带", "Belt display name must use the new dragon-wing name");
}
Console.WriteLine($"PASS {checks} LoongNova gun/belt energy/save/recipes/prices/hearing/performance checks");
