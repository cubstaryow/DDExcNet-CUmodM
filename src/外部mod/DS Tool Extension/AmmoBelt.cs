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
            __result = AmmoBeltRules.IsAllowed(item);
        }
    }

    /// <summary>电池减重：有电池且有电时 0，否则 0.4。</summary>
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

    /// <summary>
    /// 自动装填 + 弹药转移：
    ///  - 每 2 秒检查容器（childCount 未变时直接跳过），把一颗同口径子弹装入未满弹夹；
    ///  - 每 10 秒把玩家其他容器里的三种子弹转移到本子弹带（仅主机/单机）。
    /// </summary>
    public class AmmoBeltAutoReload : MonoBehaviour
    {
        private const float ReloadInterval = 2f;
        private const float TransferInterval = 10f;

        private Container _container;
        private float _nextReload;
        private float _nextTransfer;
        private int _lastCount = -1;

        private Item _item;
        private static Type _netType;
        private static PropertyInfo _isRunning;
        private static PropertyInfo _isClient;

        private void Awake()
        {
            _container = GetComponent<Container>();
            _nextReload = Time.unscaledTime + ReloadInterval;
            _nextTransfer = Time.unscaledTime + TransferInterval;
        }

        /// <summary>只有穿在身上（bandolier 槽）时才执行装填/转移。</summary>
        private bool IsWorn()
        {
            Body body = PlayerCamera.main != null ? PlayerCamera.main.body : null;
            return body != null && _item != null && body.GetWearableBySlotID("bandolier") == _item;
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
            float now = Time.unscaledTime;
            if (now >= _nextReload)
            {
                _nextReload = now + ReloadInterval;
                TryReload();
            }
            if (now >= _nextTransfer)
            {
                _nextTransfer = now + TransferInterval;
                TryTransfer();
            }
        }

        private void TryReload()
        {
            if (_container == null || !IsWorn() || IsRemoteClient()) return;
            int count = _container.transform.childCount;
            if (count < 2 || count == _lastCount) return;   // 内容没变：上一轮已处理过
            _lastCount = count;

            try
            {
                AmmoScript mag = null;
                Transform root = _container.transform;
                for (int i = 0; i < root.childCount; i++)
                {
                    Transform child = root.GetChild(i);
                    if (child == null) continue;
                    Item item = child.GetComponent<Item>();
                    if (item == null) continue;
                    AmmoScript ammo = item.GetComponent<AmmoScript>();
                    if (ammo != null && ammo.itemType == AmmoScript.AmmoItemType.Magazine && ammo.rounds < ammo.maxRounds)
                    {
                        mag = ammo;
                        break;
                    }
                }
                if (mag == null) return;

                for (int i = 0; i < root.childCount; i++)
                {
                    Transform child = root.GetChild(i);
                    if (child == null) continue;
                    Item item = child.GetComponent<Item>();
                    if (item == null) continue;
                    AmmoScript ammo = item.GetComponent<AmmoScript>();
                    if (ammo == null || ammo.itemType != AmmoScript.AmmoItemType.Round) continue;
                    if (ammo.ammoType != mag.ammoType) continue;
                    mag.LoadRound(ammo);   // 装一发即停
                    break;
                }
            }
            catch (Exception ex)
            {
                Plugin.LogWarning("[DS Tool Extension] auto reload failed: " + ex.Message);
            }
        }

        private void TryTransfer()
        {
            if (!IsWorn() || IsRemoteClient()) return;
            try
            {
                Body body = PlayerCamera.main != null ? PlayerCamera.main.body : null;
                if (body == null) return;
                var rules = body.GetComponentsInChildren<AmmoBeltRules>(true);
                if (rules.Length == 0) return;
                Container belt = rules[0].GetComponent<Container>();
                if (belt == null) return;

                Container[] containers = body.GetComponentsInChildren<Container>(true);
                for (int c = 0; c < containers.Length; c++)
                {
                    Container cont = containers[c];
                    if (cont == null || cont == belt) continue;
                    for (int i = cont.transform.childCount - 1; i >= 0; i--)
                    {
                        Transform child = cont.transform.GetChild(i);
                        if (child == null) continue;
                        Item item = child.GetComponent<Item>();
                        if (item == null || !AmmoBeltRules.IsAllowed(item)) continue;
                        AmmoScript ammo = item.GetComponent<AmmoScript>();
                        if (ammo == null || ammo.itemType != AmmoScript.AmmoItemType.Round) continue;
                        if (!belt.CanHoldItem(item)) return;   // 容量满了就停

                        item.transform.SetParent(null, false);
                        item.transform.position = belt.transform.position;
                        item.rb.simulated = true;
                        belt.LoadItem(item);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.LogWarning("[DS Tool Extension] ammo transfer failed: " + ex.Message);
            }
        }
    }
}

