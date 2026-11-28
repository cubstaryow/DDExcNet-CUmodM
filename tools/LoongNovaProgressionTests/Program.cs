using System;
using System.Collections.Generic;
using DDEN.LoongNova;
using DDEN.Shared;
using HarmonyLib;
using Newtonsoft.Json.Linq;

internal static class Program
{
    private static int _checks;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); _checks++; }
    private static Recipe Make(string id) => new Recipe { result = new RecipeResult { id = id } };
    private static Body Peer()
    {
        var body = new Body(); var player = new Together.ScavPlayer { body = body };
        body.Net = new Together.NetBody { body = body, plr = player }; return body;
    }
    private static void Main()
    {
        var harmony = new Harmony(LoongNovaPlugin.Guid);
        try
        {
            // Use the production startup order so a locale patch failure cannot be concealed by recipe-only tests.
            harmony.PatchAll(typeof(LoongLocalePatches));
            harmony.PatchAll(typeof(SmeltingRecipePatches));
            harmony.PatchAll(typeof(RecipeUnlockPatches));
            RecipeUnlockPatches.InstallMultiplayer();
            Check(RecipeUnlockPatches.Installed, "Production MP scope/matcher hooks must actually detour independent mocks");
            Run();
            Console.WriteLine("PASS " + _checks + " real Harmony progression/runtime checks on independent fixtures; no Unity/game execution");
        }
        catch (Exception error) { Console.WriteLine(error); Environment.ExitCode = 1; }
        finally { RecipeUnlockPatches.Stop(); harmony.UnpatchSelf(); }
    }
    private static void Run()
    {
        Check(Locale.GetOther("cq"+Registration.SmeltingQuality)=="smeltingQuality","Real Harmony binds the native str parameter and localizes the smelting quality");
        Check(Locale.GetOther("cq"+Registration.DragonQuality)=="dragonQuality","The dragon quality uses the same working positional binding");
        Check(Locale.GetOther("unrelated")=="native:unrelated","Other native locale keys are untouched");
        Locale.LoadLanguage();Check(LoongLocalization.Refreshed==1,"Language refresh hooks install alongside the quality hook");
        Check(Item.GetItem(Registration.SmelterId).fullName=="localized:"+Registration.SmelterId,"Item locale hook remains installed");
        var micro = Make("dden.loongnova.micro-space-supply-crate");
        var wrench = Make(Registration.WrenchId); var cell = Make(Registration.CellId); var vanilla = Make("rifle");
        Recipes.recipes.AddRange(new[] { wrench, cell, vanilla });
        var first = Peer(); var second = Peer();
        PlayerCamera.main = new PlayerCamera { body = first };
        Check(!wrench.visible && !cell.visible && vanilla.visible, "Fresh characters see neither locked stage but keep native recipes");
        PlayerCamera.main.TryCraft(); Check(PlayerCamera.main.Crafted == 0, "A stale selected index cannot craft a locked recipe");
        Check(wrench.GetItemsForRecipe() == null, "Locked recipes must not expose a usable native ingredient list");
        first.PickUpItem(new Item(Registration.DragonId), 0);
        Check(wrench.visible && !cell.visible && RecipeUnlocks.Stage(first) == 1, "Actual successful pickup grants only the wrench recipe");
        first.Inventory.Clear(); Check(wrench.visible, "Consumption/dropping does not revoke acquired knowledge");
        Mp.IsRunning = Mp.TogetherReady = true;
        Check(!RecipeUnlocks.Allows(wrench, second), "Another authenticated player remains locked");
        Check(!Together.CraftCommit.Commit(second.Net, 1, 1, wrench, new ushort[0]) && Together.CraftCommit.Spent == 0,
            "A locked remote player is denied before native material spending");
        Check(Together.CraftCommit.Commit(first.Net, 1, 2, wrench, new ushort[0]) && Together.CraftCommit.Spent == 1,
            "The unlocked actor can use the original MP transaction");
        second.PickUpItem(new Item(Registration.WrenchId), 0);
        Check(RecipeUnlocks.Allows(cell,second),"High INT plus successful wrench acquisition unlocks the arsenal recipe");
        second.Inventory.Clear();
        Check(RecipeUnlocks.Allows(cell,second),"Previously acquired wrench recipes remain visible after the wrench is put down");
        Check(!RecipeUnlocks.Allows(micro, second), "Wrench knowledge alone does not reveal the microspace-crate recipe");
        second.PickUpItem(new Item("dden.loongnova.supply-crate"), 0);
        Check(RecipeUnlocks.Allows(micro, second) && !RecipeUnlocks.Allows(micro, first), "Ordinary crate acquisition grants only its owner the additional recipe requirement");
        Check(RecipeUnlocks.Stage(second) == 2 && RecipeUnlocks.Stage(first) == 1, "A purchased/looted wrench grants only its recipient all recipes");
        Check(Together.CraftCommit.Commit(second.Net, 1, 3, cell, new ushort[0]) && Together.CraftCommit.Spent == 2, "The wrench stage enables the native commit");
        var provider = new RecipeUnlocks.SaveProvider();
        var saved = provider.Capture(second);
        var restored = Peer(); provider.Restore(restored, saved, 1, null);
        Check(RecipeUnlocks.HasCrate(restored) && RecipeUnlocks.Allows(micro, restored), "Ordinary crate knowledge survives CCL player save restoration");
        Check(RecipeUnlocks.Stage(restored) == 2 && RecipeUnlocks.Stage(first) == 1, "CCL player payload restores only the corresponding character");
        var replacement = new Body { Net = second.Net }; second.Net.body = replacement; second.Net.plr.body = replacement;
        Check(RecipeUnlocks.Stage(replacement) == 2, "Player-owned knowledge survives a body/layer replacement");
        var oldSave = Peer(); oldSave.Inventory.Add(new Item(Registration.WrenchId));
        Check(RecipeUnlocks.Inspect(oldSave) == 2, "Old saves backfill knowledge from currently carried items");
        var bagOwner = Peer(); var bag = new Container { Parent = bagOwner };
        bag.LoadItem(new Item(Registration.DragonId));
        Check(RecipeUnlocks.Stage(bagOwner) == 1, "Acquisition through a carried container is observed");
        var groundBag = new Container(); groundBag.LoadItem(new Item(Registration.WrenchId));
        Check(RecipeUnlocks.Stage(first) == 1, "A ground container cannot unlock the local player");
        var nestedOwner = Peer(); var nestedBag = new Item("bag"); nestedBag.gameObject.AddComponent<Container>();
        var innerBag = new Item("inner-bag"); innerBag.Children.Add(new Item(Registration.WrenchId)); nestedBag.Children.Add(innerBag);
        nestedOwner.PickUpItem(nestedBag, 0);
        Check(RecipeUnlocks.Stage(nestedOwner) == 2, "Picking up nested containers grants knowledge from their actual contents");
        first.Intelligence = 1; Check(!wrench.visible && wrench.INT == 18, "Unlocking never removes native INT requirements or mutates recipe definitions");
        first.Intelligence = 30;
        Together.PlayerCamera_TryCraft_MultiplayerPatch.Throw = true;
        Exception observed = null;
        try { Together.CraftCommit.Commit(first.Net, 1, 4, wrench, new ushort[0]); } catch (Exception error) { observed = error; }
        Together.PlayerCamera_TryCraft_MultiplayerPatch.Throw = false;
        Check(observed is InvalidOperationException, "The native matcher exception propagates unchanged");
        Check(Together.PlayerCamera_TryCraft_MultiplayerPatch.CheckIfTheseItemsAreEnoughForTheRecipe(cell, new List<Item>()) != null,
            "Exception cleanup restores the actor scope; unrelated matcher calls are unaffected");
        Check(!Together.CraftCommit.Commit(first.Net, 1, 5, cell, new ushort[0]), "The next actor transaction remains correctly gated after an exception");
        Check(Recipes.recipes.Count == 3 && Recipes.recipes[0] == wrench && Recipes.recipes[1] == cell, "Gating preserves shared recipe definitions and index order");
        Mp.IsServer = false;
        PlayerCamera.main.body = first; first.Inventory.Add(new Item(Registration.WrenchId));
        Check(RecipeUnlocks.Inspect(first) == 1, "Client inventory predictions cannot grant authoritative knowledge");
        UnityEngine.Time.unscaledTime = 100; RecipeUnlocks.Tick(); var reply = Mp.Response;
        Check(reply != null, "A client asks the existing authenticated CCL channel for its own state");
        reply(new JObject { ["ok"] = true, ["stage"] = 2 });
        Check(RecipeUnlocks.Stage(first) == 2, "An authenticated host response grants the client its stage");
        UnityEngine.Time.unscaledTime += 3; RecipeUnlocks.Tick(); var stale = Mp.Response;
        NetworkAccess.Connection = new object(); RecipeUnlocks.Tick();
        stale(new JObject { ["ok"] = true, ["stage"] = 0 });
        Check(RecipeUnlocks.Stage(first) == 2, "A delayed reply from an earlier connection cannot overwrite current knowledge");
        Mp.IsServer=true;
        var smith=Peer();var otherSmith=Peer();PlayerCamera.main.body=smith;
        var melt=Make("titaniumslab");melt.items=new List<RecipeItem>{new RecipeItem{quality=new CraftingQuality{id="loongnova.smelting"}}};
        Check(!RecipeUnlocks.Allows(melt,smith),"Smelting recipes start locked without owning the smelter");
        var smelter=new Item("dden.loongnova.portable-dragonflame-smelter");smith.PickUpItem(smelter,0);
        Check(RecipeUnlocks.Allows(melt,smith)&&!RecipeUnlocks.Allows(melt,otherSmith),"Acquiring the smelter unlocks only the acquiring character's smelting recipes");
        Check(melt.visible&&melt.simpleName=="smeltTitanium"&&melt.fullName=="<sprite index=25>smeltTitanium","Smelting visibility and recipe names work after the locale and recipe patches actually install");
        melt.hasMadeBefore=true;Check(melt.fullName=="smeltTitanium","Known smelting recipe keeps the native new-recipe indicator convention");
        Check(vanilla.simpleName=="native recipe"&&vanilla.fullName=="native full recipe","Other recipe names are untouched");
        melt.items[0].UseItem(smelter);Check(Math.Abs(smelter.condition-0.99f)<0.000001f,"Smelting retains the original one-percent tool debit without charging twice");
        for(int i=1;i<100;i++)melt.items[0].UseItem(smelter);
        Check(smelter.condition==0,"The hundredth native smelt settles floating-point wear to zero");
        smith.Inventory.Clear();Check(RecipeUnlocks.Allows(melt,smith),"Smelting knowledge persists after dropping the smelter");
        var smeltSave=provider.Capture(smith);var reloadedSmith=Peer();provider.Restore(reloadedSmith,smeltSave,1,null);
        Check(RecipeUnlocks.Allows(melt,reloadedSmith),"Smelting knowledge survives the existing player save provider");
        RecipeUnlocks.ResetView();
    }
}
