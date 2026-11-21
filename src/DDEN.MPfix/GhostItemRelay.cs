using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DDEN.Shared;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DDEN.MPfix
{
    /// <summary>
    /// Host-authorized retirement of saved inventory replicas. Capture identity BEFORE native detach;
    /// client lookup is exact (entity ID + version + resource), independent of distance or motion.
    /// No world scans, freezing, teleportation, or deletion based on a guessed nearby item.
    /// </summary>
    internal static class GhostItemRelay
    {
        private const string Channel = "ddn.mp.ghostitems.v2";
        private const string VerifyChannel = "ddn.mp.ghostitems.verify.v2";
        private const int BatchSize = 64;
        private const float BatchDelay = 0.1f;
        private const float RetryLifetime = 10f;
        private sealed class HostEntry
        {
            internal GameObject Object;
            internal object Binding;
            internal ushort Net;
            internal byte Version;
            internal string Resource;
            internal float Expires;
            internal int Key => Net | (Version << 16);
        }
        private sealed class Work
        {
            internal NetworkObjectIndex<Item>.Lease Lease;
            internal float Deadline, Due;
        }
        private static readonly NetworkObjectIndex<Item> Index = new NetworkObjectIndex<Item>();
        private static readonly Queue<HostEntry> Outbound = new Queue<HostEntry>();
        private static readonly HashSet<int> OutboundKeys = new HashSet<int>();
        private static readonly Dictionary<int, HostEntry> Authority = new Dictionary<int, HostEntry>();
        private static readonly Queue<HostEntry> AuthorityExpiry = new Queue<HostEntry>();
        private static readonly HashSet<NetworkObjectIndex<Item>.Lease> Queued = new HashSet<NetworkObjectIndex<Item>.Lease>();
        private static readonly FrameWorkQueue<Work> Incoming = new FrameWorkQueue<Work>();
        private static readonly List<Work> Retry = new List<Work>();
        private static readonly List<Item> Children = new List<Item>();
        private static readonly Action<Work> ProcessWork = Process;
        private static FieldInfo _registry, _netRegistry, _go, _tracker, _trackerSync, _entityId, _entityVersion;
        private static MethodInfo _destroy, _destroyEntity;
        private static object _retiringBinding;
        private static Type _trackerType;
        private static WorldGeneration _world;
        private static bool _running, _installed;
        private static int _layer = int.MinValue, _epoch, _removed, _expired;
        private static float _flushAt = -1f, _logAt;
        private static bool Enabled => MPfixPlugin.Active && MPfixPlugin.CfgRelayGhostItems.Value;

        internal static void Install(Harmony harmony)
        {
            if (_installed) return;
            try
            {
                Type reg = AccessTools.TypeByName("Together.NetObjectRegistry");
                Type sync = AccessTools.TypeByName("Together.SyncInfo");
                _trackerType = AccessTools.TypeByName("Together.SyncInfoGameObjectTracker");
                _registry = AccessTools.Field(reg, "SyncRegistry");
                _netRegistry = AccessTools.Field(reg, "NetIdToSyncInfoDict");
                _go = AccessTools.Field(sync, "go");
                _tracker = AccessTools.Field(sync, "tracker");
                _trackerSync = AccessTools.Field(_trackerType, "syncInfo");
                _entityId = AccessTools.Field(sync, "Id");
                _entityVersion = AccessTools.Field(sync, "Version");
                _destroy = AccessTools.Method(reg, "SafeDestroyObject", new[] { typeof(GameObject) });
                _destroyEntity = AccessTools.Method(sync, "DestroyInternal");
                MethodInfo detach = AccessTools.Method(reg, "DetachSyncKeepMesh", new[] { typeof(GameObject) });
                MethodInfo create = AccessTools.Method(sync, "CreateObject");
                MethodInfo destroyMesh = AccessTools.Method(sync, "DestroyTrackedGameObject");
                if (_registry == null || _netRegistry == null || _go == null || _tracker == null
                    || _trackerSync == null || _entityId == null || _entityVersion == null
                    || _destroy == null || _destroyEntity == null || detach == null || create == null || destroyMesh == null)
                    throw new MissingMemberException("inventory retirement signatures");
                Mp.RegisterClientHandler(Channel, OnClientReceive);
                // Bridge broadcast channels may be relayed by clients. A notice alone never authorizes deletion.
                if (!MpChannels.TryRegisterSender(VerifyChannel, Verify)) throw new InvalidOperationException("retirement verification channel");
                harmony.Patch(detach, prefix: new HarmonyMethod(typeof(GhostItemRelay), nameof(BeforeDetach)),
                    postfix: new HarmonyMethod(typeof(GhostItemRelay), nameof(AfterDetach)));
                harmony.Patch(create, postfix: new HarmonyMethod(typeof(GhostItemRelay), nameof(AfterCreate)));
                harmony.Patch(destroyMesh, prefix: new HarmonyMethod(typeof(GhostItemRelay), nameof(BeforeDestroyMesh)));
                _installed = true;
                MPfixPlugin.ClearPatchError("离场物品清理");
                MPfixPlugin.Log.Info("[遗留物品] 已启用按网络身份清理（解绑前记录、分批可靠广播、客机分帧处理）");
            }
            catch (Exception ex)
            {
                MPfixPlugin.NotePatchError("离场物品清理", ex.Message);
                MPfixPlugin.Log.Warn("[遗留物品] 安装失败：" + ex);
            }
        }

        internal static void Reset()
        {
            Index.Clear(); ClearWork();
            _world = WorldGeneration.world;
            _running = Mp.IsRunning;
            _layer = _world != null ? _world.biomeDepth : int.MinValue;
            _epoch++;
            _removed = _expired = 0;
        }
        private static void ClearWork()
        {
            Outbound.Clear(); OutboundKeys.Clear(); Incoming.Clear(); Queued.Clear(); Retry.Clear();
            Authority.Clear(); AuthorityExpiry.Clear(); _flushAt = -1f;
        }
        private static void CheckWorld()
        {
            var world = WorldGeneration.world;
            int layer = world != null ? world.biomeDepth : int.MinValue;
            if (_world != world || _running != Mp.IsRunning || layer != _layer) Reset();
        }
        private static bool IsSavedInventory(GameObject go)
        {
            for (Transform parent = go.transform.parent; parent != null; parent = parent.parent)
                if (parent.name.StartsWith("SAVESTATE_", StringComparison.Ordinal)) return true;
            return false;
        }
        private static object Binding(GameObject go)
        {
            var registry = _registry.GetValue(null) as IDictionary;
            object binding = registry != null && registry.Contains(go) ? registry[go] : null;
            if (binding == null)
            {
                Component tracker = go.GetComponent(_trackerType);
                if (tracker != null) binding = _trackerSync.GetValue(tracker);
            }
            return binding;
        }
        private static void BeforeDetach(GameObject go, out HostEntry __state)
        {
            __state = null;
            try
            {
                if (!Enabled || !Mp.IsRunning || !Mp.IsServer || go == null || !IsSavedInventory(go)) return;
                CheckWorld();
                Item item = go.GetComponent<Item>();
                object binding = Binding(go);
                if (item == null || binding == null || string.IsNullOrEmpty(item.id)) return;
                ushort id = (ushort)_entityId.GetValue(binding);
                if (id == 0 || id >= 64000) return;
                __state = new HostEntry { Object = go, Binding = binding, Net = id,
                    Version = (byte)_entityVersion.GetValue(binding), Resource = item.id };
            }
            catch (Exception ex) { MPfixPlugin.Log.Debug("[遗留物品] 记录身份失败：" + ex.Message); }
        }
        private static void AfterDetach(HostEntry __state)
        {
            try
            {
                if (__state == null || !Enabled || !Mp.IsRunning || !Mp.IsServer || __state.Object == null) return;
                // Only a successful native unlink is authoritative. Never announce a no-op/failed detach.
                if (_go.GetValue(__state.Binding) as GameObject != null || !IsSavedInventory(__state.Object)) return;
                if (!OutboundKeys.Add(__state.Key)) return;
                __state.Expires = Time.unscaledTime + 15f;
                Authority[__state.Key] = __state;
                AuthorityExpiry.Enqueue(__state);
                Outbound.Enqueue(__state);
                if (_flushAt < 0f) _flushAt = Time.unscaledTime + BatchDelay;
            }
            catch (Exception ex) { MPfixPlugin.Log.Debug("[遗留物品] 排队失败：" + ex.Message); }
        }
        private static void AfterCreate(object __instance)
        {
            try
            {
                if (!Enabled || !Mp.IsRunning || !Mp.IsClient) return;
                CheckWorld(); Track(__instance);
            }
            catch (Exception ex) { MPfixPlugin.Log.Debug("[遗留物品] 索引身份失败：" + ex.Message); }
        }
        private static NetworkObjectIndex<Item>.Lease Track(object binding)
        {
            if (binding == null) return null;
            GameObject go = _go.GetValue(binding) as GameObject;
            Item item = go != null ? go.GetComponent<Item>() : null;
            if (item == null) return null;
            var lease = Index.Track((ushort)_entityId.GetValue(binding), (byte)_entityVersion.GetValue(binding), item.id, item, binding);
            if (lease != null)
            {
                var lifetime = go.GetComponent<RetirementIdentity>();
                if (lifetime == null) lifetime = go.AddComponent<RetirementIdentity>();
                lifetime.Item = item;
            }
            return lease;
        }
        internal static void Forget(Item item) => Index.Remove(item);

        internal static void Tick()
        {
            if (!_installed) return;
            CheckWorld();
            while (AuthorityExpiry.Count > 0 && AuthorityExpiry.Peek().Expires <= Time.unscaledTime)
            {
                HostEntry expired = AuthorityExpiry.Dequeue();
                if (Authority.TryGetValue(expired.Key, out var current) && ReferenceEquals(current, expired)) Authority.Remove(expired.Key);
            }
            if (!Enabled || !Mp.IsRunning || !Game.InWorld) { ClearWork(); return; }
            if (Mp.IsServer && Outbound.Count > 0 && Time.unscaledTime >= _flushAt) Flush();
            if (Mp.IsClient && Incoming.Count > 0)
            {
                Incoming.Drain(ProcessWork, 8, 1.0);
                foreach (Work work in Retry) Incoming.Enqueue(work);
                Retry.Clear();
            }
            if ((_removed > 0 || _expired > 0) && Time.unscaledTime >= _logAt)
            {
                _logAt = Time.unscaledTime + 5f;
                MPfixPlugin.Log.Info($"[遗留物品] 已清理 {_removed} 件；保留 {_expired} 件（被持有或有未确认内容物）；待处理 {Incoming.Count} 件。");
                _removed = _expired = 0;
            }
        }
        private static void Flush()
        {
            var items = new JArray();
            int examined = 0;
            foreach (HostEntry entry in Outbound)
            {
                examined++;
                // A rapid reconnect may already have restored the saved inventory. Cancel its old notice.
                if (entry.Object != null && IsSavedInventory(entry.Object) && _go.GetValue(entry.Binding) as GameObject == null)
                    items.Add(new JObject { ["net"] = entry.Net, ["ver"] = entry.Version, ["id"] = entry.Resource });
                if (examined >= BatchSize) break;
            }
            bool sent = items.Count == 0 || Mp.Broadcast(Channel,
                new JObject { ["protocol"] = 2, ["layer"] = _layer, ["items"] = items }, false);
            if (sent)
            {
                for (int i = 0; i < examined; i++) OutboundKeys.Remove(Outbound.Dequeue().Key);
                _flushAt = Outbound.Count > 0 ? Time.unscaledTime : -1f;
            }
            else _flushAt = Time.unscaledTime + BatchDelay; // Reliable transport failure retains the batch.
        }
        private static void OnClientReceive(JToken token)
        {
            try
            {
                if (!Enabled || !Mp.IsRunning || !Mp.IsClient || !Game.InWorld || !(token is JObject)) return;
                CheckWorld();
                if (token.Value<int?>("protocol") != 2 || token.Value<int?>("layer") != _layer) return;
                JArray items = token["items"] as JArray;
                if (items == null || items.Count > BatchSize) return;
                int epoch = _epoch;
                Mp.RequestServer(VerifyChannel, token, response =>
                {
                    try
                    {
                        CheckWorld();
                        if (epoch != _epoch || !Enabled || !Mp.IsRunning || !Mp.IsClient || !Game.InWorld
                            || !(response is JObject) || response.Value<bool?>("ok") != true
                            || response.Value<int?>("layer") != _layer) return;
                        EnqueueApproved(response["items"] as JArray);
                    }
                    catch (Exception ex) { MPfixPlugin.Log.Debug("[遗留物品] 确认点名失败：" + ex.Message); }
                });
            }
            catch (Exception ex) { MPfixPlugin.Log.Debug("[遗留物品] 接收点名失败：" + ex.Message); }
        }
        private static JToken Verify(uint sender, JToken token)
        {
            try
            {
                if (!Enabled || !Mp.IsRunning || !Mp.IsServer || !Game.InWorld || !(token is JObject)) return Denied();
                CheckWorld();
                if (token.Value<int?>("layer") != _layer || token.Value<int?>("protocol") != 2) return Denied();
                JArray items = token["items"] as JArray;
                if (items == null || items.Count > BatchSize || MpChannels.BodyForClient(sender) == null) return Denied();
                var approved = new JArray();
                foreach (JToken item in items)
                {
                    if (!(item is JObject)) continue;
                    int id = item.Value<int?>("net") ?? 0, version = item.Value<int?>("ver") ?? -1;
                    if (id <= 0 || id > ushort.MaxValue || version < 0 || version > byte.MaxValue) continue;
                    if (!Authority.TryGetValue(id | (version << 16), out var entry)
                        || entry.Resource != item.Value<string>("id") || entry.Expires <= Time.unscaledTime
                        || entry.Object == null || !IsSavedInventory(entry.Object)
                        || _go.GetValue(entry.Binding) as GameObject != null) continue;
                    approved.Add(new JObject { ["net"] = id, ["ver"] = version, ["id"] = entry.Resource });
                }
                return new JObject { ["ok"] = true, ["layer"] = _layer, ["items"] = approved };
            }
            catch (Exception ex) { MPfixPlugin.Log.Debug("[遗留物品] 主机校验失败：" + ex.Message); return Denied(); }
        }
        private static JObject Denied() => new JObject { ["ok"] = false };
        private static void EnqueueApproved(JArray items)
        {
            if (items == null || items.Count > BatchSize) return;
            foreach (JToken entry in items)
            {
                if (!(entry is JObject)) continue;
                int id = entry.Value<int?>("net") ?? 0;
                int version = entry.Value<int?>("ver") ?? -1;
                string resource = entry.Value<string>("id");
                if (id <= 0 || id > ushort.MaxValue || version < 0 || version > byte.MaxValue || string.IsNullOrEmpty(resource)) continue;
                var lease = Index.Find((ushort)id, (byte)version, resource);
                if (lease == null)
                {
                    // Direct entity lookup also supports enabling this fix after objects were created.
                    var registry = _netRegistry.GetValue(null) as IDictionary;
                    object key = MpChannels.MakeKnet((uint)id);
                    object binding = key != null && registry != null && registry.Contains(key) ? registry[key] : null;
                    if (binding != null && (byte)_entityVersion.GetValue(binding) == version)
                    { Track(binding); lease = Index.Find((ushort)id, (byte)version, resource); }
                }
                if (lease != null && Queued.Add(lease)) Incoming.Enqueue(new Work { Lease = lease, Deadline = Time.unscaledTime + RetryLifetime });
            }
        }
        private static void Process(Work work)
        {
            var lease = work.Lease;
            Item item = lease.Value;
            if (!Enabled || !Index.IsCurrent(lease) || item == null || item.id != lease.Resource) { Queued.Remove(lease); return; }
            if (Time.unscaledTime < work.Due) { Retry.Add(work); return; }
            try
            {
                // A live new binding or a new holder always wins over a delayed retirement notice.
                object binding = Binding(item.gameObject);
                if (binding != null && !ReferenceEquals(binding, lease.Binding)) { Queued.Remove(lease); return; }
                var boundObject = _go.GetValue(lease.Binding) as GameObject;
                if (boundObject != null && boundObject != item.gameObject) { Queued.Remove(lease); return; }
                if (!TryDestroyLeaf(item, lease.Binding))
                {
                    if (Time.unscaledTime < work.Deadline) { work.Due = Time.unscaledTime + 0.1f; Retry.Add(work); }
                    else { _expired++; Queued.Remove(lease); }
                    return;
                }
                Queued.Remove(lease);
                Index.Remove(item);
                _removed++;
            }
            catch (Exception ex)
            {
                Queued.Remove(lease);
                MPfixPlugin.Log.Debug("[遗留物品] 清理失败，保留原物品：" + ex.Message);
            }
        }
        private static bool TryDestroyLeaf(Item item, object binding = null)
        {
            Children.Clear(); item.GetComponentsInChildren<Item>(true, Children);
            if (item.GetComponentInParent<Body>() != null || Children.Count > 1) return false;
            GameObject go = item.gameObject;
            // Public Entity.Destroy is a no-op for remote client entities. After explicit host
            // authorization use the native internal teardown, otherwise CreateObject can respawn the mesh.
            if (binding != null)
            {
                object previous = _retiringBinding;
                _retiringBinding = binding;
                try { _destroyEntity.Invoke(binding, null); }
                finally { _retiringBinding = previous; }
            }
            if (go != null) _destroy.Invoke(null, new object[] { go });
            if (binding != null)
            {
                _go.SetValue(binding, null);
                _tracker.SetValue(binding, null);
            }
            return true;
        }
        // Replica retirement must not reapply an old inventory packet, trigger destruction effects,
        // or scan the registry to find a replacement mesh. The caller removes the exact checked leaf.
        private static bool BeforeDestroyMesh(object __instance) => !ReferenceEquals(__instance, _retiringBinding);
        internal static bool SelfTest()
        {
            if (!_installed || _entityId.FieldType != typeof(ushort) || _entityVersion.FieldType != typeof(byte)) return false;
            // Probe only disposable objects in the menu; never touch a live world's inventory.
            if (Game.InWorld || Mp.IsRunning) return true;
            GameObject bag = null, child = null;
            try
            {
                bag = new GameObject("DDEN_retirement_probe_parent");
                var parentItem = bag.AddComponent<Item>(); parentItem.id = "__dden_probe__";
                child = new GameObject("DDEN_retirement_probe_child");
                var childItem = child.AddComponent<Item>(); childItem.id = "__dden_probe_child__";
                child.transform.SetParent(bag.transform);
                if (TryDestroyLeaf(parentItem)) return false; // Unconfirmed contents must be preserved.
                if (!TryDestroyLeaf(childItem)) return false;
                UnityEngine.Object.DestroyImmediate(child); child = null;
                return TryDestroyLeaf(parentItem); // Native cleanup of an exact leaf, no proximity guessing.
            }
            finally
            {
                if (child != null) UnityEngine.Object.DestroyImmediate(child);
                if (bag != null) UnityEngine.Object.DestroyImmediate(bag);
            }
        }
    }

    // No Update/FixedUpdate: ties the identity cache to the item, even if MP removes its tracker first.
    internal sealed class RetirementIdentity : MonoBehaviour
    {
        internal Item Item;
        private void OnDestroy()
        {
            try { GhostItemRelay.Forget(Item); }
            catch (Exception ex) { MPfixPlugin.Log.Debug("清理物品身份索引失败：" + ex.Message); }
        }
    }
}
