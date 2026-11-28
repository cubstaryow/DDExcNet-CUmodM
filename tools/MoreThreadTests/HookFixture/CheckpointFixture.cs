// Owned fixture recovered from the 2026-11-28 test executable; never loads or executes game assemblies.
#nullable enable annotations
using CUCoreLib.Saving;
using DDEN.MPreducer.Core;
using DDEN.MPreducer.Integration;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using QoL_Unknown;
using System.Collections.Generic;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System;
using Together;

internal static class CheckpointFixtureState
{
	internal static readonly int MainThread = Thread.CurrentThread.ManagedThreadId;

	internal static readonly List<int> UnityAccessThreads = new List<int>();

	internal static string Root;

	internal static int ZipCalls;

	internal static int UnzipCalls;

	internal static int GameSaveCalls;

	internal static void UnityAccess()
	{
		int managedThreadId = Thread.CurrentThread.ManagedThreadId;
		UnityAccessThreads.Add(managedThreadId);
		if (managedThreadId != MainThread)
		{
			throw new InvalidOperationException("Unity fixture accessed outside main thread");
		}
	}

	internal static byte[] Compress(string text)
	{
		byte[] bytes = Encoding.ASCII.GetBytes(text);
		using MemoryStream memoryStream = new MemoryStream();
		using (GZipStream gZipStream = new GZipStream(memoryStream, CompressionLevel.Optimal, leaveOpen: true))
		{
			gZipStream.Write(bytes, 0, bytes.Length);
		}
		return memoryStream.ToArray();
	}

	internal static string Expand(byte[] bytes)
	{
		using MemoryStream stream = new MemoryStream(bytes);
		using GZipStream stream2 = new GZipStream(stream, CompressionMode.Decompress);
		using StreamReader streamReader = new StreamReader(stream2, Encoding.ASCII);
		return streamReader.ReadToEnd();
	}

	internal static JObject Read(string path)
	{
		return JObject.Parse(Expand(File.ReadAllBytes(path)));
	}

	internal static void Seed(string path, JObject root)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path));
		File.WriteAllBytes(path, Compress(root.ToString(Formatting.None)));
	}
}

public sealed class WorldGeneration
{
	public static WorldGeneration world = new WorldGeneration();

	public bool worldExists = true;

	public bool generatingWorld;

	public int biomeDepth = 7;
}

public static class SaveSystem
{
	public static string Destination;

	public static string Marker = "fresh";
    public static JObject MutablePayload = new JObject { ["value"] = 1 };

	public static Action DuringSave;

