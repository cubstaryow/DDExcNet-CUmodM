using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using DDEN.MPreducer.Core;
using HarmonyLib;

namespace DDEN.MPreducer.Integration;

internal sealed class CheckpointHooks : IDisposable
{
	internal const string Owner = "com.dden.morethreadonmp.checkpoint";

	private static CheckpointHooks _current;

	private readonly Harmony _harmony;

	private readonly List<Tuple<MethodBase, MethodInfo>> _installed = new List<Tuple<MethodBase, MethodInfo>>();

	private readonly int _mainThread = Thread.CurrentThread.ManagedThreadId;

	private readonly CheckpointBindings _bindings;

	private readonly CheckpointWriter _writer;

	private readonly Action<string> _report;

	private CheckpointCapture _capture;

	private CheckpointCapture _pending;

	private int _tickDepth;

	private int _saveDepth;

	private int _bypass;

	private long _epoch = 1L;

	private long _revision;

	private object _world;

	private string _session;

	private bool _disposed;
	private bool _disposeRequested;

	internal long Captured;

	internal long Completed;

	internal long Fallbacks;

	internal long Conflicts;

	internal long MergedWrites;

	internal double PeakCaptureMs;

	internal double PeakPrepareMs;

	internal double PeakCommitMs;

	internal double PeakBarrierMs;

	internal bool Enabled { get; private set; }

	internal string Problem { get; private set; }

	internal bool Busy => _writer.Busy;

	private bool OnMain => Thread.CurrentThread.ManagedThreadId == _mainThread;

	private static CheckpointHooks Current
	{
		get
		{
			if (_current == null || !_current.OnMain || _current._disposed)
			{
				return null;
			}
			return _current;
		}
	}

	private static CheckpointCapture Capture => Current?._capture;

	internal CheckpointHooks(CheckpointBindings bindings = null, CheckpointWriter writer = null, Action<string> report = null)
	{
		_writer = writer ?? new CheckpointWriter();
		_report = report;
		try
		{
			_bindings = bindings ?? new CheckpointBindings();
			_harmony = new Harmony(Owner);
		}
		catch
		{
			_writer.Dispose();
			throw;
		}
	}

	internal void Install()
	{
		RequireMain();
		if (_disposed) throw new ObjectDisposedException(nameof(CheckpointHooks));
		if (_installed.Count != 0)
		{
			return;
		}
		if (_current != null)
		{
			throw new InvalidOperationException("Checkpoint adapter already installed");
		}
		try
		{
			_current = this;
			foreach (MethodInfo redirectMethod in _bindings.RedirectMethods)
			{
				Patch(redirectMethod, null, null, "Redirect");
			}
			Patch(_bindings.Tick, "TickBefore", "TickAfter");
			Patch(_bindings.WriteOne, "WriteBefore", "WriteAfter");
			Patch(_bindings.SaveGame, "SaveBefore", "SaveAfter");
			foreach (MethodInfo item in _bindings.Barriers.Distinct())
			{
				if (item != _bindings.Tick && item != _bindings.WriteOne && item != _bindings.SaveGame)
				{
					Patch(item, "BarrierBefore");
				}
			}
		}
		catch
		{
			Dispose();
			throw;
		}
	}

	private void Patch(MethodBase method, string prefix = null, string finalizer = null, string transpiler = null)
	{
		if (method == null)
		{
			throw new MissingMethodException("Checkpoint hook unavailable");
		}
		HarmonyMethod MakeHook(string name, int priority)
		{
			if (name == null) return null;
			MethodInfo hook = typeof(CheckpointHooks).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
			if (hook == null) throw new MissingMethodException(typeof(CheckpointHooks).FullName, name);
			// Register before patching so a partially installed patch can still be removed.
			_installed.Add(Tuple.Create(method, hook));
			return new HarmonyMethod(hook) { priority = priority };
		}
		_harmony.Patch(method, MakeHook(prefix, Priority.First), null, MakeHook(transpiler, Priority.Last), MakeHook(finalizer, Priority.Last), null);
	}

	private void RequireMain()
	{
		if (!OnMain) throw new InvalidOperationException("Checkpoint integration must run on its owning main thread");
	}

	internal void Configure(bool enabled)
	{
		RequireMain();
		if (!_disposed)
		{
			if (_disposeRequested && enabled) throw new ObjectDisposedException(nameof(CheckpointHooks));
			if (!enabled && Enabled)
			{
				Drain();
			}
			if (enabled && !Enabled)
			{
				Problem = null;
			}
			Enabled = enabled;
		}
	}

