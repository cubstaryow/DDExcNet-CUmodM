using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace DDEN.MoreThreadOnMP.Core
{
    internal static class ThreadRoles
    {
        internal static readonly ConcurrentDictionary<int, string> Names = new ConcurrentDictionary<int, string>();
        internal static int Register(string role)
        {
            int id;
            try { id = (int)WindowsAffinity.GetCurrentThreadId(); } catch { id = 0; }
            if (id != 0) Names[id] = role;
            return id;
        }
        internal static void Remove(int id) { Names.TryRemove(id, out _); }
    }
    internal readonly struct CpuDescription
    {
        internal readonly ushort Group;
        internal readonly byte Index, Efficiency, Flags;
        internal CpuDescription(ushort group, byte index, byte efficiency, byte flags)
        { Group = group; Index = index; Efficiency = efficiency; Flags = flags; }
    }
    internal static class CpuSelection
    {
        internal static ulong PerformanceMask(IEnumerable<CpuDescription> cpus, ushort group, ulong allowed, out bool hybrid)
        {
            var entries = cpus.ToArray();
            hybrid = entries.Select(cpu => cpu.Efficiency).Distinct().Count() > 1;
            if (!hybrid) return 0; // Do not guess core classes from processor numbers or SMT layout.
            byte maximum = entries.Max(cpu => cpu.Efficiency);
            ulong mask = 0;
            foreach (var cpu in entries)
                if (cpu.Group == group && cpu.Index < 64 && cpu.Efficiency == maximum
                    && ((cpu.Flags & 2) == 0 || (cpu.Flags & 4) != 0)) mask |= 1UL << cpu.Index;
            return mask & allowed;
        }
    }
    internal sealed class WindowsAffinity
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct GroupAffinity { internal UIntPtr Mask; internal ushort Group, R0, R1, R2; }
        [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentThread();
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetSystemCpuSetInformation(IntPtr info, uint length, out uint returned, IntPtr process, uint flags);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessAffinityMask(IntPtr process, out UIntPtr processMask, out UIntPtr systemMask);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetThreadGroupAffinity(IntPtr thread, out GroupAffinity affinity);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern UIntPtr SetThreadAffinityMask(IntPtr thread, UIntPtr mask);
        private ulong _saved, _applied;
        private ushort _group;
        private uint _owner;
        internal bool Locked { get; private set; }
        internal string State { get; private set; } = "off";
        internal string Detail { get; private set; } = "";
        internal ulong Mask => _applied;
        internal void Apply()
        {
            try
            {
                if (_owner != 0 && _owner != GetCurrentThreadId()) throw new InvalidOperationException("Affinity called outside Unity main thread");
                if (_owner != 0) { Verify(); return; }
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || IntPtr.Size != 8) { State = "unsupported"; return; }
                var process = GetCurrentProcess(); var thread = GetCurrentThread();
                if (!GetThreadGroupAffinity(thread, out var current) || !GetProcessAffinityMask(process, out var allowed, out _)) throw new System.ComponentModel.Win32Exception();
                GetSystemCpuSetInformation(IntPtr.Zero, 0, out uint bytes, process, 0);
                if (bytes == 0 || bytes > 1024 * 1024) throw new InvalidOperationException("CPU topology unavailable");
                var data = Marshal.AllocHGlobal((int)bytes);
                var cpus = new List<CpuDescription>();
                try
                {
                    if (!GetSystemCpuSetInformation(data, bytes, out uint returned, process, 0)) throw new System.ComponentModel.Win32Exception();
                    for (int at = 0; at + 8 <= returned;)
                    {
                        int size = Marshal.ReadInt32(data, at);
                        if (size < 8 || size > returned - at) throw new InvalidOperationException("Invalid CPU topology record");
                        if (Marshal.ReadInt32(data, at + 4) == 0 && size >= 20)
                            cpus.Add(new CpuDescription((ushort)Marshal.ReadInt16(data, at + 12), Marshal.ReadByte(data, at + 14), Marshal.ReadByte(data, at + 18), Marshal.ReadByte(data, at + 19)));
                        at += size;
                    }
                }
                finally { Marshal.FreeHGlobal(data); }
                ulong target = CpuSelection.PerformanceMask(cpus, current.Group, allowed.ToUInt64(), out bool hybrid);
                if (!hybrid) { State = "uniform"; return; }
                if (target == 0) { State = "unavailable"; Detail = "No permitted performance cores in current processor group"; return; }
                var previous = SetThreadAffinityMask(thread, new UIntPtr(target));
                if (previous == UIntPtr.Zero) throw new System.ComponentModel.Win32Exception();
                _saved = previous.ToUInt64(); _applied = target; _group = current.Group; _owner = GetCurrentThreadId();
                Verify();
            }
            catch (Exception error) { Locked = false; State = "failed"; Detail = error.GetType().Name + ": " + error.Message; }
        }
        internal void Verify()
        {
            if (_owner == 0) return;
            if (GetCurrentThreadId() != _owner) throw new InvalidOperationException("Wrong affinity owner");
            if (!GetThreadGroupAffinity(GetCurrentThread(), out var current)) throw new System.ComponentModel.Win32Exception();
            if (current.Group != _group) { Locked = false; State = "conflict"; Detail = "Processor group changed"; return; }
            if (current.Mask.ToUInt64() != _applied && SetThreadAffinityMask(GetCurrentThread(), new UIntPtr(_applied)) == UIntPtr.Zero)
                throw new System.ComponentModel.Win32Exception();
            Locked = GetThreadGroupAffinity(GetCurrentThread(), out current) && current.Group == _group && current.Mask.ToUInt64() == _applied;
            State = Locked ? "locked" : "failed";
            Detail = Locked ? "0x" + _applied.ToString("X") : "Affinity verification failed";
        }
        internal void Restore()
        {
            if (_owner == 0) { Locked = false; State = "off"; return; }
            try
            {
                if (GetCurrentThreadId() != _owner) throw new InvalidOperationException("Wrong affinity owner");
                if (!GetThreadGroupAffinity(GetCurrentThread(), out var current)) throw new System.ComponentModel.Win32Exception();
                // Preserve another component's later affinity choice instead of overwriting it on unload.
                if (current.Group == _group && current.Mask.ToUInt64() == _applied
                    && SetThreadAffinityMask(GetCurrentThread(), new UIntPtr(_saved)) == UIntPtr.Zero) throw new System.ComponentModel.Win32Exception();
                _owner = 0; Locked = false; State = "off"; Detail = ""; _applied = 0;
            }
            catch (Exception error) { Locked = false; State = "failed"; Detail = error.Message; }
        }
    }
    internal sealed class ThreadLoad
    {
        internal int Id;
        internal string Role;
        internal string Name;
        internal long Started;
        internal double Load;
        internal double RecentLoad;
        internal bool Available;
    }
    internal sealed class ThreadSnapshot
    {
        internal ThreadLoad[] Rows = Array.Empty<ThreadLoad>();
        internal int Active, Workers;
        internal string Error;
    }
    internal static class WindowsThreadTimes
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct ThreadEntry
        { internal uint Size, Usage, ThreadId, ProcessId; internal int BasePriority, DeltaPriority; internal uint Flags; }
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint process);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool Thread32First(IntPtr snapshot, ref ThreadEntry entry);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool Thread32Next(IntPtr snapshot, ref ThreadEntry entry);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentProcessId();
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenThread(uint access, bool inherit, uint id);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetThreadTimes(IntPtr thread, out long created, out long exited, out long kernel, out long user);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll")] private static extern int GetThreadDescription(IntPtr thread, out IntPtr description);
        [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
        private static bool _descriptionAvailable = true;
        private static string Description(IntPtr thread)
        {
            if (!_descriptionAvailable) return null;
            IntPtr text = IntPtr.Zero;
            try { return GetThreadDescription(thread, out text) >= 0 && text != IntPtr.Zero ? Marshal.PtrToStringUni(text) : null; }
            catch (EntryPointNotFoundException) { _descriptionAvailable = false; return null; }
            finally { if (text != IntPtr.Zero) LocalFree(text); }
        }
        internal sealed class Times { internal int Id; internal long Created; internal string Name; internal double CpuMs; internal bool Available; }
        internal static List<Times> Read()
        {
            // Unity's shipped Mono ProcessThread.TotalProcessorTime is a TODO returning zero.
            // Native thread times are required; do not fabricate idle samples from those stubs.
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) throw new PlatformNotSupportedException("Windows thread timing unavailable");
            var snapshot = CreateToolhelp32Snapshot(4, 0);
            if (snapshot == new IntPtr(-1)) throw new System.ComponentModel.Win32Exception();
            try
            {
                uint owner = GetCurrentProcessId(); var rows = new List<Times>();
                var entry = new ThreadEntry { Size = (uint)Marshal.SizeOf(typeof(ThreadEntry)) };
                if (!Thread32First(snapshot, ref entry)) throw new System.ComponentModel.Win32Exception();
                do
                {
                    if (entry.ProcessId != owner) continue;
                    var row = new Times { Id = (int)entry.ThreadId };
                    var handle = OpenThread(0x0800, false, entry.ThreadId);
                    if (handle != IntPtr.Zero)
                    {
                        try
                        {
                            row.Available = GetThreadTimes(handle, out long created, out _, out long kernel, out long user);
                            row.Created = created; row.CpuMs = (kernel + user) / 10000.0;
                            row.Name = Description(handle);
                        }
                        finally { CloseHandle(handle); }
                    }
                    rows.Add(row);
                } while (Thread32Next(snapshot, ref entry));
                if (Marshal.GetLastWin32Error() != 18) throw new System.ComponentModel.Win32Exception();
                return rows;
            }
            finally { CloseHandle(snapshot); }
        }
    }
    internal sealed class ThreadSampler : IDisposable
    {
        private readonly ManualResetEvent _stop = new ManualResetEvent(false);
        private readonly AutoResetEvent _reschedule = new AutoResetEvent(false);
        private int _intervalMs;
        private readonly Thread _thread;
        private volatile ThreadSnapshot _snapshot = new ThreadSnapshot();
        internal ThreadSnapshot Snapshot => _snapshot;
        internal int IntervalMilliseconds => Volatile.Read(ref _intervalMs);
        internal ThreadSampler(int intervalMilliseconds = 3000)
        { _intervalMs = ThreadHeatmap.SampleInterval(intervalMilliseconds); _thread = new Thread(Run) { IsBackground = true, Name = "DDEN thread sampling" }; _thread.Start(); }
        internal void SetInterval(int milliseconds)
        { if (Interlocked.Exchange(ref _intervalMs, ThreadHeatmap.SampleInterval(milliseconds)) != ThreadHeatmap.SampleInterval(milliseconds)) _reschedule.Set(); }
        private void Run()
        {
            int ownId = ThreadRoles.Register("monitor");
            var previous = new Dictionary<int, (long Start, double Cpu)>(); double last = WireClock.Now;
            var history = new ThreadActivityWindow(); var waits = new WaitHandle[] { _stop, _reschedule };
            try
            {
                var baseline = WindowsThreadTimes.Read();
                foreach (var row in baseline) if (row.Available) previous[row.Id] = (row.Created, row.CpuMs);
                _snapshot = new ThreadSnapshot { Rows = baseline.Select(row => new ThreadLoad { Id = row.Id, Started = row.Created, Name = row.Name,
                    Role = ThreadRoles.Names.TryGetValue(row.Id, out var role) ? role : "game" }).OrderBy(row => row.Id).ToArray(),
                    Workers = baseline.Count(row => ThreadRoles.Names.TryGetValue(row.Id, out var role) && role != "main") };
                last = WireClock.Now;
            }
            catch (Exception error) { _snapshot = new ThreadSnapshot { Error = error.Message }; }
            try
            {
                while (true)
                {
                    int signal = WaitHandle.WaitAny(waits, IntervalMilliseconds);
                    if (signal == 0) break;
                    if (signal == 1) continue;
                    try
                    {
                        var rows = new List<ThreadLoad>(); var next = new Dictionary<int, (long, double)>();
                        double now = WireClock.Now, elapsed = now - last;
                        foreach (var thread in WindowsThreadTimes.Read())
                        {
                                int id = thread.Id; long birth = thread.Created; double cpu = thread.CpuMs;
                                var old = default((long Start, double Cpu));
                                bool valid = thread.Available && elapsed > 0 && previous.TryGetValue(id, out old) && old.Start == birth;
                                var row = new ThreadLoad { Id = id, Started = birth, Name = thread.Name, Role = ThreadRoles.Names.TryGetValue(id, out var role) ? role : "game",
                                    Available = valid, Load = valid ? Math.Max(0, Math.Min(1, (cpu - old.Cpu) / elapsed)) : 0 };
                                history.Update(row, now); rows.Add(row);
                                if (thread.Available) next[id] = (birth, cpu);
                        }
                        history.Retain(rows.Select(row => row.Id));
                        _snapshot = new ThreadSnapshot { Rows = rows.OrderBy(row => row.Id).ToArray(), Active = rows.Count(row => row.Available && row.Load > 0),
                            Workers = rows.Count(row => row.Role != "game" && row.Role != "main") };
                        previous = next; last = now;
                    }
                    catch (Exception error) { _snapshot = new ThreadSnapshot { Error = error.GetType().Name + ": " + error.Message }; }
                }
            }
            finally { ThreadRoles.Remove(ownId); }
        }
        public void Dispose() { _stop.Set(); if (Thread.CurrentThread != _thread) _thread.Join(); _stop.Dispose(); _reschedule.Dispose(); }
    }
}
