using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;
using DDEN.MPfix;
using Newtonsoft.Json.Linq;

internal static class Program
{
    private static int _checks;
    private static void Check(bool condition, string message)
    { _checks++; if (!condition) throw new Exception(message); }
    private sealed class Settings
    {
        internal readonly ConfigFile File;
        internal readonly AuthoritySetting<bool> Flag;
        internal readonly AuthoritySetting<int> Count;
        internal readonly AuthoritySetting<float> Scale;
        internal readonly AuthorityOverlay Overlay;
        internal int Changed;
        internal Settings(string path, bool flag, int count)
        {
            File = new ConfigFile(path, true);
            Flag = File.Bind("repair", "flag", flag);
            Count = File.Bind("repair", "count", count, new ConfigDescription("limit", new AcceptableValueRange<int>(1, 200)));
            Scale = File.Bind("repair", "scale", 0.5f, new ConfigDescription("scale", new AcceptableValueRange<float>(0, 1)));
            Overlay = new AuthorityOverlay(new IAuthoritySetting[] { Flag, Count, Scale }, _ => Changed++);
        }
    }
    private static void Main()
    {
        string dir = Path.Combine(Path.GetTempPath(), "DDEN-authority-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try { Run(dir); Console.WriteLine("PASS " + _checks + " checks"); }
        finally
        {
            string expectedRoot = Path.GetFullPath(Path.GetTempPath());
            string target = Path.GetFullPath(dir);
            if (!target.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(target).StartsWith("DDEN-authority-tests-", StringComparison.Ordinal))
                throw new Exception("unexpected test cleanup path");
            Directory.Delete(target, true);
        }
    }
    private static void Run(string dir)
    {
        var host = new Settings(Path.Combine(dir, "host.cfg"), false, 77);
        var client = new Settings(Path.Combine(dir, "client.cfg"), true, 15);
        string session = Guid.NewGuid().ToString("N");
        var snapshot = host.Overlay.Capture(session, 1, true);
        Check(AuthoritySnapshot.TryRead(snapshot.ToJson(true), out var read), "snapshot round trip");
        Check(client.Overlay.Apply(read) && client.Overlay.Locked, "complete host snapshot applies");
        Check(!client.Flag.Value && client.Count.Value == 77, "all runtime readers see host values");
        Check(client.Flag.Local.Value && client.Count.Local.Value == 15, "native ConfigEntry retains local preferences");
        client.File.Save(); client.File.Reload();
        Check(client.Flag.Local.Value && client.Count.Local.Value == 15 && client.Count.Value == 77, "save/reload during authority never persists remote values");
        var disk = new ConfigFile(client.File.ConfigFilePath, false);
        Check(disk.Bind("repair", "flag", false).Value && disk.Bind("repair", "count", 1).Value == 15, "independent reader confirms file is local");
        client.Count.Local.Value = 20;
        Check(client.Count.Value == 77 && (int)client.Overlay.Display(client.Count.Local) == 77, "local edits stay ineffective and UI shows effective host value");
        client.Overlay.Release();
        Check(client.Count.Value == 20 && client.Flag.Value && !client.Overlay.Locked, "release restores latest local preference, not a stale backup");
        Check(!AuthoritySnapshot.TryRead(new JObject { ["protocol"] = 1, ["session"] = session, ["revision"] = 1, ["enabled"] = false, ["values"] = new JObject() }, out _), "disabled flag forbids values");
        Check(!AuthoritySnapshot.TryRead(new JValue("invalid"), out _), "malformed envelope rejected");
        snapshot.Revision = 2; snapshot.Values[host.Scale.Id].Text = "NaN";
        Check(!client.Overlay.Apply(snapshot) && client.Flag.Value && client.Count.Value == 20 && !client.Overlay.Locked, "NaN rejects whole snapshot without partial changes");
        snapshot = host.Overlay.Capture(session, 3, true); snapshot.Values[host.Count.Id].Text = "201";
        Check(!client.Overlay.Apply(snapshot), "out-of-range value is rejected rather than silently clamped");
        snapshot = host.Overlay.Capture(session, 4, true); snapshot.Values.Remove(host.Scale.Id);
        Check(!client.Overlay.Apply(snapshot), "incomplete snapshot cannot lock partially synchronized settings");
        snapshot = host.Overlay.Capture(session, 5, true);
        Check(client.Overlay.Apply(snapshot), "valid revision applies after invalid data");
        int changed = client.Changed;
        Check(!client.Overlay.Apply(snapshot) && changed == client.Changed, "duplicate revision has no repeated side effects");
        var off = host.Overlay.Capture(session, 6, false);
        Check(off.Values.Count == 0 && off.ToJson(true)["values"] == null, "host disabled snapshot contains no setting values");
        Check(client.Overlay.Apply(off) && client.Flag.Value && client.Count.Value == 20, "disabled authority releases all temporary settings");
        Check(!client.Overlay.Apply(snapshot) && !client.Overlay.Locked, "old enabled reply cannot roll back a newer disabled revision");

        double now = 0; bool hostOpt = true, clientOpt = true, connected = true, clientServer = false;
        object hostConnection = new object(), clientConnection = new object();
        var notices = new List<JObject>();
        var replies = new Queue<Action>(); int pulls = 0;
        var hostSync = new HostConfigSync(host.Overlay, () => hostOpt, () => new AuthorityPeer(hostConnection, connected, true), () => now,
            token => { notices.Add((JObject)token.DeepClone()); return true; }, (_, __) => false, _ => { });
        var clientSync = new HostConfigSync(client.Overlay, () => clientOpt, () => new AuthorityPeer(clientConnection, connected, clientServer), () => now,
            _ => true, (request, callback) => { pulls++; var response = hostSync.HandleRequest(request)?.DeepClone(); replies.Enqueue(() => callback(response)); return true; }, _ => { });
        hostSync.Tick(); clientSync.Tick(); replies.Dequeue()();
        Check(clientSync.Locked && client.Count.Value == 77, "join handshake adopts authoritative host config");
        Check(notices.Count == 1 && notices[0]["values"] == null, "host publishes one initial marker without broadcast values");
        for (int i = 0; i < 1000; i++) { now += 0.01; hostSync.Tick(); clientSync.Tick(); }
        Check(pulls == 1 && notices.Count == 1, "steady-state ticks do not poll or resend config");
        host.Count.Local.Value = 90; hostSync.LocalChanged(); now += 0.1; hostSync.Tick();
        Check(notices.Count == 1, "rapid host edits are coalesced");
        now += 0.2; hostSync.Tick(); clientSync.OnNotice(notices[notices.Count - 1]); clientSync.Tick(); replies.Dequeue()();
        Check(client.Count.Value == 90 && notices.Count == 2, "host edit is applied by client after an authenticated pull");
        hostOpt = false; hostSync.LocalChanged();
        Check(notices[notices.Count - 1].Value<bool>("enabled") == false && notices[notices.Count - 1]["values"] == null, "host opting out sends disabled marker with no values");
        clientSync.OnNotice(notices[notices.Count - 1]); now += 1.1; clientSync.Tick(); replies.Dequeue()();
        Check(!clientSync.Locked && client.Count.Value == 20, "host disable releases client configuration controls and values");
        int sends = notices.Count; host.Count.Local.Value = 100; hostSync.LocalChanged(); now += 0.3; hostSync.Tick();
        Check(notices.Count == sends, "editing ordinary settings while host authority is disabled sends nothing");
        hostOpt = true; hostSync.LocalChanged(); now += 1.1;
        clientOpt = false; clientSync.Tick(); int before = pulls; clientSync.OnNotice(notices[notices.Count - 1]); clientSync.Tick();
        Check(pulls == before && client.Count.Value == 20, "client opt-out ignores host broadcasts and values");
        clientOpt = true; clientSync.Tick();
        Check(replies.Count == 1, "opting in mid-session requests the current policy once");
        clientOpt = false; clientSync.Tick(); replies.Dequeue()();
        Check(!clientSync.Locked && client.Count.Value == 20, "late reply after opt-out is ignored");
        clientOpt = true; clientSync.Tick(); var stale = replies.Dequeue();
        connected = false; clientSync.Tick(); hostSync.Tick(); connected = true; hostConnection = new object(); clientConnection = new object();
        hostSync.Tick(); clientSync.Tick(); stale();
        Check(!clientSync.Locked, "old-session reply cannot lock a new session");
        replies.Dequeue()(); Check(clientSync.Locked && client.Count.Value == 100, "fresh session reply applies");
        connected = false; clientSync.Tick();
        Check(!clientSync.Locked && client.Count.Value == 20 && client.Flag.Value, "disconnect restores local configuration");
        connected = true; clientConnection = new object(); clientServer = true; clientSync.Tick();
        Check(!clientSync.Locked, "becoming host never locks the host's controls");
        clientSync.Stop(); hostSync.Stop();
        int stoppedPulls = pulls; clientSync.OnNotice(new JObject { ["protocol"] = 1 }); clientSync.Tick();
        Check(pulls == stoppedPulls && !client.Overlay.Locked, "stopped controller cannot reactivate from a late notice");

        var retrySettings = new Settings(Path.Combine(dir, "retry.cfg"), true, 10);
        now = 0; connected = true; object retryConnection = new object(); int failedSends = 0;
        var retry = new HostConfigSync(retrySettings.Overlay, () => true, () => new AuthorityPeer(retryConnection, true, false), () => now,
            _ => true, (_, __) => { failedSends++; return false; }, _ => { });
        for (int i = 0; i < 100; i++) { now = i; retry.Tick(); }
        Check(failedSends == 3 && !retry.Locked, "unavailable host retries are bounded and never lock the client");
        retry.Stop();
        int invalidReplies = 0; now = 0;
        var malformed = new HostConfigSync(retrySettings.Overlay, () => true, () => new AuthorityPeer(retryConnection, true, false), () => now,
            _ => true, (_, callback) => { invalidReplies++; callback(new JValue("malformed")); return true; }, _ => { });
        for (int i = 0; i < 20; i++) { now = i; malformed.Tick(); }
        Check(invalidReplies == 3 && !malformed.Locked, "malformed responses neither throw nor loop forever");
        malformed.Stop();

        now = 0; replies.Clear(); hostOpt = true; connected = true; clientServer = false;
        hostConnection = new object(); clientConnection = new object();
        var trustHost = new HostConfigSync(host.Overlay, () => true, () => new AuthorityPeer(hostConnection, true, true), () => now,
            _ => true, (_, __) => false, _ => { });
        trustHost.Tick();
        var trustClient = new HostConfigSync(retrySettings.Overlay, () => true, () => new AuthorityPeer(clientConnection, true, false), () => now,
            _ => true, (request, callback) => { var response = trustHost.HandleRequest(request)?.DeepClone(); replies.Enqueue(() => callback(response)); return true; }, _ => { });
        trustClient.Tick(); replies.Dequeue()();
        Check(trustClient.Locked, "trusted initial response applied");
        var forged = new JObject { ["protocol"] = 1, ["enabled"] = false, ["values"] = new JObject { [host.Count.Id] = "1" } };
        now = 2; trustClient.OnNotice(forged);
        Check(trustClient.Locked && retrySettings.Count.Value == host.Count.Value, "relayed disabled marker and forged values cannot release or modify authority");
        trustClient.Tick(); replies.Dequeue()();
        Check(trustClient.Locked && retrySettings.Count.Value == host.Count.Value, "refresh from real host overrides untrusted notice content");
        Check(trustClient.Notice.Contains("（主机权威模式）") && trustHost.Notice.Contains("可编辑"), "mode labels distinguish adopted client and editable host");
        trustClient.Stop(); trustHost.Stop();
    }
}
