using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using DDEN.Shared;
using HarmonyLib;

namespace DDEN.MPreducer.Integration;

internal sealed class CheckpointBindings
{
	private sealed class ExpectedPatch
	{
		internal MethodInfo Target;

		internal MethodInfo Method;

		internal string Owner;

		internal string Kind;
	}

	internal const string Owner = "com.dden.morethreadonmp.checkpoint";

	internal const string GameMvid = "a598f273-a4d7-465b-9ef9-4ff70b39d5e4";

	internal const string CoreLibMvid = "1afc0720-3c12-4604-87cd-a913865c58b9";

	internal const string QolMvid = "00541570-251e-48b6-bb5f-335d505c5a9a";

	private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

	private readonly Func<string, Type> _resolve;

	private readonly Type _coreCoordinator;

	private readonly Type _qolPatcher;

	private readonly List<ExpectedPatch> _known = new List<ExpectedPatch>();

	private readonly List<MethodInfo> _observed = new List<MethodInfo>();

	private readonly HashSet<MethodInfo> _codecs = new HashSet<MethodInfo>();

	internal readonly MethodInfo Tick;

	internal readonly MethodInfo WriteOne;

	internal readonly MethodInfo SaveGame;

	internal readonly List<MethodInfo> RedirectMethods = new List<MethodInfo>();

	internal readonly List<MethodInfo> Barriers = new List<MethodInfo>();

	internal readonly HashSet<MethodInfo> CaptureLocalBarriers = new HashSet<MethodInfo>();

	internal readonly Func<string> SessionFolder;

	internal readonly Func<object, string> PlayerFolder;

	internal readonly Func<bool> CanCapture;

	internal readonly Func<object> WorldIdentity;

	internal readonly Action<object> Replay;