	private bool SameSession(CheckpointCapture capture)
	{
		try
		{
			return capture.World == _bindings.WorldIdentity() && string.Equals(capture.Session, Path.GetFullPath(_bindings.SessionFolder()), StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return false;
		}
	}

	internal void Pump()
	{
		RequireMain();
		if (!_disposed)
		{
			if (_pending != null && !SameSession(_pending))
			{
				_epoch++;
			}
			if (_writer.TryTake(out var prepared))
			{
				Finish(prepared);
			}
		}
	}

	internal void Drain()
	{
		RequireMain();
		if (!_disposed && _writer.Busy)
		{
			long timestamp = Stopwatch.GetTimestamp();
			PreparedCheckpoint preparedCheckpoint = _writer.Drain();
			if (preparedCheckpoint != null)
			{
				Finish(preparedCheckpoint);
			}
			PeakBarrierMs = Math.Max(PeakBarrierMs, Ms(timestamp));
		}
	}

	private static double Ms(long started)
	{
		return (double)(Stopwatch.GetTimestamp() - started) * 1000.0 / (double)Stopwatch.Frequency;
	}

	private void Fail(string reason)
	{
		bool flag = Problem == null;
		Problem = reason;
		if (flag)
		{
			try { _report?.Invoke(reason); }
			catch { /* Reporting must never abort an original save or cleanup. */ }
		}
	}

	private void Replay(CheckpointCapture capture)
	{
		if (capture == null || !SameSession(capture))
		{
			return;
		}
		try
		{
			if (!capture.Unchanged())
			{
				Conflicts++;
				Fail("Checkpoint target changed; original retry skipped");
				return;
			}
		}
		catch (Exception ex)
		{
			Conflicts++;
			Fail("Checkpoint target cannot be verified; retry skipped: " + ex.GetType().Name);
			return;
		}
		_bypass++;
		try
		{
			Fallbacks++;
			_bindings.Replay(capture.Player);
		}
		catch (Exception ex2)
		{
			Fail("Original checkpoint retry failed: " + ex2.GetType().Name);
		}
		finally
		{
			_bypass--;
		}
	}

	private void Finish(PreparedCheckpoint prepared)
	{
		CheckpointCapture pending = _pending;
		_pending = null;
		bool flag = false;
		try
		{
			PeakPrepareMs = Math.Max(PeakPrepareMs, prepared.PrepareMilliseconds);
			if (pending == null || !SameSession(pending))
			{
				Conflicts++;
				return;
			}
			long timestamp = Stopwatch.GetTimestamp();
			CheckpointCommitResult checkpointCommitResult = prepared.TryCommit(_epoch, _revision);
			PeakCommitMs = Math.Max(PeakCommitMs, Ms(timestamp));
			if (checkpointCommitResult.Status == CheckpointCommitStatus.Committed)
			{
				Completed++;
				return;
			}
			if (checkpointCommitResult.ConflictDetected || checkpointCommitResult.Status == CheckpointCommitStatus.Conflict)
			{
				Conflicts++;
				Fail("Checkpoint target changed; stale result discarded");
				return;
			}
			if (checkpointCommitResult.Status == CheckpointCommitStatus.PartialCommit)
			{
				Fail("Checkpoint partially committed; optimization stopped, later saves use the original path");
				return;
			}
			Fail("Background checkpoint failed; using original save: " + (checkpointCommitResult.Error?.GetType().Name ?? "commit"));
			flag = true;
		}
		catch (Exception ex)
		{
			Fail("Checkpoint completion failed: " + ex.GetType().Name);
			flag = true;
		}
		finally
		{
			prepared.Dispose();
		}
		if (flag)
		{
			Replay(pending);
		}
	}

	private void Materialize(string reason)
	{
		CheckpointCapture capture = _capture;
		if (capture == null)
		{
			return;
		}
		try
		{
			capture.Materialize();
			_capture = null;
			Fallbacks++;
			Fail(reason);
		}
		catch (Exception failure)
		{
			Fail("Checkpoint materialization failed: " + (capture.Failure = failure).GetType().Name);
			throw;
		}
	}

	private static void TickBefore(out int __state)
	{
		CheckpointHooks current = Current;
		__state = current?._tickDepth ?? 0;
		if (current != null)
		{
			current._tickDepth++;
		}
	}

	private static void TickAfter(int __state)
	{
		if (Current != null)
		{
			Current._tickDepth = __state;
		}
	}

	private static void WriteBefore(object __0, bool __1, out CheckpointCapture __state)
	{
		__state = null;
		CheckpointHooks current = Current;
		if (current == null || current._bypass > 0)
		{
			return;
		}
		if (current._capture != null)
		{
			current.Materialize("Nested checkpoint retained on the main thread");
			return;
		}
		current.Drain();
		if (!current.Enabled || current.Problem != null || current._tickDepth != 1 || __1)
		{
			return;
		}
		try
		{
			if (!current._bindings.CanCapture()) return;
			if (!current._bindings.Compatible(out var reason))
			{
				current.Fail(reason);
				return;
			}
			object obj = current._bindings.WorldIdentity();
			string fullPath = Path.GetFullPath(current._bindings.SessionFolder());
			if (obj != current._world || !string.Equals(fullPath, current._session, StringComparison.OrdinalIgnoreCase))
			{
				current._epoch++;
				current._world = obj;
				current._session = fullPath;
			}
			current._capture = (__state = new CheckpointCapture(__0, obj, fullPath, current._bindings.PlayerFolder(__0)));
		}
		catch (Exception ex)
		{
			current.Fail("Checkpoint capture unavailable: " + ex.GetType().Name);
		}
	}

	private static void WriteAfter(CheckpointCapture __state, Exception __exception)
	{
		CheckpointHooks current = Current;
		if (current == null || __state == null)
		{
			return;
		}
		try
		{
			current.CompleteCapture(__state, __exception);
		}
		finally
		{
			// A save callback may disable or dispose the adapter. Keep virtual I/O alive
			// until WriteOne finishes, then synchronously finish this last transaction.
			if (!current.Enabled) current.Drain();
			if (current._disposeRequested) current.Dispose();
		}
	}

	private void CompleteCapture(CheckpointCapture __state, Exception __exception)
	{
		CheckpointHooks current = this;
		if (current._capture == __state)
		{
			current._capture = null;
		}
		if (__state.Materialized)
		{
			return;
		}
		current.PeakCaptureMs = Math.Max(current.PeakCaptureMs, Ms(__state.Started));
		if (__state.Failure != null || __exception != null)
		{
			current.Fail("Checkpoint capture aborted; original save retained");
			if (__exception == null)
			{
				current.Replay(__state);
			}
		}
		else
		{
			if (__state.FileCount == 0)
			{
				return;
			}
			try
			{
				CheckpointWriteTarget[] targets = __state.Targets();
				if (!current.SameSession(__state))
				{
					current.Conflicts++;
					return;
				}
				CheckpointWriteRequest request = new CheckpointWriteRequest(current._epoch, ++current._revision, targets);
				if (!current._writer.TryStart(request))
				{
					current.Fail("Checkpoint queue busy; original save retained");
					current.Replay(__state);
					return;
				}
				current._pending = __state;
				current.Captured++;
				current.MergedWrites += __state.Writes;
				__state.ReleaseBuffers();
			}
			catch (Exception ex)
			{
				current.Fail("Checkpoint preparation failed: " + ex.GetType().Name);
				current.Replay(__state);
			}
		}
	}

	private static void SaveBefore(out int __state)
	{
		CheckpointHooks current = Current;
		__state = current?._saveDepth ?? 0;
		if (current != null)
		{
			if (current._capture != null && current._saveDepth != 0)
			{
				current.Materialize("Nested SaveGame retained on the main thread");
			}
			if (current._capture == null)
			{
				current.Drain();
			}
			current._saveDepth++;
		}
	}

	private static void SaveAfter(int __state, Exception __exception)
	{
		CheckpointHooks current = Current;
		if (current != null)
		{
			current._saveDepth = __state;
			if (__exception != null && current._capture != null)
			{
				current._capture.Failure = __exception;
			}
		}
	}

	private static void BarrierBefore(MethodBase __originalMethod)
	{
		CheckpointHooks current = Current;
		if (current != null)
		{
			if (current._capture == null)
			{
				current.Drain();
			}
			else if (!(__originalMethod is MethodInfo item) || !current._bindings.CaptureLocalBarriers.Contains(item))
			{
				current.Materialize("Reentrant save reader or lifecycle operation retained on the main thread");
			}
		}
	}

	private static T Guard<T>(Func<CheckpointCapture, T> captured, Func<T> original)
	{
		CheckpointCapture capture = Capture;
		if (capture == null)
		{
			Current?.Drain();
			return original();
		}
		try
		{
			return captured(capture);
		}
		catch (Exception failure)
		{
			capture.Failure = failure;
			throw;
		}
	}

	private static bool Exists(string path)
	{
		return Guard((CheckpointCapture c) => c.Exists(path), () => File.Exists(path));
	}

	private static byte[] Read(string path)
	{
		return Guard((CheckpointCapture c) => c.Read(path), () => File.ReadAllBytes(path));
	}

	private static string Unzip(byte[] bytes)
	{
		return Guard((CheckpointCapture c) => c.Unzip(bytes), () => SaveSystem.Unzip(bytes));
	}

	private static byte[] Zip(string json)
	{
		return Guard((CheckpointCapture c) => c.Zip(json), () => SaveSystem.Zip(json));
	}

	private static void Write(string path, byte[] bytes)
	{
		Guard((CheckpointCapture c) =>
		{
			c.Write(path, bytes);
			return 0;
		}, () =>
		{
			File.WriteAllBytes(path, bytes);
			return 0;
		});
	}

	private static void Copy(string source, string target, bool overwrite)
	{
		Guard((CheckpointCapture c) =>
		{
			c.Copy(source, target, overwrite);
			return 0;
		}, () =>
		{
			File.Copy(source, target, overwrite);
			return 0;
		});
	}

	internal static IEnumerable<CodeInstruction> Redirect(IEnumerable<CodeInstruction> instructions)
	{
		List<CodeInstruction> list = instructions.ToList();
		int num = 0;
		foreach (CodeInstruction item in list)
		{
			if ((item.opcode != OpCodes.Call && item.opcode != OpCodes.Callvirt) || !(item.operand is MethodInfo methodInfo))
			{
				continue;
			}
			string text = null;
			Type[] first = (from p in methodInfo.GetParameters()
				select p.ParameterType).ToArray();
			if (methodInfo.DeclaringType == typeof(File))
			{
				if (methodInfo.Name == "Exists" && Enumerable.SequenceEqual(first, new Type[1] { typeof(string) }))
				{
					text = "Exists";
				}
				else if (methodInfo.Name == "ReadAllBytes" && Enumerable.SequenceEqual(first, new Type[1] { typeof(string) }))
				{
					text = "Read";
				}
				else if (methodInfo.Name == "WriteAllBytes" && Enumerable.SequenceEqual(first, new Type[2]
				{
					typeof(string),
					typeof(byte[])
				}))
				{
					text = "Write";
				}
				else if (methodInfo.Name == "Copy" && Enumerable.SequenceEqual(first, new Type[3]
				{
					typeof(string),
					typeof(string),
					typeof(bool)
				}))
				{
					text = "Copy";
				}
			}
			else if (methodInfo.DeclaringType == typeof(SaveSystem))
			{
				if (methodInfo.Name == "Zip" && Enumerable.SequenceEqual(first, new Type[1] { typeof(string) }))
				{
					text = "Zip";
				}
				else if (methodInfo.Name == "Unzip" && Enumerable.SequenceEqual(first, new Type[1] { typeof(byte[]) }))
				{
					text = "Unzip";
				}
			}
			if (text != null)
			{
				item.opcode = OpCodes.Call;
				item.operand = typeof(CheckpointHooks).GetMethod(text, BindingFlags.Static | BindingFlags.NonPublic);
				num++;
			}
		}
		if (num == 0)
		{
			throw new NotSupportedException("Checkpoint I/O call sites changed");
		}
		return list;
	}

	public void Dispose()
	{
		RequireMain();
		if (_disposed && _installed.Count == 0)
		{
			return;
		}
		Enabled = false;
		_disposeRequested = true;
		if (_capture != null) return;
		try
		{
			Drain();
		}
		finally
		{
			try
			{
				for (int num = _installed.Count - 1; num >= 0; num--)
				{
					try
					{
						_harmony.Unpatch(_installed[num].Item1, _installed[num].Item2);
						_installed.RemoveAt(num);
					}
					catch (Exception ex)
					{
						Fail("Checkpoint hook removal failed: " + ex.GetType().Name);
					}
				}
			}
			finally
			{
				try
				{
					_writer.Dispose();
				}
				finally
				{
					_disposed = true;
					// Keep the ownership reservation if an unpatch failed. A new adapter
					// must not install duplicate redirects over the unresolved hooks.
					if (_current == this && _installed.Count == 0)
					{
						_current = null;
					}
				}
			}
		}
	}
}
