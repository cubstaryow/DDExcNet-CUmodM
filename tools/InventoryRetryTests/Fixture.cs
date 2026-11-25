using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
public static class Fixture
{
    public struct Wait { public object Info; public bool Complete; public float Until; public int Fingerprint; }
    public static readonly Dictionary<int, Wait> Pending = new Dictionary<int, Wait>();
    public static readonly List<int> Done = new List<int>();
    public static readonly List<int> Visits = new List<int>();
    public static bool Server, Throw;
    public static int Cleanup;
    public static readonly Exception Failure = new Exception("fixture");
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Tick()
    {
        if (Server) return;
        if (Pending.Count == 0) return;
        Done.Clear();
        try
        {
            foreach (var entry in Pending)
            {
                int key = entry.Key;
                Wait value = entry.Value;
                Visits.Add(key);
                if (Throw) throw Failure;
                if (value.Complete) Done.Add(key);
            }
        }
        finally { Cleanup++; }
        foreach (int key in Done) Pending.Remove(key);
    }
}
