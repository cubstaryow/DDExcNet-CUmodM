using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace DDEN.MPfix
{
    /// <summary>One display-only resolver for live players and historical messages. No Unity, networking or refresh loop.</summary>
    internal sealed class NameDisplayCache
    {
        internal sealed class Snapshot
        {
            internal readonly string Native, Source;
            internal readonly ulong Steam;
            internal string Profile, Display;
            internal Snapshot(string native, string source, ulong steam)
            { Native = native ?? ""; Source = source ?? ""; Steam = steam; }
        }
        private sealed class PlayerState
        {
            public PlayerState() { }
            internal string Raw;
            internal ulong RawSteam;
            internal Snapshot Snapshot;
        }
        private sealed class Persona
        {
            internal string Name;
            internal float RetryAt;
            internal int Epoch = -1;
        }
        private readonly Func<ulong, string> _lookup;
        private ConditionalWeakTable<object, PlayerState> _players = new ConditionalWeakTable<object, PlayerState>();
        private readonly Dictionary<ulong, Persona> _personas = new Dictionary<ulong, Persona>();
        private int _epoch;
        internal int Revision { get; private set; }

        internal NameDisplayCache(Func<ulong, string> lookup) { _lookup = lookup; }
        internal void Capture(object player, string raw, ulong steam)
        {
            if (player == null) return;
            var state = _players.GetOrCreateValue(player);
            state.Raw = raw; state.RawSteam = steam; state.Snapshot = null;
            Revision++;
        }
        internal Snapshot CaptureSnapshot(object player, string native, ulong steam)
        {
            var state = _players.GetOrCreateValue(player);
            if (state.RawSteam != 0 && state.RawSteam != steam) state.Raw = null;
            state.RawSteam = steam;
            if (state.Snapshot == null || state.Snapshot.Native != native || state.Snapshot.Steam != steam)
                state.Snapshot = new Snapshot(native, state.Raw ?? native, steam);
            return state.Snapshot;
        }
        internal string Resolve(object player, string native, ulong steam, float now) => Resolve(CaptureSnapshot(player, native, steam), now);
        internal Snapshot CaptureMessage(object player, string messageName, string native, ulong steam)
        {
            var live = CaptureSnapshot(player, native, steam);
            // History first encountered after a rename keeps the name that message originally carried.
            return new Snapshot(messageName, messageName == native ? live.Source : messageName, steam);
        }
        internal string Resolve(Snapshot snapshot, float now)
        {
            string profile = GetPersona(snapshot.Steam, now) ?? snapshot.Profile;
            if (snapshot.Display == null || snapshot.Profile != profile)
            {
                snapshot.Profile = profile;
                snapshot.Display = NameDisplayRules.Choose(snapshot.Source, profile, snapshot.Steam);
            }
            return snapshot.Display;
        }
        private string GetPersona(ulong steam, float now)
        {
            if (steam == 0) return null;
            if (!_personas.TryGetValue(steam, out var persona))
            {
                if (_personas.Count >= 256) _personas.Clear();
                _personas[steam] = persona = new Persona();
            }
            if (persona.Epoch != _epoch || now >= persona.RetryAt)
            {
                persona.Epoch = _epoch; persona.RetryAt = now + 5f;
                // A transient SteamID/unknown response must not erase a successfully recovered name.
                string candidate = null;
                try { candidate = _lookup?.Invoke(steam); } catch { }
                if (NameDisplayRules.ValidPersona(candidate, steam))
                {
                    candidate = NameDisplayRules.Clean(candidate);
                    if (persona.Name != candidate) { persona.Name = candidate; Revision++; }
                }
            }
            return persona.Name;
        }
        internal void Invalidate() { _epoch++; Revision++; }
        internal void Clear()
        {
            _players = new ConditionalWeakTable<object, PlayerState>();
            _personas.Clear(); Invalidate();
        }
    }
}
