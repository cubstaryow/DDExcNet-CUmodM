using System;
using System.Collections.Generic;

namespace DDEN.Shared
{
    internal static class Log { public static void Debug(string s) {} public static void Info(string s) {} public static void Warn(string s) {} public static void Exception(Exception e, string s) {} }
    internal static class Compat { public static bool IsQolAutosaveBusy() => false; }
    internal static class Game { public static Body LocalBody => null; public static bool InWorld => false; }
    internal static class Reflect
    {
        public static bool TrySetInstance(object o, string s, object v) => false;
        public static Type Type(string name) => null;
        public static object GetStatic(Type type, string name) => null;
        public static object InvokeStatic(Type type, string name) => null;
    }
    internal static class LiveFields
    {
        public static readonly object BodyFields = null, LimbFields = null;
        public static bool ApplyNumber(object o, object fields, string s, double v) => false;
        public static bool ApplyBool(object o, object fields, string s, bool v) => false;
    }
}
namespace DDEN.SaveEditor
{
    internal static class EditorLabels { public static string Limb(int i) => "部位" + i; public static string Field(FieldRow row) => row.Label; public static string Item(string id) => id == "torch" ? "火把" : id; }
}
