using System;
using System.Collections.Generic;

namespace DDExcNet.CUmodM
{
    internal static class ConfigNavigation
    {
        internal const string Manager = "config:@manager";
        internal static bool Selected(string page, string id) => page == id || page == "config:" + id;
        // IDdenModule requires DrawPage; external providers may omit their home page.
        internal static string Back(string home, bool hasHome) => hasHome ? home : "overview";
    }
    internal sealed class ConfigFolds
    {
        private readonly HashSet<string> _closed = new HashSet<string>(StringComparer.Ordinal);
        internal bool Expanded(string key) => !_closed.Contains(key);
        internal void Toggle(string key) { if (!_closed.Remove(key)) _closed.Add(key); }
        internal float Height(string key, float entries) => 36f + (Expanded(key) ? entries : 0f);
    }
}
