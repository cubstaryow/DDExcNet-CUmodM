using System;
using System.Collections.Generic;

namespace DDEN.UnitListMgr
{
    internal sealed class SpawnRecord
    {
        internal DateTime Time;
        internal string Player, ItemId, ItemName, Mode, Detail, Parameters;
        internal uint Sender;
        internal int Requested, Created;
        internal float X, Y;
        internal bool Success;
    }

    /// <summary>In-memory only. Never forwards entries to BepInEx, a console or a file.</summary>
    internal sealed class SpawnJournal
    {
        internal const int Limit = 500;
        private readonly List<SpawnRecord> _records = new List<SpawnRecord>();
        internal IReadOnlyList<SpawnRecord> Records => _records;
        internal void Add(SpawnRecord record)
        {
            if (_records.Count == Limit) _records.RemoveAt(0);
            _records.Add(record);
        }
        internal void Clear() => _records.Clear();
    }

    internal static class SpawnPolicy
    {
        internal static bool AllowsGuest(bool remote, bool allowGuests) => !remote || allowGuests;
        internal static bool ShouldMark(bool enabled, int created) => enabled && created > 0;
    }
}
