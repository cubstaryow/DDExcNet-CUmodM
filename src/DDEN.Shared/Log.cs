using System;
using BepInEx.Logging;

namespace DDEN.Shared
{
    /// <summary>共享代码用的日志门面（写到最近一次 Attach 的源）。</summary>
    public static class Log
    {
        private static ManualLogSource _source;
        private static string _tag = "DDEN";
        private static bool _verbose;

        public static bool Verbose
        {
            get => _verbose;
            set => _verbose = value;
        }

        public static void Attach(ManualLogSource source, string tag, bool verbose = false)
        {
            _source = source;
            _tag = tag ?? "DDEN";
            _verbose = verbose;
        }

        /// <summary>给每个插件一份独立通道，避免 A 的日志被标成 B 的标签。</summary>
        public static LogChannel For(ManualLogSource source, string tag, bool verbose = false)
        {
            return new LogChannel(source, tag, verbose);
        }

        public static void Info(string message) => Write(LogLevel.Info, message);
        public static void Warn(string message) => Write(LogLevel.Warning, message);
        public static void Error(string message) => Write(LogLevel.Error, message);

        public static void Debug(string message)
        {
            if (_verbose) Write(LogLevel.Debug, message);
        }

        public static void Exception(Exception ex, string context = null)
        {
            Write(LogLevel.Error, (context == null ? string.Empty : context + " -> ") + ex);
        }

        private static void Write(LogLevel level, string message)
        {
            string line = $"[{_tag}] {message}";
            if (_source == null)
            {
                UnityEngine.Debug.Log(line);
                return;
            }
            _source.Log(level, line);
        }
    }

    /// <summary>插件级日志通道（前缀固定为自己）。</summary>
    public sealed class LogChannel
    {
        private readonly ManualLogSource _source;
        private readonly string _tag;
        public bool Verbose { get; set; }

        public LogChannel(ManualLogSource source, string tag, bool verbose = false)
        {
            _source = source;
            _tag = tag ?? "DDEN";
            Verbose = verbose;
        }

        public void Info(string message) => Write(LogLevel.Info, message);
        public void Warn(string message) => Write(LogLevel.Warning, message);
        public void Error(string message) => Write(LogLevel.Error, message);

        public void Debug(string message)
        {
            if (Verbose) Write(LogLevel.Debug, message);
        }

        public void Exception(Exception ex, string context = null)
        {
            Write(LogLevel.Error, (context == null ? string.Empty : context + " -> ") + ex);
        }

        private void Write(LogLevel level, string message)
        {
            string line = $"[{_tag}] {message}";
            if (_source == null) UnityEngine.Debug.Log(line);
            else _source.Log(level, line);
        }
    }
}
