using System;
using System.Collections.Generic;

namespace DDEN.UnitListMgr
{
    /// <summary>Bounded replay cache, scoped to one multiplayer session by Clear.</summary>
    internal sealed class RequestLedger<T>
    {
        private readonly Dictionary<string, (float Time, T Value)> _values = new Dictionary<string, (float, T)>();
        private readonly Queue<string> _order = new Queue<string>();
        private readonly int _capacity;
        private readonly float _lifetime;
        internal RequestLedger(int capacity = 256, float lifetime = 120) { _capacity = capacity; _lifetime = lifetime; }
        internal bool TryGet(string key, out T value)
        {
            if (_values.TryGetValue(key, out var entry)) { value = entry.Value; return true; }
            value = default; return false;
        }
        internal void Put(string key, T value, float now)
        {
            if (!_values.ContainsKey(key)) _order.Enqueue(key);
            _values[key] = (now, value);
            while (_order.Count > _capacity || _order.Count > 0 && now - _values[_order.Peek()].Time > _lifetime)
                _values.Remove(_order.Dequeue());
        }
        internal void Clear() { _values.Clear(); _order.Clear(); }
    }
}
