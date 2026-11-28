using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using DDEN.MPreducer.Core;

namespace DDEN.MPreducer.Integration;

internal sealed class CheckpointCapture
{
	private sealed class Document
	{
		internal readonly byte[] Token = new byte[0];

		internal readonly string Json;

		internal Document(string json)
		{
			Json = json;
		}
	}

	private const int MaxJsonCharacters = 16777216;

	private readonly Dictionary<string, CheckpointFileStamp> _allowed = new Dictionary<string, CheckpointFileStamp>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, Document> _files = new Dictionary<string, Document>(StringComparer.OrdinalIgnoreCase);

	private readonly List<Document> _documents = new List<Document>();

	internal readonly object Player;

	internal readonly object World;

	internal readonly string Session;

	internal readonly long Started;

	internal Exception Failure;

	internal bool Materialized;

	internal int Writes;

	internal int Copies;

	internal int FileCount => _files.Count;

	internal CheckpointCapture(object player, object world, string session, string playerFolder)
	{
		Player = player;
		World = world;
		Session = Path.GetFullPath(session);
		Started = Stopwatch.GetTimestamp();
		string fullPath = Path.GetFullPath(Path.Combine(playerFolder, "save.sv"));
		string fullPath2 = Path.GetFullPath(Path.Combine(session, "save.sv"));
		if (!fullPath.StartsWith(Session.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || string.Equals(fullPath, fullPath2, StringComparison.OrdinalIgnoreCase))
		{
			throw new NotSupportedException("Checkpoint player path is outside the session");
		}
		_allowed.Add(fullPath, CheckpointFileStamp.Capture(fullPath));
		_allowed.Add(fullPath2, CheckpointFileStamp.Capture(fullPath2));
	}

	private static string Canonical(string path)
	{
		return Path.GetFullPath(path);
	}

	private Document Find(byte[] token)
	{
		return _documents.Find((Document doc) => doc.Token == token);
	}

	internal bool Exists(string path)
	{
		if (!_files.ContainsKey(Canonical(path)))
		{
			return File.Exists(path);
		}
		return true;
	}

	internal byte[] Read(string path)
	{
		if (!_files.TryGetValue(Canonical(path), out var value))
		{
			return File.ReadAllBytes(path);
		}
		return value.Token;
	}

	internal string Unzip(byte[] bytes)
	{
		return Find(bytes)?.Json ?? SaveSystem.Unzip(bytes);
	}

	internal byte[] Zip(string json)
	{
		if (json == null || json.Length > 16777216 || _documents.Count >= 16)
		{
			throw new NotSupportedException("Checkpoint payload exceeds capture bounds");
		}
		for (int i = 0; i < json.Length; i++)
		{
			if (json[i] > '\u007f')
			{
				json = Encoding.ASCII.GetString(Encoding.ASCII.GetBytes(json));
				break;
			}
		}
		Document document = new Document(json);
		_documents.Add(document);
		return document.Token;
	}

	internal void Write(string path, byte[] bytes)
	{
		string key = Canonical(path);
		Document document = Find(bytes);
		if (document == null)
		{
			if (_allowed.ContainsKey(key))
			{
				throw new NotSupportedException("Unexpected checkpoint binary writer");
			}
			File.WriteAllBytes(path, bytes);
			return;
		}
		if (!_allowed.ContainsKey(key))
		{
			throw new NotSupportedException("Unexpected checkpoint output path");
		}
		_files[key] = document;
		Writes++;
	}

	internal void Copy(string source, string target, bool overwrite)
	{
		string key = Canonical(target);
		if (!_files.TryGetValue(Canonical(source), out var value))
		{
			if (_allowed.ContainsKey(key))
			{
				throw new NotSupportedException("Unexpected physical checkpoint copy");
			}
			File.Copy(source, target, overwrite);
			return;
		}
		if (!_allowed.ContainsKey(key))
		{
			throw new NotSupportedException("Unexpected checkpoint copy path");
		}
		if (!overwrite && Exists(target))
		{
			throw new IOException("Checkpoint copy destination exists");
		}
		_files[key] = value;
		Copies++;
	}

	internal CheckpointWriteTarget[] Targets()
	{
		List<CheckpointWriteTarget> list = new List<CheckpointWriteTarget>(_files.Count);
		foreach (KeyValuePair<string, Document> file in _files)
		{
			string json = file.Value.Json;
			if (json.Length > 16777216)
			{
				throw new NotSupportedException("Checkpoint payload exceeds capture bounds");
			}
			list.Add(new CheckpointWriteTarget(file.Key, json, _allowed[file.Key]));
		}
		return list.ToArray();
	}

	internal void Materialize()
	{
		if (!Unchanged())
		{
			throw new IOException("Checkpoint target changed before synchronous fallback");
		}
		if (_files.Count != 0)
		{
			using CheckpointWriter checkpointWriter = new CheckpointWriter();
			checkpointWriter.TryStart(new CheckpointWriteRequest(0L, 0L, Targets()));
			using PreparedCheckpoint preparedCheckpoint = checkpointWriter.Drain();
			CheckpointCommitResult checkpointCommitResult = preparedCheckpoint.TryCommit(0L, 0L);
			if (checkpointCommitResult.Status != CheckpointCommitStatus.Committed)
			{
				throw new IOException("Checkpoint synchronous fallback: " + checkpointCommitResult.Status, checkpointCommitResult.Error);
			}
		}
		Materialized = true;
	}

	internal bool Unchanged()
	{
		foreach (KeyValuePair<string, CheckpointFileStamp> item in _allowed)
		{
			if (!item.Value.Matches(item.Key))
			{
				return false;
			}
		}
		return true;
	}

	internal void ReleaseBuffers()
	{
		_files.Clear();
		_documents.Clear();
	}
}
