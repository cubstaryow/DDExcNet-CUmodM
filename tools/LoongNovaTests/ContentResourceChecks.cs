using DDEN.LoongNova;
using Mono.Cecil;
using Newtonsoft.Json.Linq;

internal static class ContentResourceChecks
{
    internal static void Inspect(AssemblyDefinition module, Action<bool,string> check, string root)
    {
        var actual=System.Reflection.Assembly.Load(File.ReadAllBytes(Path.Combine(root,"src/DDEN.LoongNova/bin/Release/DDEN.LoongNova.dll")));
        // The original suffix predicate fails on these three real resources. Run the production opener against the actual DLL.
        check(actual.GetManifestResourceNames().Count(n=>n.EndsWith("ampoule.png",StringComparison.Ordinal))==3,
            "The regression fixture must include the real empty/medical/regeneration suffix collision");
        foreach(string name in actual.GetManifestResourceNames().Where(n=>n.EndsWith(".png",StringComparison.Ordinal)))
        {
            const string prefix="DDEN.LoongNova.images.";
            check(name.StartsWith(prefix,StringComparison.Ordinal),"Runtime artwork must retain its exact resource namespace");
            string file=name.Substring(prefix.Length);
            using var stream=ContentResources.Open(actual,"images/"+file);
            using var bytes=new MemoryStream();stream.CopyTo(bytes);
            check(bytes.ToArray().SequenceEqual(File.ReadAllBytes(Path.Combine(root,"src/DDEN.LoongNova/images",file))),
                "The real resource loader must read the exact requested image bytes: "+file);
        }
        using(var stream=ContentResources.Open(actual,"Locales/zh-CN.json"))
        using(var reader=new StreamReader(stream))
            check(JObject.Parse(reader.ReadToEnd())["other"]!.Value<string>("dden.loongnova.module")=="龙域武库","Native locale files and images must use their own exact resource paths");
        foreach(string missing in new[]{"images/missing.png","images/source/ampoule.png","ampoule.png","Content.json"})
        {
            bool failed=false;
            try{using var unexpected=ContentResources.Open(actual,missing);}catch(FileNotFoundException error){failed=error.FileName=="DDEN.LoongNova."+missing.Replace('/','.');}
            check(failed,"Missing full paths must fail explicitly rather than resolve a similarly named sprite: "+missing);
        }
        var registration=module.MainModule.Types.Single(t=>t.Name=="Registration");
        check(registration.Methods.Single(m=>m.Name=="Resource").Body.Instructions.Any(i=>i.Operand is MethodReference m
            &&m.DeclaringType.Name=="ContentResources"&&m.Name=="Open"),"Registration must call the actually tested production resource loader");
        check(registration.Methods.Single(m=>m.Name=="LoadSprite").Body.Instructions.Any(i=>i.Operand as string=="images/"),
            "Sprite registration must supply full image resource paths");
        var spriteCalls=registration.Methods.Single(m=>m.Name=="LoadSprite").Body.Instructions
            .Select(i=>i.Operand).OfType<MethodReference>().ToArray();
        check(!spriteCalls.Any(m=>m.Name=="SetPixels32"||m.Name=="Destroy")
            &&spriteCalls.Any(m=>m.Name=="LoadImage")&&spriteCalls.Any(m=>m.DeclaringType.Name=="Sprite"&&m.Name=="Create"),
            "Sprite loading must retain the decoded source texture, without resampling or destroying its pixels");
        foreach(int resolution in new[]{8,12,16,32,64})
            check(Math.Abs(resolution/SpritePixelScale.PixelsPerUnit(resolution,resolution,8,0.5f)-0.5f)<0.00001f,
                "Membrane/component detail resolution must not change their half-unit world size");
        check(SpritePixelScale.PixelsPerUnit(24,72,12,1,logicalPixelsPerUnit:36)==72,
            "Doubling round artwork resolution must preserve its physical size, not double the displayed round");
        check(16/SpritePixelScale.PixelsPerUnit(11,16,16,1,worldLength:2)==2,
            "The source smelter portrait must keep its explicit two-unit world length without a raster resize");
        foreach(string file in registration.Methods.Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions)
            .Select(i=>i.Operand as string).Where(s=>s!=null&&s.EndsWith(".png",StringComparison.Ordinal)).Distinct())
        {
            using var stream=ContentResources.Open(actual,"images/"+file);
            check(stream.Length>0,"Every image requested by registration must be readable through the production opener: "+file);
        }
    }
}
