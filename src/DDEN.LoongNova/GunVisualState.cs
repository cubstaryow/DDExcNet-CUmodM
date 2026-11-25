namespace DDEN.LoongNova
{
    internal static class GunVisualState
    {
        // Before a custom-state reply arrives, retain the native networked magazine flag.
        internal static T Select<T>(bool known, bool loaded, bool nativeHasMag, T installed, T empty) =>
            (known ? loaded : nativeHasMag) ? installed : empty;
    }
}
