using System;
using System.Collections.Generic;
using System.Reflection;
using DDEN.Shared;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DDEN.UnitListMgr
{
    internal sealed class SpawnService
    {
        private const string Channel = "dden.unitlist.spawn.v1";
        private readonly RequestLedger<JObject> _completed = new RequestLedger<JObject>();
        private readonly Dictionary<uint, float> _next = new Dictionary<uint, float>();
        private Delegate _handler;
        private bool _networkWasRunning;
        private float _nextRegister;
        private string _pending;
        private float _deadline;
        internal string Status = "";
        internal bool Busy => _pending != null;
        internal bool Registered => _handler != null;
        internal bool Enabled = true;
        internal static Body LocalBody => PlayerCamera.main != null ? PlayerCamera.main.body : null;
        private static bool Rule(string name) => Catalog.Read(Reflect.GetStatic("Together.Multiplayer", "rules"), name) is bool flag && flag;
        internal static bool CanGive(bool remote) => SpawnRules.Allowed(Mp.IsRunning, Rule("sv_cheats"), remote, Rule("AllowClientCheatCommands"));

        internal void Tick()
        {
            bool running = Mp.IsRunning;
            if (running != _networkWasRunning)
            {
                _completed.Clear(); _next.Clear(); _pending = null;
                _networkWasRunning = running;
            }
            if (_handler == null && Time.unscaledTime >= _nextRegister)
            {
                _nextRegister = Time.unscaledTime + 5;
                TryRegister();
            }
            if (_pending != null && Time.unscaledTime > _deadline)
            {
                _pending = null;
                Status = UnitListPlugin.T("主机未确认请求；请检查主机是否安装物品工具。为避免重复生成，不会自动重试。", "No host confirmation. Check the host has UnitListMgr. The request will not be retried automatically.");
            }
        }

        private void TryRegister()
        {
            try
            {
                var bridge = Reflect.Type("CUCoreLib.Networking.MultiplayerBridge");
                if (bridge == null) return;
                foreach (var method in bridge.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                {
                    var args = method.GetParameters();
                    if (method.Name != "RegisterServerHandler" || args.Length != 2 || args[0].ParameterType != typeof(string)) continue;
                    var invoke = args[1].ParameterType.GetMethod("Invoke");
                    var parameters = invoke?.GetParameters();
                    if (parameters == null || parameters.Length != 2 || parameters[0].ParameterType != typeof(uint)
                        || parameters[1].ParameterType != typeof(JToken) || invoke.ReturnType != typeof(JToken)) continue;
                    var handler = Delegate.CreateDelegate(args[1].ParameterType, this, GetType().GetMethod(nameof(Receive), BindingFlags.NonPublic | BindingFlags.Instance));
                    method.Invoke(null, new object[] { Channel, handler });
                    _handler = handler; return;
                }
                // No sender-aware bridge: fail closed, never trust an ID supplied in JSON.
            }
            catch (Exception ex) { UnitListPlugin.Log.Debug("Register spawn channel: " + ex.GetBaseException().Message); }
        }

        private JToken Receive(uint sender, JToken payload)
        {
            if (!Enabled || !Mp.IsRunning || !Mp.IsServer) return Error("Host module unavailable");
            try
            {
                if (payload == null || payload.ToString(Newtonsoft.Json.Formatting.None).Length > 8192) return Error("Invalid packet");
                // The bridge supplies sender. The request deliberately has no player identity field.
                var get = Reflect.Type("Together.ScavPlayer")?.GetMethod("GetBodyFromClientId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (get == null || sender > ushort.MaxValue) return Error("No authenticated player");
                object id = Activator.CreateInstance(get.GetParameters()[0].ParameterType, (ushort)sender);
                var body = get.Invoke(null, new[] { id }) as Body;
                return Execute(payload.ToObject<SpawnRequest>(), body, true, sender);
            }
            catch (Exception ex) { UnitListPlugin.Log.Exception(ex, "Receive item request"); return Error("Invalid request"); }
        }

        internal void Submit(SpawnRequest request)
        {
            if (!Enabled || Busy) return;
            string problem = SpawnRules.Validate(request);
            if (problem != null) { Status = problem; return; }
            if (!CanGive(Mp.IsRunning && !Mp.IsServer))
            {
                Status = UnitListPlugin.T("需要主机允许作弊；客机还需要“允许客机作弊”。", "Enable host cheats; guests also require Allow client cheats."); return;
            }
            if (Mp.IsRunning && !Mp.IsServer)
            {
                if (!Registered || !Mp.BridgeReady) { Status = UnitListPlugin.T("当前 CCL 联机桥不支持安全的物品请求。", "A sender-aware CCL bridge is required."); return; }
                string pending = _pending = request.RequestId; _deadline = Time.unscaledTime + 8;
                if (!Mp.RequestServer(Channel, request, result =>
                {
                    if (_pending != pending) return;
                    _pending = null; SetResult(result);
                })) { _pending = null; Status = UnitListPlugin.T("请求未发送。", "Request was not sent."); }
                else if (_pending != null) Status = UnitListPlugin.T("等待主机确认…", "Waiting for host…");
            }
            else SetResult(Execute(request, LocalBody, false, 0));
        }
        private void SetResult(JToken response)
        {
            if (response?["ok"]?.Value<bool>() == true)
                Status = UnitListPlugin.T("已生成 ", "Created ") + (response["count"]?.ToString() ?? "0");
            else Status = UnitListPlugin.T("生成未完成：", "Creation incomplete: ") + (response?["error"]?.ToString() ?? "No response")
                + (response?["count"]?.Value<int>() > 0 ? " (" + response["count"] + ")" : "");
        }
        private static JObject Error(string text, int count = 0) => new JObject { ["ok"] = false, ["error"] = text, ["count"] = count };

        private JObject Execute(SpawnRequest r, Body body, bool remote, uint sender)
        {
            string invalid = SpawnRules.Validate(r);
            if (invalid != null) return Error(invalid);
            string key = sender + ":" + r.RequestId;
            if (_completed.TryGet(key, out var previous)) return previous;
            if (!CanGive(remote)) return Error("Host cheats / client cheats disabled");
            var world = WorldGeneration.world;
            if (body == null || !body.alive || world == null || !world.worldExists || world.generatingWorld
                || Catalog.Read(world, "doingRegen") is bool regenerating && regenerating)
                return Error("Player/world not ready");
            if (_next.TryGetValue(sender, out float next) && Time.unscaledTime < next) return Error("Please wait before generating more items");
            _next[sender] = Time.unscaledTime + 0.5f;
            Vector2 at = Feet(body);
            if (r.AtCursor)
            {
                at = new Vector2(r.X, r.Y);
                // Also bounds native Item.Start's player-proximity network registration path.
                if ((at - (Vector2)body.transform.position).sqrMagnitude > 60 * 60) return Error("Cursor must be within 60 m of your player");
            }
            int made = 0;
            JObject result;
            try
            {
                var spec = Catalog.Inspect(r.ItemId);
                invalid = CheckProperties(r, spec);
                if (invalid != null) return Error(invalid);
                // Record before side effects. Replayed bridge requests cannot create a second copy.
                Remember(key, Error("Request already started"));
                for (int i = 0; i < r.Count; i++)
                {
                    var obj = Create(r.ItemId, at + new Vector2(i * 0.06f, 0));
                    if (obj == null) throw new InvalidOperationException("Item factory returned no object");
                    var item = obj.GetComponent<Item>();
                    if (item == null) { UnityEngine.Object.Destroy(obj); throw new InvalidOperationException("Template has no Item"); }
                    made++;
                    Apply(item, r);
                }
                result = new JObject { ["ok"] = true, ["count"] = made };
            }
            catch (Exception ex)
            {
                UnitListPlugin.Log.Exception(ex, "Create item " + r.ItemId);
                result = Error(ex.GetBaseException().Message, made);
            }
            Remember(key, result);
            return result;
        }
        private void Remember(string key, JObject result)
        {
            _completed.Put(key, result, Time.unscaledTime);
        }
        private static Vector2 Feet(Body body)
        {
            Vector2 at = body.transform.position;
            float lowest = at.y;
            if (body.legLimbs != null)
                foreach (var limb in body.legLimbs)
                    if (limb != null)
                    {
                        var col = limb.GetComponent<Collider2D>();
                        lowest = Mathf.Min(lowest, col != null ? col.bounds.min.y : limb.transform.position.y);
                    }
            return new Vector2(at.x, lowest + 0.25f);
        }
        private static string CheckProperties(SpawnRequest r, ItemDefaults s)
        {
            if (r.Rounds.HasValue && (!s.MaxRounds.HasValue || r.Rounds > s.MaxRounds)) return "Ammunition exceeds capacity";
            if (r.HasMagazine.HasValue && !s.HasGun) return "Item is not a gun";
            if (s.UsesMagazine && r.Rounds > 0 && r.HasMagazine != true) return "No magazine is installed";
            if (r.Battery != null)
            {
                if (!s.HasBattery) return "Item has no battery slot";
                if (r.Battery.Length != 0 && (!Item.GlobalItems.TryGetValue(r.Battery, out var info)
                    || !(info is BatteryInfo battery) || battery.maxCharge > s.BatteryLimit)) return "Incompatible battery";
            }
            if (r.Liquids != null)
            {
                if (s.Capacity <= 0) return "Item has no liquid capacity";
                float amount = 0;
                foreach (var liquid in r.Liquids)
                {
                    if (!global::Liquids.Registry.ContainsKey(liquid.Id)) return "Unknown liquid";
                    amount += liquid.Amount;
                }
                if (!SpawnRules.Finite(amount) || amount > s.Capacity + 0.0001f) return "Liquid exceeds capacity";
                if (r.Condition.HasValue) return "Liquid quantity determines container condition";
            }
            return null;
        }
        private static GameObject Create(string id, Vector2 at)
        {
            GameObject obj;
            if (Resources.Load<GameObject>(id) != null) obj = Utils.Create(id, at, 0);
            else
            {
                var factory = Reflect.Type("CUCoreLib.Helpers.CustomInstantiate")?.GetMethod("InstantiateReturn", new[] { typeof(string), typeof(Vector3), typeof(Quaternion), typeof(float?) });
                if (factory == null) throw new InvalidOperationException("CCL item factory is unavailable");
                obj = factory.Invoke(null, new object[] { id, (Vector3)at, Quaternion.identity, null }) as GameObject;
            }
            // Consume CCL's pending battery initialization BEFORE applying user overrides and
            // BEFORE native Item.Start registers the initial network state. Never spawn twice.
            var item = obj != null ? obj.GetComponent<Item>() : null;
            var initialize = Reflect.Type("CUCoreLib.Patches.ItemRegistryPatches")?.GetMethod("ApplyCustomItemRuntime", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (item != null && initialize != null) initialize.Invoke(null, new object[] { item, false, false });
            return obj;
        }
        private static void Apply(Item item, SpawnRequest r)
        {
            var ammo = item.GetComponent<AmmoScript>();
            if (ammo != null) ammo.rounds = r.Rounds ?? ammo.maxRounds;
            var gun = item.GetComponent<GunScript>();
            if (gun != null)
            {
                gun.hasMag = r.HasMagazine ?? false;
                gun.roundsInMag = r.Rounds ?? 0;
                gun.roundInChamber = GunScript.RoundInChamber.None;
            }
            var battery = item.GetComponent<BatteryItem>();
            if (r.Battery != null && battery != null)
            {
                battery.batteryType = r.Battery;
                battery.maxCharge = r.Battery.Length == 0 ? 0 : ((BatteryInfo)Item.GlobalItems[r.Battery]).maxCharge;
                if (r.Battery.Length == 0) item.condition = 0;
            }
            if (r.Liquids != null)
            {
                var container = item.GetComponent<WaterContainerItem>();
                if (container == null) throw new InvalidOperationException("Liquid component was not initialized");
                container.stack = new List<LiquidStack>();
                foreach (var liquid in r.Liquids) container.stack.Add(new LiquidStack(liquid.Id, liquid.Amount));
                container.UpdateCondition();
            }
            if (r.Condition.HasValue && (battery == null || battery.hasBattery)) item.condition = r.Condition.Value;
        }
    }
}
