using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using DDEN.MPfix;

class Program
{
    static int checks;
    static void Check(bool value,string why) { checks++; if(!value) throw new Exception(why); }
    static void Main()
    {
        try
        {
            var harmony=new Harmony("dden.chinese.fixture");
            TogetherChineseRepair.Install(harmony);
            string key="hello"; bool allow=false;
            Check(Together.Lang.Get(in key,in allow)=="你好","readonly string/bool Harmony adapter selects embedded Chinese");
            key="rule_name_RagdollImpact"; Check(Together.Lang.Get(in key,in allow)=="布娃娃冲击","missing Chinese translation repaired");
            Together.Lang.CurrentLocale="EN"; key="hello"; Check(Together.Lang.Get(in key,in allow)=="original hello","English preview preserved");
            Together.Lang.CurrentLocale="zh-TW"; Check(Together.Lang.Get(in key,in allow)=="original hello","other language preserved");
            Together.Lang.CurrentLocale="zh-CN"; PlayerNameRepair.FontEnabled=false;
            Check(Together.Lang.Get(in key,in allow)=="original hello","disabled feature retains native translation");
            PlayerNameRepair.FontEnabled=true;
            var label=new TMPro.TextMeshProUGUI(); string name="hello", empty=""; bool translate=true;
            Together.UIManager.SetUITooltip(in label,in name,in empty,in empty,translate);
            Check(label.text=="你好" && PlayerNameFonts.Prepared==1,"readonly derived TMP label binds base-class postfix parameter");
            var style=new UnityEngine.GUIStyle(); Together.UIBullshit.DoFontThings(style);
            Check(PlayerNameRepair.Styles==1,"native menu font callback runs original and postfix");
            harmony.UnpatchSelf(); Check(Together.Lang.Get(in key,in allow)=="original hello","only owned hooks are removable");
            Console.WriteLine("PASS "+checks+" real Harmony Chinese callback checks; independent mocks only");
        }
        catch(Exception error) { Console.WriteLine(error.GetType().FullName+": "+error.Message); Environment.ExitCode=1; }
    }
}
namespace UnityEngine { public class GUIStyle {} }
namespace TMPro { public class TMP_Text { public string text; } public class TextMeshProUGUI:TMP_Text {} }
namespace DDEN.Shared
{
    public static class Reflect { public static Type Type(string name) => typeof(Reflect).Assembly.GetType(name); }
}
namespace DDEN.MPfix
{
    static class MPfixPlugin { public static Logs Log=new Logs(); }
    class Logs { public void Debug(string text) {} }
    static class PlayerNameRepair { public static bool FontEnabled=true; public static int Styles; public static void ObserveText(string text) {} public static void PrepareGuiStyle(UnityEngine.GUIStyle style) { Styles++; } }
    static class PlayerNameFonts { public static int Prepared; public static void Prepare(TMPro.TMP_Text label) { Prepared++; } }
}
namespace Together
{
    static class Lang
    {
        internal static string CurrentLocale { get; set; }="zh-CN";
        internal static Dictionary<string,Dictionary<string,string>> dict=new Dictionary<string,Dictionary<string,string>> { ["zh-CN"]=new Dictionary<string,string> { ["hello"]="你好" } };
        [MethodImpl(MethodImplOptions.NoInlining)] public static string Get(in string key,in bool allow=false)=>"original "+key;
    }
    static class UIManager
    {
        [MethodImpl(MethodImplOptions.NoInlining)] public static void SetUITooltip(in TMPro.TextMeshProUGUI text,in string label,in string tooltip,in string desc,bool translate=true) { text.text=Lang.Get(in label); }
    }
    static class UIBullshit { [MethodImpl(MethodImplOptions.NoInlining)] public static void DoFontThings(UnityEngine.GUIStyle style) {} }
}
