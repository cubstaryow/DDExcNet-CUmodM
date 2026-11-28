using System;
namespace Steamworks
{
    public struct HSteamNetConnection
    {
        public uint m_HSteamNetConnection;
        public HSteamNetConnection(uint handle) { m_HSteamNetConnection = handle; }
    }
    public enum EResult { k_EResultOK = 1, k_EResultLimitExceeded = 25 }
    public static class SteamNetworkingSockets
    {
        public static int Calls;
        public static EResult SendMessageToConnection(HSteamNetConnection connection, IntPtr data, uint bytes, int flags, out long number)
        { Calls++; number = 99; return EResult.k_EResultOK; }
    }
    public static class SteamGameServerNetworkingSockets
    {
        public static int Calls;
        public static EResult SendMessageToConnection(HSteamNetConnection connection, IntPtr data, uint bytes, int flags, out long number)
        { Calls++; number = 100; return EResult.k_EResultOK; }
    }
    public static class SteamAPI { public static void Shutdown() { } }
    public static class GameServer { public static void Shutdown() { } }
}
