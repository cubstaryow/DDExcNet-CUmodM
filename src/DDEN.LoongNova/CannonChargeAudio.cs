using DDEN.Shared;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DDEN.LoongNova
{
    public sealed class CannonChargeAudio : MonoBehaviour
    {
        private static AudioClip _clip;
        private static readonly HashSet<CannonChargeAudio> Instances = new HashSet<CannonChargeAudio>();
        private AudioSource _source;
        private DragonGun _gun;
        private bool _lastSent;
        private long _sequence;
        private object _connection;
        private readonly ChargeAudioLease _remote = new ChargeAudioLease();
        private void Awake() => Instances.Add(this);
        private void Update()
        {
            RefreshConnection();
            if (_gun == null) _gun = GetComponent<DragonGun>();
            var camera = PlayerCamera.main;
            var body = camera != null ? camera.body : null;
            bool authority = !Mp.IsRunning || Mp.IsServer;
            bool active = _gun != null && _gun.IsChargeGun && (authority ? _gun.Charge.Active
                : _remote.Active(Time.unscaledTime));
            if (Mp.IsRunning && Mp.IsServer && Mp.CoreReady && active != _lastSent)
            { if (Broadcast(active)) _lastSent = active; }
            if (!Mp.IsRunning) _lastSent = false;
            active &= camera != null && (!Mp.IsRunning || Mp.CoreReady) && !PauseHandler.paused;
            if (!active) { Stop(); return; }
            bool owner = body != null && body.GetItem(body.handSlot) == _gun.Item;
            float progress = authority ? (float)(_gun.Charge.Elapsed(Time.time) / ChargeCycle.Maximum)
                : _remote.Progress(Time.unscaledTime);
            Vector2 listener = body != null ? (Vector2)body.transform.position : (Vector2)camera.transform.position;
            float volume = ChargeTone.ListenerGain(progress, owner, Vector2.Distance(listener, transform.position));
            if (volume <= 0) { Stop(); return; }
            if (_clip == null)
            {
                var samples = ChargeTone.Samples();
                _clip = AudioClip.Create("LoongNova charge hum", samples.Length, 1, ChargeTone.SampleRate, false);
                _clip.SetData(samples, 0);
            }
            if (_source == null)
            {
                _source = gameObject.AddComponent<AudioSource>();
                _source.playOnAwake = false; _source.loop = true; _source.spatialBlend = 0;
                _source.dopplerLevel = 0; _source.clip = _clip;
            }
            if (WorldGeneration.world != null) _source.outputAudioMixerGroup = WorldGeneration.world.soundMixerGroup;
            // Manual 2D distance attenuation avoids the camera's Z offset affecting world hearing.
            _source.pitch = ChargeTone.Pitch(progress);
            _source.volume = volume;
            if (!_source.isPlaying) _source.Play();
        }
        private void Stop() { if (_source != null && _source.isPlaying) _source.Stop(); }
        private bool Broadcast(bool active)
        {
            if (_gun == null || NetworkAccess.Id(_gun.Item) == 0) return false;
            return Mp.Broadcast(WeaponService.VisualChannel, new JObject { ["chargeAudio"] = true,
                ["gun"] = NetworkAccess.Id(_gun.Item), ["active"] = active,
                ["elapsed"] = active ? _gun.Charge.Elapsed(Time.time) : 0, ["audioRevision"] = ++_sequence });
        }
        internal static void Receive(JToken data)
        {
            if (!Mp.IsRunning || Mp.IsServer || !Mp.CoreReady || data["active"]?.Type != JTokenType.Boolean) return;
            float elapsed = data.Value<float?>("elapsed") ?? float.NaN;
            if (float.IsNaN(elapsed) || float.IsInfinity(elapsed) || elapsed < 0 || elapsed > ChargeCycle.Maximum) return;
            var item = NetworkAccess.Item(data.Value<int>("gun"));
            if (item == null || item.id != Registration.ChargeGunId) return;
            var audio = item.GetComponent<CannonChargeAudio>() ?? item.gameObject.AddComponent<CannonChargeAudio>();
            audio.RefreshConnection();
            if (!audio._remote.Apply(data.Value<long?>("audioRevision") ?? -1, data.Value<bool>("active"), elapsed, Time.unscaledTime)) return;
            if (!audio._remote.Active(Time.unscaledTime)) audio.Stop();
        }
        private void RefreshConnection()
        {
            var connection = LoongNovaPlugin.Service.Connection;
            if (ReferenceEquals(_connection, connection)) return;
            _connection = connection; _lastSent = false; _remote.Reset(); Stop();
        }
        private void OnDisable()
        {
            if (_lastSent && Mp.IsRunning && Mp.IsServer && Mp.CoreReady) Broadcast(false);
            _lastSent = false; _remote.Reset(); Stop();
        }
        private void OnDestroy() { Instances.Remove(this); Stop(); if (_source != null) Destroy(_source); }
        internal static void Dispose()
        {
            foreach (var audio in Instances) if (audio != null) audio.enabled = false;
            if (_clip != null) Destroy(_clip); _clip = null;
        }
    }
}
