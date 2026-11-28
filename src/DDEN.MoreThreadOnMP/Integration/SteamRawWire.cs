using System;
using System.Runtime.InteropServices;
using DDEN.MoreThreadOnMP.Core;
using Steamworks;

namespace DDEN.MoreThreadOnMP.Integration
{
    internal sealed class SteamRawWire : IRawWire
    {
        private readonly bool _server;
        private readonly IntPtr[] _one = new IntPtr[1];
        internal SteamRawWire(bool server) { _server = server; }
        public byte[] Receive(WirePeer peer)
        {
            var handle = new HSteamNetConnection(peer.Handle);
            int count = peer.Server ? SteamGameServerNetworkingSockets.ReceiveMessagesOnConnection(handle, _one, 1)
                : SteamNetworkingSockets.ReceiveMessagesOnConnection(handle, _one, 1);
            if (count <= 0) return null;
            IntPtr pointer = _one[0]; _one[0] = IntPtr.Zero;
            try
            {
                var message = SteamNetworkingMessage_t.FromIntPtr(pointer);
                var bytes = new byte[message.m_cbSize];
                if (bytes.Length != 0) Marshal.Copy(message.m_pData, bytes, 0, bytes.Length);
                return bytes;
            }
            finally { SteamNetworkingMessage_t.Release(pointer); }
        }
        public int Send(WirePeer peer, byte[] data, int flags)
        {
            var pinned = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                var handle = new HSteamNetConnection(peer.Handle);
                return (int)(peer.Server ? SteamGameServerNetworkingSockets.SendMessageToConnection(handle, pinned.AddrOfPinnedObject(), (uint)data.Length, flags, out _)
                    : SteamNetworkingSockets.SendMessageToConnection(handle, pinned.AddrOfPinnedObject(), (uint)data.Length, flags, out _));
            }
            finally { pinned.Free(); }
        }
        public void ReleaseThreadMemory()
        {
            if (_server) GameServer.ReleaseCurrentThreadMemory();
            else SteamAPI.ReleaseCurrentThreadMemory();
        }
    }
}
