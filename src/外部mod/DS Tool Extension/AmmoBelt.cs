using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DsToolExtension
{
    /// <summary>自动装填子弹带：只收三种子弹与手枪/步枪弹夹。</summary>
    public class AmmoBeltRules : MonoBehaviour
    {
        internal static readonly HashSet<string> Allowed = new HashSet<string>
        {
            "9mmround", "556round", "12gauge",   // 三种子弹
            "smallmagazine", "riflemagazine"      // 手枪弹夹 / 步枪弹夹
        };

        internal static bool IsAllowed(Item item)
        {
            return item != null && item.Stats != null && Allowed.Contains(item.id);
        }
    }

    [HarmonyPatch(typeof(Container), "CanHoldItem")]
    internal static class AmmoBeltCanHoldPatch
    {
        private static void Postfix(Container __instance, Item item, ref bool __result)
        {
            if (__instance == null || __result == false) return;
            if (__instance.GetComponent<AmmoBeltRules>() == null) return;
            __result = AmmoBeltRules.IsAllowed(item);   // 容量/重量由原方法判定，这里收紧到白名单
        }
    }

    /// <summary>电池减重：有电池且有电时 0（不计负重），否则 0.4。</summary>
    public class AmmoBeltBattery : MonoBehaviour
    {
        private Item _item;
        private Container _container;
        private float _next;

        private void Awake()
        {
            _item = GetComponent<Item>();
            _container = GetComponent<Container>();
        }

        private void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.5f;
            if (_item == null || _container == null) return;
            float target = (_item.battery != null && _item.battery.hasCharge) ? 0f : 0.4f;
            if (Mathf.Abs(_container.encumberanceMult - target) > 0.0001f)
                _container.encumberanceMult = target;
        }
    }

    /// <summary>自动装填：每 1 秒把容器内一颗子弹装入同口径弹夹；只在主机/单机执行。</summary>
    public class AmmoBeltAutoReload : MonoBehaviour
    {
        private Container _container;
        private float _next;
        private static Type _netType;
        private static PropertyInfo _isRunning;
        private static PropertyInfo _isClient;

        private void Awake()
        {
            _container = GetComponent<Container>();
        }

        private static bool IsRemoteClient()
        {
            try
            {
                if (_netType == null)
                {
                    foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (asm.GetName().Name != "CasualtiesMP") continue;
                        _netType = asm.GetType("Together.Net");
                        break;
                    }
                    if (_netType != null)
                    {
                        _isRunning = _netType.GetProperty("IsRunning", BindingFlags.Public | BindingFlags.Static);
                        _isClient = _netType.GetProperty("IsClient", BindingFlags.Public | BindingFlags.Static);
                    }
                }
                if (_netType == null || _isRunning == null || _isClient == null) return false;
                return (bool)_isRunning.GetValue(null) && (bool)_isClient.GetValue(null);
            }
            catch { return false; }
        }

        private void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1f;
            if (_container == null || _container.itemCount < 2) return;
            if (IsRemoteClient()) return;

            try
            {
                AmmoScript mag = null;
                var rounds = new List<AmmoScript>();
                Transform root = _container.transform;
                for (int i = 0; i < root.childCount; i++)
                {
                    Transform child = root.GetChild(i);
                    if (child == null) continue;
                    Item item = child.GetComponent<Item>();
                    if (item == null) continue;
                    AmmoScript ammo = item.GetComponent<AmmoScript>();
                    if (ammo == null) continue;
                    if (ammo.itemType == AmmoScript.AmmoItemType.Magazine)
                    {
                        if (mag == null && ammo.rounds < ammo.maxRounds) mag = ammo;
                    }
                    else if (ammo.itemType == AmmoScript.AmmoItemType.Round)
                    {
                        rounds.Add(ammo);
                    }
                }
                if (mag == null || rounds.Count == 0) return;
                foreach (AmmoScript round in rounds)
                {
                    if (mag.rounds >= mag.maxRounds) break;
                    if (round == null) continue;
                    if (round.ammoType == mag.ammoType) mag.LoadRound(round);
                }
            }
            catch (Exception ex)
            {
                Plugin.LogWarning("[DS Tool Extension] auto reload failed: " + ex.Message);
            }
        }
    }
}
