using DDEN.Shared;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DDEN.LoongNova
{
    public sealed class DragonGun : MonoBehaviour
    {
        internal const float ShotInterval = 0.5f;
        internal readonly EnergyState State = new EnergyState();
        internal bool Known;
        internal double NextShot;
        internal string Status = "";
        internal GunScript Gun { get; private set; }
        internal Item Item { get; private set; }
        internal Component Tracker;
        private readonly Dictionary<string, float> _pending = new Dictionary<string, float>();
        private readonly List<string> _expired = new List<string>(8);
        private bool _transaction;
        private bool _ejectRequested;
        private float _nextRead, _nextRequest, _nextAction;
        private object _connection;
        private SpriteRenderer _renderer;

        internal static DragonGun For(Item item)
        {
            var state = item.GetComponent<DragonGun>();
            return state != null ? state : item.gameObject.AddComponent<DragonGun>();
        }

        private void Awake()
        {
            Item = GetComponent<Item>();
            Gun = GetComponent<GunScript>();
            _renderer = GetComponent<SpriteRenderer>();
        }

        internal void Request(string operation, Body body, Item cell = null)
        {
            if (Item == null || Item.id != Registration.GunId || body == null) return;
            if (Gun == null) Gun = GetComponent<GunScript>();
            if (Gun == null) return;
            if (Mp.IsRunning && !Mp.CoreReady) { Status = Registration.Text("unavailable"); return; }
            if (operation == "eject" && (_pending.Count > 0 || Time.unscaledTime < _nextAction)) { _ejectRequested = true; return; }
            if (_pending.Count >= 8 || _transaction || operation != "shoot" && _pending.Count > 0
                || operation == "shoot" && Time.unscaledTime < _nextRequest
                || operation != "shoot" && Time.unscaledTime < _nextAction) return;
            if (operation == "shoot" && (Gun.safe || Item.condition <= 0 || Known && (!State.Loaded || State.Energy < EnergyState.ShotCost))) return;
            if (operation == "shoot") _nextRequest = Time.unscaledTime + ShotInterval;
            else _nextAction = Time.unscaledTime + 0.15f;
            if (!Mp.IsRunning || Mp.IsServer)
            {
                try { ApplyResponse(LoongNovaPlugin.Service.Execute(operation, this, cell, body)); }
                catch (System.Exception error)
                {
                    Status = Registration.Text("rejected");
                    LoongNovaPlugin.Log.LogWarning("LoongNova action: " + error.GetBaseException().Message);
                }
                return;
            }
            if (!LoongNovaPlugin.Service.Ready || NetworkAccess.Id(Item) == 0) { Status = Registration.Text("unavailable"); return; }
            string request = System.Guid.NewGuid().ToString("N");
            _pending.Add(request, Time.unscaledTime + 5);
            _transaction = operation != "shoot";
            object connection = NetworkAccess.Connection;
            var packet = new Newtonsoft.Json.Linq.JObject
            {
                ["request"] = request, ["operation"] = operation,
                ["gun"] = NetworkAccess.Id(Item), ["cell"] = cell == null ? 0 : NetworkAccess.Id(cell),
                ["dx"] = Gun.transform.right.x * (body.isRight ? 1 : -1),
                ["dy"] = Gun.transform.right.y * (body.isRight ? 1 : -1)
            };
            Status = Registration.Text("waiting");
            bool sent = Mp.RequestServer(WeaponService.Channel, packet, response =>
            {
                if (this == null || !_pending.Remove(request)) return;
                if (_pending.Count == 0) _transaction = false;
                if (!Mp.CoreReady || !Mp.IsRunning || Mp.IsServer
                    || !ReferenceEquals(connection, NetworkAccess.Connection)) return;
                if (response?.Value<string>("request") != request) return;
                ApplyResponse(response);
            });
            if (!sent) { _pending.Remove(request); if (_pending.Count == 0) _transaction = false; Status = Registration.Text("unavailable"); }
        }

        private void ApplyResponse(Newtonsoft.Json.Linq.JToken response)
        {
            if (response == null) return;
            long revision = response["state"]?.Value<long?>("revision") ?? -1;
            if (revision >= State.Revision && State.Restore(response["state"])) Known = true;
            _nextRead = Time.unscaledTime + 5;
            Status = response.Value<bool?>("ok") == true ? "" : Registration.Text("rejected");
            Normalize();
        }

        private void Update()
        {
            if (Item == null || Item.id != Registration.GunId) return;
            object connection = LoongNovaPlugin.Service.Connection;
            if (!ReferenceEquals(_connection, connection))
            {
                _connection = connection; _pending.Clear(); _transaction = _ejectRequested = false; _nextRead = 0;
                if (Mp.IsRunning && !Mp.IsServer) { Known = false; State.Revision = 0; }
            }
            _expired.Clear();
            foreach (var request in _pending) if (Time.unscaledTime >= request.Value) _expired.Add(request.Key);
            foreach (string request in _expired) _pending.Remove(request);
            if (_expired.Count > 0)
            {
                if (_pending.Count == 0) _transaction = false;
                Status = Registration.Text("rejected");
            }
            var body = PlayerCamera.main != null ? PlayerCamera.main.body : null;
            if (_ejectRequested && _pending.Count == 0 && Time.unscaledTime >= _nextAction)
            {
                _ejectRequested = false;
                Request("eject", body);
            }
            if (Mp.IsRunning && !Mp.IsServer && body != null && body.GetItem(body.handSlot) == Item && Time.unscaledTime >= _nextRead)
            {
                _nextRead = Time.unscaledTime + 1;
                Request("read", body);
            }
        }

        internal void Normalize()
        {
            if (Gun == null) Gun = GetComponent<GunScript>();
            if (Gun == null) return;
            Gun.racked = Gun.lastRacked = false;
            Gun.firingPinStruck = false;
            Gun.magCapacity = 20;
            Gun.structureDamage = 3000; Gun.animalDamage = 650; Gun.knockBack = 2;
            Gun.conditionLossPerShot = 0.2f; Gun.desiredGasTime = ShotInterval;
            if (!Mp.IsRunning || Mp.IsServer || Known)
            {
                Gun.hasMag = State.Loaded;
                Gun.roundsInMag = State.Loaded ? Mathf.FloorToInt(State.Energy / EnergyState.ShotCost) : 0;
                Gun.roundInChamber = GunScript.RoundInChamber.None;
            }
            // The stock gun update is suppressed, so explicitly keep the generated held sprite.
            if (_renderer != null && Registration.GunSprite != null && _renderer.sprite != Registration.GunSprite)
                _renderer.sprite = Registration.GunSprite;
        }
    }
}
