using System;
using System.Collections.Generic;
using DDEN.Shared;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DDEN.LoongNova
{
    internal static class PulseVisual
    {
        private static Material _material;
        private static float _window;
        private static int _count;
        private static FireInfo _capturing;
        private static Vector2 _end;
        private static readonly Stack<LineRenderer> Available = new Stack<LineRenderer>();
        private static readonly List<ActiveBeam> Active = new List<ActiveBeam>(64);
        private struct ActiveBeam { internal LineRenderer Line; internal float Until; }

        internal static void Begin(FireInfo shot)
        {
            _capturing = shot;
            _end = shot.pos;
        }
        internal static Vector2 End()
        {
            _capturing = null;
            return _end;
        }

        // The transpiler passes the originating FireInfo, so nested turret shots stay independent.
        internal static void SetPosition(LineRenderer line, int index, Vector3 point, FireInfo info)
        {
            line.SetPosition(index, point);
            if (!ReferenceEquals(info, _capturing) || index != 1) return;
            _end = point;
            Style(line);
        }

        private static bool Style(LineRenderer line)
        {
            if (_material == null)
            {
                var shader = Shader.Find("Sprites/Default");
                if (shader == null) return false;
                _material = new Material(shader);
            }
            line.sharedMaterial = _material;
            line.startWidth = 0.08f; line.endWidth = 0.045f;
            line.startColor = new Color(1, 0.85f, 0.35f, 1);
            line.endColor = new Color(1, 0.25f, 0.02f, 0.9f);
            line.sortingOrder = 100;
            return true;
        }

        internal static void Play(JToken data, bool drawBeam = true, Item originGun = null)
        {
            if (data == null) return;
            if (data.Value<bool?>("healingFields") == true) { HealingFields.Receive(data); return; }
            if (data["spatial"] != null) { MicroSpaceCrate.Receive(data); return; }
            if (data["injector"] != null) { MedicalInjector.Receive(data); return; }
            if (data.Value<bool?>("breachDoors") == true) { CannonBreach.ReceiveDoors(data); return; }
            if (data.Value<bool?>("chargeAudio") == true) { CannonChargeAudio.Receive(data); return; }
            if (Mp.IsRunning && !Mp.IsServer && data["state"] is JObject state)
            {
                var item = NetworkAccess.Item(data.Value<int>("gun"));
                if (item != null && item.id == Registration.ChargeGunId) DragonGun.For(item).ApplyServerState(state);
            }
            if (data["pellets"] is JArray pellets)
            {
                if (pellets.Count < 1 || pellets.Count > 20) return;
                foreach (var pellet in pellets)
                    if (pellet is JObject && pellet["pellets"] == null) Play(pellet, drawBeam, originGun);
                return;
            }
            float x = data.Value<float>("x"), y = data.Value<float>("y");
            float dx = data.Value<float>("dx"), dy = data.Value<float>("dy");
            if (!Finite(x) || !Finite(y) || !Finite(dx) || !Finite(dy)) return;
            Vector2 start = new Vector2(x, y), direction = new Vector2(dx, dy);
            if (direction.sqrMagnitude < 0.25f || direction.sqrMagnitude > 4) return;
            if (Time.unscaledTime - _window >= 1) { _window = Time.unscaledTime; _count = 0; }
            if (++_count > 256) return;
            direction.Normalize();
            Vector2 end = new Vector2(data.Value<float>("ex"), data.Value<float>("ey"));
            if (!Finite(end.x) || !Finite(end.y) || Vector2.Distance(start, end) > 201) return;
            if (drawBeam && Active.Count < 64)
            {
                LineRenderer line = null;
                while (line == null && Available.Count > 0) line = Available.Pop();
                if (line == null) line = new GameObject("LoongNova pulse").AddComponent<LineRenderer>();
                line.gameObject.SetActive(true);
                if (Style(line))
                {
                    line.positionCount = 2;
                    line.useWorldSpace = true;
                    line.SetPosition(0, start); line.SetPosition(1, end);
                    Active.Add(new ActiveBeam { Line = line, Until = Time.unscaledTime + 0.05f });
                }
                else { line.gameObject.SetActive(false); Available.Push(line); }
            }
            if (data.Value<bool?>("feedback") == false) return;
            bool cannon = originGun != null ? originGun.id == Registration.ChargeGunId : data.Value<bool?>("cannon") == true;
            Sound.Play(cannon ? "rifleshot" : "laser", start, pitchShift: !cannon);
            var body = PlayerCamera.main != null ? PlayerCamera.main.body : null;
            var held = body != null ? body.GetItem(body.handSlot) : null;
            bool ours = held != null && Registration.IsGun(held.id)
                && (originGun != null ? held == originGun : Mp.IsRunning && NetworkAccess.Id(held) == data.Value<int>("gun"));
            if (ours)
            {
                body.rb.velocity -= direction * (cannon ? 6 : 2);
                body.eyeScareTime = 1;
            }
        }
        private static bool Finite(float number) => !float.IsNaN(number) && !float.IsInfinity(number);
        internal static void Tick()
        {
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                var beam = Active[i];
                if (Time.unscaledTime < beam.Until) continue;
                if (beam.Line != null) { beam.Line.gameObject.SetActive(false); Available.Push(beam.Line); }
                Active.RemoveAt(i);
            }
        }
        internal static void Dispose()
        {
            foreach (var beam in Active) if (beam.Line != null) UnityEngine.Object.Destroy(beam.Line.gameObject);
            foreach (var line in Available) if (line != null) UnityEngine.Object.Destroy(line.gameObject);
            Active.Clear(); Available.Clear();
            if (_material != null) UnityEngine.Object.Destroy(_material);
            _material = null;
        }
    }
}
