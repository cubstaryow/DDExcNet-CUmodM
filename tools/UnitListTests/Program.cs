using DDEN.UnitListMgr;

int checks = 0;
void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
SpawnRequest Request() => new() { RequestId = Guid.NewGuid().ToString("N"), ItemId = "opiumsyringe" };
for (int i = 0; i < 16; i++)
{
    bool mp = (i & 1) != 0, cheats = (i & 2) != 0, remote = (i & 4) != 0, clientCheats = (i & 8) != 0;
    bool expected = !mp || cheats && (!remote || clientCheats);
    Check(SpawnRules.Allowed(mp, cheats, remote, clientCheats) == expected, "permission truth table " + i);
}
var r = Request(); Check(SpawnRules.Validate(r) == null, "default main-panel request");
r.AtCursor = true; Check(SpawnRules.Validate(r) != null, "cursor requests cannot customize");
r.DefaultOnly = true; Check(SpawnRules.Validate(r) == null, "fresh radial request preserves native defaults");
r.Condition = 1; Check(SpawnRules.Validate(r) != null, "radial rejects condition override"); r.Condition = null;
r.Battery = ""; Check(SpawnRules.Validate(r) != null, "radial rejects even empty battery override"); r.Battery = null;
r.Liquids = new(); Check(SpawnRules.Validate(r) != null, "radial rejects empty-container override"); r.Liquids = null;
r.Rounds = 0; Check(SpawnRules.Validate(r) != null, "radial rejects ammo override"); r.Rounds = null;
r.HasMagazine = false; Check(SpawnRules.Validate(r) != null, "radial rejects magazine override"); r.HasMagazine = null;
r.Count = 2; Check(SpawnRules.Validate(r) != null, "one item per quick-menu release");
r = Request(); r.Condition = float.NaN; Check(SpawnRules.Validate(r) != null, "reject nonfinite condition");
r = Request(); r.Y = 0; r.X = float.PositiveInfinity; Check(SpawnRules.Validate(r) != null, "reject invalid position");
r = Request(); r.RequestId = "invalid"; Check(SpawnRules.Validate(r) != null, "require deduplication identity");
r = Request(); r.Count = 21; Check(SpawnRules.Validate(r) != null, "bound generation batch");
r = Request(); r.Condition = 1.01f; Check(SpawnRules.Validate(r) != null, "condition bounded by native range");
r = Request(); r.Liquids = new() { new() { Id = "water", Amount = 1 }, new() { Id = "water", Amount = 2 } };
Check(SpawnRules.Validate(r) != null, "duplicate liquid rows must be merged");
r.Liquids[1].Id = "opium"; Check(SpawnRules.Validate(r) == null, "mixed liquids supported");
r.Liquids[1].Amount = float.NaN; Check(SpawnRules.Validate(r) != null, "reject nonfinite liquid amount");
r.Liquids[1].Amount = -1; Check(SpawnRules.Validate(r) != null, "reject negative liquid amount");
Check(SpawnRules.Sector(0, 0) == -1 && SpawnRules.Sector(39, 0) == -1, "center cancels");
float[,] directions = { { 0, -100 }, { 100, -100 }, { 100, 0 }, { 100, 100 }, { 0, 100 }, { -100, 100 }, { -100, 0 }, { -100, -100 } };
for (int i = 0; i < 8; i++) Check(SpawnRules.Sector(directions[i, 0], directions[i, 1]) == i, "marking-menu direction " + i);
var ledger = new RequestLedger<string>(2, 120);
ledger.Put("player1:requestA", "started", 0);
Check(ledger.TryGet("player1:requestA", out var remembered) && remembered == "started", "remember request before spawning");
ledger.Put("player1:requestA", "complete", 1);
Check(ledger.TryGet("player1:requestA", out remembered) && remembered == "complete", "replay returns previous result");
Check(!ledger.TryGet("player2:requestA", out _), "different sender cannot read another player's result");
ledger.Put("player2:requestA", "complete", 2); ledger.Put("player2:requestB", "complete", 3);
Check(!ledger.TryGet("player1:requestA", out _) && ledger.TryGet("player2:requestA", out _), "cache bounds memory without duplicate queue entries");
ledger.Put("player2:requestC", "complete", 125);
Check(!ledger.TryGet("player2:requestA", out _), "expire old replay entries");
ledger.Clear(); Check(!ledger.TryGet("player2:requestC", out _), "new session clears old requests");
Console.WriteLine($"PASS {checks} item request/permission/default-mode/replay/marking-menu checks; no game started");

public class Body { }
namespace UnityEngine { public struct Rect { } }
