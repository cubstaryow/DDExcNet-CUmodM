using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using DDEN.MoreThreadOnMP.Core;
using DDEN.Shared;
using Steamworks;

namespace DDEN.MoreThreadOnMP.Integration
{
    internal sealed class MpRoute
    {
        internal object Connection;
        internal object Key;
        internal WirePeer Peer;
    }
    internal sealed class MpBindings
    {
        // Binary pin is deliberate for this experimental transport adapter; no broad ignore-version switch.
        internal const string TestedMvid = "b3b58466-cc0d-42b6-910c-d744dd135db2";
        internal readonly MethodInfo Poll, Send, Reset;
        internal readonly Action<object, object, byte[]> Dispatch;
        internal readonly Action<int> NoteIncoming;
        internal readonly FieldInfo Connections, SteamServer;
        private readonly FieldInfo _connectionHandle;
        internal readonly Type TransportType;
        internal MpBindings()
        {
            var assembly = Reflect.FindAssembly("CasualtiesMP") ?? throw new InvalidOperationException("CasualtiesMP unavailable");
            if (assembly.ManifestModule.ModuleVersionId.ToString() != TestedMvid)
                throw new NotSupportedException("Untested CasualtiesMP binary: " + assembly.ManifestModule.ModuleVersionId);
            const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            TransportType = assembly.GetType("Together.TransportSteamworks", true);
            Poll = TransportType.GetMethod("_Update_PollMessages", any) ?? throw new MissingMethodException("_Update_PollMessages");
            Send = TransportType.GetMethod("SendThroughSteamSocket", any, null,
                new[] { typeof(HSteamNetConnection).MakeByRefType(), typeof(ReadOnlySpan<byte>), typeof(int) }, null)
                ?? throw new MissingMethodException("SendThroughSteamSocket(in HSteamNetConnection, ReadOnlySpan<byte>, int)");
            Reset = assembly.GetType("Together.Net", true).GetMethod("ShutdownReset", any) ?? throw new MissingMethodException("ShutdownReset");
            Connections = TransportType.GetField("connectionMapping", any) ?? throw new MissingFieldException("connectionMapping");
            SteamServer = TransportType.GetField("is_steamserver", any) ?? throw new MissingFieldException("is_steamserver");
            var connectionType = TransportType.GetNestedType("SteamConnectionData", any) ?? throw new MissingMemberException("SteamConnectionData");
            _connectionHandle = connectionType.GetField("connection", any) ?? throw new MissingFieldException("connection");
            if (_connectionHandle.FieldType != typeof(HSteamNetConnection)) throw new NotSupportedException("Steam handle type changed");
            var method = TransportType.GetMethod("_K_OnReceiveNetworkingMessage", any, null, new[] { connectionType, typeof(byte[]) }, null)
                ?? throw new MissingMethodException("_K_OnReceiveNetworkingMessage");
            var transport = Expression.Parameter(typeof(object)); var connection = Expression.Parameter(typeof(object)); var bytes = Expression.Parameter(typeof(byte[]));
            Dispatch = Expression.Lambda<Action<object, object, byte[]>>(Expression.Call(Expression.Convert(transport, TransportType), method,
                Expression.Convert(connection, connectionType), bytes), transport, connection, bytes).Compile();
            var note = assembly.GetType("Together.FindTheBugInator", true).GetMethod("NoteSteamIn", any, null, new[] { typeof(int) }, null)
                ?? throw new MissingMethodException("NoteSteamIn");
            NoteIncoming = (Action<int>)Delegate.CreateDelegate(typeof(Action<int>), note);
        }
        internal List<MpRoute> Routes(object transport, Dictionary<object, MpRoute> previous, ref long nextLease)
        {
            var result = new List<MpRoute>(); bool server = (bool)SteamServer.GetValue(transport);
            foreach (DictionaryEntry entry in (IDictionary)Connections.GetValue(transport))
            {
                object connection = entry.Value;
                var handle = (HSteamNetConnection)_connectionHandle.GetValue(connection);
                if (handle.m_HSteamNetConnection == 0) continue;
                if (!previous.TryGetValue(connection, out var route) || route.Peer.Handle != handle.m_HSteamNetConnection || route.Peer.Server != server)
                    route = new MpRoute { Connection = connection, Key = entry.Key, Peer = new WirePeer(handle.m_HSteamNetConnection, server, ++nextLease) };
                result.Add(route);
            }
            return result;
        }
        internal bool IsLive(object transport, MpRoute route) => ReferenceEquals(((IDictionary)Connections.GetValue(transport))[route.Key], route.Connection);
    }
}
