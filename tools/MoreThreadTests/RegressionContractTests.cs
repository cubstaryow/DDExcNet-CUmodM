using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class RegressionBinaryContract
{
    // Metadata only; do not load the game/Unity assembly into the fixture process.
    internal static int Verify(string game, Action<bool, string> observe = null, Type fixtureTimingHooks = null)
    {
        int checks = 0;
        Action<bool, string> check = (value, message) => { observe?.Invoke(value, message); if (!value) throw new Exception(message); checks++; };
        using (var module = ModuleDefinition.ReadModule(Path.Combine(game, "BepInEx/plugins/Together/CasualtiesMP.dll")))
        {
            check(module.Mvid.ToString() == "b3b58466-cc0d-42b6-910c-d744dd135db2", "regression adapter requires verified 5.5.5 MVID");
            var range = module.Types.Single(type => type.FullName == "KrokV5Optimization.FixSimRange");
            var mark = range.Methods.Single(method => method.Name == "Mark");
            check(mark.IsStatic && mark.ReturnType.FullName == "System.Void" && mark.Parameters.Count == 3
                && mark.Parameters[0].ParameterType is ArrayType grid && grid.Rank == 2 && grid.ElementType.FullName == "System.Boolean"
                && mark.Parameters[1].ParameterType.FullName == "WorldGeneration" && mark.Parameters[2].ParameterType.FullName == "UnityEngine.Vector2",
                "actual radius interception signature");
            var fours = mark.Body.Instructions.Where(instruction => instruction.OpCode == OpCodes.Ldc_I4_4
                || instruction.OpCode == OpCodes.Ldc_I4 && Equals(instruction.Operand, 4)
                || instruction.OpCode == OpCodes.Ldc_I4_S && Convert.ToInt32(instruction.Operand) == 4).ToArray();
            check(fours.Length == 4 && fours.All(instruction => instruction.Next.OpCode == OpCodes.Add || instruction.Next.OpCode == OpCodes.Sub)
                && fours.Count(instruction => instruction.Next.OpCode == OpCodes.Add) == 2, "actual Mark has exactly four verified +/-4 loop-bound constants");
            check(mark.Body.Instructions.Count(instruction => instruction.Operand is MethodReference call && call.Name == "GetLength") == 2,
                "actual Mark retains bounds for both grid dimensions");
            var rebuild = range.Methods.Single(method => method.Name == "Rebuild");
            check(rebuild.IsStatic && rebuild.ReturnType.FullName == "System.Boolean" && rebuild.Parameters.Count == 0,
                "actual original rebuild callable without game-state arguments");
            check(rebuild.Body.Instructions.Count(instruction => instruction.Operand is MethodReference call && call.DeclaringType.FullName == range.FullName && call.Name == "Mark") == 2,
                "actual rebuild marks player positions and camera fallback");
            check(rebuild.Body.Instructions.Any(instruction => instruction.Operand is MethodReference call && call.DeclaringType.FullName == "Together.SimKeep" && call.Name == "Stamp"),
                "actual rebuild preserves retained-object stamping");
            var shared = module.Types.Single(type => type.FullName == "Together.SharedMain");
            var table = shared.Fields.Single(field => field.Name == "CHUNKS_VISIBLE_TO_PLAYERS");
            check(table.IsStatic && table.FieldType is ArrayType array && array.Rank == 2 && array.ElementType.FullName == "System.Boolean",
                "actual sampler reads existing bool simulation table");
            var net = module.Types.Single(type => type.FullName == "Together.Net");
            foreach (string name in new[] { "IsRunning", "IsServer" })
            {
                var getter = net.Properties.Single(property => property.Name == name).GetMethod;
                check(getter.IsStatic && getter.ReturnType.FullName == "System.Boolean" && getter.Parameters.Count == 0, "actual host gate getter " + name);
            }
            var reports = module.Types.Single(type => type.FullName == "Together.FindTheBugReports");
            var sample = reports.NestedTypes.Single(type => type.Name == "BagSample");
            var bags = reports.Fields.Single(field => field.Name == "Bags");
            var items = reports.Fields.Single(field => field.Name == "BagItems");
            check(sample.IsValueType && sample.Fields.All(field => field.FieldType.IsPrimitive), "actual bag diagnostic sample contains only value counters");
            check(bags.IsStatic && bags.IsInitOnly && bags.FieldType is GenericInstanceType dictionary
                && dictionary.ElementType.FullName == "System.Collections.Generic.Dictionary`2"
                && dictionary.GenericArguments[0].FullName == "System.Int32" && dictionary.GenericArguments[1].FullName == sample.FullName,
                "actual Bags field has exact clearable dictionary shape");
            check(items.IsStatic && items.IsInitOnly && items.FieldType is GenericInstanceType list
                && list.ElementType.FullName == "System.Collections.Generic.List`1" && list.GenericArguments[0].FullName == "Item",
                "actual BagItems field has exact Item reference list shape");
            var note = reports.Methods.Single(method => method.Name == "NoteDecay");
            check(note.IsStatic && note.ReturnType.FullName == "System.Void" && note.Parameters.Count == 1
                && note.Parameters[0].ParameterType.FullName == "Item", "actual diagnostic producer NoteDecay signature");
            check(note.Body.Instructions.Any(instruction => instruction.Operand is FieldReference field && field.FullName == bags.FullName)
                && !note.Body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Stsfld
                    || instruction.OpCode == OpCodes.Stfld && instruction.Operand is FieldReference field && field.DeclaringType.FullName != sample.FullName),
                "actual NoteDecay only updates its diagnostic value sample and dictionary, not gameplay fields");
            var scanBags = reports.Methods.Single(method => method.Name == "ScanBags");
            check(scanBags.Body.Instructions.Count(instruction => instruction.Operand is MethodReference call && call.Name == "Clear") == 2,
                "actual ScanBags owns both diagnostic container cleanup calls");
            var tick = reports.Methods.Single(method => method.Name == "Tick");
            check(tick.IsStatic && tick.ReturnType.FullName == "System.Void" && tick.Parameters.Count == 0, "actual diagnostic Tick signature");
            var calls = tick.Body.Instructions.Select(instruction => instruction.Operand as MethodReference).Where(call => call != null).ToArray();
            check(calls.Any(call => call.DeclaringType.FullName == net.FullName && call.Name == "get_IsRunning")
                && calls.Count(call => call.DeclaringType.FullName == reports.FullName && call.Name == "ScanBags") == 1,
                "actual suppressed body is MP diagnostic scan entry");
            check(!calls.Any(call => call.DeclaringType.FullName == "Together.LayerBuildingStream"), "business registration is outside suppressed entry");
        }
        using (var gameModule = ModuleDefinition.ReadModule(Path.Combine(game, "CasualtiesUnknown_Data/Managed/Assembly-CSharp.dll")))
        {
            var world = gameModule.Types.Single(type => type.FullName == "WorldGeneration");
            var field = world.Fields.SingleOrDefault(candidate => candidate.Name == "world");
            var getter = world.Properties.SingleOrDefault(candidate => candidate.Name == "world")?.GetMethod;
            check(field != null ? field.IsStatic && field.FieldType.FullName == world.FullName
                : getter != null && getter.IsStatic && getter.ReturnType.FullName == world.FullName, "actual world identity getter");
        }
        VerifyTimingHooks(Path.Combine(game, "BepInEx/plugins/DDEN/DDEN.NetPerfTracker.Enhanced.dll"), fixtureTimingHooks, check);
        return checks;
    }
    private static void VerifyTimingHooks(string path, Type fixture, Action<bool, string> check)
    {
        using (var module = ModuleDefinition.ReadModule(path))
        {
            const string hooksName = "DDEN.NetPerfTracker.Enhanced.DeepHooks";
            const string stampName = "DDEN.NetPerfTracker.Enhanced.CallStamp";
            var hooks = module.Types.Single(type => type.FullName == hooksName);
            var stamp = module.Types.Single(type => type.FullName == stampName);
            check(module.Assembly.Name.Name == "DDEN.NetPerfTracker.Enhanced" && stamp.IsValueType,
                "installed Enhanced owns the actual timing state value type");
            var before = hooks.Methods.Single(method => method.Name == "Before");
            var after = hooks.Methods.Single(method => method.Name == "After");
            check(before.IsStatic && before.ReturnType.FullName == "System.Void" && before.Parameters.Count == 2
                && before.Parameters[0].ParameterType.FullName == "System.Reflection.MethodBase" && before.Parameters[1].IsOut
                && before.Parameters[1].ParameterType is ByReferenceType state && state.ElementType.FullName == stampName,
                "installed Enhanced prefix uses out DDEN.NetPerfTracker.Enhanced.CallStamp");
            check(after.IsStatic && after.ReturnType.FullName == "System.Void" && after.Parameters.Count == 2
                && after.Parameters[0].ParameterType.FullName == stampName && after.Parameters[1].ParameterType.FullName == "System.Exception",
                "installed Enhanced finalizer takes timing state and exception by value");
            if (fixture == null) return;
            foreach (var method in new[] { before, after })
            {
                var candidate = fixture.GetMethod(method.Name, System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                check(candidate != null && candidate.DeclaringType.FullName == hooksName && candidate.IsStatic == method.IsStatic
                    && candidate.IsPrivate == method.IsPrivate && candidate.ReturnType.FullName == method.ReturnType.FullName
                    && candidate.GetParameters().Select(parameter => parameter.ParameterType.FullName).SequenceEqual(method.Parameters.Select(parameter => parameter.ParameterType.FullName))
                    && candidate.GetParameters().Select(parameter => parameter.IsOut).SequenceEqual(method.Parameters.Select(parameter => parameter.IsOut)),
                    "timing fixture signature must match installed Enhanced metadata: " + method.Name);
            }
        }
    }
}

#if !REGRESSION_STANDALONE
internal static partial class Program
{
    private static void RegressionContract(string game) => RegressionBinaryContract.Verify(game, Check);
}
#endif
