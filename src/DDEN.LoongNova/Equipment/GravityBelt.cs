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
        private bool _boosting, _ejectQueued;
        private int _activityMode, _reportedMode;
        private bool _streaming;
        private readonly BeltActivitySession _activity = new BeltActivitySession();
        private Body _streamOwner;
        private double UnconfirmedLimit => _streaming ? BeltPowerState.FlightWorkRate * 65 : BeltPowerState.MaxUnconfirmedWork;
        private readonly PendingActions _requests = new PendingActions();
        private bool Pending => _requests.Count > 0;
        private float _nextReport, _nextRead, _protectedUntil, _nextActionReport;
        private double _confirmedWork;
        private long _serverRevision = -1;
        private object _connection;
        private Body _lastOwner;
        private Transform _wearParent;
        private Limb _wearLimb;
        private readonly WorkReportBudget _reports = new WorkReportBudget();
        private Container _container;
        private bool _flying, _audioTemplateTried;
        private bool _lastWingReported, _lastWingPublished;
        private float _nextWingPublish;
        private long _wingSequence;
        private readonly WingFlightLease _remoteWings = new WingFlightLease();
        private AudioSource _flightAudio;
        private Talker _warningTalker;
        private readonly LowEnergyNotice _lowEnergyNotice = new LowEnergyNotice();

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
            && (!Mp.IsRunning || Mp.IsServer || Known && State.WorkTotal - _confirmedWork < UnconfirmedLimit - 1e-6);
        internal bool WingsOpen
        {
            get
            {
                var owner = Owner;
                if (owner == null) return false;
                return Local(owner) ? _flying && Powered : _remoteWings.IsFlying(Time.unscaledTime);
            }
        }
        internal long NextWingSequence() => ++_wingSequence;
        internal void ReceiveWingPose(long sequence, bool flying) => _remoteWings.Apply(sequence, flying, Time.unscaledTime);
        internal bool CanBoost(Body body) => Owner == body && Local(body) && Powered
            && (!Mp.IsRunning || Mp.IsServer || State.WorkTotal - _confirmedWork < (_streaming ? UnconfirmedLimit : 2) - 1e-6);
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
            if (Item != null) Item.condition = State.ChargeFraction;
            float multiplier = State.EncumbranceMultiplier;
            if (_container != null && _container.encumberanceMult != multiplier) _container.encumberanceMult = multiplier;
        }

        private void FixedUpdate()
        {
            _flying = false; _activityMode = 0;
            if (Item == null || Item.id != Registration.BeltId) return;
            RefreshEncumbrance();
            var body = Owner;
            if (body != _lastOwner) { _lastOwner = body; _boosting = false; }
            if (!Local(body) || !Powered) return;
            var rb = body.standing || _wearLimb == null ? body.rb : _wearLimb.rb;
            float cap = Mathf.Clamp(body.jumpSpeed * 0.5f, 1, 12);
            if (body.grounded || rb.velocity.y <= 0) _boosting = false;
            bool flying = body.conscious && !DdenHub.AnyPanelVisible && !body.forceWalk && body.currentClimbable == null
                && Input.GetKey(KeyBinds.GetBind("up"));
            bool slowing = !flying && !body.grounded && rb.velocity.y <= -cap + 0.05f;
            if (!flying && !_boosting && !slowing) return;
            double budget = Mp.IsRunning && !Mp.IsServer
                ? Math.Max(0, (_streaming ? UnconfirmedLimit : flying ? BeltPowerState.MaxUnconfirmedWork : 2) - (State.WorkTotal - _confirmedWork)) : double.PositiveInfinity;
            float paidSeconds = (float)State.AdvanceActivity(Time.fixedDeltaTime, flying, budget);
            if (paidSeconds <= 0) return;
            _activityMode = flying ? 16 : 1;
            if (flying) { _flying = true; ApplyFlight(body, paidSeconds); }
            else if (slowing)
            {
                Clamp(body.rb, cap);
                if (!body.standing)
                    foreach (var limb in body.limbs) if (limb != null && !limb.dismembered) Clamp(limb.rb, cap);
                body.lastTimeStepVelocity = new Vector2(body.lastTimeStepVelocity.x, Mathf.Max(body.lastTimeStepVelocity.y, -cap));
                _protectedUntil = Time.time + Time.fixedDeltaTime * 2;
            }
            RefreshEncumbrance();
        }

        private void ApplyFlight(Body body, float seconds)
        {
            // Use the native jetpack's thrust and steering on the owning player's physics only.
            var target = body.standing ? body.rb : _wearLimb.rb;
            Vector2 upward = body.standing ? Vector2.up : (Vector2)_wearLimb.transform.up;
            if (!body.standing)
                target.MoveRotation(Mathf.LerpAngle(_wearLimb.transform.eulerAngles.z, 0, seconds * 15));
            target.AddForce((upward * 198000 + Vector2.right * (60000 * body.moveDir.x)) * seconds);
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
                _connection = connection; _requests.Clear(); _ejectQueued = false;
                _nextRead = 0; _serverRevision = -1; _confirmedWork = 0; _activity.Reset(); _streaming = false; _reportedMode = _activityMode = 0;
                _remoteWings.Reset(); _lastWingReported = _lastWingPublished = false; _nextWingPublish = 0;
                if (Mp.IsRunning && !Mp.IsServer) Known = false;
            }
            if (_requests.Expire(Time.unscaledTime, out _))
            {
                _ejectQueued = false; _reportedMode = -1; _nextActionReport = Time.unscaledTime + 1; _nextRead = Time.unscaledTime + 1; Status = Registration.Text("rejected");
            }
            if (Mp.IsRunning && Mp.IsServer && _activity.Running)
                _activity.Tick(State, Time.time, _streamOwner != null && Owner == _streamOwner && _streamOwner.conscious);
            PublishLocalWingPose();
            UpdateFeedback();
            var body = PlayerCamera.main != null ? PlayerCamera.main.body : null;
            if (_ejectQueued && !Pending) { Request("eject", body); return; }
            if (!Mp.IsRunning || Mp.IsServer || body == null || !Known && Time.unscaledTime < _nextRead) return;
            if (!Pending && (_streaming ? (_activityMode != _reportedMode || State.WorkTotal > _confirmedWork + 1e-7 && Time.unscaledTime >= _nextReport)
                : (State.WorkTotal > _confirmedWork + 1e-7 || _flying != _lastWingReported) && Time.unscaledTime >= _nextReport) && (! _streaming || Time.unscaledTime >= _nextActionReport))
            {
                _nextActionReport = Time.unscaledTime + 0.15f;
                if (Owner == body || NetworkAccess.Carries(body, Item)) Request("work", body);
                else
                {
                    Known = false;
                    _confirmedWork = State.WorkTotal; // Stop retrying an item no longer carried by this player.
                }
            }
            else if (!Pending && (!_streaming || _activityMode == 0) && Owner == body && Time.unscaledTime >= _nextRead)
                Request("read", body);
        }

        internal void Request(string operation, Body body, Item cell = null)
        {
            if (Item == null || body == null) return;
            if (operation == "eject") _ejectQueued = true;
            if (Pending) return;
            if (!Mp.IsRunning || Mp.IsServer)
            {
                try { ApplyResponse(Execute(operation, cell, body, null)); }
                catch (Exception error)
                {
                    Status = Registration.Text("rejected");
                    LoongNovaPlugin.Log.Warn("LoongNova belt action: " + error.GetBaseException().Message);
                }
                finally { _ejectQueued = false; }
                return;
            }
            if (!LoongNovaPlugin.Service.Ready || NetworkAccess.Id(Item) == 0)
            { Status = Registration.Text("unavailable"); return; }
            if (operation == "eject") _ejectQueued = false;
            if ((operation == "read" || operation == "work" && _streaming && _activityMode == _reportedMode)
                && !NetworkReadBudget.TryAcquire(Time.unscaledTime, NetworkAccess.Connection)) return;
            string request = Guid.NewGuid().ToString("N");
            var packet = new JObject
            {
                ["request"] = request, ["operation"] = operation, ["gun"] = NetworkAccess.Id(Item),
                ["cell"] = cell != null ? NetworkAccess.Id(cell) : 0,
                ["mode"] = _activityMode, ["epoch"] = State.Epoch, ["work"] = State.WorkTotal, ["flying"] = _flying
            };
            if (operation == "read") packet["knownRevision"] = Known ? _serverRevision : -1;
            _requests.Add(request, operation, Time.unscaledTime);
            if (operation == "work") { _nextActionReport = Time.unscaledTime + 0.15f; _lastWingReported = _flying; _reportedMode = _activityMode; }
            _nextReport = Time.unscaledTime + (_streaming ? 60 : 0.5f); _nextRead = Time.unscaledTime + 60;
            var connection = NetworkAccess.Connection;
            bool sent = Mp.RequestServer(WeaponService.Channel, packet, response =>
            {
                if (this == null || !_requests.Finish(request)) return;
                if (!ReferenceEquals(connection, NetworkAccess.Connection)) return;
                try
                {
                    if (response?.Value<string>("request") != request)
                    { _ejectQueued = false; _reportedMode = -1; _nextActionReport = Time.unscaledTime + 1; _nextRead = Time.unscaledTime + 1; Status = Registration.Text("rejected"); return; }
                    if (Mp.CoreReady && Mp.IsRunning && !Mp.IsServer) ApplyResponse(response);
                }
                catch (Exception error)
                {
                    _ejectQueued = false; _reportedMode = -1; _nextActionReport = Time.unscaledTime + 1; _nextRead = Time.unscaledTime + 1; Status = Registration.Text("rejected");
                    LoongNovaPlugin.Log.Warn("LoongNova belt response: " + error.GetBaseException().Message);
                }
            });
            if (!sent) { _requests.Finish(request); _ejectQueued = false; _reportedMode = -1; _nextActionReport = Time.unscaledTime + 1; _nextRead = Time.unscaledTime + 1; Status = Registration.Text("unavailable"); }
        }

        private void PublishLocalWingPose()
        {
            if (!Mp.IsRunning || !Mp.IsServer || !LoongNovaPlugin.Service.Ready || !Local(Owner)) return;
            bool open = WingsOpen;
            if (open == _lastWingPublished && (!open || Time.unscaledTime < _nextWingPublish)) return;
            _lastWingPublished = open;
            _nextWingPublish = Time.unscaledTime + 60;
            WingVisualSync.Publish(this, open);
        }
        private void UpdateFeedback()
        {
            var owner = Owner;
            bool localWearer = owner != null && PlayerCamera.main != null && PlayerCamera.main.body == owner;
            bool active = localWearer && _flying && Powered && Time.timeScale > 0;
            if (active && _flightAudio == null && !_audioTemplateTried)
            {
                _audioTemplateTried = true;
                var prefab = Resources.Load<GameObject>("jetpack");
                var original = prefab != null ? prefab.GetComponent<AudioSource>() : null;
                if (original != null && original.clip != null)
                {
                    _flightAudio = gameObject.AddComponent<AudioSource>();
                    _flightAudio.playOnAwake = false;
                    _flightAudio.clip = original.clip;
                    _flightAudio.loop = true;
                    _flightAudio.outputAudioMixerGroup = original.outputAudioMixerGroup;
                    _flightAudio.spatialBlend = original.spatialBlend;
                    _flightAudio.rolloffMode = original.rolloffMode;
                    _flightAudio.minDistance = original.minDistance;
                    _flightAudio.maxDistance = original.maxDistance;
                    _flightAudio.dopplerLevel = original.dopplerLevel;
                    _flightAudio.volume = _flightAudio.pitch = 0;
                }
                else LoongNovaPlugin.Log.Warn("LoongNova: native jetpack audio unavailable");
            }
            if (_flightAudio != null)
            {
                float step = Time.unscaledDeltaTime * (active ? 3 : 5);
                _flightAudio.volume = Mathf.MoveTowards(_flightAudio.volume, active ? 1 : 0, step);
                _flightAudio.pitch = Mathf.MoveTowards(_flightAudio.pitch, active ? 1 : 0, step);
                if (active && !_flightAudio.isPlaying) _flightAudio.Play();
                else if (!active && _flightAudio.volume <= 0 && _flightAudio.isPlaying) _flightAudio.Stop();
            }
            if (localWearer && Time.timeScale > 0 && (!Mp.IsRunning || Mp.IsServer || Known)
                && _lowEnergyNotice.ShouldWarn(State.Epoch, State.Cell.Loaded, State.Remaining, Time.time))
                ShowLowEnergy(owner);
        }

        private void ShowLowEnergy(Body owner)
        {
            var head = owner.limbs != null && owner.limbs.Length > 0 ? owner.limbs[(int)Body.LimbNum.Head] : null;
            if (head == null) return;
            if (_warningTalker == null)
            {
                var watch = Resources.Load<GameObject>("watch");
                var template = watch != null ? watch.GetComponent<Talker>() : null;
                if (template == null || template.textPrefab == null) return;
                var anchor = new GameObject("LoongNova wing warning");
                _warningTalker = anchor.AddComponent<Talker>();
                _warningTalker.textPrefab = template.textPrefab;
                _warningTalker.talkSoundCustom = template.talkSoundCustom;
                _warningTalker.talkTimeMult = template.talkTimeMult;
                _warningTalker.isSoundGlobal = template.isSoundGlobal;
                var textObject = UnityEngine.Object.Instantiate(template.textPrefab, head.transform.position, Quaternion.identity);
                var textType = HarmonyLib.AccessTools.TypeByName("TMPro.TextMeshPro");
                HarmonyLib.AccessTools.Field(typeof(Talker), "text").SetValue(_warningTalker, textObject.GetComponent(textType));
            }
            _warningTalker.transform.SetParent(head.transform, false);
            _warningTalker.transform.localPosition = Vector3.zero;
            // Talker.Start initializes its text on the next frame. Its native offset places text above the head.
            _warningTalker.TalkDelayed(0, Registration.Text("beltLowEnergy"));
        }

        private void OnDestroy()
        {
            if (_warningTalker == null) return;
            var text = Reflect.GetInstance(_warningTalker, "text") as Component;
            UnityEngine.Object.Destroy(_warningTalker.gameObject);
            // Let native Talker.OnDestroy clear the text before removing the separate text object.
            if (text != null) UnityEngine.Object.Destroy(text.gameObject, 0.1f);
        }
        private void OnDisable()
        {
            if (Mp.IsRunning && Mp.IsServer && _activity.Running)
            { _activity.Tick(State, Time.time, false); WingVisualSync.Publish(this, false); }
            _requests.Clear(); _ejectQueued = false;
            _flying = false; _activityMode = 0;
            _remoteWings.Reset();
            if (_flightAudio != null)
            {
                _flightAudio.Stop();
                _flightAudio.volume = _flightAudio.pitch = 0;
            }
        }
        private void ApplyResponse(JToken response)
        {
            if (response?.Value<bool?>("ok") != true) { _reportedMode = -1; _nextActionReport = Time.unscaledTime + 1; }
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
                Known = true; _streaming = response.Value<bool?>("stream") == true;
                RefreshEncumbrance();
            }
            Status = response?.Value<bool?>("ok") == true ? "" : Registration.Text("rejected");
        }

        // The action channel supplies an authenticated body; clients never choose their owner identity.
        internal JObject Execute(string operation, Item cell, Body body, JToken packet)
        {
            if (Item == null || Item.id != Registration.BeltId || body == null
                || Mp.IsRunning && (!Mp.IsServer || !LoongNovaPlugin.Service.Ready)
                || (operation == "load" || operation == "eject" || operation == "read" ? !NetworkAccess.CanAccess(body, Item) : !NetworkAccess.Carries(body, Item))) return Reply(false);
            if (packet != null && (operation == "work" || operation == "eject"))
            {
                if (!(packet["mode"] != null
                    ? _activity.Report(State, _reports, packet.Value<string>("epoch"), packet.Value<double?>("work") ?? double.NaN,
                        operation == "eject" ? 0 : packet.Value<int>("mode"), Time.time)
                    : _reports.Apply(State, packet.Value<string>("epoch"), packet.Value<double?>("work") ?? double.NaN, Time.time)))
                    return Reply(false);
            }
            if (operation == "work") _streamOwner = body;
            if (operation == "read" || operation == "work")
            {
                if (packet != null && operation == "work")
                    WingVisualSync.Publish(this, packet.Value<bool?>("flying") == true && Owner == body
                        && body.conscious && State.Remaining > 0);
                return Reply(true);
            }
            if (!body.conscious) return Reply(false);
            if (operation == "load")
            {
                if (State.Cell.Loaded || cell == null || cell.id != Registration.CellId
                    || !BeltPowerState.Finite(cell.condition) || !NetworkAccess.CanAccess(body, cell)) return Reply(false);
                float energy = Mathf.Clamp01(cell.condition) * EnergyState.Capacity;
                bool favourite = cell.favourited;
                // Match native BatteryItem.LoadBattery without a separate detach/parent-state transaction.
                State.Load(energy, favourite);

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
                catch (Exception error) { LoongNovaPlugin.Log.Warn("LoongNova belt pickup: " + error.Message); }
            }
            else return Reply(false);
            Known = true;
            WingVisualSync.Publish(this, false);
            Sound.Play("batteryinsert", transform.position);
            return Reply(true);
        }

        private JObject Reply(bool ok)
        {
            RefreshEncumbrance();
            return new JObject { ["ok"] = ok, ["stream"] = true, ["state"] = State.Capture() };
        }
    }
}
