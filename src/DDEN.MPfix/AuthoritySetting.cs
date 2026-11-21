using System;
using System.Collections.Generic;
using BepInEx.Configuration;

namespace DDEN.MPfix
{
    internal interface IAuthoritySetting
    {
        ConfigEntryBase Entry { get; }
        string Id { get; }
        string TypeName { get; }
        object EffectiveValue { get; }
        Func<bool> Available { get; set; }
        string SerializeLocal();
        bool TryDecode(string value, out object decoded);
        void Override(object value);
        void Release();
    }

    /// <summary>Remote values live only here. ConfigEntry and ConfigFile always retain the user's local preferences.</summary>
    internal sealed class AuthoritySetting<T> : IAuthoritySetting
    {
        internal readonly ConfigEntry<T> Local;
        private bool _overridden;
        private T _remote;
        private AuthoritySetting(ConfigEntry<T> entry)
        {
            Local = entry;
            Id = entry.Definition.Section + "/" + entry.Definition.Key;
            AuthoritySettings.All.Add(this);
        }
        internal T Value
        {
            get => typeof(T) == typeof(bool) && Available != null && !Available() ? (T)(object)false : _overridden ? _remote : Local.Value;
            set => Local.Value = value;
        }
        public Func<bool> Available { get; set; }
        public ConfigEntryBase Entry => Local;
        public string Id { get; }
        public string TypeName => typeof(T).FullName;
        public object EffectiveValue => Value;
        public string SerializeLocal() => TomlTypeConverter.ConvertToString(Local.Value, typeof(T));
        public bool TryDecode(string value, out object decoded)
        {
            decoded = null;
            try
            {
                object candidate = TomlTypeConverter.ConvertToValue(value, typeof(T));
                if (candidate is float f && (float.IsNaN(f) || float.IsInfinity(f))) return false;
                if (Local.Description.AcceptableValues != null && !Local.Description.AcceptableValues.IsValid(candidate)) return false;
                decoded = candidate; return candidate is T;
            }
            catch { return false; }
        }
        public void Override(object value) { _remote = (T)value; _overridden = true; }
        public void Release() { _overridden = false; _remote = default; }
        public static implicit operator AuthoritySetting<T>(ConfigEntry<T> entry) => entry == null ? null : new AuthoritySetting<T>(entry);
        public static implicit operator ConfigEntry<T>(AuthoritySetting<T> setting) => setting?.Local;
    }

    internal static class AuthoritySettings
    {
        internal static readonly List<IAuthoritySetting> All = new List<IAuthoritySetting>();
    }
}
