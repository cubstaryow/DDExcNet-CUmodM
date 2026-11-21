using System.Reflection;
using DDEN.Shared;

namespace DDEN.MPfix
{
    internal static class AuthorityConnection
    {
        private static PropertyInfo _client, _server;
        internal static bool Available
        {
            get
            {
                if (_client != null && _server != null) return true;
                var type = Reflect.Type("Together.Net");
                if (type == null) return false;
                const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                _client = type.GetProperty("ClientEntityManager", flags); _server = type.GetProperty("ServerEntityManager", flags);
                return _client != null && _server != null;
            }
        }
        internal static AuthorityPeer Read()
        {
            if (!Mp.CoreAvailable || !Mp.IsRunning || !CUCoreLib.Networking.MultiplayerApi.BridgeReceiversInstalled || !Available) return default;
            bool server = Mp.IsServer;
            return new AuthorityPeer((server ? _server : _client).GetValue(null), true, server);
        }
    }
}