	public static string LoadedMarker;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void SaveGame()
	{
		CheckpointFixtureState.UnityAccess();
		CheckpointFixtureState.GameSaveCalls++;
		JObject jObject = new JObject();
		jObject["marker"] = Marker;
        jObject["payload"] = MutablePayload;
		jObject["biome"] = WorldGeneration.world.biomeDepth + 1;
		jObject["untouched"] = new JObject
		{
			["unicode"] = "检查点",
			["array"] = new JArray(1, null, "three")
		};
		JObject jObject2 = jObject;
		File.WriteAllBytes(Path.Combine(Destination, "save.sv"), Zip(jObject2.ToString(Formatting.None)));
		DuringSave?.Invoke();
		PlayerCheckpoint.StampCrafted(Destination, new List<int> { 2, 5, 8 });
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static byte[] Zip(string text)
	{
		Interlocked.Increment(ref CheckpointFixtureState.ZipCalls);
		return CheckpointFixtureState.Compress(text);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string Unzip(byte[] bytes)
	{
		Interlocked.Increment(ref CheckpointFixtureState.UnzipCalls);
		return CheckpointFixtureState.Expand(bytes);
	}

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void TryLoadGame() { LoadGame(); }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool HasSave() { return File.Exists(Path.Combine(Destination, "save.sv")); }
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void LoadGame()
	{
		LoadedMarker = (string?)CheckpointFixtureState.Read(Path.Combine(Destination, "save.sv"))["marker"];
	}
}

internal sealed class FixtureRootConverter : JsonConverter
{
	public override bool CanConvert(Type objectType)
	{
		return objectType == typeof(JObject);
	}

	public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
	{
		JObject jObject = (JObject)((JObject)value).DeepClone();
		jObject["converterWitness"] = true;
		jObject.WriteTo(writer);
	}

	public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
	{
		throw new NotSupportedException();
	}
}

internal static class CheckpointProgram
{
	private static int _checks;

	private static readonly List<int> CompressThreads = new List<int>();

	private static int _compressCalls;

	private static string Personal => Path.Combine(CheckpointFixtureState.Root, PlayerCheckpoint.Player.Id, "save.sv");

	private static string Shared => Path.Combine(CheckpointFixtureState.Root, "save.sv");

	private static void Check(bool value, string message)
	{
		if (!value)
		{
			throw new InvalidOperationException(message);
		}
		_checks++;
	}

	private static void UnreviewedSaveHook()
	{
	}

	private static byte[] WorkerCompress(string text)
	{
		lock (CompressThreads)
		{
			CompressThreads.Add(Thread.CurrentThread.ManagedThreadId);
			_compressCalls++;
		}
		return CheckpointWriter.Compress(text);
	}

	private static void Seed()
	{
		CheckpointFixtureState.Seed(Personal, new JObject { ["marker"] = "old-player" });
		CheckpointFixtureState.Seed(Shared, new JObject { ["marker"] = "old-shared" });
		CheckpointFixtureState.ZipCalls = (CheckpointFixtureState.UnzipCalls = 0);
		SaveSystem.Marker = "fresh";
		SaveSystem.Destination = Path.GetDirectoryName(Personal);
	}

	public static void Main(string[] args)
	{
		string fullPath = Path.GetFullPath(Path.GetTempPath());
		string text = (CheckpointFixtureState.Root = Path.GetFullPath(Path.Combine(fullPath, "DDEN-checkpoint-harmony-" + Guid.NewGuid().ToString("N"))));
		try
		{
			Directory.CreateDirectory(text);
            string game = args.Length > 0 ? args[0] : @"L:\SteamLibrary\steamapps\common\Casualties Unknown";
            int metadataChecks = CheckpointBinaryContract.Verify(game, Check);
            int beforeWriter = _checks;
            CheckpointWriterTests.Run(Check);
            int writerChecks = _checks - beforeWriter;
            var cclHarmony = new Harmony("net.cucorelib");
            var saveTarget = typeof(SaveSystem).GetMethod("SaveGame");
            cclHarmony.Patch(saveTarget, postfix: new HarmonyMethod(typeof(CUCoreLib.Patches.SaveSystemPatches), "SaveGame_Postfix"));
            try { Run(); CheckpointAdditionalFixture.Run(Check); }
            finally { cclHarmony.Unpatch(saveTarget, HarmonyPatchType.All, cclHarmony.Id); }
            Console.WriteLine("PASS " + _checks + " checkpoint checks (metadata=" + metadataChecks + ", writer=" + writerChecks + ", Harmony=" + (_checks - metadataChecks - writerChecks) + "); temporary files and owned fixtures only");
		}
		catch (Exception value)
		{
			Console.WriteLine(value);
			Environment.ExitCode = 1;
		}
		finally
		{
			string value2 = fullPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
			if (text.StartsWith(value2, StringComparison.OrdinalIgnoreCase) && Directory.Exists(text))
			{
				Directory.Delete(text, recursive: true);
			}
		}
	}

	private static void Run()
	{
		Seed();
		PlayerCheckpoint.Tick();
		JObject t = CheckpointFixtureState.Read(Personal);
		JObject t2 = CheckpointFixtureState.Read(Shared);
		Check(CheckpointFixtureState.ZipCalls > 2 && CheckpointFixtureState.UnzipCalls > 0, "native fixture exercises repeated save/Stamp compression");
		CheckpointBindings bindings = new CheckpointBindings(verifyIdentity: false, (string name) => typeof(SaveSystem).Assembly.GetType(name, throwOnError: false));
		CheckpointWriter writer = new CheckpointWriter(WorkerCompress);
		List<string> list = new List<string>();
		CheckpointHooks checkpointHooks = new CheckpointHooks(bindings, writer, list.Add);
		try
		{
			checkpointHooks.Install();
			checkpointHooks.Configure(enabled: true);
			Seed();
			int gameSaveCalls = CheckpointFixtureState.GameSaveCalls;
			int providerCalls = SaveCoordinator.ProviderCalls;
			int compressCalls = _compressCalls;
			PlayerCheckpoint.Tick();
			Check(checkpointHooks.Captured == 1 && checkpointHooks.Problem == null, "periodic Tick/WriteOne(false) enters capture");
			Check((string?)CheckpointFixtureState.Read(Personal)["marker"] == "old-player", "worker preparation cannot replace final files before main-thread commit");
			Check(CheckpointFixtureState.GameSaveCalls == gameSaveCalls + 1 && SaveCoordinator.ProviderCalls == providerCalls + 1, "original game snapshot and CCL provider each execute exactly once");
			Check(CheckpointFixtureState.ZipCalls == 0 && CheckpointFixtureState.UnzipCalls == 0, "scope combines all native compression/decompression passes");
			checkpointHooks.Drain();
			Check(checkpointHooks.Completed == 1 && !checkpointHooks.Busy && _compressCalls == compressCalls + 2, "one background compression for each final player/shared document");
			Check((bool)CheckpointFixtureState.Read(Personal)["CUCoreLib"]["fixture"]["sawNativeAscii"], "CCL provider observes the native ASCII roundtrip before deriving saved fields");
			Check(JToken.DeepEquals(t, CheckpointFixtureState.Read(Personal)) && JToken.DeepEquals(t2, CheckpointFixtureState.Read(Shared)), "merged fields and opaque CCL data exactly match native JSON");
			Check(CheckpointFixtureState.Read(Personal)["worldSpawn"] == null && CheckpointFixtureState.Read(Shared)["worldSpawn"] != null, "host-copy spawn stamp never mutates personal document");
			Check(CompressThreads.All((int id) => id != CheckpointFixtureState.MainThread) && CheckpointFixtureState.UnityAccessThreads.All((int id) => id == CheckpointFixtureState.MainThread), "compression runs on worker while all Unity/provider reads stay on main thread");
			checkpointHooks.Configure(enabled: false);
			Seed();
			PlayerCheckpoint.FailBeforeSpeciesWrite = true;
			PlayerCheckpoint.Tick();
			JObject t3 = CheckpointFixtureState.Read(Personal);
			checkpointHooks.Configure(enabled: true);
			Seed();
			PlayerCheckpoint.Tick();
			checkpointHooks.Drain();
			Check(CheckpointFixtureState.Read(Personal)["ggSpecies"] == null && JToken.DeepEquals(t3, CheckpointFixtureState.Read(Personal)), "Stamp mutation followed by caught failure without Write cannot leak uncommitted JSON fields");
			PlayerCheckpoint.FailBeforeSpeciesWrite = false;
			JsonConvert.DefaultSettings = () => new JsonSerializerSettings
			{
				Converters = new List<JsonConverter>
				{
					new FixtureRootConverter()
				}
			};
			try
			{
				Seed();
				PlayerCheckpoint.Tick();
				checkpointHooks.Drain();
				Check((bool?)CheckpointFixtureState.Read(Personal)["converterWitness"] == true, "original JsonConvert default-settings converter still participates in CCL serialization");
			}
			finally
			{
				JsonConvert.DefaultSettings = null;
			}
			Seed();
			long captured = checkpointHooks.Captured;
			PlayerCheckpoint.WriteOne(PlayerCheckpoint.Player, nextLayer: false);
			Check(checkpointHooks.Captured == captured && CheckpointFixtureState.ZipCalls > 0, "direct WriteOne outside periodic Tick stays synchronous");
			Seed();
			PlayerCheckpoint.WriteLayerTransition();
			Check(checkpointHooks.Captured == captured && (int)CheckpointFixtureState.Read(Personal)["biome"] == 8, "layer-transition save retains next-layer biome and native route");
			Seed();
			checkpointHooks.Configure(enabled: false);
			PlayerCheckpoint.Tick();
			Check(checkpointHooks.Captured == captured && CheckpointFixtureState.ZipCalls > 0, "feature disabled preserves original Tick save");
			checkpointHooks.Configure(enabled: true);
			Seed();
			Net.Server = false;
			PlayerCheckpoint.Tick();
			Net.Server = true;
			Check(checkpointHooks.Captured == captured && (string?)CheckpointFixtureState.Read(Personal)["marker"] == "old-player", "client role never captures checkpoints");
			SavesystemPatch.PreserveSave = true;
			PlayerCheckpoint.Tick();
			SavesystemPatch.PreserveSave = false;
			Check(checkpointHooks.Captured == captured && (string?)CheckpointFixtureState.Read(Personal)["marker"] == "old-player", "preserve-save guard remains effective");
			PlayerCheckpoint.Player = new ScavPlayer
			{
				Id = "guest",
				IsLocal = false
			};
			Seed();
			compressCalls = _compressCalls;
			PlayerCheckpoint.Tick();
			checkpointHooks.Drain();
			Check(_compressCalls == compressCalls + 1 && (string?)CheckpointFixtureState.Read(Shared)["marker"] == "old-shared", "guest checkpoint prepares only its own file");
			PlayerCheckpoint.Player = new ScavPlayer();
			Seed();
			SaveSystem.Marker = "pending-manual";
			PlayerCheckpoint.Tick();
			SaveSystem.Marker = "manual-newer";
			SaveSystem.SaveGame();
			checkpointHooks.Drain();
			Check((string?)CheckpointFixtureState.Read(Personal)["marker"] == "manual-newer", "manual SaveGame drains older checkpoint before native write");
			Seed();
			SaveSystem.Marker = "pending-qol";
			PlayerCheckpoint.Tick();
			MultiplayerSaveBundle.SaveHostBundle();
			checkpointHooks.Drain();
			Check((string?)CheckpointFixtureState.Read(Personal)["marker"] == "qol-newer", "QoL bundle barrier prevents stale checkpoint overwrite");
			Seed();
			SaveSystem.Marker = "pending-read";
			PlayerCheckpoint.Tick();
			Check(MultiplayerSaveBundle.TryReadLiveMpPlayerInventorySnapshot() == "pending-read", "QoL reader drains checkpoint before observing saved inventory");
			Seed();
			SaveSystem.Marker = "pending-load";
			PlayerCheckpoint.Tick();
			PlayerCheckpoint.TryLoadBodyOnly(PlayerCheckpoint.Player);
			Check(SaveSystem.LoadedMarker == "pending-load", "checkpoint load barrier sees latest completed document");
			Seed();
			SaveSystem.Marker = "pending-stamp";
			PlayerCheckpoint.Tick();
			PlayerCheckpoint.StampSpecies(Path.GetDirectoryName(Personal), "external-newer", 42);
			checkpointHooks.Drain();
			Check((string?)CheckpointFixtureState.Read(Personal)["ggSpecies"] == "external-newer", "external Stamp drains then updates latest file");
			Seed();
			SaveSystem.Marker = "shutdown-drain";
			PlayerCheckpoint.Tick();
			Net.ShutdownReset();
			Check(!checkpointHooks.Busy && (string?)CheckpointFixtureState.Read(Personal)["marker"] == "shutdown-drain", "network shutdown drains snapshot before session reset");
			Net.Running = true;
			Seed();
			SaveSystem.Marker = "old-world";
			PlayerCheckpoint.Tick();
			WorldGeneration world = WorldGeneration.world;
			WorldGeneration.world = new WorldGeneration();
			checkpointHooks.Drain();
			WorldGeneration.world = world;
			Check(!checkpointHooks.Busy && (string?)CheckpointFixtureState.Read(Personal)["marker"] == "old-player", "replaced world discards stale checkpoint without writing its files");
			Seed();
			SaveSystem.Marker = "external-conflict";
			gameSaveCalls = CheckpointFixtureState.GameSaveCalls;
			PlayerCheckpoint.Tick();
			CheckpointFixtureState.Seed(Personal, new JObject { ["marker"] = "outside-newer" });
			checkpointHooks.Drain();
			Check((string?)CheckpointFixtureState.Read(Personal)["marker"] == "outside-newer" && (string?)CheckpointFixtureState.Read(Shared)["marker"] == "old-shared", "unexpected external writer prevents all stale document commits");
			Check(checkpointHooks.Problem != null && CheckpointFixtureState.GameSaveCalls == gameSaveCalls + 1, "conflict is visible and never replays over newer external data");
			checkpointHooks.Configure(enabled: false);
			checkpointHooks.Configure(enabled: true);
			MethodInfo method = typeof(SaveSystem).GetMethod("SaveGame");
			Harmony harmony = new Harmony("org.bepinex.plugins.qol.savesystem");
			harmony.Patch(method, new HarmonyMethod(typeof(SaveSystemPatcher), "SaveGame_Prefix_AdjustDepth"), new HarmonyMethod(typeof(SaveSystemPatcher), "SaveGame_Postfix_RestoreDepthForCapture"), null, new HarmonyMethod(typeof(SaveSystemPatcher), "SaveGame_Finalizer_RestoreState"), null);
			try
			{
				Seed();
				captured = checkpointHooks.Captured;
				int calls = SaveSystemPatcher.Calls;
				PlayerCheckpoint.Tick();
				checkpointHooks.Drain();
				Check(checkpointHooks.Captured == captured + 1 && SaveSystemPatcher.Calls == calls + 3, "reviewed QoL prefix/postfix/finalizer remain intact during capture");
			}
			finally
			{
				harmony.Unpatch(method, HarmonyPatchType.All, harmony.Id);
			}
			Harmony harmony2 = new Harmony("fixture.checkpoint.unreviewed");
			harmony2.Patch(method, new HarmonyMethod(typeof(CheckpointProgram), "UnreviewedSaveHook"));
			try
			{
				Seed();
				captured = checkpointHooks.Captured;
				PlayerCheckpoint.Tick();
				Check(checkpointHooks.Captured == captured && checkpointHooks.Problem != null && CheckpointFixtureState.ZipCalls > 0, "new unreviewed patch causes visible native fallback without snapshot suppression");
			}
			finally
			{
				harmony2.Unpatch(method, HarmonyPatchType.All, harmony2.Id);
			}
			checkpointHooks.Configure(enabled: false);
			checkpointHooks.Configure(enabled: true);
			MethodInfo method2 = typeof(SaveSystem).GetMethod("Zip");
			harmony2.Patch(method2, null, new HarmonyMethod(typeof(CheckpointProgram), "UnreviewedSaveHook"));
			try
			{
				Seed();
				captured = checkpointHooks.Captured;
				PlayerCheckpoint.Tick();
				Check(checkpointHooks.Captured == captured && checkpointHooks.Problem != null && CheckpointFixtureState.ZipCalls > 0, "unreviewed codec postfix prevents bypassing changed compression semantics");
			}
			finally
			{
				harmony2.Unpatch(method2, HarmonyPatchType.All, harmony2.Id);
			}
			checkpointHooks.Configure(enabled: false);
			checkpointHooks.Configure(enabled: true);
			Seed();
			SaveSystem.Marker = "disable-drain";
			PlayerCheckpoint.Tick();
			checkpointHooks.Configure(enabled: false);
			Check(!checkpointHooks.Busy && (string?)CheckpointFixtureState.Read(Personal)["marker"] == "disable-drain", "feature shutdown waits for and commits accepted snapshot");
			checkpointHooks.Configure(enabled: true);
			Seed();
			gameSaveCalls = CheckpointFixtureState.GameSaveCalls;
			bool nested = false;
			SaveSystem.DuringSave = () =>
			{
				if (!nested)
				{
					nested = true;
					SaveSystem.Marker = "inner-save";
					SaveSystem.SaveGame();
				}
			};
			PlayerCheckpoint.Tick();
			SaveSystem.DuringSave = null;
			checkpointHooks.Drain();
			Check(CheckpointFixtureState.GameSaveCalls == gameSaveCalls + 2 && (string?)CheckpointFixtureState.Read(Personal)["marker"] == "inner-save", "reentrant SaveGame materializes once and preserves newer native save without duplicate sampling");
			Check(checkpointHooks.Problem != null && !checkpointHooks.Busy, "reentrant downgrade is visible and leaves no asynchronous stale write");
			checkpointHooks.Configure(enabled: false);
			checkpointHooks.Configure(enabled: true);
			Seed();
			SaveSystem.Marker = "current-provider-read";
			string providerObserved = null;
			captured = checkpointHooks.Captured;
			gameSaveCalls = CheckpointFixtureState.GameSaveCalls;
			SaveSystem.DuringSave = () =>
			{
				providerObserved = MultiplayerSaveBundle.TryReadLiveMpPlayerInventorySnapshot();
			};
			try
			{
				PlayerCheckpoint.Tick();
			}
			finally
			{
				SaveSystem.DuringSave = null;
			}
			checkpointHooks.Drain();
			Check(providerObserved == "current-provider-read" && (string?)CheckpointFixtureState.Read(Personal)["marker"] == "current-provider-read", "reentrant QoL reader observes current checkpoint snapshot instead of previous disk version");
			Check(!checkpointHooks.Busy && checkpointHooks.Captured == captured && CheckpointFixtureState.GameSaveCalls == gameSaveCalls + 1, "reentrant reader materializes once and leaves no stale async result or duplicate game sampling");
			checkpointHooks.Configure(enabled: false);
			checkpointHooks.Configure(enabled: true);
			Seed();
			SaveSystem.Marker = "before-provider-write";
			captured = checkpointHooks.Captured;
			gameSaveCalls = CheckpointFixtureState.GameSaveCalls;
			SaveSystem.DuringSave = MultiplayerSaveBundle.SaveHostBundle;
			try
			{
				PlayerCheckpoint.Tick();
			}
			finally
			{
				SaveSystem.DuringSave = null;
			}
			checkpointHooks.Drain();
			Check(MultiplayerSaveBundle.BundleInputMarker == "before-provider-write" && (string?)CheckpointFixtureState.Read(Personal)["marker"] == "qol-newer" && (string?)CheckpointFixtureState.Read(Shared)["marker"] == "qol-newer", "reentrant QoL bundle write remains newer through subsequent native stamps and host copy");
			Check(!checkpointHooks.Busy && checkpointHooks.Captured == captured && CheckpointFixtureState.GameSaveCalls == gameSaveCalls + 1, "reentrant provider write cannot leave a delayed checkpoint that overwrites its result");
			checkpointHooks.Configure(enabled: false);
			checkpointHooks.Configure(enabled: true);
			bool[] array = new bool[2] { false, true };
			foreach (bool swallowNestedFailure in array)
			{
				Seed();
				gameSaveCalls = CheckpointFixtureState.GameSaveCalls;
				bool entered = false;
				JObject externalVersion = new JObject
				{
					["marker"] = "external-during-capture",
					["authoritative"] = new JArray(31, 47)
				};
				SaveSystem.DuringSave = () =>
				{
					if (entered)
					{
						return;
					}
					entered = true;
					CheckpointFixtureState.Seed(Personal, externalVersion);
					SaveSystem.Marker = "nested-stale";
					try
					{
						SaveSystem.SaveGame();
					}
					catch (IOException)
					{
						if (!swallowNestedFailure)
						{
							throw;
						}
					}
				};
				try
				{
					PlayerCheckpoint.Tick();
				}
				catch (IOException)
				{
				}
				finally
				{
					SaveSystem.DuringSave = null;
				}
				checkpointHooks.Drain();
				Check(JToken.DeepEquals(externalVersion, CheckpointFixtureState.Read(Personal)) && (string?)CheckpointFixtureState.Read(Shared)["marker"] == "old-shared", "reentrant materialization conflict preserves complete newer external file, including when the native caller catches the error");
				Check(CheckpointFixtureState.GameSaveCalls == gameSaveCalls + 1 && !checkpointHooks.Busy && checkpointHooks.Problem != null, "conflicting reentry never executes nested save body or schedules a stale asynchronous replay");
				checkpointHooks.Configure(enabled: false);
				checkpointHooks.Configure(enabled: true);
			}
			Seed();
			PlayerCheckpoint.ThrowAfterSave = true;
			bool flag = false;
			try
			{
				PlayerCheckpoint.Tick();
			}
			catch (InvalidOperationException)
			{
				flag = true;
			}
			finally
			{
				PlayerCheckpoint.ThrowAfterSave = false;
			}
			Check(flag && !checkpointHooks.Busy && (string?)CheckpointFixtureState.Read(Personal)["marker"] == "old-player", "failed capture preserves prior disk data and original exception");
			SaveSystem.Marker = "after-error";
			SaveSystem.SaveGame();
			Check((string?)CheckpointFixtureState.Read(Personal)["marker"] == "after-error", "failed capture cannot leak tokens into subsequent native saves");
			checkpointHooks.Configure(enabled: false);
			checkpointHooks.Configure(enabled: true);
			Seed();
			SaveSystem.Marker = "dispose-drain";
			PlayerCheckpoint.Tick();
			checkpointHooks.Dispose();
			Check((string?)CheckpointFixtureState.Read(Personal)["marker"] == "dispose-drain", "disposing drains accepted data before removing hooks");
			Seed();
			PlayerCheckpoint.Tick();
			Check(CheckpointFixtureState.ZipCalls > 0, "owned hooks are removed and native route restored");
			Check(list.Count > 0, "fallback explanations remain observable");
		}
		finally
		{
			SaveSystem.DuringSave = null;
			checkpointHooks.Dispose();
		}
		int failures = 0;
		CheckpointWriter writer2 = new CheckpointWriter((string text) =>
		{
			failures++;
			throw new IOException("fixture compressor failure");
		});
		using (CheckpointHooks checkpointHooks2 = new CheckpointHooks(new CheckpointBindings(verifyIdentity: false, (string name) => typeof(SaveSystem).Assembly.GetType(name, throwOnError: false)), writer2))
		{
			checkpointHooks2.Install();
			checkpointHooks2.Configure(enabled: true);
			Seed();
			int gameSaveCalls2 = CheckpointFixtureState.GameSaveCalls;
			PlayerCheckpoint.Tick();
			checkpointHooks2.Drain();
			Check(failures == 1 && checkpointHooks2.Problem != null && checkpointHooks2.Fallbacks == 1, "worker compression failure becomes visible and invokes one original save fallback");
			Check(CheckpointFixtureState.GameSaveCalls == gameSaveCalls2 + 2 && (string?)CheckpointFixtureState.Read(Personal)["marker"] == "fresh", "original fallback writes valid checkpoint after preparation failure");
			PlayerCheckpoint.Tick();
			Check(failures == 1 && !checkpointHooks2.Busy, "latched worker failure does not retry compression on every periodic tick");
		}

	}
}

namespace DDEN.Shared
{

public static class Mp
{
	public static bool IsServer => Net.IsServer;

	public static bool IsRunning => Net.IsRunning;
}
}

namespace DDEN.Shared
{

public static class Reflect
{
	public static Type Type(string name)
	{
		return typeof(SaveSystem).Assembly.GetType(name, throwOnError: false);
	}

	public static object GetStatic(string type, string member)
	{
		Type type2 = Type(type);
		BindingFlags bindingAttr = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
		object obj = type2?.GetField(member, bindingAttr)?.GetValue(null);
		if (obj == null)
		{
			if ((object)type2 == null)
			{
				return null;
			}
			PropertyInfo property = type2.GetProperty(member, bindingAttr);
			if ((object)property == null)
			{
				return null;
			}
			obj = property.GetValue(null, null);
		}
		return obj;
	}
}
}

namespace Together
{

public sealed class ScavPlayer
{
	public string Id = "host";

	public bool IsLocal = true;

	public string Species = "fixture-species";

	public int Slot = 3;
}
}

namespace Together
{

public static class Net
{
	public static bool Running = true;

	public static bool Server = true;

	public static bool IsRunning => Running;

	public static bool IsServer => Server;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ShutdownReset()
	{
		Running = false;
	}
}
}

namespace Together
{

public static class SharedMain
{
	public static bool LocalWorldIsGenerated
	{
		get
		{
			if (WorldGeneration.world != null)
			{
				return WorldGeneration.world.worldExists;
			}
			return false;
		}
	}
}
}

namespace Together
{

public static class SavesystemPatch
{
	public static bool PreserveSave;

	public static bool InDebugMap;

	public static string SessionSaveFolder => CheckpointFixtureState.Root;
}
}

namespace Together
{

public static class PlayerCheckpoint
{
	public static ScavPlayer Player = new ScavPlayer();

	public static bool ThrowAfterSave;

	public static bool NestedWrite;

	public static bool FailBeforeSpeciesWrite;

	public static int TickCalls;
    private static float _wait;
    internal static float Elapsed { get => _wait; set => _wait = value; }
    internal static float Delta = 31f;

	public static int WriteCalls;

	private static bool _writing;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void Tick()
	{
		TickCalls++;
        if (!Net.IsServer || !Net.IsRunning || !SharedMain.LocalWorldIsGenerated) return;
        _wait += Delta;
        if (_wait < 30f) return;
        _wait = 0f;
        WriteOne(Player, nextLayer: false);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string Folder(ScavPlayer player)
	{
		CheckpointFixtureState.UnityAccess();
		return Path.Combine(SavesystemPatch.SessionSaveFolder, player.Id);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void WriteOne(ScavPlayer player, bool nextLayer)
	{
		CheckpointFixtureState.UnityAccess();
		WriteCalls++;
		if (_writing || SavesystemPatch.PreserveSave || !Net.IsServer || player == null)
		{
			return;
		}
		_writing = true;
		string destination = SaveSystem.Destination;
		try
		{
			string text = Folder(player);
			Directory.CreateDirectory(text);
			SaveSystem.Destination = text;
			SaveSystem.SaveGame();
			if (ThrowAfterSave)
			{
				throw new InvalidOperationException("fixture failure after snapshot");
			}
			if (!nextLayer)
			{
				StoreCurrentBiome(text, WorldGeneration.world.biomeDepth);
			}
			StampSpecies(text, player.Species, player.Slot);
			if (NestedWrite)
			{
				WriteOne(player, nextLayer: false);
			}
			if (player.IsLocal)
			{
				CopyHostFlag(text);
			}
		}
		finally
		{
			SaveSystem.Destination = destination;
			_writing = false;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void StampCrafted(string folder, List<int> crafted)
	{
		string path = Path.Combine(folder, "save.sv");
		if (File.Exists(path))
		{
			JObject jObject = JObject.Parse(SaveSystem.Unzip(File.ReadAllBytes(path)));
			jObject["craftedBefore"] = new JArray(crafted);
			File.WriteAllBytes(path, SaveSystem.Zip(jObject.ToString(Formatting.None)));
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void StoreCurrentBiome(string folder, int biome)
	{
		string path = Path.Combine(folder, "save.sv");
		JObject jObject = JObject.Parse(SaveSystem.Unzip(File.ReadAllBytes(path)));
		jObject["biome"] = biome;
		File.WriteAllBytes(path, SaveSystem.Zip(jObject.ToString(Formatting.None)));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void StampSpecies(string folder, string species, int slot)
	{
		string path = Path.Combine(folder, "save.sv");
		if (!File.Exists(path))
		{
			return;
		}
		try
		{
			JObject jObject = JObject.Parse(SaveSystem.Unzip(File.ReadAllBytes(path)));
			jObject["ggSpecies"] = species;
			jObject["ggSpeciesSlot"] = slot;
			if (FailBeforeSpeciesWrite)
			{
				throw new IOException("fixture stamp failed before final write");
			}
			File.WriteAllBytes(path, SaveSystem.Zip(jObject.ToString(Formatting.None)));
		}
		catch (IOException)
		{
			if (!FailBeforeSpeciesWrite)
			{
				throw;
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void CopyHostFlag(string folder)
	{
		string text = Path.Combine(folder, "save.sv");
		if (File.Exists(text))
		{
			File.Copy(text, Path.Combine(SavesystemPatch.SessionSaveFolder, "save.sv"), overwrite: true);
		}
		WorldSpawn.Stamp();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void WriteLayerTransition()
	{
		WriteOne(Player, nextLayer: true);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool TryLoadBodyOnly(ScavPlayer player)
	{
		SaveSystem.LoadedMarker = (string?)CheckpointFixtureState.Read(Path.Combine(Folder(player), "save.sv"))["marker"];
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void OnHostTransferInstalled()
	{
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void RememberSpecies(string id, string species, int slot)
	{
		StampSpecies(Path.Combine(SavesystemPatch.SessionSaveFolder, id), species, slot);
	}
}
}

namespace Together
{

public static class WorldSpawn
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void Stamp()
	{
		string path = Path.Combine(SavesystemPatch.SessionSaveFolder, "save.sv");
		if (File.Exists(path))
		{
			CheckpointFixtureState.UnityAccess();
			JObject jObject = JObject.Parse(SaveSystem.Unzip(File.ReadAllBytes(path)));
			jObject["worldSpawn"] = new JArray(12.5, 88.0);
			File.WriteAllBytes(path, SaveSystem.Zip(jObject.ToString(Formatting.None)));
		}
	}
}
}

namespace CUCoreLib.Saving
{

public static class SaveCoordinator
{
	public static int ProviderCalls;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void EmbedIntoSaveFile()
	{
		string path = Path.Combine(SaveSystem.Destination, "save.sv");
		if (File.Exists(path))
		{
			JObject jObject = JObject.Parse(SaveSystem.Unzip(File.ReadAllBytes(path)));
			jObject["CUCoreLib"] = CaptureProvider(jObject);
			File.WriteAllBytes(path, SaveSystem.Zip(JsonConvert.SerializeObject(jObject, Formatting.None)));
		}
	}

	private static JObject CaptureProvider(JObject saved)
	{
		CheckpointFixtureState.UnityAccess();
		ProviderCalls++;
		return new JObject { ["fixture"] = new JObject
		{
			["opaque"] = "provider-data",
			["value"] = 17,
			["sawNativeAscii"] = (string?)saved["untouched"]?["unicode"] == "???"
		} };
	}
}
}

namespace QoL_Unknown
{

public static class SaveSystemPatcher
{
	public static int Calls;

	public static void SaveGame_Prefix_AdjustDepth(out int? __state)
	{
		CheckpointFixtureState.UnityAccess();
		Calls++;
		__state = null;
	}

	public static void SaveGame_Postfix_RestoreDepthForCapture(int? __state)
	{
		CheckpointFixtureState.UnityAccess();
		Calls++;
	}

	public static Exception SaveGame_Finalizer_RestoreState(Exception __exception, int? __state)
	{
		CheckpointFixtureState.UnityAccess();
		Calls++;
		return __exception;
	}
}
}

namespace QoL_Unknown
{

public static class MultiplayerSaveBundle
{
	public static string BundleInputMarker;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void SaveHostBundle()
	{
		BundleInputMarker = (string?)CheckpointFixtureState.Read(Path.Combine(SaveSystem.Destination, "save.sv"))["marker"];
		CheckpointFixtureState.Seed(Path.Combine(SaveSystem.Destination, "save.sv"), new JObject { ["marker"] = "qol-newer" });
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void RestoreHostBundleToMultiplayerSave()
	{
		SaveHostBundle();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string TryReadLiveMpPlayerInventorySnapshot()
	{
		return (string?)CheckpointFixtureState.Read(Path.Combine(SaveSystem.Destination, "save.sv"))["marker"];
	}
}
}

namespace CUCoreLib.Patches
{
    public static class SaveSystemPatches
    {
        public static void SaveGame_Postfix() => CUCoreLib.Saving.SaveCoordinator.EmbedIntoSaveFile();
    }
}
