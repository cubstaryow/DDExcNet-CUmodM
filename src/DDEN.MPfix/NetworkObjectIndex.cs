using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace DDEN.MPfix
{
    /// <summary>Exact entity identity plus a binding lease. Queued work cannot follow a reused ID or mesh.</summary>
    internal sealed class NetworkObjectIndex<T> where T : class
    {
        internal sealed class Lease
        {
            internal readonly int Key;
            internal readonly string Resource;
            internal readonly T Value;
            internal readonly object Binding;
            internal Lease(int key, string resource, T value, object binding)
            { Key = key; Resource = resource; Value = value; Binding = binding; }
        }
        private sealed class ByReference : IEqualityComparer<T>
        {
            public bool Equals(T a, T b) => ReferenceEquals(a, b);
            public int GetHashCode(T value) => RuntimeHelpers.GetHashCode(value);
        }
        private readonly Dictionary<int, Lease> _ids = new Dictionary<int, Lease>();
        private readonly Dictionary<T, Lease> _objects = new Dictionary<T, Lease>(new ByReference());
        internal int Count => _ids.Count;
        internal Lease Track(ushort id, byte version, string resource, T value, object binding)
        {
            // LiteEntitySystem reserves 64000+ for peer-local entities, which are not shared identities.
            if (id == 0 || id >= 64000 || string.IsNullOrEmpty(resource) || ReferenceEquals(value, null) || binding == null) return null;
            int key = id | (version << 16);
            if (_objects.TryGetValue(value, out var previous))
            {
                if (previous.Key == key && previous.Resource == resource && ReferenceEquals(previous.Binding, binding)) return previous;
                Remove(value);
            }
            if (_ids.TryGetValue(key, out previous)) Remove(previous.Value);
            var lease = new Lease(key, resource, value, binding);
            _ids[key] = lease;
            _objects[value] = lease;
            return lease;
        }
        internal Lease Find(ushort id, byte version, string resource) =>
            _ids.TryGetValue(id | (version << 16), out var lease) && lease.Resource == resource ? lease : null;
        internal bool IsCurrent(Lease lease) => lease != null && _ids.TryGetValue(lease.Key, out var current)
            && ReferenceEquals(lease, current);
        internal void Remove(T value)
        {
            if (ReferenceEquals(value, null) || !_objects.TryGetValue(value, out var lease)) return;
            _objects.Remove(value);
            if (IsCurrent(lease)) _ids.Remove(lease.Key);
        }
        internal void Clear() { _ids.Clear(); _objects.Clear(); }
    }
}
