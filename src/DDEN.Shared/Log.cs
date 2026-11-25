using System;
using System.Collections.Generic;
using BepInEx.Logging;

namespace DDEN.Shared
{
    public static class Log
    {
        private sealed class Sink { internal ManualLogSource Source; internal int Priority; }
        private static readonly Dictionary<string, Sink> Sinks = new Dictionary<string, Sink>();
        private static readonly object Sync = new object();
        private static volatile ManualLogSource _sink;
        private static ManualLogSource _source;
        private static string _tag = "DDEN";
        private static volatile bool _verbose, _managed;
        public static bool Verbose { get => _verbose; set { _verbose = value; _managed = true; } }
        internal static bool Managed => _managed;
        public static void RegisterSink(string owner, ManualLogSource source, int priority)
        {
            if (string.IsNullOrEmpty(owner) || source == null) return;
            lock (Sync) { Sinks[owner] = new Sink { Source = source, Priority = priority }; SelectSink(); }
        }
        public static void UnregisterSink(string owner)
        { lock (Sync) { Sinks.Remove(owner); SelectSink(); } }
        private static void SelectSink()
        {
            Sink best = null;
            foreach (var candidate in Sinks.Values) if (best == null || candidate.Priority > best.Priority) best = candidate;
            _sink = best?.Source;
        }
        public static void Attach(ManualLogSource source, string tag, bool verbose = false)
        { _source = source; _tag = tag ?? "DDEN"; if (!_managed) _verbose = verbose; }
        public static LogChannel For(ManualLogSource source, string tag, bool verbose = false) => new LogChannel(source, tag, verbose);
        public static void Info(string message) => Write(_source, _tag, LogLevel.Info, message);
        public static void Warn(string message) => Write(_source, _tag, LogLevel.Warning, message);
        public static void Error(string message) => Write(_source, _tag, LogLevel.Error, message);
        public static void Debug(string message) { if (Verbose) Write(_source, _tag, LogLevel.Debug, message); }
        public static void Exception(Exception ex, string context = null) => Error((context == null ? "" : context + " -> ") + ex);
        internal static void Write(ManualLogSource fallback, string tag, LogLevel level, string message)
        {
            var source = _sink ?? fallback;
            string line = "[" + tag + "] " + message;
            if (source == null) UnityEngine.Debug.Log(line); else source.Log(level, line);
        }
    }
    public sealed class LogChannel
    {
        private readonly ManualLogSource _source;
        private readonly string _tag;
        private bool _verbose;
        public bool Verbose { get => Log.Managed ? Log.Verbose : _verbose; set => _verbose = value; }
        public LogChannel(ManualLogSource source, string tag, bool verbose = false)
        { _source = source; _tag = tag ?? "DDEN"; _verbose = verbose; }
        public void Info(string message) => Log.Write(_source, _tag, LogLevel.Info, message);
        public void Warn(string message) => Log.Write(_source, _tag, LogLevel.Warning, message);
        public void Error(string message) => Log.Write(_source, _tag, LogLevel.Error, message);
        public void Debug(string message) { if (Verbose) Log.Write(_source, _tag, LogLevel.Debug, message); }
        public void Exception(Exception ex, string context = null) => Error((context == null ? "" : context + " -> ") + ex);
    }
}