	internal CheckpointBindings(bool verifyIdentity = true, Func<string, Type> resolver = null)
	{
		CheckpointBindings checkpointBindings = this;
		_resolve = resolver ?? new Func<string, Type>(Reflect.Type);
		Type type = RequireType("Together.PlayerCheckpoint");
		Type type2 = RequireType("Together.ScavPlayer");
		Type type3 = RequireType("SaveSystem");
		Type type4 = RequireType("Together.SavesystemPatch");
		Type type5 = RequireType("Together.Net");
		Type type6 = RequireType("Together.SharedMain");
		Type type7 = RequireType("WorldGeneration");
		Type type8 = RequireType("Together.WorldSpawn");
		if (verifyIdentity)
		{
			Pin(type.Assembly, "b3b58466-cc0d-42b6-910c-d744dd135db2");
			Pin(type3.Assembly, "a598f273-a4d7-465b-9ef9-4ff70b39d5e4");
		}
		Tick = RequireMethod(type, "Tick", typeof(void), Type.EmptyTypes);
		WriteOne = RequireMethod(type, "WriteOne", typeof(void), new Type[2]
		{
			type2,
			typeof(bool)
		});
		SaveGame = RequireMethod(type3, "SaveGame", typeof(void), Type.EmptyTypes);
		MethodInfo method = RequireMethod(type, "Folder", typeof(string), new Type[1] { type2 });
		SessionFolder = StaticGetter<string>(type4, "SessionSaveFolder");
		WorldIdentity = StaticGetter<object>(type7, "world");
		Func<bool> running = StaticGetter<bool>(type5, "IsRunning");
		Func<bool> server = StaticGetter<bool>(type5, "IsServer");
		Func<bool> generated = StaticGetter<bool>(type6, "LocalWorldIsGenerated");
		Func<bool> preserve = StaticGetter<bool>(type4, "PreserveSave");
		Func<bool> debug = StaticGetter<bool>(type4, "InDebugMap");
		Func<object, bool> exists = InstanceGetter<bool>(type7, "worldExists");
		Func<object, bool> generating = InstanceGetter<bool>(type7, "generatingWorld");
		CanCapture = () =>
		{
			try
			{
				object obj = checkpointBindings.WorldIdentity();
				return running() && server() && generated() && !preserve() && !debug() && obj != null && exists(obj) && !generating(obj);
			}
			catch
			{
				return false;
			}
		};
		ParameterExpression parameterExpression = Expression.Parameter(typeof(object), "player");
		PlayerFolder = Expression.Lambda<Func<object, string>>(Expression.Call(method, Expression.Convert(parameterExpression, type2)), new ParameterExpression[1] { parameterExpression }).Compile();
		Replay = Expression.Lambda<Action<object>>(Expression.Call(WriteOne, Expression.Convert(parameterExpression, type2), Expression.Constant(false)), new ParameterExpression[1] { parameterExpression }).Compile();
		RedirectMethods.Add(SaveGame);
		RedirectMethods.Add(RequireMethod(type, "StampCrafted", typeof(void), new Type[2]
		{
			typeof(string),
			typeof(List<int>)
		}));
		RedirectMethods.Add(RequireMethod(type, "StoreCurrentBiome", typeof(void), new Type[2]
		{
			typeof(string),
			typeof(int)
		}));
		RedirectMethods.Add(RequireMethod(type, "StampSpecies", typeof(void), new Type[3]
		{
			typeof(string),
			typeof(string),
			typeof(int)
		}));
		RedirectMethods.Add(RequireMethod(type, "CopyHostFlag", typeof(void), new Type[1] { typeof(string) }));
		RedirectMethods.Add(RequireMethod(type8, "Stamp", typeof(void), Type.EmptyTypes));
		_coreCoordinator = _resolve("CUCoreLib.Saving.SaveCoordinator");
		if (_coreCoordinator != null)
		{
			if (verifyIdentity)
			{
				Pin(_coreCoordinator.Assembly, "1afc0720-3c12-4604-87cd-a913865c58b9");
			}
			RedirectMethods.Add(RequireMethod(_coreCoordinator, "EmbedIntoSaveFile", typeof(void), Type.EmptyTypes));
		}
		_qolPatcher = _resolve("QoL_Unknown.SaveSystemPatcher");
		if ((_qolPatcher != null) & verifyIdentity)
		{
			Pin(_qolPatcher.Assembly, "00541570-251e-48b6-bb5f-335d505c5a9a");
		}
		AddBarriers("SaveSystem", verifyIdentity, "SaveGame", "TryLoadGame", "HasSave");
		AddBarriers("Together.PlayerCheckpoint", verifyIdentity, "WriteOne", "WriteLayerTransition", "TryLoadBodyOnly", "HasPlayerFile", "TryReadSpecies", "ReadCrafted", "RememberSpecies", "OnHostTransferInstalled");
		AddBarriers("Together.WorldSpawn", verifyIdentity, "Read", "SpawnOnlyFile", "Stamp");
		AddBarriers("Together.SavesystemPatch", verifyIdentity, "TryPeekMpWorldState", "DeleteMPSave", "DeleteDebugSave", "HasMultiplayerSaveFile");
		AddBarriers("Together.CharacterSelectWait", verifyIdentity, "TryReadSavedCharacter");
		AddBarriers("Together.TransferArchive", verifyIdentity, "Build", "BuildSaved", "CopyTree", "NormalizeSource", "Install");
		AddBarriers("Together.Net", verifyIdentity, "ShutdownReset");
		if (_qolPatcher != null)
		{
			AddBarriers("QoL_Unknown.MultiplayerSaveBundle", verifyIdentity, "SaveHostBundle", "RestoreHostBundleToMultiplayerSave", "TryReadLiveMpPlayerInventorySnapshot", "StageSingleplayerSlotAsHostBundle");
		}
		foreach (MethodInfo barrier in Barriers)
		{
			if ((barrier.DeclaringType == type && barrier.Name == "TryReadSpecies") || (barrier.DeclaringType == type8 && barrier.Name == "Stamp"))
			{
				CaptureLocalBarriers.Add(barrier);
			}
		}
		AddKnown("Together.SaveSystem_SaveGame_MultiplayerPatch", "Prefix", "CasualtiesMP", "prefix", verifyIdentity);
		AddKnown("Together.SaveSystem_SaveGame_MultiplayerPatch", "Postfix", "CasualtiesMP", "postfix", verifyIdentity);
		AddKnown("Together.SaveSystem_SaveGame_MultiplayerPatch", "Transpiler", "CasualtiesMP", "transpiler", verifyIdentity);
		if (_coreCoordinator != null)
		{
			AddKnown("CUCoreLib.Patches.SaveSystemPatches", "SaveGame_Postfix", "net.cucorelib", "postfix", verifyIdentity);
		}
		if (_qolPatcher != null)
		{
			AddKnown("QoL_Unknown.SaveSystemPatcher", "SaveGame_Prefix_AdjustDepth", "org.bepinex.plugins.qol.savesystem", "prefix", verifyIdentity);
			AddKnown("QoL_Unknown.SaveSystemPatcher", "SaveGame_Postfix_RestoreDepthForCapture", "org.bepinex.plugins.qol.savesystem", "postfix", verifyIdentity);
			AddKnown("QoL_Unknown.SaveSystemPatcher", "SaveGame_Finalizer_RestoreState", "org.bepinex.plugins.qol.savesystem", "finalizer", verifyIdentity);
		}
		_codecs.Add(RequireMethod(type3, "Zip", typeof(byte[]), new Type[1] { typeof(string) }));
		_codecs.Add(RequireMethod(type3, "Unzip", typeof(string), new Type[1] { typeof(byte[]) }));
		foreach (MethodInfo codec in _codecs)
		{
			_observed.Add(codec);
		}
		_observed.Add(Tick);
		_observed.Add(WriteOne);
		foreach (MethodInfo redirectMethod in RedirectMethods)
		{
			if (!_observed.Contains(redirectMethod))
			{
				_observed.Add(redirectMethod);
			}
		}
	}

