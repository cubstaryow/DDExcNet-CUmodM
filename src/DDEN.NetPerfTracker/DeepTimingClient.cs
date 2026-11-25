using System;
using System.Diagnostics;
using DDEN.Diagnostics;
using DDEN.Shared;
using Newtonsoft.Json;

namespace DDEN.NetPerfTracker
{
    internal sealed class DeepTimingClient
    {
        private Func<bool> _present, _enabled;
        private Action<bool> _setEnabled;
        private Func<string> _capture, _evidence;
        private Action _stop, _clear;
        private long _nextResolve;
        private bool _failed;
        private void Resolve()
        {
            if (_present != null || Stopwatch.GetTimestamp() < _nextResolve) return;
            _nextResolve = Stopwatch.GetTimestamp() + Stopwatch.Frequency;
            var type = Reflect.Type("DDEN.NetPerfTracker.Enhanced.DeepProbeBridge");
            if (type == null) return;
            try
            {
                var version = type.GetProperty("ApiVersion")?.GetValue(null);
                if (!(version is int api) || api != 1) { _failed = true; return; }
                var present = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), type.GetProperty("Present").GetGetMethod());
                _enabled = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), type.GetProperty("Enabled").GetGetMethod());
                _setEnabled = (Action<bool>)Delegate.CreateDelegate(typeof(Action<bool>), type.GetMethod("SetEnabled"));
                _capture = (Func<string>)Delegate.CreateDelegate(typeof(Func<string>), type.GetMethod("CaptureWindow"));
                _stop = (Action)Delegate.CreateDelegate(typeof(Action), type.GetMethod("Stop"));
                _clear = (Action)Delegate.CreateDelegate(typeof(Action), type.GetMethod("Clear"));
                var evidence = type.GetMethod("CaptureEvidence");
                if (evidence != null) _evidence = (Func<string>)Delegate.CreateDelegate(typeof(Func<string>), evidence);
                _present = present; _failed = false;
            }
            catch { _failed = true; }
        }
        internal bool Available { get { Resolve(); try { return _present?.Invoke() == true; } catch { return false; } } }
        internal bool Enabled { get { if (!Available) return false; try { return _enabled(); } catch { return false; } } }
        internal void Toggle() { try { if (Available) _setEnabled(!Enabled); } catch { _failed = true; } }
        internal void Stop() { try { if (Available) _stop(); } catch { _failed = true; } }
        internal void Clear() { try { if (Available) _clear(); } catch { _failed = true; } }
        internal bool Failed => _failed;
        internal string Evidence()
        {
            if (!Available || _evidence == null) return null;
            try
            {
                string json = _evidence();
                if (string.IsNullOrEmpty(json) || json.Length > 262144) return null;
                var value = JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(json,
                    new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None, MaxDepth = 8 });
                return value?.ToString(Formatting.None);
            }
            catch { return null; }
        }
        internal DeepWindow Capture()
        {
            if (!Enabled) return null;
            try
            {
                string json = _capture();
                if (string.IsNullOrEmpty(json) || json.Length > 65536) return null;
                var data = JsonConvert.DeserializeObject<DeepWindow>(json,
                    new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None, MaxDepth = 8 });
                if (data == null || data.Schema != 1 || data.Rows == null || data.Rows.Length > 64
                    || data.Counts == null || data.Counts.Length > 16) { _failed = true; return null; }
                return data;
            }
            catch { _failed = true; return null; }
        }
    }
}
