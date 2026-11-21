using System;
using System.Collections.Generic;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;

namespace DDEN.NetPerfTracker
{
    /// <summary>Observe an exact 2D simulation marker only. Never infer physics duration from frame scheduling.</summary>
    internal sealed class PhysicsTiming : IDisposable
    {
        private ProfilerRecorder _recorder;
        private bool _tried;
        internal string Source { get; private set; } = "unavailable";
        internal void Start()
        {
            if (_tried) return;
            _tried = true;
            try
            {
                var handles = new List<ProfilerRecorderHandle>();
                ProfilerRecorderHandle.GetAvailable(handles);
                foreach (var handle in handles)
                {
                    var d = ProfilerRecorderHandle.GetDescription(handle);
                    if (d.Name != "Physics2D.Simulate" || d.UnitType != ProfilerMarkerDataUnit.TimeNanoseconds) continue;
                    _recorder = new ProfilerRecorder(handle, 1,
                        ProfilerRecorderOptions.Default | ProfilerRecorderOptions.StartImmediately);
                    if (_recorder.Valid) Source = d.Name;
                    break;
                }
                NetPerfTrackerPlugin.Log.Info("[采样来源] 2D 模拟计时：" + Source + "（缺失时显示不可用，不估算）");
            }
            catch (Exception ex) { NetPerfTrackerPlugin.Log.Debug("2D 计时不可用：" + ex.Message); }
        }
        internal float ReadMs()
        {
            try { return _recorder.Valid && _recorder.Count > 0 ? _recorder.LastValue / 1000000f : -1f; }
            catch { return -1f; }
        }
        public void Dispose()
        {
            if (_recorder.Valid) _recorder.Dispose();
            _recorder = default; _tried = false; Source = "unavailable";
        }
    }
}