	internal bool Compatible(out string reason)
	{
		reason = null;
		try
		{
			if (_resolve("CUCoreLib.Saving.SaveCoordinator") != _coreCoordinator || _resolve("QoL_Unknown.SaveSystemPatcher") != _qolPatcher)
			{
				reason = "Checkpoint save dependency changed; rebind before capture.";
				return false;
			}
			foreach (MethodInfo item in _observed)
			{
				Patches patchInfo = Harmony.GetPatchInfo((MethodBase)item);
				if (patchInfo != null && (!Check(item, patchInfo.Prefixes, "prefix", out reason) || !Check(item, patchInfo.Postfixes, "postfix", out reason) || !Check(item, patchInfo.Transpilers, "transpiler", out reason) || !Check(item, patchInfo.Finalizers, "finalizer", out reason)))
				{
					return false;
				}
			}
			return true;
		}
		catch (Exception ex)
		{
			reason = "Checkpoint patch audit failed: " + ex.GetType().Name;
			return false;
		}
	}

	private bool Check(MethodInfo target, IEnumerable<Patch> patches, string kind, out string reason)
	{
		reason = null;
		foreach (Patch patch in patches)
		{
			MethodInfo method = patch.PatchMethod;
			if (OwnPatch(target, patch.owner, method, kind) || TimingOnly(patch.owner, method, kind) || IntervalPatch(target, patch.owner, method, kind) || _known.Any(x => x.Target == target && x.Method == method && x.Owner == patch.owner && x.Kind == kind))
				continue;
			reason = "Unreviewed save patch: " + target.DeclaringType?.FullName + "." + target.Name + " <- " + patch.owner + "/" + method?.DeclaringType?.FullName + "." + method?.Name + " (" + kind + ")";
			return false;
		}
		return true;
	}

	private bool OwnPatch(MethodInfo target, string owner, MethodInfo method, string kind)
	{
		if (owner != Owner || method == null || _codecs.Contains(target)) return false;
		string expected = null;
		if (kind == "transpiler" && RedirectMethods.Contains(target)) expected = "Redirect";
		else if (target == Tick && kind == "prefix") expected = "TickBefore";
		else if (target == Tick && kind == "finalizer") expected = "TickAfter";
		else if (target == WriteOne && kind == "prefix") expected = "WriteBefore";
		else if (target == WriteOne && kind == "finalizer") expected = "WriteAfter";
		else if (target == SaveGame && kind == "prefix") expected = "SaveBefore";
		else if (target == SaveGame && kind == "finalizer") expected = "SaveAfter";
		else if (kind == "prefix" && Barriers.Contains(target)) expected = "BarrierBefore";
		return expected != null && method == typeof(CheckpointHooks).GetMethod(expected, BindingFlags.Static | BindingFlags.NonPublic);
	}

