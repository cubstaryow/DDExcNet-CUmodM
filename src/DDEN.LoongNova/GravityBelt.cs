using System;
using CUCoreLib.Helpers;
using DDEN.Shared;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DDEN.LoongNova
{
    public sealed class GravityBelt : MonoBehaviour
    {
        internal readonly BeltPowerState State = new BeltPowerState();
        internal Item Item { get; private set; }
        internal bool Known;
        internal string Status = "";
        private bool _boosting, _working, _pending, _ejectQueued;
        private float _nextReport, _nextRead, _deadline, _protectedUntil;
        private double _confirmedWork;
        private long _serverRevision = -1;
        private object _connection;
        private Body _lastOwner;
        private Transform _wearParent;
        private Limb _wearLimb;
        private string _request;
        private readonly WorkReportBudget _reports = new WorkReportBudget();
        private Container _container;

        internal static GravityBelt For(Item item) => item.GetComponent<GravityBelt>() ?? item.gameObject.AddComponent<GravityBelt>();
        internal static GravityBelt Worn(Body body)
        {
            if (body == null) return null;
            var torso = body.limbs != null && body.limbs.Length > (int)Body.LimbNum.DownTorso
                ? body.limbs[(int)Body.LimbNum.DownTorso] : null;
            var item = torso != null ? body.GetWearableBySlotID("belt", torso) : null;
            return item != null && item.id == Registration.BeltId ? For(item) : null;
        }
        internal Body Owner
        {
            get
            {
                var parent = transform.parent;
                if (parent != _wearParent)
                {
                    _wearParent = parent;
                    _wearLimb = parent != null ? parent.GetComponent<Limb>() : null;
                    if (_wearLimb != null && _wearLimb.name != "DownTorso") _wearLimb = null;
                }
                return _wearLimb != null ? _wearLimb.body : null;
            }
        }
        internal static bool Local(Body body) => body != null && (!Mp.IsRunning || PlayerCamera.main != null && PlayerCamera.main.body == body);
        internal bool Powered => State.Remaining > 0 && (!Mp.IsRunning || LoongNovaPlugin.Service.Ready)
            && (!Mp.IsRunning || Mp.IsServer || Known && State.WorkTotal - _confirmedWork < 2 - 1e-6);
        internal bool CanBoost(Body body) => Owner == body && Local(body) && Powered;
        internal void Boost(Body body)
        {
            if (!CanBoost(body)) return;
            body.rb.velocity = new Vector2(body.rb.velocity.x, body.rb.velocity.y * 1.44f);
            _boosting = true;
        }
        private void Awake()
        {
            Item = GetComponent<Item>();
            RefreshEncumbrance();
        }

        internal void RefreshEncumbrance(Container container = null)
        {
            if (container != null) _container = container;
            if (_container == null) _container = GetComponent<Container>();
            float multiplier = State.EncumbranceMultiplier;
            if (_container != null && _container.encumberanceMult != multiplier) _container.encumberanceMult = multiplier;
        }

        private void FixedUpdate()
        {
            if (Item == null || Item.id != Registration.BeltId) return;
            Item.condition = 1;
            var body = Owner;
            if (body != _lastOwner) { _lastOwner = body; _boosting = false; }
            if (!Local(body) || !Powered) { _working = false; return; }
            var rb = body.standing || _wearLimb == null ? body.rb : _wearLimb.rb;
            float cap = Mathf.Clamp(body.jumpSpeed * 0.5f, 1, 12);
            if (body.grounded || rb.velocity.y <= 0) _boosting = false;
            bool slowing = !body.grounded && rb.velocity.y <= -cap + 0.05f;
            if (slowing)
            {
                Clamp(body.rb, cap);
                if (!body.standing)
                    foreach (var limb in body.limbs) if (limb != null && !limb.dismembered) Clamp(limb.rb, cap);
                body.lastTimeStepVelocity = new Vector2(body.lastTimeStepVelocity.x, Mathf.Max(body.lastTimeStepVelocity.y, -cap));
                _protectedUntil = Time.time + Time.fixedDeltaTime * 2;
            }
            _working = _boosting || slowing;
            if (_working)
            {
                double seconds = Time.fixedDeltaTime;
                if (Mp.IsRunning && !Mp.IsServer) seconds = Math.Min(seconds, Math.Max(0, 2 - (State.WorkTotal - _confirmedWork)));
                State.Advance(seconds);
                if (State.Remaining <= 0) RefreshEncumbrance();
            }
        }
        private static void Clamp(Rigidbody2D rb, float cap)
        {
            if (rb != null && rb.velocity.y < -cap) rb.velocity = new Vector2(rb.velocity.x, -cap);
        }

        internal bool ProtectLanding(Body body, Collision2D collision)
        {
            if (Owner != body || !Local(body) || collision == null || body.lastTimeStepVelocity.y >= -0.1f
                || !(Powered || _protectedUntil > 0 && Time.time <= _protectedUntil)) return false;
            for (int i = 0; i < collision.contactCount; i++)
                if (collision.GetContact(i).normal.y > 0.5f) return true;
            return false;
        }

        private void Update()
        {
            if (Item == null || Item.id != Registration.BeltId) return;
            object connection = LoongNovaPlugin.Service.Connection;
            if (!ReferenceEquals(_connection, connection))
            {
                _connection = connection; _pending = _ejectQueued = false; _request = null;
                _nextRead = 0; _serverRevision = -1; _confirmedWork = 0;
                if (Mp.IsRunning && !Mp.IsServer) Known = false;
            }
            if (_pending && Time.unscaledTime >= _deadline)
            {
                _pending = false; _request = null; Known = false; Status = Registration.Text("rejected");
            }
            var body = PlayerCamera.main != null ? PlayerCamera.main.body : null;
            if (_ejectQueued && !_pending) { Request("eject", body); return; }
            if (!Mp.IsRunning || Mp.IsServer || body == null || !Known && Time.unscaledTime < _nextRead) return;
            if (!_pending && State.WorkTotal > _confirmedWork + 1e-7 && Time.unscaledTime >= _nextReport)
            {
                _nextReport = Time.unscaledTime + 1;
                if (Owner == body || NetworkAccess.Carries(body, Item)) Request("work", body);
                else
                {
                    Known = false;
                    _confirmedWork = State.WorkTotal; // Stop retrying an item no longer carried by this player.
                }
            }
            else if (!_pending && Owner == body && Time.unscaledTime >= _nextRead)
                Request("read", body);
        }

        internal void Request(string operation, Body body, Item cell = null)
        {
            if (Item == null || body == null) return;
            if (operation == "eject") _ejectQueued = true;
            if (_pending) return;
            if (!Mp.IsRunning || Mp.IsServer)
            {
                ApplyResponse(Execute(operation, cell, body, null));
                _ejectQueued = false;
                return;
            }
            if (!LoongNovaPlugin.Service.Ready || NetworkAccess.Id(Item) == 0)
            { Status = Registration.Text("unavailable"); return; }
            if (operation == "eject") _ejectQueued = false;
            string request = Guid.NewGuid().ToString("N");
            var packet = new JObject
            {
                ["request"] = request, ["operation"] = operation, ["gun"] = NetworkAccess.Id(Item),
                ["cell"] = cell != null ? NetworkAccess.Id(cell) : 0,
                ["epoch"] = State.Epoch, ["work"] = State.WorkTotal
            };
            _pending = true; _request = request; _deadline = Time.unscaledTime + 5;
            _nextReport = Time.unscaledTime + 0.5f; _nextRead = Time.unscaledTime + 5;
            var connection = NetworkAccess.Connection;
            bool sent = Mp.RequestServer(WeaponService.Channel, packet, response =>
            {
                if (this == null || !_pending || _request != request || !ReferenceEquals(connection, NetworkAccess.Connection)
                    || response?.Value<string>("request") != request) return;
                _pending = false; _request = null;
                if (Mp.CoreReady && Mp.IsRunning && !Mp.IsServer) ApplyResponse(response);
            });
            if (!sent) { _pending = false; Known = false; Status = Registration.Text("unavailable"); }
        }

        private void ApplyResponse(JToken response)
        {
            var payload = response?["state"];
            long revision = payload?.Value<long?>("revision") ?? -1;
            if (revision < _serverRevision) return;
            string epoch = State.Epoch;
            double work = State.WorkTotal;
            if (State.Restore(payload))
            {
                _serverRevision = revision; _confirmedWork = State.WorkTotal;
                if (Mp.IsRunning && !Mp.IsServer && State.Epoch == epoch)
                    State.Advance(Math.Max(0, work - _confirmedWork));
                Known = true;
                RefreshEncumbrance();
            }
            Status = response?.Value<bool?>("ok") == true ? "" : Registration.Text("rejected");
        }

        // The action channel supplies an authenticated body; clients never choose their owner identity.
        internal JObject Execute(string operation, Item cell, Body body, JToken packet)
        {
            if (Item == null || Item.id != Registration.BeltId || body == null
                || Mp.IsRunning && (!Mp.IsServer || !LoongNovaPlugin.Service.Ready)
                || !NetworkAccess.Carries(body, Item)) return Reply(false);
            if (packet != null && (operation == "work" || operation == "eject"))
            {
                if (!_reports.Apply(State, packet.Value<string>("epoch"), packet.Value<double?>("work") ?? double.NaN, Time.time))
                    return Reply(false);
            }
            if (operation == "read" || operation == "work") return Reply(true);
            if (!body.conscious) return Reply(false);
            if (operation == "load")
            {
                if (State.Cell.Loaded || cell == null || cell.id != Registration.CellId
                    || !BeltPowerState.Finite(cell.condition) || !NetworkAccess.Carries(body, cell)) return Reply(false);
                float energy = Mathf.Clamp01(cell.condition) * EnergyState.Capacity;
                bool favourite = cell.favourited;
                if (cell.TryGetParentContainer(out var container)) container.UnloadItem(cell);
                body.DropItem(cell);
                if (cell.transform.parent != null) return Reply(false);
                State.Load(energy, favourite);
                cell.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(cell.gameObject);
            }
            else if (operation == "eject")
            {
                if (!State.Cell.Loaded) return Reply(false);
                var created = CustomInstantiate.InstantiateReturn(Registration.CellId, transform.position, Quaternion.identity,
                    State.Remaining / EnergyState.Capacity);
                var result = created != null ? created.GetComponent<Item>() : null;
                if (result == null) { if (created != null) UnityEngine.Object.Destroy(created); return Reply(false); }
                result.favourited = State.Cell.Favourite;
                State.Clear();
                try { body.AutoPickUpItem(result); }
                catch (Exception error) { LoongNovaPlugin.Log.LogWarning("LoongNova belt pickup: " + error.Message); }
            }
            else return Reply(false);
            Known = true;
            return Reply(true);
        }

        private JObject Reply(bool ok)
        {
            RefreshEncumbrance();
            return new JObject { ["ok"] = ok, ["state"] = State.Capture() };
        }
    }
}
