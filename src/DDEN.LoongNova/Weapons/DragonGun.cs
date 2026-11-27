using DDEN.Shared;
using CUCoreLib.Helpers;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DDEN.LoongNova
{
    public sealed class DragonGun : MonoBehaviour
    {
        internal const float ShotInterval = 0.15f;
        internal readonly EnergyState State = new EnergyState();
        internal bool Known;
        internal double NextShot;
        internal readonly CannonAmmoState Ammo = new CannonAmmoState();
        internal readonly ChargeCycle Charge = new ChargeCycle();
        private bool _chargeHeld, _chargeAck, _releaseQueued, _cancelQueued, _keyWasDown;
        private float _localChargeStarted, _pressStarted;
        internal bool IsChargeGun => Item != null && Item.id == Registration.ChargeGunId;
        internal bool ChargeVisible => IsChargeGun && (_chargeHeld || _releaseQueued);
        internal bool ChargeAcknowledged => _chargeAck;
        internal float ChargeProgress => _chargeAck ? Mathf.Clamp01((Time.time - _localChargeStarted) / (float)ChargeCycle.Maximum) : 0;
        internal string Status = "";
        internal GunScript Gun { get; private set; }
        internal Item Item { get; private set; }
        internal Component Tracker;
        private readonly PendingActions _pending = new PendingActions();
        private bool _ejectRequested;
        private float _nextRead, _nextRequest, _nextAction;
        private object _connection;
        private SpriteRenderer _renderer;
        private Sprite _visibleSprite;

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
            if (Item == null || !Registration.IsGun(Item.id) || body == null) return;
            if (Gun == null) Gun = GetComponent<GunScript>();
            if (Gun == null) return;
            if (Mp.IsRunning && !Mp.CoreReady) { Status = Registration.Text("unavailable"); return; }
            if (operation == "eject" && (_pending.Count > 0 || Time.unscaledTime < _nextAction)) { _ejectRequested = true; return; }
            if (_pending.Count >= 8 || _pending.Busy || operation != "shoot" && _pending.Count > 0
                || operation == "shoot" && Time.unscaledTime < _nextRequest
                || operation != "shoot" && Time.unscaledTime < _nextAction) return;
            if (operation == "shoot" && (Gun.safe || Item.condition <= 0 || Known && (!State.Loaded || State.Energy < EnergyState.ShotCost))) return;
            if (operation == "shoot") _nextRequest = Time.unscaledTime + ShotInterval;
            else _nextAction = Time.unscaledTime + 0.15f;
            if (!Mp.IsRunning || Mp.IsServer)
            {
                try { HandleResponse(operation, LoongNovaPlugin.Service.Execute(operation, this, cell, body)); }
                catch (System.Exception error)
                {
                    RequestFailed();
                    Status = Registration.Text("rejected");
                    LoongNovaPlugin.Log.Warn("LoongNova action: " + error.GetBaseException().Message);
                }
                return;
            }
            if (!LoongNovaPlugin.Service.Ready || NetworkAccess.Id(Item) == 0) { Status = Registration.Text("unavailable"); return; }
            string request = System.Guid.NewGuid().ToString("N");
            object connection = NetworkAccess.Connection;
            var packet = new Newtonsoft.Json.Linq.JObject
            {
                ["request"] = request, ["operation"] = operation,
                ["gun"] = NetworkAccess.Id(Item), ["cell"] = cell == null ? 0 : NetworkAccess.Id(cell),
                ["dx"] = Gun.transform.right.x * (body.isRight ? 1 : -1),
                ["dy"] = Gun.transform.right.y * (body.isRight ? 1 : -1)
            };
            _pending.Add(request, operation, Time.unscaledTime);
            Status = Registration.Text("waiting");
            bool sent = Mp.RequestServer(WeaponService.Channel, packet, response =>
            {
                if (this == null || !_pending.Finish(request)) return;
                if (!Mp.CoreReady || !Mp.IsRunning || Mp.IsServer
                    || !ReferenceEquals(connection, NetworkAccess.Connection)) return;
                try
                {
                    if (response?.Value<string>("request") != request) { RequestFailed(); return; }
                    HandleResponse(operation, response);
                }
                catch (System.Exception error)
                {
                    RequestFailed();
                    LoongNovaPlugin.Log.Warn("LoongNova response: " + error.GetBaseException().Message);
                }
            });
            if (!sent) { _pending.Finish(request); RequestFailed(); Status = Registration.Text("unavailable"); }
        }
        private void RequestFailed()
        {
            _chargeHeld = _chargeAck = _releaseQueued = _cancelQueued = false;
            _ejectRequested = false; _nextRead = Time.unscaledTime + 1;
            Status = Registration.Text("rejected");
        }

        private void HandleResponse(string operation, JToken response)
        {
            ApplyResponse(response);
            if (operation == "charge")
            {
                _chargeAck = response?.Value<bool?>("ok") == true;
                _localChargeStarted = Time.time - (response?.Value<float?>("chargeElapsed") ?? 0);
                if (!_chargeAck) { _chargeHeld = _releaseQueued = _cancelQueued = false; }
            }
        }
        private void ApplyResponse(Newtonsoft.Json.Linq.JToken response)
        {
            if (response == null) return;
            long revision = response["state"]?.Value<long?>("revision") ?? -1;
            if (revision >= StateRevision && RestoreState(response["state"])) Known = true;
            _nextRead = Time.unscaledTime + 5;
            Status = response.Value<bool?>("ok") == true ? "" : Registration.Text("rejected");
            Normalize();
        }

        private void Update()
        {
            if (Item == null || !Registration.IsGun(Item.id)) return;
            object connection = LoongNovaPlugin.Service.Connection;
            if (!ReferenceEquals(_connection, connection))
            {
                Charge.Cancel(); _chargeHeld = _chargeAck = _releaseQueued = _cancelQueued = _keyWasDown = false;
                _connection = connection; _pending.Clear(); _ejectRequested = false; _nextRead = Time.unscaledTime + 1;
                if (Mp.IsRunning && !Mp.IsServer) { Known = false; State.Revision = Ammo.Revision = 0; }
            }
            if (IsChargeGun) UpdateCharge();
            RefreshSprite();
            if (_pending.Expire(Time.unscaledTime, out bool chargeExpired))
            {
                RequestFailed();
                // At most one cancel after a lost charge/release; a lost load/read/cancel cannot loop.
                _cancelQueued = IsChargeGun && chargeExpired;
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

        internal void QueueChargeCancel()
        {
            if (!IsChargeGun) return;
            _chargeHeld = _releaseQueued = _chargeAck = false; _cancelQueued = true;
        }
        internal void ApplyServerState(JToken data)
        {
            long revision = data?.Value<long?>("revision") ?? -1;
            if (revision < StateRevision || !RestoreState(data)) return;
            Known = true;
            if (!Ammo.Ready) _chargeHeld = _releaseQueued = _cancelQueued = _chargeAck = false;
            Normalize();
        }
        private void UpdateCharge()
        {
            // The host must invalidate interrupted charges even if the owner sends no cancel packet.
            var chargeOwner = Charge.Owner as Body;
            if (Charge.Active && (!Mp.IsRunning || Mp.IsServer)
                && (Charge.Elapsed(Time.time) >= ChargeCycle.Maximum || chargeOwner == null || !chargeOwner.conscious
                    || !chargeOwner.allowUseItem || chargeOwner.forceNoMovement || chargeOwner.GetItem(chargeOwner.handSlot) != Item
                    || Gun == null || Gun.safe || Item.condition <= 0 || !Ammo.Ready))
            {
                if (!CannonService.ForceCommitted(this)) Charge.Cancel();
            }
            var camera = PlayerCamera.main;
            var body = camera != null ? camera.body : null;
            bool held = body != null && body.GetItem(body.handSlot) == Item;
            bool pressed = held && Input.GetKey(KeyBinds.GetBind("attack"));
            bool valid = held && body.conscious && body.allowUseItem && !body.forceNoMovement && !DdenHub.AnyPanelVisible && !PauseHandler.paused
                && !camera.radialOpen && !camera.woundView.activeSelf && !camera.tradeMenu.activeSelf
                && !UIUtil.IsPointerOverUIElement() && !camera.blockItemUsage
                && (MinigameBase.main == null || MinigameBase.main.currentMinigame == null) && LiquidTransfer.instance == null
                && Time.timeScale > 0 && Gun != null && !Gun.safe && Item.condition > 0
                && (!Known || Ammo.Ready);
            if (_chargeHeld && (!valid || Time.time - _localChargeStarted > ChargeCycle.Lifetime))
            { _chargeHeld = false; _releaseQueued = false; _cancelQueued = true; }
            if (_chargeHeld && !pressed)
            {
                _chargeHeld = false;
                // Round-trip delay must never turn a short local tap into an unintended shot.
                if (Time.time - _pressStarted < ChargeCycle.Minimum) _cancelQueued = true;
                else _releaseQueued = true;
            }
            if (valid && pressed && !_keyWasDown && !_releaseQueued && !_cancelQueued && _pending.Count == 0
                && Time.unscaledTime >= _nextAction && Time.unscaledTime >= NextShot)
            {
                _chargeHeld = true; _chargeAck = false; _pressStarted = _localChargeStarted = Time.time;
                Request("charge", body);
            }
            _keyWasDown = pressed;
            if ((_cancelQueued || _releaseQueued) && _pending.Count == 0 && Time.unscaledTime >= _nextAction)
            {
                string operation = _cancelQueued || !valid ? "cancel" : "release";
                _cancelQueued = _releaseQueued = false;
                if (body != null) Request(operation, body);
                _chargeAck = false;
            }
        }
        private void OnDisable()
        {
            _pending.Clear(); _ejectRequested = false;
            Charge.Cancel();
            _chargeHeld = _chargeAck = _releaseQueued = _keyWasDown = false;
            _cancelQueued = true;
        }
        // Reads are throttled and use the existing authenticated carried-item path.
        internal string CellHint()
        {
            if (Mp.IsRunning && !Mp.IsServer)
            {
                var body = PlayerCamera.main != null ? PlayerCamera.main.body : null;
                if (body != null && Time.unscaledTime >= _nextRead
                    && (body.GetItem(body.handSlot) == Item || NetworkAccess.Carries(body, Item)))
                {
                    _nextRead = Time.unscaledTime + 1;
                    Request("read", body);
                }
                if (!Known) return Registration.Text("waiting");
            }
            if (IsChargeGun)
                return Ammo.Loaded ? string.Format(Registration.Text("cannonLoaded"), Registration.RoundName(Ammo.Id))
                    : State.Loaded ? Registration.Text("legacyCannonCell") : Registration.Text("cannonEmpty");
            return State.Loaded
                ? string.Format(Registration.Text("beltSocketLoaded"), State.Energy, State.Energy / EnergyState.Capacity * 100)
                : Registration.Text("beltSocketEmpty");
        }

        internal long StateRevision => IsChargeGun ? Ammo.Revision + State.Revision : State.Revision;
        internal JObject CaptureState() => IsChargeGun ? CannonSaveState.Capture(Ammo, State) : State.Capture();
        internal bool RestoreState(JToken data) => IsChargeGun ? CannonSaveState.Restore(Ammo, State, data) : State.Restore(data);
        internal void Normalize()
        {
            if (Gun == null) Gun = GetComponent<GunScript>();
            if (Gun == null) return;
            // Native racked means the bolt is OPEN, not that a round is ready.
            Gun.racked = Gun.lastRacked = IsChargeGun && !Ammo.Chambered;
            Gun.firingPinStruck = false;
            Gun.magCapacity = IsChargeGun ? 1 : 100;
            Gun.structureDamage = 625; Gun.animalDamage = 225; Gun.knockBack = IsChargeGun ? 6 : 2;
            if (IsChargeGun)
            {
                var profile = CannonAmmoState.Profile(Ammo.Id);
                Gun.structureDamage = profile.Structure; Gun.animalDamage = profile.Creature;
            }
            Gun.conditionLossPerShot = 0.2f; Gun.desiredGasTime = IsChargeGun ? 0.5f : ShotInterval;
            if (!Mp.IsRunning || Mp.IsServer || Known)
            {
                Gun.hasMag = IsChargeGun ? Ammo.Loaded || State.Loaded : State.Loaded;
                Gun.roundsInMag = IsChargeGun ? (Ammo.MagazineLoaded ? 1 : 0) : State.Loaded ? Mathf.FloorToInt(State.Energy / EnergyState.ShotCost) : 0;
                Gun.roundInChamber = IsChargeGun && Ammo.Chambered ? (Ammo.Spent ? GunScript.RoundInChamber.Casing : GunScript.RoundInChamber.Round) : GunScript.RoundInChamber.None;
            }
            RefreshSprite();
        }

        private void LateUpdate() => RefreshSprite();
        internal void RefreshSprite()
        {
            if (_renderer == null || Gun == null) return;
            var sprite = IsChargeGun ? Registration.CannonSprite : GunVisualState.Select(!Mp.IsRunning || Mp.IsServer || Known, State.Loaded, Gun.hasMag,
                Registration.GunLoadedSprite, Registration.GunSprite);
            if (sprite == null) return;
            // CCL inventory rendering uses an instance icon override rather than the world renderer alone.
            if (_visibleSprite != sprite && CUCoreUtils.SetItemSprite(Item, sprite)) _visibleSprite = sprite;
            if (_renderer.sprite != sprite) _renderer.sprite = sprite;
        }
    }
}