	private bool IntervalPatch(MethodInfo target, string owner, MethodInfo method, string kind)
	{
		if (target != Tick || owner != "com.dden.morethreadonmp.checkpoint.interval" || kind != "transpiler" || method == null) return false;
		Type type = typeof(CheckpointBindings).Assembly.GetType("DDEN.MPreducer.Integration.CheckpointIntervalHooks", false);
		return method == type?.GetMethod("IntervalTranspiler", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(IEnumerable<CodeInstruction>) }, null);
	}

	private bool TimingOnly(string owner, MethodInfo method, string kind)
	{
		if (owner != "com.dden.perfdeep" || method == null || !method.IsStatic || method.ReturnType != typeof(void) || method.DeclaringType != _resolve("DDEN.NetPerfTracker.Enhanced.DeepHooks")) return false;
		Type stamp = method.DeclaringType.Assembly.GetType("DDEN.NetPerfTracker.Enhanced.CallStamp", false);
		if (stamp == null || !stamp.IsValueType) return false;
		ParameterInfo[] args = method.GetParameters();
		if (args.Length != 2) return false;
		return kind == "prefix" && method.Name == "Before" && args[0].ParameterType == typeof(MethodBase) && args[1].IsOut && args[1].ParameterType == stamp.MakeByRefType()
			|| kind == "finalizer" && method.Name == "After" && args[0].ParameterType == stamp && args[1].ParameterType == typeof(Exception);
	}

	private void AddKnown(string typeName, string methodName, string owner, string kind, bool required)
	{
		MethodInfo[] array = _resolve(typeName)?.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Where((MethodInfo x) => x.Name == methodName).ToArray() ?? Array.Empty<MethodInfo>();
		if (array.Length != 1)
		{
			if (required)
			{
				throw new MissingMethodException(typeName, methodName);
			}
		}
		else
		{
			_known.Add(new ExpectedPatch
			{
				Target = SaveGame,
				Method = array[0],
				Owner = owner,
				Kind = kind
			});
		}
	}

	private void AddBarriers(string typeName, bool required, params string[] names)
	{
		Type type = _resolve(typeName);
		if (type == null)
		{
			if (required)
			{
				throw new TypeLoadException(typeName);
			}
			return;
		}
		foreach (string name in names)
		{
			MethodInfo[] array = (from x in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
				where x.Name == name && !x.ContainsGenericParameters
				select x).ToArray();
			if (required && array.Length == 0)
			{
				throw new MissingMethodException(typeName, name);
			}
			MethodInfo[] array2 = array;
			foreach (MethodInfo item in array2)
			{
				if (!Barriers.Contains(item))
				{
					Barriers.Add(item);
				}
			}
		}
	}

	private Type RequireType(string name)
	{
		return _resolve(name) ?? throw new TypeLoadException(name);
	}

	private static MethodInfo RequireMethod(Type type, string name, Type result, Type[] arguments)
	{
		MethodInfo method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, arguments, null);
		if (method == null || !method.IsStatic || method.ReturnType != result || method.ContainsGenericParameters)
		{
			throw new MissingMethodException(type.FullName, name);
		}
		return method;
	}

	private static void Pin(Assembly assembly, string mvid)
	{
		if (!string.Equals(assembly.ManifestModule.ModuleVersionId.ToString(), mvid, StringComparison.OrdinalIgnoreCase))
		{
			throw new NotSupportedException("Untested checkpoint dependency: " + assembly.GetName().Name + "/" + assembly.ManifestModule.ModuleVersionId.ToString());
		}
	}

	private static Func<T> StaticGetter<T>(Type type, string name)
	{
		return Expression.Lambda<Func<T>>(Expression.Convert(Member(type, name, null), typeof(T)), Array.Empty<ParameterExpression>()).Compile();
	}

	private static Func<object, T> InstanceGetter<T>(Type type, string name)
	{
		ParameterExpression parameterExpression = Expression.Parameter(typeof(object), "instance");
		return Expression.Lambda<Func<object, T>>(Expression.Convert(Member(type, name, Expression.Convert(parameterExpression, type)), typeof(T)), new ParameterExpression[1] { parameterExpression }).Compile();
	}

	private static Expression Member(Type type, string name, Expression instance)
	{
		FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
		if (field != null && field.IsStatic == (instance == null))
		{
			return Expression.Field(instance, field);
		}
		MethodInfo methodInfo = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetGetMethod(nonPublic: true);
		if (methodInfo != null && methodInfo.IsStatic == (instance == null))
		{
			return Expression.Call(instance, methodInfo);
		}
		throw new MissingMemberException(type.FullName, name);
	}
}
