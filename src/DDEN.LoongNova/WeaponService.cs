using System;
using System.Collections.Generic;
using System.Reflection;
using CUCoreLib.Helpers;
using DDEN.Shared;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DDEN.LoongNova
{
    internal sealed class WeaponService
    {
        internal const string Channel = "dden.loongnova.action.v1";
        internal const string VisualChannel = "dden.loongnova.pulse.v1";
        private Delegate _handler;
        private float _retryAt;
        private object _connection;
        internal object Connection => _connection;
        private readonly Dictionary<uint, PeerRequests> _peers = new Dictionary<uint, PeerRequests>();
        internal bool Ready => Mp.CoreReady && _handler != null && MultiplayerCombat.Ready;

        private sealed class PeerRequests
        {
            internal readonly Dictionary<string, JObject> Completed = new Dictionary<string, JObject>();
            internal readonly Queue<string> Order = new Queue<string>();
            internal float Window;
            internal int Count;
        }

        internal void Tick()
        {
            object connection = NetworkAccess.Connection;
            if (!ReferenceEquals(_connection, connection)) { _connection = connection; _peers.Clear(); }
            if (!Mp.CoreReady || _handler != null || Time.unscaledTime < _retryAt) return;
            _retryAt = Time.unscaledTime + 2;
            try
            {
                var bridge = Reflect.Type("CUCoreLib.Networking.MultiplayerBridge");
                foreach (var method in bridge.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                {
                    var parameters = method.GetParameters();
                    if (method.Name != "RegisterServerHandler" || parameters.Length != 2 || parameters[0].ParameterType != typeof(string)) continue;
                    var signature = parameters[1].ParameterType.GetMethod("Invoke");
                    var args = signature?.GetParameters();
                    if (args == null || args.Length != 2 || args[0].ParameterType != typeof(uint)
                        || args[1].ParameterType != typeof(JToken) || signature.ReturnType != typeof(JToken)) continue;
                    var receiver = Delegate.CreateDelegate(parameters[1].ParameterType, this,
                        GetType().GetMethod(nameof(Receive), BindingFlags.NonPublic | BindingFlags.Instance));
                    method.Invoke(null, new object[] { Channel, receiver });
                    if (!Mp.RegisterClientHandler(VisualChannel, token =>
                    {
                        if (Mp.CoreReady && Mp.IsRunning && !Mp.IsServer) PulseVisual.Play(token);
                    })) return;
                    if (!Mp.RegisterClientHandler(WingVisualSync.Channel, WingVisualSync.Receive)) return;
                    if (!Mp.RegisterSyncModule("dden.loongnova.breach-doors.v1", CannonBreach.CaptureDoors, data => CannonBreach.ReceiveDoors(data))) return;
                    if (!Mp.RegisterSyncModule("dden.loongnova.healing-fields.v1", HealingFields.Capture, data => HealingFields.Receive(data, true))) return;
                    MultiplayerCombat.InstallGuards();
                    _handler = receiver;
                    break;
                }
            }
            catch (Exception error) { LoongNovaPlugin.Log.Warn("LoongNova bridge: " + error.GetBaseException().Message); }
        }

        private JToken Receive(uint sender, JToken packet)
        {
            if (!Ready || !Mp.IsRunning || !Mp.IsServer || NetworkAccess.Connection == null) return new JObject { ["ok"] = false };
            string request = null;
            DragonGun gun = null;
            PeerRequests peer = null;
            try
            {
                if (!(packet is JObject) || packet.ToString(Newtonsoft.Json.Formatting.None).Length > 1024) return new JObject { ["ok"] = false };
                request = packet.Value<string>("request");
                if (!System.Guid.TryParseExact(request, "N", out _)) return new JObject { ["ok"] = false };
                if (!_peers.TryGetValue(sender, out peer))
                {
                    if (_peers.Count >= 64) return new JObject { ["ok"] = false, ["request"] = request };
                    _peers.Add(sender, peer = new PeerRequests());
                }
                if (peer.Completed.TryGetValue(request, out var prior)) return prior;
                if (Time.unscaledTime - peer.Window >= 1) { peer.Window = Time.unscaledTime; peer.Count = 0; }
                if (++peer.Count > 24) return new JObject { ["ok"] = false, ["request"] = request };
                var item = NetworkAccess.Item(packet.Value<int>("gun"));
                if (item == null || !Registration.IsGun(item.id) && item.id != Registration.BeltId && item.id != MedicalCatalog.InjectorId)
                    return new JObject { ["ok"] = false, ["request"] = request };
                if (item.id == MedicalCatalog.InjectorId)
                {
                    var reply = MedicalInjector.For(item).Execute(packet.Value<string>("operation"),
                        NetworkAccess.Item(packet.Value<int>("cell")), NetworkAccess.Body(sender));
                    reply["request"] = request; Remember(peer, request, reply); return reply;
                }
                if (item.id == Registration.BeltId)
                {
                    var reply = GravityBelt.For(item).Execute(packet.Value<string>("operation"),
                        NetworkAccess.Item(packet.Value<int>("cell")), NetworkAccess.Body(sender), packet);
                    reply["request"] = request;
                    Remember(peer, request, reply);
                    return reply;
                }
                gun = DragonGun.For(item);
                var body = NetworkAccess.Body(sender);
                var result = Execute(packet.Value<string>("operation"), gun, NetworkAccess.Item(packet.Value<int>("cell")), body,
                    new Vector2(packet.Value<float>("dx"), packet.Value<float>("dy")));
                result["request"] = request;
                Remember(peer, request, result);
                return result;
            }
            catch (Exception error)
            {
                LoongNovaPlugin.Log.Warn("LoongNova request: " + error.GetBaseException().Message);
                var result = Reply(gun, false);
                result["request"] = request;
                if (peer != null && request != null) Remember(peer, request, result);
                return result;
            }
        }

        internal JObject Execute(string operation, DragonGun gun, Item cell, Body body, Vector2? direction = null)
        {
            if (gun == null || gun.Item == null || !Registration.IsGun(gun.Item.id)
                || Mp.IsRunning && (!Mp.IsServer || !Ready) || body == null || !body.conscious
                || (operation == "load" || operation == "eject" || operation == "read" ? !NetworkAccess.CanAccess(body, gun.Item) : !NetworkAccess.Carries(body, gun.Item))) return Reply(gun, false);
            if (gun.IsChargeGun) return CannonService.Execute(operation, gun, cell, body, direction);
            if (operation == "read") return Reply(gun, true);
            if (operation == "cancel") { gun.Charge.Cancel(); return Reply(gun, true); }
            if (operation == "load" || operation == "eject") gun.Charge.Cancel();
            if (operation == "load")
            {
                if (gun.State.Loaded || cell == null || cell.id != Registration.CellId || !NetworkAccess.CanAccess(body, cell)) return Reply(gun, false);
                if (!Finite(cell.condition)) return Reply(gun, false);
                float energy = Mathf.Clamp01(cell.condition) * EnergyState.Capacity;
                bool favourite = cell.favourited;
                // Match native BatteryItem.LoadBattery: store charge, then consume the item in place.
                gun.State.Set(true, energy, favourite);

                UnityEngine.Object.Destroy(cell.gameObject);
                Sound.Play("gunloadmag", gun.transform.position);
            }
            else if (operation == "eject")
            {
                if (!gun.State.Loaded) return Reply(gun, false);
                var created = CustomInstantiate.InstantiateReturn(Registration.CellId, gun.transform.position, Quaternion.identity,
                    gun.State.Energy / EnergyState.Capacity);
                var result = created != null ? created.GetComponent<Item>() : null;
                if (result == null) { if (created != null) UnityEngine.Object.Destroy(created); return Reply(gun, false); }
                result.favourited = gun.State.Favourite;
                gun.State.Set(false, 0, false);
                gun.Normalize();
                // The loose item is already committed even if inventory pickup cannot complete.
                try { body.AutoPickUpItem(result); }
                catch (Exception error) { LoongNovaPlugin.Log.Warn("LoongNova pickup: " + error.Message); }
                Sound.Play("gununloadmag", gun.transform.position);
            }
            else if (operation == "shoot")
            {
                if (body.GetItem(body.handSlot) != gun.Item || gun.Gun.safe || gun.Item.condition <= 0
                    || Time.unscaledTime < gun.NextShot) return Reply(gun, false);
                Vector2 aim = direction ?? (Vector2)(gun.Gun.transform.right * (body.isRight ? 1 : -1));
                if (!Finite(aim.x) || !Finite(aim.y) || aim.sqrMagnitude < 0.25f || aim.sqrMagnitude > 4) return Reply(gun, false);
                aim.Normalize();
                Vector2 origin = gun.Gun.barrel.position;
                if (Vector2.Distance(origin, body.transform.position) > 6) return Reply(gun, false);
                if (Mp.IsRunning && NetworkAccess.NetBody(body) == null) return Reply(gun, false);
                if (!gun.State.TrySpendShot()) return Reply(gun, false);
                gun.NextShot = Time.unscaledTime + DragonGun.ShotInterval;
                gun.Item.condition = Mathf.Max(0, gun.Item.condition - 0.002f);
                if (gun.Item.condition < 0.00001f) gun.Item.condition = 0;
                gun.Normalize();
                var fire = new FireInfo { pos = origin, dir = aim, structureDamage = 625, animalDamage = 225, doTinnitus = false, ignoreBody = !Mp.IsRunning };
                PulseVisual.Begin(fire);
                Vector2 end;
                try
                {
                    if (Mp.IsRunning) MultiplayerCombat.Fire(gun, body, fire);
                    else TurretScript.Shoot(fire);
                }
                finally { end = PulseVisual.End(); }
                var visual = new JObject { ["gun"] = NetworkAccess.Id(gun.Item), ["x"] = origin.x, ["y"] = origin.y,
                    ["dx"] = aim.x, ["dy"] = aim.y, ["ex"] = end.x, ["ey"] = end.y };
                PulseVisual.Play(visual, false, gun.Item);
                if (Mp.IsRunning) Mp.Broadcast(VisualChannel, visual);
            }
            else return Reply(gun, false);
            gun.Known = true;
            gun.Normalize();
            return Reply(gun, true);
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static void Remember(PeerRequests peer, string request, JObject result)
        {
            if (peer.Completed.ContainsKey(request)) return;
            peer.Completed.Add(request, result);
            peer.Order.Enqueue(request);
            if (peer.Order.Count > 128) peer.Completed.Remove(peer.Order.Dequeue());
        }
        private static JObject Reply(DragonGun gun, bool ok) => new JObject { ["ok"] = ok, ["state"] = gun?.CaptureState(), ["chargeElapsed"] = gun?.Charge.Elapsed(Time.time) ?? 0 };
    }
}
