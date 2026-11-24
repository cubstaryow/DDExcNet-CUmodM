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
Check(ItemFilters.Classify("container", "", true, false) == (ItemKind.Container | ItemKind.Wearable), "wearable containers retain both categories");
Check(ItemFilters.Classify("custom", "material,tool", false, false) == (ItemKind.Material | ItemKind.Tool), "metadata tags classify crafting materials and tools");
Check(ItemFilters.Classify("custom", "materialistic", false, false) == ItemKind.Other, "category does not guess from substrings");
Check(ItemFilters.Classify("drug", "", false, true) == (ItemKind.Medical | ItemKind.Liquid), "liquid medicine can match either use");
Check(ItemFilters.Matches(ItemSource.Ccl, ItemKind.Container, ItemSource.Native | ItemSource.Ccl, ItemKind.Container | ItemKind.Tool), "OR within filter groups");
Check(!ItemFilters.Matches(ItemSource.Other, ItemKind.Container, ItemSource.Native | ItemSource.Ccl, ItemKind.Container), "AND across source and use");
Check(ItemFilters.Matches(ItemSource.Native, ItemKind.Material, ItemSource.All, 0), "empty use filter means all uses");
Check(!ItemFilters.Matches(ItemSource.Native, ItemKind.Material, 0, 0), "no source selections yields no results");
Check(NativeCatalog.Contains("vanilla_test") && !NativeCatalog.Contains("category_test") && !NativeCatalog.Contains("modded_test"), "native registration parser distinguishes dictionary keys from property strings");
Check(Item.SetupCalls == 0 && Item.GlobalItems.ContainsKey("existing"), "source classification never reinitializes the item registry");
foreach (var dimensions in new[] { (1920f, 1080f), (1280f, 720f), (640f, 360f) })
foreach (var anchor in new[] { (0f, 0f), (dimensions.Item1, dimensions.Item2), (dimensions.Item1 / 2, dimensions.Item2 / 2) })
{
    var layout = new RadialLayout(dimensions.Item1, dimensions.Item2, anchor.Item1, anchor.Item2);
    for (int i = 0; i < 8; i++)
    {
        float x = layout.CellX(i), y = layout.CellY(i), w = layout.HalfWidth * 2, h = layout.HalfHeight * 2;
        Check(x >= 0 && y >= 0 && x + w <= dimensions.Item1 && y + h <= dimensions.Item2, "wheel cell stays within screen");
        for (int j = i + 1; j < 8; j++)
            Check(x + w <= layout.CellX(j) || layout.CellX(j) + w <= x || y + h <= layout.CellY(j) || layout.CellY(j) + h <= y, "wheel cards never overlap");
    }
}
Check(SpawnPolicy.AllowsGuest(false, false), "host can spawn while guest access is off");
Check(!SpawnPolicy.AllowsGuest(true, false), "host switch rejects authenticated guests");
Check(SpawnPolicy.AllowsGuest(true, true), "guest switch is an independent additional gate");
for (int i = 0; i < 16; i++)
{
    bool mp = (i & 1) != 0, cheats = (i & 2) != 0, remote = (i & 4) != 0, guestCheats = (i & 8) != 0;
    Check(!(SpawnRules.Allowed(mp, cheats, remote, guestCheats) && SpawnPolicy.AllowsGuest(true, false)), "extra guest gate cannot bypass MP permissions");
}
var journal = new SpawnJournal();
for (int i = 0; i < 510; i++) journal.Add(new SpawnRecord { Time = DateTime.Now, Player = "玩家", Sender = (uint)i,
    ItemId = "item", ItemName = "物品", Requested = 2, Created = i % 2, Success = i % 2 == 1,
    Mode = "cursor", Detail = "test", Parameters = "{}", X = 1, Y = 2 });
Check(journal.Records.Count == 500 && journal.Records[0].Sender == 10 && journal.Records[499].Sender == 509, "memory journal bounds growth and preserves latest attempts");
Check(journal.Records.Any(r => !r.Success) && journal.Records.Any(r => r.Success), "journal retains failures and successes");
journal.Clear(); Check(journal.Records.Count == 0, "memory history clears without files");
SteamManager.disableAchievements = false;
DDEN.Shared.Mp.IsRunning = false;
AchievementPolicy.Mark(false, 1); Check(!SteamManager.disableAchievements, "disabled setting never marks an uncheated game");
AchievementPolicy.Mark(true, 0); Check(!SteamManager.disableAchievements, "failed creation does not mark achievements");
AchievementPolicy.Mark(true, 1); Check(SteamManager.disableAchievements, "created items set the native saved cheat flag");
AchievementPolicy.Mark(false, 1); Check(SteamManager.disableAchievements, "turning option off never clears existing cheat flags");
SteamManager.disableAchievements = false;
DDEN.Shared.Mp.IsRunning = true; DDEN.Shared.Mp.IsServer = true;
AchievementPolicy.Mark(true, 1);
Check(SteamManager.disableAchievements && TestAchievementSync.Calls == 1 && !TestAchievementSync.LastAlert, "host broadcasts achievement state via native MP API without oversized alerts");
AchievementPolicy.Mark(true, 2); Check(TestAchievementSync.Calls == 1, "batch creation never repeats the achievement broadcast");
Console.WriteLine($"PASS {checks} item permissions/journal/achievements/filter/layout checks; no game started");

public class Body { }
namespace UnityEngine { public struct Rect { } }

public static class Item
{
    public static int SetupCalls;
    public static System.Collections.Generic.Dictionary<string, TestInfo> GlobalItems = new() { ["existing"] = new() };
    public static void SetupItems()
    {
        SetupCalls++;
        GlobalItems = new();
        GlobalItems.Add("vanilla_test", new TestInfo { Category = "category_test" });
    }
}
public class TestInfo { public string Category; }
namespace DDEN.UnitListMgr
{
    internal static class UnitListPlugin { internal static readonly TestLog Log = new(); }
    internal class TestLog { internal void Debug(string value) { } }
}

public static class SteamManager { public static bool disableAchievements; }
public static class TestAchievementSync
{
    public static int Calls; public static bool LastAlert;
    public static void Server_Broadcast(bool showAlert, System.Collections.Generic.List<object> only = null) { Calls++; LastAlert = showAlert; }
}
namespace DDEN.Shared
{
    public static class Mp { public static bool IsRunning, IsServer; }
    public static class Reflect { public static Type Type(string name) => name == "Together.AchievementBreakSync" ? typeof(TestAchievementSync) : null; }
}
