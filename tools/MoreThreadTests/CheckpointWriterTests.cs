using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using DDEN.MPreducer.Core;

// Tests exercise the real owned writer, real gzip and temporary files. No game/runtime types enter the worker.
internal static class CheckpointWriterTests
{
    internal static void Run(Action<bool, string> check)
    {
        string temporaryRoot = Path.GetFullPath(Path.GetTempPath());
        string directory = Path.GetFullPath(Path.Combine(temporaryRoot, "DDEN-checkpoint-core-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        try
        {
            string first = Path.Combine(directory, "player.sv"), second = Path.Combine(directory, "shared.sv");
            Action seed = () => { File.WriteAllText(first, "old-player"); File.WriteAllText(second, "old-shared"); };
            seed();
            var original = new CheckpointWriteTarget(first, "{\"name\":\"检查点\",\"nested\":[1,2]}", CheckpointFileStamp.Capture(first));
            var targets = new[] { original };
            var request = new CheckpointWriteRequest(10, 20, targets);
            targets[0] = new CheckpointWriteTarget(second, "wrong", CheckpointFileStamp.Capture(second));
            check(ReferenceEquals(request.Targets[0], original) && ((IList<CheckpointWriteTarget>)request.Targets).IsReadOnly,
                "checkpoint request defensively copies the target array and exposes immutable targets");
            check(typeof(CheckpointWriteTarget).GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .All(property => property.SetMethod == null), "worker target exposes no mutable JSON graph or public setter");
            int main = Thread.CurrentThread.ManagedThreadId, compressThread = main, writeThread = main;
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            using (var writer = new CheckpointWriter(text =>
            {
                compressThread = Thread.CurrentThread.ManagedThreadId;
                entered.Set();
                if (!release.Wait(5000)) throw new TimeoutException("Core fixture did not release compression");
                return CheckpointWriter.Compress(text);
            }, (path, bytes) => { writeThread = Thread.CurrentThread.ManagedThreadId; File.WriteAllBytes(path, bytes); }))
            {
                try
                {
                    check(writer.TryStart(request) && entered.Wait(5000), "first immutable checkpoint is accepted and reaches worker");
                    check(!writer.TryStart(new CheckpointWriteRequest(10, 21, original)), "busy writer rejects a second revision instead of reordering saves");
                    check(File.ReadAllText(first) == "old-player" && File.ReadAllText(second) == "old-shared",
                        "background compression cannot change final files");
                }
                finally { release.Set(); }
                PreparedCheckpoint taken = null;
                var timer = Stopwatch.StartNew();
                while (!writer.TryTake(out taken))
                { if (timer.ElapsedMilliseconds > 6000) throw new TimeoutException("Worker result"); Thread.Sleep(1); }
                check(writer.Busy && !writer.TryStart(request), "taking the result retains exclusive ownership until commit or disposal");
                check(ReferenceEquals(writer.Drain(), taken), "drain recovers an already taken but unfinished result");
                Exception wrongThread = OnWorker(() => taken.TryCommit(10, 20));
                check(wrongThread is InvalidOperationException && File.ReadAllText(first) == "old-player",
                    "commit rejects non-owner threads before any final-file write");
                var result = taken.TryCommit(10, 20);
                check(result.Status == CheckpointCommitStatus.Committed && result.CommittedCount == 1 && !writer.Busy,
                    "main-thread commit releases the writer after a successful atomic replacement");
                check(ReferenceEquals(result, taken.TryCommit(10, 20)), "repeated commit returns the existing result and cannot write twice");
                check(Expand(File.ReadAllBytes(first)) == "{\"name\":\"???\",\"nested\":[1,2]}" && File.ReadAllText(second) == "old-shared",
                    "request copy preserves original destination and native ASCII gzip representation");
                check(compressThread != main && writeThread != main, "compression and temporary-file writes execute outside the main thread");
                check(writer.TryStart(Request(first, "second-revision", 10, 21)), "completed commit admits the next revision in order");
                using (var next = writer.Drain())
                    check(next.TryCommit(10, 21).Status == CheckpointCommitStatus.Committed, "next accepted revision commits after its predecessor");
                taken.TryCommit(10, 20);
                check(Expand(File.ReadAllBytes(first)) == "second-revision", "reusing an older completed result cannot overwrite a newer committed revision");
                check(OnWorker(() => writer.Drain()) is InvalidOperationException && OnWorker(() => writer.TryStart(request)) is InvalidOperationException,
                    "scheduling and drain reject foreign threads");
            }
            check(!Directory.GetFiles(directory, "*.tmp").Any(), "successful checkpoint leaves no temporary artifacts");

            foreach (bool wrongEpoch in new[] { true, false })
            {
                seed();
                using var writer = new CheckpointWriter();
                writer.TryStart(Request(first, "new", 30, 40));
                using var ready = writer.Drain();
                var result = ready.TryCommit(wrongEpoch ? 31 : 30, wrongEpoch ? 40 : 41);
                check(result.Status == CheckpointCommitStatus.Conflict && File.ReadAllText(first) == "old-player",
                    "changed world epoch or revision cannot commit stale snapshots");
            }

            seed();
            using (var writer = new CheckpointWriter())
            {
                writer.TryStart(Request(first, "stale", 1, 1));
                using var ready = writer.Drain();
                DateTime timestamp = File.GetLastWriteTimeUtc(first);
                File.WriteAllText(first, "new-player"); // Same length as old-player.
                File.SetLastWriteTimeUtc(first, timestamp);
                var result = ready.TryCommit(1, 1);
                check(result.Status == CheckpointCommitStatus.Conflict && File.ReadAllText(first) == "new-player",
                    "content hash detects an external edit even when file length and timestamp are unchanged");
            }

            seed();
            using (var writer = new CheckpointWriter(_ => throw new IOException("compress failed")))
            {
                writer.TryStart(Request(first, "new", 1, 1));
                using var ready = writer.Drain();
                File.WriteAllText(first, "newer-external-data");
                var result = ready.TryCommit(1, 1);
                check(ready.Error is IOException && result.Status == CheckpointCommitStatus.Conflict && result.ConflictDetected,
                    "external save conflict takes precedence over worker failure so fallback cannot overwrite newer data");
            }

            seed();
            int writes = 0;
            using (var writer = new CheckpointWriter(writeTemporary: (path, bytes) =>
            {
                File.WriteAllBytes(path, bytes);
                if (++writes == 2) throw new IOException("second temporary write failed");
            }))
            {
                writer.TryStart(new CheckpointWriteRequest(1, 1,
                    new CheckpointWriteTarget(first, "player-new", CheckpointFileStamp.Capture(first)),
                    new CheckpointWriteTarget(second, "shared-new", CheckpointFileStamp.Capture(second))));
                using var ready = writer.Drain();
                var result = ready.TryCommit(1, 1);
                check(result.Status == CheckpointCommitStatus.Failed && result.CommittedCount == 0
                    && File.ReadAllText(first) == "old-player" && File.ReadAllText(second) == "old-shared",
                    "partial temporary preparation failure preserves both complete old files");
            }
            check(!Directory.GetFiles(directory, "*.tmp").Any(), "failed preparation cleans all owned temporary files");

            seed();
            using (var writer = new CheckpointWriter())
            {
                writer.TryStart(new CheckpointWriteRequest(1, 1,
                    new CheckpointWriteTarget(first, "player-new", CheckpointFileStamp.Capture(first)),
                    new CheckpointWriteTarget(second, "shared-new", CheckpointFileStamp.Capture(second))));
                using var ready = writer.Drain();
                using var locked = new FileStream(second, FileMode.Open, FileAccess.Read, FileShare.Read);
                var result = ready.TryCommit(1, 1);
                check(result.Status == CheckpointCommitStatus.PartialCommit && result.CommittedCount == 1,
                    "failure replacing second target reports a partial commit explicitly");
                check(Expand(File.ReadAllBytes(first)) == "player-new" && File.ReadAllText(second) == "old-shared",
                    "partial commit retains complete new player file and complete old shared file");
            }

            seed();
            var disposed = new CheckpointWriter();
            disposed.TryStart(Request(first, "discard-on-dispose", 1, 1));
            disposed.Dispose();
            check(!disposed.Busy && !disposed.TryStart(Request(first, "late", 1, 2)) && File.ReadAllText(first) == "old-player",
                "writer disposal joins and cleans without guessing a valid world/revision or committing implicitly");
            check(!Directory.GetFiles(directory, "*.tmp").Any(), "all writer outcomes clean temporary artifacts");
        }
        finally
        {
            string prefix = temporaryRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (directory.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static CheckpointWriteRequest Request(string path, string text, long epoch, long revision)
        => new CheckpointWriteRequest(epoch, revision, new CheckpointWriteTarget(path, text, CheckpointFileStamp.Capture(path)));
    private static Exception OnWorker(Action action)
    {
        Exception error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { error = exception; } });
        thread.Start();
        if (!thread.Join(6000)) throw new TimeoutException("Foreign-thread guard fixture");
        return error;
    }
    private static string Expand(byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.ASCII);
        return reader.ReadToEnd();
    }
}
