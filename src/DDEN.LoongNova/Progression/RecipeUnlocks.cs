using System;
using System.Collections.Generic;
using CUCoreLib.Saving;
using DDEN.Shared;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DDEN.LoongNova
{
    // Body provider is included in CCL's player-scoped MP saves. The companion component on
    // ScavPlayer carries knowledge across layer/body replacement without sharing it with peers.
    internal sealed class RecipeKnowledge : MonoBehaviour { internal int Stage; internal bool SupplyCrate; internal int ScanFrame = -1, ObserveFrame = -1; }

    internal static class RecipeUnlocks
    {
        private static float _nextScan, _nextRequest;
        private static long _serial;
        private static Body _local;
        private static int _shown = -1;
        private static object _connection;
        internal static bool MultiplayerReady => RecipeUnlockPatches.Installed;

        private static Component Player(Body body)
        {
            var player = Reflect.GetInstance(NetworkAccess.NetBody(body), "plr") as Component;
            return player != null && ReferenceEquals(Reflect.GetInstance(player, "body"), body) ? player : null;
        }
        internal static bool IsPlayer(Body body) => body != null &&
            (PlayerCamera.main != null && PlayerCamera.main.body == body || Mp.IsRunning && Player(body) != null);
        private static RecipeKnowledge State(Component target) => target.GetComponent<RecipeKnowledge>() ?? target.gameObject.AddComponent<RecipeKnowledge>();
        internal static int Stage(Body body)
        {
            if (body == null) return 0;
            var state = State(body);
            var player = Mp.IsRunning ? Player(body) : null;
            if (player != null)
            {
                var shared = State(player);
                state.Stage = shared.Stage = Math.Max(RecipeUnlockRules.Clamp(state.Stage), RecipeUnlockRules.Clamp(shared.Stage));
                state.SupplyCrate = shared.SupplyCrate = state.SupplyCrate || shared.SupplyCrate;
            }
            return RecipeUnlockRules.Clamp(state.Stage);
        }
        internal static bool HasCrate(Body body) { if (body == null) return false; Stage(body); return State(body).SupplyCrate; }
        internal static void Observe(Body body, List<Item> items)
        {
            if (!IsPlayer(body) || Mp.IsRunning && !Mp.IsServer) return;
            int stage = Stage(body);
            var cached = State(body);
            if (cached.ObserveFrame == Time.frameCount) return;
            cached.ObserveFrame = Time.frameCount;
            if (items == null || stage == 2 && HasCrate(body)) return;
            bool supply = HasCrate(body);
            foreach (var item in items) if (item != null) { stage = RecipeUnlockRules.Acquire(stage, item.id); supply |= item.id == "dden.loongnova.supply-crate"; }
            Grant(body, stage, supply);
        }
        private static void Grant(Body body, int stage, bool supply = false)
        {
            var state = State(body); state.Stage = Math.Max(state.Stage, RecipeUnlockRules.Clamp(stage)); state.SupplyCrate |= supply;
            var player = Mp.IsRunning ? Player(body) : null;
            if (player != null) { var owner = State(player); owner.Stage = Math.Max(owner.Stage, state.Stage); owner.SupplyCrate |= state.SupplyCrate; }
        }
        internal static int Inspect(Body body)
        {
            if (body == null) return 0;
            if (Stage(body) == 2 && HasCrate(body)) return 2;
            var cache = State(body);
            if (cache.ScanFrame == Time.frameCount) return Stage(body);
            cache.ScanFrame = Time.frameCount;
            if (!Mp.IsRunning || Mp.IsServer)
            {
                var items = body.GetAllItemsThorough();
                Observe(body, items);
                int stage = Stage(body); bool supply = HasCrate(body);
                if ((stage < 2 || !supply) && IsPlayer(body))
                {
                    foreach (var item in items)
                    {
                        if (item == null || item.GetComponent<Container>() == null) continue;
                        foreach (var nested in item.GetComponentsInChildren<Item>(true))
                            if (nested != null) { stage = RecipeUnlockRules.Acquire(stage, nested.id); supply |= nested.id == "dden.loongnova.supply-crate"; }
                        if (stage == 2 && supply) break;
                    }
                    Grant(body, stage, supply);
                }
            }
            return Stage(body);
        }
        internal static void Acquired(Body body, Item item)
        {
            if (!IsPlayer(body) || item == null || Stage(body) == 2 && HasCrate(body)) return;
            if (Mp.IsRunning && !Mp.IsServer)
            {
                if (RecipeUnlockRules.Acquire(Stage(body), item.id) > Stage(body)
                    || item.id == "dden.loongnova.supply-crate" || item.GetComponent<Container>() != null)
                    _nextRequest = Math.Min(_nextRequest, Time.unscaledTime + 0.5f);
                return;
            }
            State(body).ScanFrame = State(body).ObserveFrame = -1;
            int stage = RecipeUnlockRules.Acquire(Stage(body), item.id); bool supply = HasCrate(body) || item.id == "dden.loongnova.supply-crate";
            if ((stage < 2 || !supply) && item.GetComponent<Container>() != null)
                foreach (var nested in item.GetComponentsInChildren<Item>(true))
                    if (nested != null) { stage = RecipeUnlockRules.Acquire(stage, nested.id); supply |= nested.id == "dden.loongnova.supply-crate"; }
            Grant(body, stage, supply);
        }
        internal static bool Allows(Recipe recipe, Body body) => recipe == null || RecipeUnlockRules.Required(recipe.result?.id) == 0
            || RecipeUnlockRules.Allows(Inspect(body), recipe.result?.id, HasCrate(body));
        internal static JObject Reply(Body body) => new JObject { ["ok"] = body != null, ["stage"] = Inspect(body), ["supplyCrate"] = HasCrate(body) };
        internal static string LockHint(string resultId, Body body) => resultId == "dden.loongnova.micro-space-supply-crate" && Stage(body) >= 2 && !HasCrate(body) ? "unlockNeedsSupplyCrate" : RecipeUnlockRules.Required(resultId) == 1 ? "unlockNeedsDragon" : "unlockNeedsWrench";
        internal static string CatalogDetail(string resultId, string detail)
        {
            if (Recipes.recipes == null || !Recipes.recipes.Exists(recipe => recipe.result?.id == resultId)) return detail;
            var body = PlayerCamera.main != null ? PlayerCamera.main.body : null;
            return RecipeUnlockRules.Allows(Inspect(body), resultId, HasCrate(body)) ? detail
                : Registration.Text(LockHint(resultId, body));
        }
        internal static void Tick()
        {
            if (Mp.TogetherReady && !RecipeUnlockPatches.Installed) RecipeUnlockPatches.InstallMultiplayer();
            var body = PlayerCamera.main != null ? PlayerCamera.main.body : null;
            var connection = NetworkAccess.Connection;
            if (_local != body || !ReferenceEquals(_connection, connection))
            { _local = body; _connection = connection; _nextScan = _nextRequest = 0; _shown = -1; _serial++; }
            if (body == null) return;
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 1;
                Inspect(body);
            }
            if (Mp.IsRunning && !Mp.IsServer && LoongNovaPlugin.Service.Ready && Time.unscaledTime >= _nextRequest)
            {
                if (!NetworkReadBudget.TryAcquire(Time.unscaledTime, connection)) return;
                _nextRequest = Time.unscaledTime + 60;
                long serial = ++_serial; object session = connection;
                Mp.RequestServer(WeaponService.Channel, new JObject { ["operation"] = "recipes", ["request"] = System.Guid.NewGuid().ToString("N") }, reply =>
                {
                    if (serial != _serial || _local != body || !ReferenceEquals(session, NetworkAccess.Connection)
                        || !Mp.IsRunning || Mp.IsServer || reply?.Value<bool>("ok") != true) return;
                    // Only the authenticated host's reply grants a client knowledge. Never trust a claimed stage.
                    int stage = RecipeUnlockRules.Clamp(reply.Value<int>("stage"));
                    State(body).Stage = stage; State(body).SupplyCrate = reply.Value<bool?>("supplyCrate") == true;
                    var player = Player(body); if (player != null) { State(player).Stage = stage; State(player).SupplyCrate = State(body).SupplyCrate; }
                    Refresh(body);
                });
            }
            Refresh(body);
        }
        private static void Refresh(Body body)
        {
            int stage = Stage(body) * 2 + (HasCrate(body) ? 1 : 0);
            if (_shown == stage) return;
            _shown = stage;
            var camera = PlayerCamera.main;
            if (camera != null && camera.body == body && camera.craftingPanel != null && camera.craftingPanel.activeSelf)
                camera.RefreshRecipeList();
        }
        internal static void ResetView() { _local = null; _connection = null; _shown = -1; _serial++; }

        internal sealed class SaveProvider : IBodySaveProvider
        {
            public int GetVersion() => 1;
            public JToken Capture(Body body) => IsPlayer(body) ? new JObject { ["stage"] = Inspect(body), ["supplyCrate"] = HasCrate(body) } : null;
            public void Restore(Body body, JToken payload, int version, SaveRestoreContext context)
            {
                if (body == null || version != 1 || !(payload is JObject)) return;
                State(body).Stage = RecipeUnlockRules.Clamp(payload.Value<int>("stage"));
                State(body).SupplyCrate = payload.Value<bool?>("supplyCrate") == true;
            }
        }
    }
}
