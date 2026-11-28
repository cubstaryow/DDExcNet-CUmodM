using System;
using System.Collections.Generic;
using CUCoreLib.Helpers;
using DDEN.Shared;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DDEN.LoongNova
{
    public sealed class MicroSpaceCrate : MonoBehaviour
    {
        private static readonly HashSet<MicroSpaceCrate> Live = new HashSet<MicroSpaceCrate>();
        private static readonly List<MicroSpaceCrate> Snapshot = new List<MicroSpaceCrate>();
        internal readonly SpatialPowerState State = new SpatialPowerState();
        internal Item Item;
        internal bool Known;
        private SpriteRenderer _renderer;
        private Sprite _visibleSprite;
        private bool IsCharger => Item != null && Item.id == SpatialCatalog.ChargerId;
        private Container _container;
        private readonly PendingActions _requests = new PendingActions();
        private double _lastTime = -1;
        private float _readAt, _backgroundReadAt;
        private long _revision = -1;
        private object _connection;
        private int _publishedMode = -1, _remoteMode;

        private void Awake() { Item = GetComponent<Item>(); _container = GetComponent<Container>(); Live.Add(this); }
        private void OnDestroy() { Live.Remove(this); }
        internal static MicroSpaceCrate For(Item item)
        {
            var box = item.GetComponent<MicroSpaceCrate>() ?? item.gameObject.AddComponent<MicroSpaceCrate>();
            box.Item = item; Live.Add(box); return box;
        }
        internal void Refresh(Container container = null)
        {
            if (container != null) _container = container;
            if (_container == null) _container = GetComponent<Container>();
            if (_container != null) _container.encumberanceMult = IsCharger ? 0 : State.Multiplier;
            RefreshSprite();
            // Item.condition remains the container's durability, never its installed battery charge.
        }
        private void LateUpdate() => RefreshSprite();
        internal void RefreshSprite()
        {
            if (!IsCharger) return;
            var sprite = State.Cell.Loaded && State.Activated && (!Mp.IsRunning || Mp.IsServer || Known)
                ? Registration.RechargerActiveSprite : Registration.RechargerInactiveSprite;
            if (sprite == null) return;
            if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
            // Reuse CCL's per-instance icon override for inventory and the existing world renderer.
            if (_visibleSprite != sprite && CUCoreUtils.SetItemSprite(Item, sprite)) _visibleSprite = sprite;
            if (_renderer != null && _renderer.sprite != sprite) _renderer.sprite = sprite;
        }
        internal void Settle()
        {
            double now = Time.timeAsDouble;
            if (_lastTime < 0 || now < _lastTime) { _lastTime = now; return; }
            double elapsed = now - _lastTime; _lastTime = now;
            if (Item == null) return;
            if (Mp.IsRunning && !Mp.IsServer)
            {
                if (Known && _remoteMode == 1) State.Advance(elapsed, true);
                else if (Known && _remoteMode == 2) State.SelfCharge(elapsed);
                Refresh(); return;
            }
            Refresh();
            if (IsCharger)
            {
                if (State.Activated && State.Cell.Loaded && _container != null)
                {
                    State.SelfCharge(elapsed);
                    foreach (Transform child in _container.transform)
                        if (child.TryGetComponent<Item>(out var battery) && battery.Stats is BatteryInfo info)
                            battery.condition = SpatialChargeRules.Charge(battery.condition, info.maxCharge, elapsed);
                }
            }
            else State.Advance(elapsed, _container != null && _container.itemCount > 0);
            Refresh();
        }
        internal static void Tick()
        {
            Snapshot.Clear(); Snapshot.AddRange(Live);
            var body = PlayerCamera.main != null ? PlayerCamera.main.body : null;
            foreach (var box in Snapshot)
            {
                if (box == null || box.Item == null || !SpatialCatalog.IsDevice(box.Item.id) || !box.gameObject.scene.IsValid()) continue;
                box.Settle(); box.PublishIfChanged();
                if (box._requests.Expire(Time.unscaledTime, out _)) box.Known = false;
                object connection = NetworkAccess.Connection;
                if (!ReferenceEquals(box._connection, connection))
                { box._connection = connection; box._revision = -1; box.Known = false; box._requests.Clear(); box._readAt = box._backgroundReadAt = 0; box._remoteMode = 0; box._publishedMode = -1; }
                if (Mp.IsRunning && !Mp.IsServer && body != null && box._requests.Count == 0
                    && Time.unscaledTime >= box._readAt && Time.unscaledTime >= box._backgroundReadAt && (NetworkAccess.Carries(body, box.Item)
                    || box.Item.GetComponentInParent<Body>() == body))
                {
                    box._backgroundReadAt = Time.unscaledTime + (box.Known ? 60 : 2);
                    box.Request("read", body);
                }
            }
        }
        internal static void Unload(Body body, Item item)
        {
            // Body.UseItem already performs native MP forwarding and reach validation.
            if (Mp.IsRunning && !Mp.IsServer || body == null || item == null || !SpatialCatalog.IsDevice(item.id)) return;
            For(item).Execute("eject", null, body);
        }
        internal void Request(string operation, Body body, Item cell = null)
        {
            if (body == null || Item == null || _requests.Count != 0 || operation == "read" && Time.unscaledTime < _readAt) return;
            if (!Mp.IsRunning || Mp.IsServer) { Execute(operation, cell, body); return; }
            if (!LoongNovaPlugin.Service.Ready || NetworkAccess.Id(Item) == 0) return;
            if (operation == "read" && !NetworkReadBudget.TryAcquire(Time.unscaledTime, NetworkAccess.Connection)) return;
            string request = Guid.NewGuid().ToString("N");
            var connection = NetworkAccess.Connection;
            _requests.Add(request, operation, Time.unscaledTime); _readAt = Time.unscaledTime + (Known ? 60 : 2);
            bool sent = Mp.RequestServer(WeaponService.Channel, new JObject
            {
                ["request"] = request, ["operation"] = operation, ["gun"] = NetworkAccess.Id(Item),
                ["knownEnergyMode"] = _remoteMode, ["knownRevision"] = Known ? _revision : -1, ["cell"] = cell != null ? NetworkAccess.Id(cell) : 0
            }, reply =>
            {
                if (this == null || !_requests.Finish(request) || !ReferenceEquals(connection, NetworkAccess.Connection)
                    || !Mp.IsRunning || Mp.IsServer || reply?.Value<string>("request") != request) return;
                if (reply.Value<string>("error") == "rechargerActivationNeed") EnergyCellNotice.Show(body);
                var state = reply["state"];
                long revision = state?.Value<long?>("revision") ?? -1;
                if (revision < _revision) return;
                if (State.Restore(state)) { _revision = revision; _remoteMode = reply.Value<int?>("energyMode") ?? 0; Known = true; _readAt = Time.unscaledTime + 60; _lastTime = Time.timeAsDouble; Refresh(); }
            });
            if (!sent) { _requests.Finish(request); _readAt = Time.unscaledTime + 1; }
        }
        internal JObject Execute(string operation, Item cell, Body body)
        {
            if (Item == null || !SpatialCatalog.IsDevice(Item.id) || body == null || Mp.IsRunning && (!Mp.IsServer || !LoongNovaPlugin.Service.Ready)
                || !NetworkAccess.CanAccess(body, Item)) return Reply(false);
            Settle();
            if (operation == "read") return Reply(true);
            if (!body.conscious) return Reply(false);
            if (operation == "load")
            {
                if (State.Cell.Loaded || cell == null || cell.id != Registration.CellId || !BeltPowerState.Finite(cell.condition)
                    || !NetworkAccess.CanAccess(body, cell)) return Reply(false);
                float energy = Mathf.Clamp01(cell.condition) * EnergyState.Capacity;
                if (IsCharger)
                {
                    if (!State.Activate(energy, cell.favourited))
                    { EnergyCellNotice.Show(body); var denied = Reply(false); denied["error"] = "rechargerActivationNeed"; return denied; }
                }
                else State.Load(energy, cell.favourited);
                UnityEngine.Object.Destroy(cell.gameObject);
            }
            else if (operation == "eject")
            {
                if (!State.Cell.Loaded) return Reply(false);
                var made = CustomInstantiate.InstantiateReturn(Registration.CellId, transform.position, Quaternion.identity, (float)(State.Energy / EnergyState.Capacity));
                var item = made != null ? made.GetComponent<Item>() : null;
                if (item == null) { if (made != null) UnityEngine.Object.Destroy(made); return Reply(false); }
                item.favourited = State.Cell.Favourite; State.Clear();
                if (item.GetComponent<FreshItemDrop>() == null) item.gameObject.AddComponent<FreshItemDrop>();
                try { body.AutoPickUpItem(item); } catch (Exception error) { LoongNovaPlugin.Log.Warn("Spatial cell pickup: " + error.Message); }
            }
            else return Reply(false);
            Known = true; Refresh(); PublishIfChanged(); Sound.Play("batteryinsert", transform.position); return Reply(true);
        }
        private int EnergyMode => !State.Cell.Loaded ? 0
            : IsCharger ? (State.Activated && State.Energy < EnergyState.Capacity ? 2 : 0)
            : State.Energy > 0 && _container != null && _container.itemCount > 0 ? 1 : 0;
        private JObject Reply(bool ok) { Refresh(); return new JObject { ["ok"] = ok, ["energyMode"] = EnergyMode, ["state"] = State.Capture() }; }
        private void PublishIfChanged()
        {
            if (!Mp.IsRunning || !Mp.IsServer || !LoongNovaPlugin.Service.Ready || Item == null || NetworkAccess.Id(Item) == 0) return;
            int mode = EnergyMode;
            int stamp = mode | (State.Cell.Loaded ? 4 : 0) | (State.Activated ? 8 : 0)
                | (State.Energy >= EnergyState.Capacity ? 16 : 0) | (State.Energy <= 0 ? 32 : 0)
                | (State.Cell.Favourite ? 64 : 0);
            if (_publishedMode == stamp) return;
            if (_publishedMode == -1 && !State.Cell.Loaded) { _publishedMode = stamp; return; }
            // Minute queries provide checkpoints; only structural and activity transitions broadcast.
            _publishedMode = stamp;
            Mp.Broadcast(WeaponService.VisualChannel, new JObject { ["spatial"] = NetworkAccess.Id(Item),
                ["energyMode"] = mode, ["state"] = State.Capture() });
        }
        internal static void Receive(JToken data)
        {
            if (!Mp.IsRunning || Mp.IsServer) return;
            var item = NetworkAccess.Item(data.Value<int>("spatial"));
            if (item == null || !SpatialCatalog.IsDevice(item.id)) return;
            var box = For(item); long revision = data["state"]?.Value<long?>("revision") ?? -1;
            if (revision < box._revision || !box.State.Restore(data["state"])) return;
            box._revision = revision; box._remoteMode = data.Value<int?>("energyMode") ?? 0;
            box.Known = true; box._lastTime = Time.timeAsDouble; box._readAt = Time.unscaledTime + 60; box.Refresh();
        }
        internal static void ClearRegistry() { Live.Clear(); Snapshot.Clear(); }
    }
}
