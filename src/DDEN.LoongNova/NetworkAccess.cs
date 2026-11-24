using System;
using System.Reflection;
using DDEN.Shared;
using UnityEngine;

namespace DDEN.LoongNova
{
    internal static class NetworkAccess
    {
        private const BindingFlags Static = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private static Type _trackerType, _idType, _syncType;
        private static MethodInfo _itemById, _bodyBySender;
        private static PropertyInfo _serverManager, _clientManager;
        private static FieldInfo _trackerId, _idValue;
        internal static object Connection => !Mp.CoreReady || !Mp.IsRunning ? null
            : Manager(Mp.IsServer);

        private static object Manager(bool server)
        {
            var net = Reflect.Type("Together.Net");
            if (net == null) return null;
            if (_serverManager == null) _serverManager = net.GetProperty("ServerEntityManager", Static);
            if (_clientManager == null) _clientManager = net.GetProperty("ClientEntityManager", Static);
            return (server ? _serverManager : _clientManager)?.GetValue(null);
        }

        internal static int Id(Item item)
        {
            if (item == null || !Mp.IsRunning) return 0;
            if (_trackerType == null) _trackerType = Reflect.Type("Together.SyncInfoGameObjectTracker");
            var tracker = _trackerType == null ? null : item.GetComponent(_trackerType);
            if (tracker == null) return 0;
            if (_trackerId == null) _trackerId = _trackerType.GetField("syncId");
            var id = _trackerId?.GetValue(tracker);
            if (id == null) return 0;
            if (_idValue == null) _idValue = id.GetType().GetField("id");
            return _idValue?.GetValue(id) is ushort number ? number : 0;
        }

        internal static Item Item(int id)
        {
            if (!Mp.CoreReady || !Mp.IsRunning || id <= 0 || id > ushort.MaxValue) return null;
            if (_idType == null) _idType = Reflect.Type("Together.knetid");
            if (_syncType == null) _syncType = Reflect.Type("Together.SyncInfo");
            if (_idType == null || _syncType == null) return null;
            if (_itemById == null) _itemById = Reflect.Type("Together.ItemSync")?.GetMethod("TryGetItemSyncInfo", Static, null,
                new[] { _idType, _syncType.MakeByRefType() }, null);
            var args = new[] { Activator.CreateInstance(_idType, (ushort)id), null };
            if (!(_itemById?.Invoke(null, args) is bool found) || !found) return null;
            return Reflect.GetInstance(args[1], "item") as Item;
        }

        internal static Body Body(uint sender)
        {
            if (!Mp.CoreReady || !Mp.IsRunning || !Mp.IsServer || sender > ushort.MaxValue) return null;
            if (_bodyBySender == null) _bodyBySender = Reflect.Type("Together.ScavPlayer")?.GetMethod("GetBodyFromClientId", Static);
            if (_bodyBySender == null) return null;
            var id = Activator.CreateInstance(_bodyBySender.GetParameters()[0].ParameterType, (ushort)sender);
            return _bodyBySender.Invoke(null, new[] { id }) as Body;
        }

        internal static object NetBody(Body body)
        {
            var type = Reflect.Type("Together.NetBody");
            return type == null || body == null ? null : body.GetComponent(type);
        }

        internal static bool Carries(Body body, Item item)
        {
            if (body == null || item == null) return false;
            if (body.HoldingItem(item)) return true;
            foreach (Item candidate in body.GetAllItemsThorough()) if (candidate == item) return true;
            return false;
        }
    }
}
