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
for (int shot = 0; shot < 20; shot++) Check(state.TrySpendShot(), "Full cell must support twenty shots");
Check(state.Loaded && state.Energy == 0 && state.Favourite, "Empty cell keeps its shell and favourite flag");
Check(!state.TrySpendShot() && state.Energy == 0, "Twenty-first shot must fail without mutating energy");
var restored = new EnergyState();
Check(restored.Restore(JObject.Parse(state.Capture().ToString())), "Empty loaded cell must survive save round-trip");
Check(restored.Loaded && restored.Energy == 0 && restored.Favourite, "Empty cell state must be retained");
state.Set(true, 62.375f, false);
Check(state.TrySpendShot() && state.Energy == 12.375f, "Fractional remainders must survive a shot");
Check(!state.TrySpendShot(), "Insufficient fractional remainder cannot fire");
Check(restored.Restore(JObject.Parse(state.Capture().ToString())) && restored.Energy == 12.375f, "Fractional charge must survive save round-trip");
var bad = state.Capture();
foreach (double invalid in new[] { -1d, 1001d, double.NaN, double.PositiveInfinity })
{
    bad["energy"] = invalid;
    Check(!restored.Restore(bad) && restored.Energy == 12.375f, "Invalid saved charge must leave current state intact");
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
Check(!reports.Apply(belt, oldEpoch, 4, 10) && belt.Capture().ToString() == checkpoint, "Work above the real-time burst budget must be rejected");
Check(reports.Apply(belt, oldEpoch, 2, 11) && belt.Remaining == 999, "Budget must replenish as time advances");
belt.Clear(); belt.Load(100, true);
checkpoint = belt.Capture().ToString();
Check(!reports.Apply(belt, oldEpoch, 2.5, 12) && belt.Capture().ToString() == checkpoint, "Delayed reports for an ejected cell must not charge a newly loaded cell");
Check(reports.Apply(belt, belt.Epoch, 0.5, 12) && belt.Remaining == 99.75f, "A newly loaded cell starts a separate cumulative report epoch");
Check(!reports.Apply(belt, belt.Epoch, double.NaN, 12), "Non-finite client work must be rejected");
string root = args.Length == 0 ? Path.GetFullPath(".") : args[0];
using (var assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly(Path.Combine(root, "src/DDEN.LoongNova/bin/Release/DDEN.LoongNova.dll")))
{
    var types = assembly.MainModule.Types;
    var controller = types.Single(type => type.Name == "DragonGun");
    Check((float)controller.Fields.Single(field => field.Name == "ShotInterval").Constant == 0.5f, "Compiled shot interval must be 0.5 seconds");
    var energy = types.Single(type => type.Name == "EnergyState");
    Check((float)energy.Fields.Single(field => field.Name == "ShotCost").Constant == 50, "Compiled shot cost must be fifty");
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
    var definition = types.Single(type => type.Name == "Registration").Methods.Single(method => method.Name == "Register");
    Check(DamageBeforeStore(definition, "StructureDamage") == 3000, "Registered structural damage must be 3000");
    Check(DamageBeforeStore(definition, "AnimalDamage") == 650, "Registered animal damage must be 650");
    var normalization = controller.Methods.Single(method => method.Name == "Normalize");
    Check(DamageBeforeStore(normalization, "structureDamage") == 3000, "Runtime structural damage must be 3000");
    Check(DamageBeforeStore(normalization, "animalDamage") == 650, "Runtime animal damage must be 650");
    Check(DamageBeforeStore(execution, "structureDamage") == 3000, "Actual shot structural damage must be 3000");
    Check(DamageBeforeStore(execution, "animalDamage") == 650, "Actual shot animal damage must be 650");
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
    Check(equipment.Body.Instructions.Count(instruction => instruction.Operand is float value && value == 0.02f) == 1, "Belt pooled rarity must match the gun's 0.02 weight");
    Check(!assembly.MainModule.AssemblyReferences.Any(reference => reference.Name == "CasualtiesMP" || reference.Name == "Together"), "Optional multiplayer integration must not become a hard assembly reference");
    Check(equipment.Body.Instructions.Count(instruction => instruction.Operand is float value && value == 100000f) == 3, "Wrench must provide all three qualities with extremely low crafting wear");
    Check(equipment.Body.Instructions.Any(instruction => instruction.Operand is string text && text == "cutting")
        && equipment.Body.Instructions.Any(instruction => instruction.Operand is string text && text == "hammering")
        && equipment.Body.Instructions.Any(instruction => instruction.Operand is string text && text == "loongnova.dragonization"), "Wrench must provide cutting, hammering and dragonization");
    Check(assembly.MainModule.Resources.Count(resource => resource.Name.EndsWith(".png")) == 3, "Generated gun/cell sprites and the supplied dragon image must all be embedded");
    using var contentStream = ((Mono.Cecil.EmbeddedResource)assembly.MainModule.Resources.Single(resource => resource.Name.EndsWith("Content.json"))).GetResourceStream();
    using var contentReader = new StreamReader(contentStream);
    var content = JObject.Parse(contentReader.ReadToEnd());
    Check(content.Value<string>("refillName") == "重装填实验性龙息单元", "Refill recipe must retain the requested display name");
    Check(content.Value<string>("beltDescription")!.Contains("不附带单元"), "Belt description must clearly state the empty socket");
}
Console.WriteLine($"PASS {checks} LoongNova gun/belt energy/save/recipes/prices/hearing/performance checks");
