using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace DDEN.MPreducer.Core
{
    // File fingerprints are captured by the owner before accepting a snapshot.
    // They detect external edits, including equal-length writes with restored timestamps.
    internal readonly struct CheckpointFileStamp : IEquatable<CheckpointFileStamp>
    {
        internal readonly bool Exists;
        internal readonly long Length, LastWriteUtcTicks;
        internal readonly string Sha256;
        private CheckpointFileStamp(long length, long modified, string hash)
        { Exists = true; Length = length; LastWriteUtcTicks = modified; Sha256 = hash; }
        internal static CheckpointFileStamp Capture(string path)
        {
            try
            {
                // Deny writes/deletion while hashing this version on Windows.
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var sha = SHA256.Create())
                    return new CheckpointFileStamp(stream.Length, File.GetLastWriteTimeUtc(path).Ticks,
                        Convert.ToBase64String(sha.ComputeHash(stream)));
            }
            catch (FileNotFoundException) { return default; }
            catch (DirectoryNotFoundException) { return default; }
        }
        public bool Equals(CheckpointFileStamp other) => Exists == other.Exists && (!Exists
            || (Length == other.Length && LastWriteUtcTicks == other.LastWriteUtcTicks
                && string.Equals(Sha256, other.Sha256, StringComparison.Ordinal)));
        internal bool Matches(string path) => Equals(Capture(path));
        public override bool Equals(object other) => other is CheckpointFileStamp stamp && Equals(stamp);
        public override int GetHashCode() => !Exists ? 0 : Length.GetHashCode() ^ LastWriteUtcTicks.GetHashCode() ^ (Sha256?.GetHashCode() ?? 0);
    }

    internal sealed class CheckpointWriteTarget
    {
        internal string Path { get; }
        internal string Json { get; }
        internal CheckpointFileStamp Expected { get; }
        internal CheckpointWriteTarget(string path, string json, CheckpointFileStamp expected)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Checkpoint path is required", nameof(path));
            Path = System.IO.Path.GetFullPath(path);
            Json = json ?? throw new ArgumentNullException(nameof(json));
            Expected = expected;
        }
    }

    internal sealed class CheckpointWriteRequest
    {
        internal long Epoch { get; }
        internal long Revision { get; }
        internal ReadOnlyCollection<CheckpointWriteTarget> Targets { get; }
        internal CheckpointWriteRequest(long epoch, long revision, params CheckpointWriteTarget[] targets)
        {
            if (targets == null || targets.Length < 1 || targets.Length > 2)
                throw new ArgumentException("A checkpoint has one or two targets", nameof(targets));
            var copy = (CheckpointWriteTarget[])targets.Clone();
            for (int i = 0; i < copy.Length; i++)
            {
                if (copy[i] == null) throw new ArgumentException("Null checkpoint target", nameof(targets));
                for (int j = 0; j < i; j++)
                    if (string.Equals(copy[i].Path, copy[j].Path, StringComparison.OrdinalIgnoreCase))
                        throw new ArgumentException("Checkpoint targets must be distinct", nameof(targets));
            }
            Epoch = epoch; Revision = revision; Targets = Array.AsReadOnly(copy);
        }
    }

    internal enum CheckpointCommitStatus { Committed, Conflict, Failed, PartialCommit }
    internal sealed class CheckpointCommitResult
    {
        internal CheckpointCommitStatus Status { get; }
        internal int CommittedCount { get; }
        // PartialCommit can also be caused by an external edit; never retry over that edit.
        internal bool ConflictDetected { get; }
        internal Exception Error { get; }
        internal CheckpointCommitResult(CheckpointCommitStatus status, int count, bool conflict, Exception error)
        { Status = status; CommittedCount = count; ConflictDetected = conflict; Error = error; }
    }

    internal sealed class PreparedCheckpoint : IDisposable
    {
        internal sealed class Temporary
        {
            internal readonly CheckpointWriteTarget Target;
            internal string Path;
            internal bool Owned;
            internal Temporary(CheckpointWriteTarget target) { Target = target; }
        }
        private readonly int _owner;
        private readonly Temporary[] _files;
        private readonly Action _released;
        private bool _finished;
        private CheckpointCommitResult _result;
        internal CheckpointWriteRequest Request { get; }
        internal Exception Error { get; }
        internal Exception CleanupError { get; private set; }
        internal double PrepareMilliseconds { get; }
        internal double CommitMilliseconds { get; private set; }
        internal long CompressedBytes { get; }
        internal PreparedCheckpoint(CheckpointWriteRequest request, Temporary[] files, int owner,
            Action released, Exception error, double milliseconds, long bytes)
        {
            Request = request; _files = files; _owner = owner; _released = released;
            Error = error; PrepareMilliseconds = milliseconds; CompressedBytes = bytes;
        }
        private void RequireOwner()
        {
            if (Thread.CurrentThread.ManagedThreadId != _owner)
                throw new InvalidOperationException("Checkpoint commit must run on its owner thread");
        }
        internal CheckpointCommitResult TryCommit(long currentEpoch, long currentRevision)
        {
            RequireOwner();
            if (_finished) return _result;
            var watch = Stopwatch.StartNew();
            int committed = 0;
            try
            {
                if (Request.Epoch != currentEpoch || Request.Revision != currentRevision)
                    return Finish(CheckpointCommitStatus.Conflict, 0, true, null);
                // All targets must still match before changing any target.
                foreach (var file in _files)
                    if (!file.Target.Expected.Equals(CheckpointFileStamp.Capture(file.Target.Path)))
                        return Finish(CheckpointCommitStatus.Conflict, 0, true, null);
                if (Error != null) return Finish(CheckpointCommitStatus.Failed, 0, false, Error);
                foreach (var file in _files)
                {
                    // Recheck immediately before each atomic operation. There is no filesystem
                    // compare-and-swap or cross-file atomic transaction; never roll back over a newer file.
                    if (!file.Target.Expected.Equals(CheckpointFileStamp.Capture(file.Target.Path)))
                        return Finish(committed == 0 ? CheckpointCommitStatus.Conflict : CheckpointCommitStatus.PartialCommit,
                            committed, true, null);
                    try
                    {
                        if (file.Target.Expected.Exists) File.Replace(file.Path, file.Target.Path, null, true);
                        else File.Move(file.Path, file.Target.Path);
                    }
                    catch (Exception error)
                    {
                        bool changed = false;
                        try { changed = !file.Target.Expected.Equals(CheckpointFileStamp.Capture(file.Target.Path)); }
                        catch { /* Preserve the original commit error if the target cannot be inspected. */ }
                        return Finish(committed > 0 ? CheckpointCommitStatus.PartialCommit
                            : changed ? CheckpointCommitStatus.Conflict : CheckpointCommitStatus.Failed, committed, changed, error);
                    }
                    file.Owned = false; committed++;
                }
                return Finish(CheckpointCommitStatus.Committed, committed, false, null);
            }
            catch (Exception error)
            { return Finish(committed > 0 ? CheckpointCommitStatus.PartialCommit : CheckpointCommitStatus.Failed, committed, false, error); }
            finally { CommitMilliseconds = watch.Elapsed.TotalMilliseconds; }
        }
        private CheckpointCommitResult Finish(CheckpointCommitStatus status, int count, bool conflict, Exception error)
        {
            _result = new CheckpointCommitResult(status, count, conflict, error);
            _finished = true;
            Cleanup(); _released();
            return _result;
        }
        private void Cleanup()
        {
            foreach (var file in _files)
            {
                if (!file.Owned) continue;
                try { File.Delete(file.Path); file.Owned = false; }
                catch (Exception error) { if (CleanupError == null) CleanupError = error; }
            }
        }
        public void Dispose()
        {
            RequireOwner();
            if (_finished) { Cleanup(); return; }
            Finish(CheckpointCommitStatus.Failed, 0, false, new ObjectDisposedException(nameof(PreparedCheckpoint)));
        }
    }

    // One bounded transaction, including its prepared-but-uncommitted result. Background
    // work owns only immutable strings and private temporary files, never Unity/save objects.
    internal sealed class CheckpointWriter : IDisposable
    {
        private readonly object _gate = new object();
        private readonly int _owner = Thread.CurrentThread.ManagedThreadId;
        private readonly Func<string, byte[]> _compress;
        private readonly Action<string, byte[]> _writeTemporary;
        private Thread _thread;
        private PreparedCheckpoint _ready, _active;
        private bool _busy, _disposed;
        internal bool Busy { get { lock (_gate) return _busy; } }
        internal CheckpointWriter(Func<string, byte[]> compress = null, Action<string, byte[]> writeTemporary = null)
        { _compress = compress ?? Compress; _writeTemporary = writeTemporary ?? WriteTemporary; }
        private void RequireOwner()
        {
            if (Thread.CurrentThread.ManagedThreadId != _owner)
                throw new InvalidOperationException("Checkpoint scheduling must run on its owner thread");
        }
        internal bool TryStart(CheckpointWriteRequest request)
        {
            RequireOwner();
            if (request == null) throw new ArgumentNullException(nameof(request));
            lock (_gate)
            {
                if (_disposed || _busy) return false;
                _busy = true;
                try
                {
                    _thread = new Thread(() => Prepare(request)) { IsBackground = true, Name = "DDEN checkpoint writer" };
                    _thread.Start();
                }
                catch { _busy = false; _thread = null; throw; }
                return true;
            }
        }
        private void Prepare(CheckpointWriteRequest request)
        {
            var watch = Stopwatch.StartNew();
            var files = new PreparedCheckpoint.Temporary[request.Targets.Count];
            for (int i = 0; i < files.Length; i++) files[i] = new PreparedCheckpoint.Temporary(request.Targets[i]);
            Exception failure = null; long bytes = 0;
            try
            {
                foreach (var file in files)
                {
                    byte[] compressed = _compress(file.Target.Json);
                    if (compressed == null) throw new InvalidDataException("Checkpoint encoder returned no bytes");
                    string directory = Path.GetDirectoryName(file.Target.Path);
                    Directory.CreateDirectory(directory);
                    file.Path = Path.Combine(directory, "." + Path.GetFileName(file.Target.Path)
                        + ".dden-checkpoint-" + Guid.NewGuid().ToString("N") + ".tmp");
                    // Reserve exclusively before invoking the writer seam. A CreateNew failure
                    // must never cause cleanup to delete a file this transaction did not create.
                    using (new FileStream(file.Path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
                    file.Owned = true;
                    _writeTemporary(file.Path, compressed);
                    bytes += compressed.Length;
                }
            }
            catch (Exception error) { failure = error; }
            var prepared = new PreparedCheckpoint(request, files, _owner, Released, failure, watch.Elapsed.TotalMilliseconds, bytes);
            lock (_gate) { _ready = prepared; _active = prepared; }
        }
        private void Released()
        { lock (_gate) { _busy = false; _thread = null; _ready = null; _active = null; } }
        internal bool TryTake(out PreparedCheckpoint prepared)
        {
            RequireOwner();
            lock (_gate) { prepared = _ready; _ready = null; return prepared != null; }
        }
        // Lifecycle barrier only. Normal frames poll TryTake; they never wait for compression/I/O.
        // Return a taken-but-unfinished result too, so shutdown can finish a transaction
        // whose owner already polled it. Commit/disposal is idempotent on that same result.
        internal PreparedCheckpoint Drain()
        {
            RequireOwner();
            Thread thread; lock (_gate) thread = _thread;
            thread?.Join();
            lock (_gate)
            {
                var prepared = _ready ?? _active;
                _ready = null;
                return prepared;
            }
        }
        internal static byte[] Compress(string json)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(json);
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, CompressionLevel.Optimal, true)) gzip.Write(bytes, 0, bytes.Length);
                return output.ToArray();
            }
        }
        private static void WriteTemporary(string path, byte[] bytes)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
            { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
        }
        public void Dispose()
        {
            // This is an explicit abort/cleanup, not a save barrier: only the owner knows
            // the current session/revision. Normal shutdown must Drain and consume the
            // commit result (or perform its guarded fallback) before disposing the writer.
            RequireOwner();
            Thread thread;
            lock (_gate) { if (_disposed) return; _disposed = true; thread = _thread; }
            thread?.Join();
            PreparedCheckpoint prepared; lock (_gate) prepared = _active;
            prepared?.Dispose();
        }
    }
}
