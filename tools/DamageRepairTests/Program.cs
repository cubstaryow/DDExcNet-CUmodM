using System;
using DDEN.MPfix;

internal static class Program
{
    private static int _checks;
    private static void Check(bool condition, string description)
    { if (!condition) throw new Exception(description); _checks++; }

    private static void Main()
    {
        var report = new AttackReport(1, 333f, 999f, 0.75f);
        var bytes = new byte[AttackReport.Size];
        report.Write(bytes);
        Check(AttackReport.TryRead(bytes, out var decoded) && decoded.Damage == 333f
            && decoded.Structural == 999f && decoded.Power == 0.75f && decoded.Branch == 1, "round trip");
        var packet = new byte[103];
        report.Write(packet.AsSpan(79));
        Check(AttackReport.TryRead(packet.AsSpan(79), out decoded), "nonzero packet offset");
        Check(!AttackReport.TryRead(packet, out decoded), "require exact trailer size");
        for (int length = 0; length < AttackReport.Size; length++)
            Check(!AttackReport.TryRead(bytes.AsSpan(0, length), out decoded), "truncated trailer");
        bytes[0] ^= 1;
        Check(!AttackReport.TryRead(bytes, out decoded), "foreign packet");
        report.Write(bytes); bytes[20] ^= 1;
        Check(!AttackReport.TryRead(bytes, out decoded), "corrupt end marker");
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1f })
        {
            Check(!new AttackReport(1, invalid, 1, 1).ValidValues, "invalid damage");
            Check(!new AttackReport(2, 1, invalid, 1).ValidValues, "invalid structural damage");
            Check(!new AttackReport(2, 1, 1, invalid).ValidValues, "invalid power");
        }
        Check(!new AttackReport(3, 1, 1, 1).ValidValues, "unknown protocol branch/version");
        Check(!new AttackReport(1, float.MaxValue, 1, 100).ValidValues, "overflow damage product");
        Check(!new AttackReport(1, 1, 1, 101).ValidValues, "unbounded strength");
        Check(new AttackReport(2, 333, 333, 1).Credible(111, 111, 1, 3), "x3 world report accepted");
        Check(!new AttackReport(2, 333, 333, 1).Credible(0, 0, 1, 3), "no host attack cannot authorize damage");
        Check(!new AttackReport(2, 333, 333, 1).Credible(111, 111, 0, 3), "zero host strength");
        Check(!new AttackReport(2, 1000, 1000, 1).Credible(111, 111, 1, 3), "out of trust range");
        Check(new AttackReport(1, 55.5f, 55.5f, 1).Credible(111, 111, 1, 0.5f), "sub-one world multiplier");

        var queue = new FrameWorkQueue<int>();
        for (int i = 0; i < 10000; i++) queue.Enqueue(i);
        int expected = 0, frames = 0;
        while (queue.Count > 0)
        {
            int count = queue.Drain(value => Check(value == expected++, "FIFO, no missing/duplicate work"),
                16, 1000, () => 0);
            Check(count <= 16, "per-frame count budget"); frames++;
        }
        Check(expected == 10000 && frames == 625, "sustained mining has no lifetime cap");
        for (int i = 0; i < 10; i++) queue.Enqueue(i);
        long ticks = 0;
        int done = queue.Drain(value => ticks += 3, 16, 5, () => ticks);
        Check(done == 2 && queue.Count == 8, "elapsed budget leaves remainder queued");
        Check(queue.Drain(value => throw new Exception(), 0, 5, () => ticks) == 0 && queue.Count == 8, "disabled count budget");
        Check(queue.Drain(value => throw new Exception(), 10, 0, () => ticks) == 0 && queue.Count == 8, "disabled time budget");
        queue.Clear();
        Check(queue.Count == 0 && queue.Drain(value => throw new Exception(), 10, 5, () => ticks) == 0, "world reset cancels stale work");
        var identities = new NetworkObjectIndex<object>();
        object original = new object(), replacement = new object(), binding = new object();
        var first = identities.Track(123, 7, "bag", original, binding);
        Check(ReferenceEquals(identities.Find(123, 7, "bag"), first), "exact identity lookup");
        Check(identities.Find(123, 8, "bag") == null, "old generation cannot match new entity");
        Check(identities.Find(123, 7, "scrap") == null, "same ID cannot change resource");
        Check(identities.Find(124, 7, "bag") == null, "nearby identical object cannot match");
        var next = identities.Track(123, 8, "bag", replacement, new object());
        Check(identities.IsCurrent(first) && identities.IsCurrent(next), "different generations remain distinct");
        var rebinding = identities.Track(123, 7, "bag", original, new object());
        Check(!identities.IsCurrent(first) && identities.IsCurrent(rebinding), "same mesh rebound invalidates queued retirement");
        var reused = identities.Track(123, 7, "bag", replacement, new object());
        Check(!identities.IsCurrent(rebinding) && !identities.IsCurrent(next) && identities.IsCurrent(reused), "ID and mesh reuse invalidate stale leases");
        identities.Remove(original);
        Check(identities.IsCurrent(reused), "late old-object destruction preserves new binding");
        identities.Clear();
        Check(identities.Count == 0 && !identities.IsCurrent(reused), "world reset cancels old identities");
        Check(identities.Track(0, 0, "bag", original, binding) == null, "no unregistered identity");
        Check(identities.Track(64000, 0, "bag", original, binding) == null, "peer-local IDs cannot authorize retirement");
        Check(identities.Track(ushort.MaxValue, 0, "bag", original, binding) == null, "peer-local ID range rejected");
        Check(identities.Track(1, 0, "", original, binding) == null, "no unnamed identity");
        Check(identities.Track(1, 0, "bag", null, binding) == null, "no missing mesh");
        Check(identities.Track(1, 0, "bag", original, null) == null, "no missing binding");
        var saved = new NetworkObjectIndex<object>.Lease[1000];
        for (int i = 0; i < saved.Length; i++) saved[i] = identities.Track((ushort)(i + 1), 1, "scrap", new object(), new object());
        Check(identities.Count == 1000, "large inventory indexed once");
        foreach (var lease in saved) { Check(identities.IsCurrent(lease), "inventory binding retained"); identities.Remove(lease.Value); }
        Check(identities.Count == 0, "destroyed inventory releases cached references");
        Console.WriteLine($"PASS: {_checks} checks (attack protocol; 10,000 queued tasks; frame budgets; 1,000 inventory identities; ID reuse; reset).");
    }
}
