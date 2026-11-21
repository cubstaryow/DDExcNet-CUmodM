using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CageFix
{
    internal sealed class CageState : MonoBehaviour
    {
        internal Item Item;
        internal Component Fire, Tracker;
        internal float OriginalMultiplier, WrittenMultiplier;
        internal bool On, Managing, NeedsSync = true;
        internal int Attempts;
        internal int Revision, DeferredRevision;
        internal CageKey? Key;
        internal bool? DeferredRestore;
    }
    internal static class CageRuntime
    {
        internal static readonly List<CageState> States = new List<CageState>();
        private static Type _fireType;
        private static FieldInfo _active;
        private static MethodInfo _setActive;
        internal static void Install(Harmony harmony, Type fire)
        {
            _fireType = fire;
            _active = AccessTools.Field(fire, "active");
            _setActive = AccessTools.Method(fire, "SetActive", new[] { typeof(bool) });
            var awake = AccessTools.Method(fire, "Awake");
            if (fire == null || _active?.FieldType != typeof(bool) || _setActive == null || awake == null) throw new MissingMemberException("加热笼开关接口不匹配");
            harmony.Patch(awake, postfix: new HarmonyMethod(typeof(CageRuntime), nameof(AfterAwake)));
            harmony.Patch(_setActive, postfix: new HarmonyMethod(typeof(CageRuntime), nameof(AfterSetActive)));
            harmony.Patch(AccessTools.Method(typeof(Item), "Start"), postfix: new HarmonyMethod(typeof(CageRuntime), nameof(AfterItemStart))
                { after = new[] { "net.cucorelib" }, priority = Priority.Last });
            harmony.Patch(AccessTools.Method(typeof(RecipeItem), "GetMatchingItem"), prefix: new HarmonyMethod(typeof(CagePatches), nameof(CagePatches.FilterHeatSources)));
            harmony.Patch(AccessTools.Method(typeof(RecipeItem), "DoesUseItemType"), postfix: new HarmonyMethod(typeof(CagePatches), nameof(CagePatches.FilterHeatType)));
        }
        private static void AfterAwake(Component __instance)
        {
            try { Track(__instance.GetComponent<Item>()); }
            catch (Exception ex) { Plugin.Error(ex, "登记加热笼"); }
        }
        private static void AfterItemStart(Item __instance)
        {
            try
            {
                var state = Track(__instance);
                if (state != null) { state.On = (bool)_active.GetValue(state.Fire); Apply(state); }
            }
            catch (Exception ex) { Plugin.Error(ex, "完成加热笼初始化"); }
        }
        private static void AfterSetActive(Component __instance)
        {
            try
            {
                var state = Track(__instance.GetComponent<Item>());
                if (state == null || state.Fire != __instance) return;
                bool previous = state.On;
                state.On = (bool)_active.GetValue(__instance);
                Apply(state);
                if (previous != state.On) { state.Revision++; CageNetwork.Changed(state); }
            }
            catch (Exception ex) { Plugin.Error(ex, "应用加热笼开关"); }
        }
        internal static CageState Track(Item item)
        {
            if (item == null || item.id != CageRules.ItemId || _fireType == null) return null;
            var fire = item.GetComponent(_fireType);
            if (fire == null) return null;
            var state = item.GetComponent<CageState>();
            if (state == null)
            {
                state = item.gameObject.AddComponent<CageState>();
            }
            // Inactive prefab clones can already contain our MonoBehaviour, with nonserialized fields reset.
            if (state.Item != item || state.Fire != fire || !States.Contains(state))
            {
                state.Managing = false; state.Tracker = null; state.Key = null; state.Attempts = 0; state.NeedsSync = true;
                state.Item = item; state.Fire = fire; state.On = (bool)_active.GetValue(fire);
                if (!States.Contains(state)) States.Add(state);
                Apply(state);
            }
            return state;
        }
        internal static bool IsOn(Item item)
        {
            var state = Track(item);
            return state == null || (bool)_active.GetValue(state.Fire);
        }
        internal static void Set(CageState state, bool on)
        {
            if (state == null || state.Item == null || state.Fire == null) return;
            _setActive.Invoke(state.Fire, new object[] { on });
            state.On = on; Apply(state);
        }
        private static void Apply(CageState state)
        {
            if (state.Item == null) return;
            if (Plugin.Active && Plugin.Decay.Value)
            {
                if (!state.Managing) { state.OriginalMultiplier = state.Item.decayMultiplier; state.Managing = true; }
                state.WrittenMultiplier = CageRules.DecayMultiplier(state.On);
                state.Item.decayMultiplier = state.WrittenMultiplier;
            }
            else Release(state);
        }
        private static void Release(CageState state)
        {
            if (state.Managing && state.Item != null && state.Item.decayMultiplier == state.WrittenMultiplier)
                state.Item.decayMultiplier = state.OriginalMultiplier;
            state.Managing = false;
        }
        internal static void ApplySettings()
        {
            foreach (var state in States) if (state != null && state.Item != null)
            { state.On = (bool)_active.GetValue(state.Fire); Apply(state); }
        }
        internal static void Tick()
        {
            for (int i = States.Count - 1; i >= 0; i--)
            {
                var state = States[i];
                if (state == null || state.Item == null || state.Fire == null) { States.RemoveAt(i); continue; }
                if (state.DeferredRestore.HasValue && Plugin.Active && Plugin.Remember.Value)
                { bool on = state.DeferredRestore.Value; state.DeferredRestore = null; if (state.DeferredRevision == state.Revision) Set(state, on); }
                else if (!Plugin.Active || !Plugin.Remember.Value) state.DeferredRestore = null;
            }
        }
        internal static void Clear()
        {
            foreach (var state in States) if (state != null) { Release(state); UnityEngine.Object.Destroy(state); }
            States.Clear();
        }
    }
}
