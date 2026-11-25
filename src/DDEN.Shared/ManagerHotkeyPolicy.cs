using System;
using UnityEngine;

namespace DDEN.Shared
{
    public static class ManagerHotkeyPolicy
    {
        private static readonly KeyCode[] Candidates = { KeyCode.F11, KeyCode.F10, KeyCode.F9, KeyCode.F8, KeyCode.F7, KeyCode.F6, KeyCode.F5, KeyCode.F4, KeyCode.F3, KeyCode.F2, KeyCode.F1 };
        public static KeyCode Choose(KeyCode current, bool modified, bool autoFix, Func<KeyCode, bool> occupied)
        {
            if (current == KeyCode.None) return current;
            if (current != KeyCode.F12 && (modified || !occupied(current) || !autoFix)) return current;
            foreach (var candidate in Candidates) if (!occupied(candidate)) return candidate;
            return KeyCode.None;
        }
    }
}
