using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class CheckpointBinaryContract
{
    // These assertions inspect the actual installed assemblies as data. They do not load Unity or execute any save method.
    internal static int Verify(string game, Action<bool, string> observe = null)
    {
        int checks = 0;
        Action<bool, string> check = (value, message) => { observe?.Invoke(value, message); if (!value) throw new Exception(message); checks++; };
        using var native = ModuleDefinition.ReadModule(Path.Combine(game, "CasualtiesUnknown_Data/Managed/Assembly-CSharp.dll"));
        using var together = ModuleDefinition.ReadModule(Path.Combine(game, "BepInEx/plugins/Together/CasualtiesMP.dll"));
        using var ccl = ModuleDefinition.ReadModule(Path.Combine(game, "BepInEx/plugins/CUCoreLib.dll"));
        string qolPath = Directory.GetFiles(Path.Combine(game, "BepInEx/plugins"), "QoL*.dll", SearchOption.AllDirectories).Single();
        using var qol = ModuleDefinition.ReadModule(qolPath);
        check(native.Mvid.ToString() == "a598f273-a4d7-465b-9ef9-4ff70b39d5e4", "checkpoint contract pins the actual game save binary");
        check(together.Mvid.ToString() == "b3b58466-cc0d-42b6-910c-d744dd135db2", "checkpoint contract pins Together 5.5.5");
        check(ccl.Mvid.ToString() == "1afc0720-3c12-4604-87cd-a913865c58b9", "checkpoint contract pins installed CCL save providers");
        check(qol.Mvid.ToString() == "00541570-251e-48b6-bb5f-335d505c5a9a", "checkpoint contract pins installed QoL save integration");

        var save = Type(native, "SaveSystem");
        var checkpoint = Type(together, "Together.PlayerCheckpoint");
        var spawn = Type(together, "Together.WorldSpawn");
        var tick = Require(checkpoint, "Tick", "System.Void", check);
        var write = Require(checkpoint, "WriteOne", "System.Void", check, "Together.ScavPlayer", "System.Boolean");
        Require(checkpoint, "Folder", "System.String", check, "Together.ScavPlayer");
        var saveGame = Require(save, "SaveGame", "System.Void", check);
        var zip = Require(save, "Zip", "System.Byte[]", check, "System.String");
        var unzip = Require(save, "Unzip", "System.String", check, "System.Byte[]");
        var redirects = new[] {
            saveGame,
            Require(checkpoint, "StampCrafted", "System.Void", check, "System.String", "System.Collections.Generic.List`1<System.Int32>"),
            Require(checkpoint, "StoreCurrentBiome", "System.Void", check, "System.String", "System.Int32"),
            Require(checkpoint, "StampSpecies", "System.Void", check, "System.String", "System.String", "System.Int32"),
            Require(checkpoint, "CopyHostFlag", "System.Void", check, "System.String"),
            Require(spawn, "Stamp", "System.Void", check),
            Require(Type(ccl, "CUCoreLib.Saving.SaveCoordinator"), "EmbedIntoSaveFile", "System.Void", check)
        };
        foreach (var method in redirects)
            check(method.HasBody && Calls(method).Any(call => call.DeclaringType.FullName == "System.IO.File"),
                "real capture call site contains file operations: " + method.FullName);
        foreach (var codec in new[] { zip, unzip })
        {
            var calls = Calls(codec).ToArray();
            check(calls.Any(call => call.DeclaringType.FullName == "System.Text.Encoding" && call.Name == "get_ASCII")
                && calls.Any(call => call.DeclaringType.FullName == "System.IO.Compression.GZipStream" && call.Name == ".ctor"),
                "real native codec uses ASCII and gzip: " + codec.Name);
        }
        check(Calls(saveGame).Any(call => call.FullName == zip.FullName)
            && Calls(saveGame).Any(call => call.FullName == "System.Void System.IO.File::WriteAllBytes(System.String,System.Byte[])"),
            "native SaveGame produces the exact Zip/WriteAllBytes call sites intercepted by the scope");
        var threshold = tick.Body.Instructions.Where(instruction => instruction.OpCode == OpCodes.Ldc_R4 && Equals(instruction.Operand, 30f)).ToArray();
        check(threshold.Length == 1 && threshold[0].Previous.OpCode == OpCodes.Ldsfld
            && threshold[0].Previous.Operand is FieldReference wait && wait.Name == "_wait" && wait.FieldType.FullName == "System.Single"
            && threshold[0].Next.OpCode == OpCodes.Bge_Un_S,
            "actual periodic Tick has the verified _wait/30f/comparison shape, rather than a mock-only timer");
        check(Calls(tick).Count(call => call.FullName == write.FullName) == 1
            && tick.Body.Instructions.Any(instruction => instruction.Operand is FieldReference field && field.Name == "_cursor"),
            "actual Tick retains one WriteOne call site and native round-robin cursor");
        check(Calls(write).Any(call => call.DeclaringType.FullName == "Together.SaveSystem_SaveGame_MultiplayerPatch" && call.Name == "WriteForced")
            && Calls(write).Any(call => call.Name == "StoreCurrentBiome") && Calls(write).Any(call => call.Name == "StampSpecies")
            && Calls(write).Any(call => call.Name == "CopyHostFlag"), "actual WriteOne keeps original snapshot and metadata stamp chain");

        Getter(Type(together, "Together.SavesystemPatch"), "SessionSaveFolder", "System.String", true, check);
        Getter(Type(native, "WorldGeneration"), "world", "WorldGeneration", true, check);
        Getter(Type(native, "WorldGeneration"), "worldExists", "System.Boolean", false, check);
        Getter(Type(native, "WorldGeneration"), "generatingWorld", "System.Boolean", false, check);
        foreach (string name in new[] { "IsRunning", "IsServer" }) Getter(Type(together, "Together.Net"), name, "System.Boolean", true, check);
        Getter(Type(together, "Together.SharedMain"), "LocalWorldIsGenerated", "System.Boolean", true, check);
        foreach (string name in new[] { "PreserveSave", "InDebugMap" }) Getter(Type(together, "Together.SavesystemPatch"), name, "System.Boolean", true, check);

        var barriers = new Dictionary<string, string[]> {
            ["SaveSystem"] = new[] { "SaveGame", "TryLoadGame", "HasSave" },
            ["Together.PlayerCheckpoint"] = new[] { "WriteOne", "WriteLayerTransition", "TryLoadBodyOnly", "HasPlayerFile", "TryReadSpecies", "ReadCrafted", "RememberSpecies", "OnHostTransferInstalled" },
            ["Together.WorldSpawn"] = new[] { "Read", "SpawnOnlyFile", "Stamp" },
            ["Together.SavesystemPatch"] = new[] { "TryPeekMpWorldState", "DeleteMPSave", "DeleteDebugSave", "HasMultiplayerSaveFile" },
            ["Together.CharacterSelectWait"] = new[] { "TryReadSavedCharacter" },
            ["Together.TransferArchive"] = new[] { "Build", "BuildSaved", "CopyTree", "NormalizeSource", "Install" },
            ["Together.Net"] = new[] { "ShutdownReset" },
            ["QoL_Unknown.MultiplayerSaveBundle"] = new[] { "SaveHostBundle", "RestoreHostBundleToMultiplayerSave", "TryReadLiveMpPlayerInventorySnapshot", "StageSingleplayerSlotAsHostBundle" }
        };
        foreach (var group in barriers)
        {
            var module = group.Key == "SaveSystem" ? native : group.Key.StartsWith("QoL_Unknown.", StringComparison.Ordinal) ? qol : together;
            var type = Type(module, group.Key);
            foreach (string name in group.Value)
                check(type.Methods.Any(method => method.Name == name && method.IsStatic && method.HasBody), "actual save/read/exit barrier exists: " + group.Key + "." + name);
        }
        var togetherPatches = Type(together, "Together.SaveSystem_SaveGame_MultiplayerPatch");
        Require(togetherPatches, "Prefix", "System.Boolean", check);
        Require(togetherPatches, "Postfix", "System.Void", check);
        Require(togetherPatches, "Transpiler", "System.Collections.Generic.IEnumerable`1<HarmonyLib.CodeInstruction>", check,
            "System.Collections.Generic.IEnumerable`1<HarmonyLib.CodeInstruction>");
        Require(Type(ccl, "CUCoreLib.Patches.SaveSystemPatches"), "SaveGame_Postfix", "System.Void", check);
        var qolPatches = Type(qol, "QoL_Unknown.SaveSystemPatcher");
        var prefix = Require(qolPatches, "SaveGame_Prefix_AdjustDepth", "System.Void", check, "System.Nullable`1<System.Int32>&");
        check(prefix.Parameters[0].IsOut, "real QoL prefix saves nullable depth using out state");
        Require(qolPatches, "SaveGame_Postfix_RestoreDepthForCapture", "System.Void", check, "System.Nullable`1<System.Int32>");
        Require(qolPatches, "SaveGame_Finalizer_RestoreState", "System.Exception", check, "System.Exception", "System.Nullable`1<System.Int32>");
        return checks;
    }

    private static TypeDefinition Type(ModuleDefinition module, string name) => module.Types.Single(type => type.FullName == name);
    private static IEnumerable<MethodReference> Calls(MethodDefinition method)
        => method.Body.Instructions.Select(instruction => instruction.Operand as MethodReference).Where(call => call != null);
    private static MethodDefinition Require(TypeDefinition type, string name, string returns, Action<bool, string> check, params string[] parameters)
    {
        var matches = type.Methods.Where(method => method.Name == name && method.IsStatic && method.ReturnType.FullName == returns
            && method.Parameters.Select(parameter => parameter.ParameterType.FullName).SequenceEqual(parameters)).ToArray();
        check(matches.Length == 1, "actual signature: " + type.FullName + "." + name + "(" + string.Join(",", parameters) + ") -> " + returns);
        return matches[0];
    }
    private static void Getter(TypeDefinition type, string name, string returns, bool isStatic, Action<bool, string> check)
    {
        var field = type.Fields.SingleOrDefault(value => value.Name == name);
        var getter = type.Properties.SingleOrDefault(value => value.Name == name)?.GetMethod;
        check(field != null ? field.IsStatic == isStatic && field.FieldType.FullName == returns
            : getter != null && getter.IsStatic == isStatic && getter.ReturnType.FullName == returns && getter.Parameters.Count == 0,
            "actual getter/field shape: " + type.FullName + "." + name);
    }
}

#if !CHECKPOINT_STANDALONE
internal static partial class Program
{
    private static void CheckpointContract(string game) => CheckpointBinaryContract.Verify(game, Check);
}
#endif
