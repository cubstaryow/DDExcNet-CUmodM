using System;
using CUCoreLib.Helpers;
using DDEN.Shared;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DDEN.LoongNova
{
    public sealed class MedicalInjector : MonoBehaviour
    {
        internal readonly AmpouleState State = new AmpouleState();
        private readonly PendingActions _pending = new PendingActions();
        internal Item Item { get; private set; }
        internal bool Known;
        private float _nextRead, _nextAction;
        private object _connection;
        private SpriteRenderer _renderer;
        private Sprite _visibleSprite;
        internal static MedicalInjector For(Item item) => item.GetComponent<MedicalInjector>() ?? item.gameObject.AddComponent<MedicalInjector>();
        private void Awake()
        {
            Item = GetComponent<Item>(); Known = !Mp.IsRunning || Mp.IsServer;
            if (Known) State.TryGrantSpawnDose();
            RefreshSprite();
        }
        private void LateUpdate() => RefreshSprite();
        internal void RefreshSprite()
        {
            if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
            var sprite = State.Loaded ? Registration.InjectorLoadedSprite : Registration.InjectorEmptySprite;
            if (sprite != null && _visibleSprite != sprite && CUCoreUtils.SetItemSprite(Item, sprite)) _visibleSprite = sprite;
            if (_renderer != null && sprite != null && _renderer.sprite != sprite) _renderer.sprite = sprite;
        }
        internal static void Unload(Body body, Item item)
        {
            if (item != null && item.id == MedicalCatalog.InjectorId) For(item).Request("eject", body);
        }

        internal string Hint()
        {
            var body = PlayerCamera.main != null ? PlayerCamera.main.body : null;
            if (Mp.IsRunning && !Mp.IsServer && body != null && Time.unscaledTime >= _nextRead) Request("read", body);
            return State.Spent ? Registration.Text("injectorSpent") : State.Ready
                ? string.Format(Registration.Text("injectorLoaded"), Registration.Text(MedicalCatalog.NameKey(State.Id))) : Registration.Text("injectorEmpty");
        }

        internal void Request(string operation, Body body, Item material = null)
        {
            if (Item == null || Item.id != MedicalCatalog.InjectorId || body == null || _pending.Count > 0
                || Time.unscaledTime < _nextAction) return;
            _nextRead = Time.unscaledTime + 2; _nextAction = Time.unscaledTime + 0.15f;
            if (!Mp.IsRunning || Mp.IsServer)
            {
                try { ApplyReply(Execute(operation, material, body)); }
                catch (Exception error) { LoongNovaPlugin.Log.Warn("Medical injector: " + error.GetBaseException().Message); }
                return;
            }
            if (!Mp.CoreReady || !LoongNovaPlugin.Service.Ready || NetworkAccess.Id(Item) == 0) return;
            string request = Guid.NewGuid().ToString("N");
            var connection = NetworkAccess.Connection;
            var packet = new JObject { ["request"] = request, ["operation"] = operation, ["gun"] = NetworkAccess.Id(Item),
                ["cell"] = material != null ? NetworkAccess.Id(material) : 0 };
            _pending.Add(request, operation, Time.unscaledTime);
            bool sent = Mp.RequestServer(WeaponService.Channel, packet, reply =>
            {
                if (this == null || !_pending.Finish(request) || !ReferenceEquals(connection, NetworkAccess.Connection)) return;
                try { if (reply?.Value<string>("request") == request && Mp.IsRunning && !Mp.IsServer) ApplyReply(reply); }
                catch (Exception error) { LoongNovaPlugin.Log.Warn("Medical injector response: " + error.GetBaseException().Message); }
            });
            if (!sent) _pending.Finish(request);
        }
        private void Update()
        {
            object connection = NetworkAccess.Connection;
            if (!ReferenceEquals(connection, _connection)) { _connection = connection; _pending.Clear(); Known = !Mp.IsRunning || Mp.IsServer; _nextRead = 0; }
            if (_pending.Expire(Time.unscaledTime, out _)) _nextRead = Time.unscaledTime + 1;
            if (!Mp.IsRunning || Mp.IsServer || Time.unscaledTime < _nextRead) return;
            _nextRead = Time.unscaledTime + 2;
            var body = PlayerCamera.main != null ? PlayerCamera.main.body : null;
            if (body != null && NetworkAccess.Carries(body, Item)) Request("read", body);
        }
        private void OnDisable() { _pending.Clear(); }
        private JObject Reply(bool ok) => new JObject { ["ok"] = ok, ["state"] = State.Capture() };
        private void ApplyReply(JToken reply) { if (reply?["state"] != null) ApplyState(reply["state"]); }
        internal void ApplyState(JToken payload)
        {
            if ((payload?.Value<long?>("revision") ?? -1) < State.Revision) return;
            if (State.Restore(payload)) { Known = true; RefreshSprite(); }
        }
        internal JObject Execute(string operation, Item material, Body body)
        {
            if (Mp.IsRunning && (!Mp.IsServer || !LoongNovaPlugin.Service.Ready) || body == null || !body.conscious
                || !NetworkAccess.CanAccess(body, Item)) return Reply(false);
            if (operation == "read") return Reply(true);
            if (operation == "load")
            {
                if (State.Loaded || material == null || !MedicalCatalog.IsAmpoule(material.id) || !NetworkAccess.CanAccess(body, material)
                    || !State.Load(material.id, material.condition, material.favourited)) return Reply(false);
                UnityEngine.Object.Destroy(material.gameObject); // Native MP handles the original ground/container slot.
                Sound.Play("gunloadmag", transform.position);
            }
            else if (operation == "eject")
            {
                if (!State.Loaded) return Reply(false);
                var created = CustomInstantiate.InstantiateReturn(State.Id, transform.position, Quaternion.identity, State.Condition);
                var returned = created != null ? created.GetComponent<Item>() : null;
                if (returned == null) { if (created != null) UnityEngine.Object.Destroy(created); return Reply(false); }
                returned.favourited = State.Favourite; State.Clear();
                if (created.GetComponent<FreshItemDrop>() == null) created.AddComponent<FreshItemDrop>();
                try { body.AutoPickUpItem(returned); } catch (Exception error) { LoongNovaPlugin.Log.Warn("Medical ampoule return: " + error.Message); }
            }
            else return Reply(false);
            Known = true; RefreshSprite(); Publish(); return Reply(true);
        }
        private void Publish()
        {
            if (Mp.IsRunning && Mp.IsServer && Mp.CoreReady)
                Mp.Broadcast(WeaponService.VisualChannel, new JObject { ["injector"] = NetworkAccess.Id(Item), ["state"] = State.Capture() });
        }
        internal static void Receive(JToken packet)
        {
            if (!Mp.IsRunning || Mp.IsServer) return;
            var item = NetworkAccess.Item(packet.Value<int>("injector"));
            if (item != null && item.id == MedicalCatalog.InjectorId) For(item).ApplyState(packet["state"]);
        }
        internal static void Inject(Limb limb, Item item)
        {
            // PlayerCamera.ApplyWoundItem and Together's native medical receiver already perform the normal use checks.
            // Clients wait for the native health packet; they must not consume or apply a second dose locally.
            if (item == null || item.id != MedicalCatalog.InjectorId || Mp.IsRunning && !Mp.IsServer) return;
            var injector = For(item);
            if (!MedicalEffects.CanInject(limb, injector.State.Id) || !injector.State.Consume(out string id)) return;
            injector.Known = true;
            try { MedicalEffects.Apply(limb, id); }
            finally { injector.RefreshSprite(); injector.Publish(); }
        }
    }
}
