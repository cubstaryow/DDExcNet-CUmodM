using System.Collections.Generic;

namespace DDEN.LoongNova
{
    // A single source of truth for in-flight actions; no separate transaction flag can stay latched.
    internal sealed class PendingActions
    {
        private readonly Dictionary<string, Action> _actions = new Dictionary<string, Action>();
        private readonly List<string> _expired = new List<string>();
        private int _mutations;
        private readonly struct Action
        {
            internal readonly string Operation;
            internal readonly double Deadline;
            internal Action(string operation, double deadline) { Operation = operation; Deadline = deadline; }
        }
        internal int Count => _actions.Count;
        internal bool Busy => _mutations > 0;
        internal void Add(string request, string operation, double now)
        {
            _actions.Add(request, new Action(operation, now + 5));
            if (operation != "shoot") _mutations++;
        }
        internal bool Finish(string request)
        {
            if (!_actions.TryGetValue(request, out var action)) return false;
            _actions.Remove(request);
            if (action.Operation != "shoot") _mutations--;
            return true;
        }
        internal bool Expire(double now, out bool chargeExpired)
        {
            chargeExpired = false; _expired.Clear();
            foreach (var entry in _actions)
            {
                if (now < entry.Value.Deadline) continue;
                _expired.Add(entry.Key);
                chargeExpired |= entry.Value.Operation == "charge" || entry.Value.Operation == "release";
            }
            foreach (string request in _expired) Finish(request);
            return _expired.Count > 0;
        }
        internal void Clear() { _actions.Clear(); _expired.Clear(); _mutations = 0; }
    }
}
