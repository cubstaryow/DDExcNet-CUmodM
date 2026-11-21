using System;
using System.IO;
using System.Linq;
using DDEN.SaveEditor;
using DDEN.Shared;
using Newtonsoft.Json.Linq;

internal static class Program
{
    private static int _checks;
    private static void Check(bool condition, string label) { _checks++; if (!condition) throw new Exception(label); }
    private static void Main(string[] args)
    {
        string folder = Path.Combine(Path.GetTempPath(), "DDEN-editor-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try { Run(folder); foreach (string path in args) ReadOnlySave(path, folder); Console.WriteLine("PASS " + _checks + " checks: editor document/codec, Unicode, ammo/battery/mixed liquids, review, paired items, scroll endpoints and icon aspect ratio."); }
        finally
        {
            string target = Path.GetFullPath(folder);
            if (!target.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(target).StartsWith("DDEN-editor-tests-")) throw new Exception("unsafe cleanup");
            Directory.Delete(target, true);
        }
    }
    private static void ReadOnlySave(string path, string folder)
    {
        byte[] before = File.ReadAllBytes(path);
        var original = SaveCodec.Read(path);
        var doc = new SaveDocument(); Check(doc.Load(path), "native save loads read-only");
        Check(JToken.DeepEquals(original, doc.Doc) && doc.Changes().Count == 0, "all native component views preserve complete original document");
        Check(doc.Items.All(i => i.PropertiesAvailable), "native save item/component arrays align");
        string copy = Path.Combine(folder, "native-roundtrip.sv"); File.WriteAllBytes(copy, SaveCodec.Encode(doc.Doc));
        Check(JToken.DeepEquals(original, SaveCodec.Read(copy)), "native save round-trips through temporary copy only");
        Check(File.ReadAllBytes(path).SequenceEqual(before), "native save bytes remain unchanged");
        Console.WriteLine("Read-only native schema: " + doc.Items.Count + " items, " + doc.Items.Sum(i => i.PropertyRows.Count()) + " component fields.");
    }
    private static void Run(string folder)
    {
        var original = JObject.Parse(@"{'cheatedSave':false,'runTime':42.5,'body':{'bloodVolume':20.0,'temperature':35.0,'skills':{'STR':2}},'limbs':[{'skinHealth':40.0,'muscleHealth':45.0,'broken':true}],'runSettings':[{'Item1':'attackdamage','Item2':3.0}],'items':[{'id':'torch','condition':0.5,'slot':0,'label':'测试😀'},{'id':'bag','condition':0.8,'slot':1},{'id':'ore','condition':0.7,'slot':1}],'itemComponents':[{'x':1},{'x':2},{'x':3}],'modData':{'unknown':[1,{'标签':'原样保留'}]}}" );
        string path = Path.Combine(folder, "fixture.sv");
        File.WriteAllBytes(path, SaveCodec.Encode(original));
        Check(JToken.DeepEquals(original, SaveCodec.Read(path)), "ASCII save container preserves Chinese and supplementary Unicode");
        var doc = new SaveDocument(); Check(doc.Load(path), "loads fixture");
        Check(!doc.Dirty && doc.Changes().Count == 0, "new session has no pending changes");
        var row = doc.BodyRows.Single(r => r.Label == "temperature");
        Check(!row.ApplyText("NaN") && row.Error != null && row.Number == 35, "NaN rejected without changing token");
        Check(!doc.ValidateForSave(), "invalid visible edit blocks save");
        Check(!row.ApplyText("Infinity") && row.Number == 35, "infinity rejected");
        Check(row.ApplyText("37.5") && row.Error == null, "valid input clears error"); doc.Dirty = true;
        Check(doc.Changes().Count == 1 && doc.Changes()[0].Path == "body.temperature", "review reports only edited field");
        doc.Save(); Check(!doc.StatusIsError && !doc.Dirty && doc.Changes().Count == 0, "successful save establishes a new baseline");
        Check(File.Exists(path + ".cumod.bak"), "save keeps backup");
        Check(JToken.DeepEquals(original, SaveCodec.Read(path + ".cumod.bak")), "backup contains the complete previous save including Unicode");
        Check(!SaveCodec.Read(path).Value<bool>("cheatedSave") && JToken.DeepEquals(original["modData"], SaveCodec.Read(path)["modData"]), "achievement and unknown mod fields unchanged");
        doc.SetItemCondition(doc.Items[0], 1); doc.DiscardChanges();
        Check(!doc.Dirty && doc.Items[0].ConditionValue == 0.5f && doc.Changes().Count == 0, "discard restores exact saved document");
        int count = doc.Items.Count; doc.DeleteItem(doc.Items[1]);
        Check(doc.Items.Count == count && doc.StatusIsError, "non-empty container deletion blocked");
        doc.DeleteItem(doc.Items[2]); Check(doc.Items.Count == 2 && ((JArray)doc.Doc["itemComponents"]).Count == 2, "child removal preserves paired component alignment");
        Check(doc.Changes().Count == 2, "structural deletion summarized as paired arrays");
        doc.DiscardChanges(); doc.Doc["itemComponents"] = new JArray(); doc.Rebuild();
        doc.DeleteItem(doc.Items[0]); Check(doc.Items.Count == 3 && doc.StatusIsError, "mismatched components block destructive edit");
        doc.DiscardChanges(); doc.PresetFullHeal(); Check(!doc.Doc.Value<bool>("cheatedSave"), "health preset never changes achievement flag");
        Check(JToken.DeepEquals(original["modData"], doc.Doc["modData"]), "health preset preserves unknown data");
        doc.DiscardChanges(); File.WriteAllBytes(path, SaveCodec.Encode(new JObject { ["newerProgress"] = true }));
        Check(!doc.ValidateForSave() && doc.StatusIsError, "external save update blocks stale overwrite");
        doc.Save(); Check(SaveCodec.Read(path).Value<bool>("newerProgress"), "stale save cannot overwrite newer bytes");
        Check(!doc.Load(Path.Combine(folder, "missing.sv")) && doc.Doc != null && doc.Items.Count == 3, "failed load preserves current document");
        doc.ItemFilter = "火把"; doc.RefreshVisibleItems(); Check(doc.VisibleGroups.Count == 1 && doc.VisibleGroups[0].Head.Id == "torch", "localized item search");
        doc.ItemFilter = "测试"; doc.RefreshVisibleItems(); Check(doc.VisibleGroups.Count == 1, "custom label search");
        var many = (JObject)original.DeepClone(); many["items"] = new JArray(Enumerable.Range(0, 701).Select(i => new JObject { ["id"] = "item" + i, ["slot"] = i, ["condition"] = 1.0 }));
        string large = Path.Combine(folder, "large.sv"); File.WriteAllBytes(large, SaveCodec.Encode(many));
        Check(doc.Load(large) && doc.Items.Count == 701, "large inventories are not silently truncated at 600");
        var booleanChange = SaveChanges.Compare(new JObject { ["cheatedSave"] = false }, new JObject { ["cheatedSave"] = true }).Single();
        Check(booleanChange.Before == "关闭" && booleanChange.After == "开启", "review uses readable boolean states");
        string blocked = Path.Combine(folder, "blocked.sv");
        byte[] originalBytes = SaveCodec.Encode(original); File.WriteAllBytes(blocked, originalBytes);
        Directory.CreateDirectory(blocked + ".cumod.bak");
        bool refused = false;
        try { SaveCodec.Write(blocked, new JObject { ["shouldNotReplace"] = true }); } catch (IOException) { refused = true; } catch (UnauthorizedAccessException) { refused = true; }
        Check(refused && File.ReadAllBytes(blocked).SequenceEqual(originalBytes), "failed backup/replacement leaves original save intact");
        foreach (int width in new[] { 100, 320, 640, 1200 }) foreach (int height in new[] { 80, 240, 600 })
        foreach (int cw in new[] { 0, 80, 310, 2000 }) foreach (int ch in new[] { 0, 60, 220, 10000 })
        {
            var scroll = new ScrollLayout(width, height, cw, ch);
            Check(scroll.Width > 0 && scroll.Height > 0 && scroll.MaxX >= 0 && scroll.MaxY >= 0, "scroll geometry remains valid");
            Check(scroll.Width <= width && scroll.Height <= height, "scroll bars stay inside viewport");
        }
        var coupled = new ScrollLayout(100, 100, 95, 101);
        Check(coupled.Horizontal && coupled.Vertical, "one scrollbar can require the other");
        var fit = new ScrollLayout(100, 100, 99, 99); Check(!fit.Horizontal && !fit.Vertical && fit.MaxY == 0, "fitting content has no phantom scrollbar");
        foreach (float track in new[] { 1f, 12f, 50f, 300f, 800f })
        foreach (float content in new[] { 0f, 20f, 320f, 10000f, 1000000f })
        {
            var axis = new ScrollAxis(track, 240, content);
            Check(axis.Thumb >= 0 && axis.Thumb <= track && axis.Travel >= 0, "thumb stays inside track");
            Check(axis.Offset(0) == 0 && axis.Value(0) == 0, "scroll starts at content top");
            if (axis.Travel > 0)
            {
                Check(Math.Abs(axis.Value(axis.Travel) - axis.Maximum) < .1, "thumb at track end reaches final content pixel");
                Check(Math.Abs(axis.Value(axis.Offset(axis.Maximum / 2)) - axis.Maximum / 2) < .1, "thumb and content use same range");
            }
        }
        ItemProperties(folder);
        foreach (var size in new[] { (200f, 30f), (20f, 180f), (64f, 64f), (3f, 400f) })
        foreach (var box in new[] { (22f, 22f), (56f, 56f), (100f, 60f) })
        {
            var fitImage = new ImageFit(box.Item1, box.Item2, size.Item1, size.Item2);
            Check(fitImage.Width <= box.Item1 + .001 && fitImage.Height <= box.Item2 + .001 && fitImage.X >= -.001 && fitImage.Y >= -.001, "item icon fits and is centered inside its cell");
            Check(Math.Abs(fitImage.Width / fitImage.Height - size.Item1 / size.Item2) < .001, "wide and tall item icons retain source aspect ratio");
        }
    }
    private static void ItemProperties(string folder)
    {
        const string gunKey = "GunScript, Assembly-CSharp, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null";
        var fixture = JObject.Parse(@"{'cheatedSave':false,'items':[{'id':'gun','slot':0,'condition':0.8,'label':null,'swatch':0},{'id':'mag','slot':1,'condition':1.0},{'id':'torch','slot':2,'condition':0.25},{'id':'bottle','slot':3,'condition':0.6}],'itemComponents':[{'GunScript, Assembly-CSharp, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null':{'roundInChamber':2,'roundsInMag':3,'magCapacity':10,'hasMag':true,'safe':false},'Custom.ModState, ExampleMod':{'nested':{'level':4},'extra':[true,'原样😀']}},{'AmmoScript, Assembly-CSharp':{'rounds':2,'maxRounds':20}},{'BatteryItem, Assembly-CSharp':{'batteryType':'smallbattery','maxCharge':50.0,'batteryWasFavourited':true}},{'WaterContainerItem, Assembly-CSharp':{'stack':[{'liquidId':'water','amount':200.0,'modTag':'保留'},{'liquidId':'heroin','amount':100.0}]}}],'unknown':{'x':42}}" );
        string path = Path.Combine(folder, "components.sv"); File.WriteAllBytes(path, SaveCodec.Encode(fixture));
        var doc = new SaveDocument { LiquidCapacity = _ => 500, IsLiquidKnown = id => id == "water" || id == "heroin" || id == "oil", BatteryLimit = _ => 100, IsBatteryKnown = id => id == "smallbattery" || id == "mediumbattery" };
        Check(doc.Load(path), "loads actual-shaped component keys");
        Check(doc.Items.All(i => i.PropertiesAvailable), "paired arrays expose all component views");
        Check(JToken.DeepEquals(fixture, doc.Doc) && doc.Changes().Count == 0, "building property views does not mutate any saved token");
        var gun = doc.Items[0]; var mag = doc.Items[1]; var torch = doc.Items[2]; var bottle = doc.Items[3];
        Check(gun.Label != null && gun.Label.ApplyText("中文枪械"), "null native item label can be edited");
        var gunRounds = gun.Sections[0].Rows.Single(r => r.Label == "roundsInMag");
        Check(gunRounds.ApplyText("10") && doc.Doc["itemComponents"][0][gunKey].Value<int>("roundsInMag") == 10, "gun count edits exact paired token");
        Check(!gunRounds.ApplyText("-1") && gunRounds.Number == 10 && !doc.ValidateForSave(), "negative ammunition rejected and invalid buffer blocks save");
        Check(!gunRounds.ApplyText("1.5") && !gunRounds.ApplyText("2147483648"), "fractional and overflowing ammo rejected");
        Check(gunRounds.ApplyText("11") && !doc.ValidateForSave(), "gun over-capacity count blocks save");
        Check(gunRounds.ApplyText("10") && doc.ValidateForSave(), "valid ammunition unblocks save");
        Check(doc.Changes().Any(c => c.Path == gunRounds.Token.Path), "review paths match qualified component field views for localization");
        var magRounds = mag.Sections[0].Rows.Single(r => r.Label == "rounds");
        Check(magRounds.ApplyText("20") && magRounds.Number == 20, "magazine has independent ammunition count");
        Check(!doc.SetBattery(torch, "mediumbattery", 300), "oversized battery rejected");
        Check(!doc.SetBattery(torch, "missingmod", 50), "unregistered battery rejected");
        Check(doc.SetBattery(torch, "mediumbattery", 100) && torch.ConditionValue == 1 && torch.BatterySection.Raw.Value<double>("maxCharge") == 100, "installing battery sets saved type, capacity and full condition");
        Check(doc.SetBattery(torch, "", 0) && torch.ConditionValue == 0 && !torch.BatterySection.Raw.Value<bool>("batteryWasFavourited"), "removing battery clears capacity, charge and favorite state");
        Check(doc.SetBattery(torch, "smallbattery", 50), "battery can be reinstalled");
        var water = bottle.LiquidSection.Liquids[0];
        Check(water.Amount.ApplyText("300") && water.Amount.Number == 300, "liquid amount edits native mL scalar");
        doc.SyncLiquidCondition(bottle, 500); Check(Math.Abs(bottle.ConditionValue - .8) < .0001, "liquid contents synchronize saved fill fraction");
        Check(!water.Amount.ApplyText("NaN") && !water.Amount.ApplyText("-20") && !doc.ValidateForSave(), "invalid liquid amount blocks save without changing content");
        Check(water.Amount.ApplyText("350"), "correct liquid input clears error");
        doc.AddLiquid(bottle, "oil", 500);
        Check(bottle.LiquidSection.Liquids.Count == 3 && bottle.LiquidSection.Liquids[2].Amount.Number == 50, "new liquid uses remaining container capacity, retaining other species");
        Check(water.Raw.Value<string>("modTag") == "保留", "liquid edits preserve mod-specific stack fields");
        Check(water.Amount.ApplyText("400") && !doc.ValidateForSave(), "combined liquid volume cannot exceed capacity");
        Check(water.Amount.ApplyText("350"), "volume can be corrected");
        Check(water.Kind.ApplyText("unregistered") && !doc.ValidateForSave(), "new unknown liquid kind cannot be saved");
        Check(water.Kind.ApplyText("water"), "known liquid kind restores valid state");
        doc.RemoveLiquid(bottle, bottle.LiquidSection.Liquids[1], 500);
        Check(bottle.LiquidSection.Liquids.Count == 2 && Math.Abs(bottle.ConditionValue - .8) < .0001, "removing only one mixture component recomputes fill");
        doc.AllItemsFullCondition(); Check(Math.Abs(bottle.ConditionValue - .8) < .0001, "bulk repair does not overwrite liquid fill fraction");
        var extension = gun.Sections.Single(s => s.Type == "ModState").Rows.Single(r => r.Label == "level");
        Check(extension.ApplyText("5") && gun.Sections[1].Raw["nested"].Value<int>("level") == 5, "mod nested saved properties remain editable through original JTokens");
        doc.Save(); Check(!doc.StatusIsError, "changed components save through normal guarded writer");
        var output = SaveCodec.Read(path);
        Check(JToken.DeepEquals(output, JObject.Parse(doc.Doc.ToString())), "ammo, battery and mixed liquids round-trip through JSON precision exactly");
        Check(JToken.DeepEquals(fixture["unknown"], output["unknown"]) && !output.Value<bool>("cheatedSave"), "item editing preserves unrelated data and achievement flag");
        Check(JToken.DeepEquals(fixture["itemComponents"][0]["Custom.ModState, ExampleMod"]["extra"], output["itemComponents"][0]["Custom.ModState, ExampleMod"]["extra"]), "untouched nested Unicode extension arrays survive save");
        doc.DeleteItem(doc.Items[1]);
        Check(doc.Items[1].Id == "torch" && doc.Items[1].BatterySection != null, "deleting preceding item retains component alignment");
        doc.DiscardChanges(); Check(doc.Items.Count == 4 && doc.Changes().Count == 0, "discard restores complete component session");
        doc.Doc["itemComponents"] = new JArray(); doc.Rebuild();
        Check(doc.Items.All(i => !i.PropertiesAvailable && i.Sections.Count == 0), "mismatched component arrays cannot edit another item's properties");
    }
}
