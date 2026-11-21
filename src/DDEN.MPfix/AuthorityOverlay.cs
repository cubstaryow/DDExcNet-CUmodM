using System;
using System.Collections.Generic;
using BepInEx.Configuration;

namespace DDEN.MPfix
{
    /// <summary>Validate the complete whitelist first, then switch all effective values before notifying consumers.</summary>
    internal sealed class AuthorityOverlay
    {
        private readonly IAuthoritySetting[] _settings;
        private readonly Dictionary<ConfigEntryBase, IAuthoritySetting> _byEntry = new Dictionary<ConfigEntryBase, IAuthoritySetting>();
        private readonly Action<ConfigEntryBase> _changed;
        private string _session;
        private long _revision;
        internal bool Locked { get; private set; }
        internal AuthorityOverlay(IEnumerable<IAuthoritySetting> settings, Action<ConfigEntryBase> changed)
        {
            _settings = new List<IAuthoritySetting>(settings).ToArray(); _changed = changed;
            foreach (var item in _settings) _byEntry.Add(item.Entry, item);
        }
        internal bool Contains(ConfigEntryBase entry) => _byEntry.ContainsKey(entry);
        internal object Display(ConfigEntryBase entry) => _byEntry.TryGetValue(entry, out var item) ? item.EffectiveValue : entry.BoxedValue;
        internal AuthoritySnapshot Capture(string session, long revision, bool enabled)
        {
            var snapshot = new AuthoritySnapshot { Session = session, Revision = revision, Enabled = enabled };
            if (enabled)
                foreach (var item in _settings) snapshot.Values.Add(item.Id, new AuthorityValue { Type = item.TypeName, Text = item.SerializeLocal() });
            return snapshot;
        }
        internal bool Apply(AuthoritySnapshot snapshot)
        {
            if (_session == snapshot.Session && snapshot.Revision <= _revision) return false;
            if (!snapshot.Enabled)
            { Release(false); _session = snapshot.Session; _revision = snapshot.Revision; return true; }
            var decoded = new object[_settings.Length];
            for (int i = 0; i < _settings.Length; i++)
                if (!snapshot.Values.TryGetValue(_settings[i].Id, out var raw) || raw.Type != _settings[i].TypeName || !_settings[i].TryDecode(raw.Text, out decoded[i])) return false;
            var changed = new bool[_settings.Length];
            for (int i = 0; i < _settings.Length; i++)
            { changed[i] = !Equals(_settings[i].EffectiveValue, decoded[i]); _settings[i].Override(decoded[i]); }
            Locked = true; _session = snapshot.Session; _revision = snapshot.Revision;
            for (int i = 0; i < _settings.Length; i++) if (changed[i]) _changed?.Invoke(_settings[i].Entry);
            return true;
        }
        internal void Release(bool forgetVersion = true)
        {
            var previous = new object[_settings.Length];
            for (int i = 0; i < _settings.Length; i++) { previous[i] = _settings[i].EffectiveValue; _settings[i].Release(); }
            Locked = false;
            if (forgetVersion) { _session = null; _revision = 0; }
            for (int i = 0; i < _settings.Length; i++) if (!Equals(previous[i], _settings[i].EffectiveValue)) _changed?.Invoke(_settings[i].Entry);
        }
    }
}
