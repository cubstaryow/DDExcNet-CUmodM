using System;
using System.Collections.Generic;
using System.Text;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

namespace DDEN.NetPerfTracker
{
    /// <summary>Explicit self-test only: discover what this installed player actually exposes before adding diagnostics.</summary>
    internal static class TimingProbe
    {
        internal static void Run()
        {
            try
            {
                bool enabled = FrameTimingManager.IsFeatureEnabled();
                NetPerfTrackerPlugin.Log.Info("[TIMINGCAP] frameTiming=" + enabled + " gpuFrequency=" + FrameTimingManager.GetGpuTimerFrequency() + " cpuFrequency=" + FrameTimingManager.GetCpuTimerFrequency());
                var handles = new List<ProfilerRecorderHandle>();
                ProfilerRecorderHandle.GetAvailable(handles);
                var names = new StringBuilder(); int count = 0;
                foreach (var handle in handles)
                {
                    var d = ProfilerRecorderHandle.GetDescription(handle);
                    string name = d.Name;
                    if (name.IndexOf("Wait", StringComparison.OrdinalIgnoreCase) < 0 &&
                        name.IndexOf("Thread", StringComparison.OrdinalIgnoreCase) < 0 &&
                        name.IndexOf("Frame", StringComparison.OrdinalIgnoreCase) < 0 &&
                        name.IndexOf("Physics", StringComparison.OrdinalIgnoreCase) < 0 &&
                        name.IndexOf("PlayerLoop", StringComparison.OrdinalIgnoreCase) < 0 &&
                        name.IndexOf("Render", StringComparison.OrdinalIgnoreCase) < 0 &&
                        name.IndexOf("BehaviourUpdate", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (names.Length > 0) names.Append(" | ");
                    names.Append(name).Append('[').Append(d.UnitType).Append(']'); count++;
                }
                NetPerfTrackerPlugin.Log.Info("[TIMINGCAP] available=" + handles.Count + " candidates=" + count + " " + names);
            }
            catch (Exception ex) { NetPerfTrackerPlugin.Log.Exception(ex, "[TIMINGCAP] 探测失败"); }
        }
    }
}
