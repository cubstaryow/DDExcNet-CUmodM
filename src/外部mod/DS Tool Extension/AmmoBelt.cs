using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace DsToolExtension
{
    /// <summary>自动装填子弹带：只收三种子弹与手枪/步枪弹夹。</summary>
    public class AmmoBeltRules : MonoBehaviour
    {
        internal static readonly HashSet<string> Allowed = new HashSet<string>
        {
            "9mmround", "556round", "12gauge",
            "smallmagazine", "riflemagazine"
        };

        internal static bool IsAllowed(Item item)
        {
            return item != null && item.Stats != null && Allowed.Contains(item.id);
        }
    }

    [HarmonyPatch(typeof(Container), "CanHoldItem")]
    internal static class AmmoBeltCanHoldPatch
    {
        // 每个容器只检查一次“是不是子弹带”，之后走弱表缓存，避免 CanHoldItem 高频调用时反复 GetComponent。
        private static readonly ConditionalWeakTable<Container, StrongBox<bool>> Cache =
            new ConditionalWeakTable<Container, StrongBox<bool>>();

        private static bool IsBelt(Container container)
        {
            if (Cache.TryGetValue(container, out StrongBox<bool> box)) return box.Value;
            bool isBelt = container.TryGetComponent<AmmoBeltRules>(out _);
            Cache.Add(container, new StrongBox<bool>(isBelt));
            return isBelt;
        }

        private static void Postfix(Container __instance, Item item, ref bool __result)
        {
            if (__instance == null || __result == false) return;
            if (!IsBelt(__instance)) return;
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
    /// 自动装填 + 弹药转移（仅子弹带穿在身上时）：
    ///  - 每 2 秒把所有未满弹夹装满；
    ///  - 每 10 秒把玩家身上（槽位、穿戴、容器内，含嵌套）的子弹/弹夹转移到子弹带，
    ///    并拆开任意位置的 12 号霰弹盒。
    /// </summary>
    public class AmmoBeltAutoReload : MonoBehaviour
    {
        private const float ReloadInterval = 2f;
        private const float TransferInterval = 10f;
        private const int MaxUnloadPerPass = 30;

        private Container _container;
        private Item _item;
        private float _nextReload;
        private float _nextTransfer;

        // 复用列表，避免每轮转移重新分配。
        private static readonly List<Item> Items = new List<Item>();
        private static readonly HashSet<int> Seen = new HashSet<int>();

        private static Type _netType;
        private static PropertyInfo _isRunning;
        private static PropertyInfo _isClient;

        private void Awake()
        {
            _container = GetComponent<Container>();
            _item = GetComponent<Item>();
            _nextReload = Time.unscaledTime + ReloadInterval;
            _nextTransfer = Time.unscaledTime + TransferInterval;
        }

        private bool IsWorn()
        {
            Transform parent = transform.parent;
            if (parent != null && parent.GetComponent<Limb>() != null) return true;
            Body body = PlayerCamera.main != null ? PlayerCamera.main.body : null;
            if (body == null || _item == null) return false;
            for (int i = 0; i < body.limbs.Length; i++)
            {
                foreach (Transform child in body.limbs[i].transform)
                {
                    if (child != null && child.GetComponent<Item>() == _item) return true;
                }
            }
            return false;
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
            try
            {
                Transform root = _container.transform;
                var mags = new List<AmmoScript>();
                var rounds = new List<AmmoScript>();
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
                        if (ammo.rounds < ammo.maxRounds) mags.Add(ammo);
                    }
                    else if (ammo.itemType == AmmoScript.AmmoItemType.Round)
                    {
                        rounds.Add(ammo);
                    }
                }
                if (mags.Count == 0 || rounds.Count == 0) return;

                int loaded = 0;
                for (int m = 0; m < mags.Count; m++)
                {
                    AmmoScript mag = mags[m];
                    if (mag == null) continue;
                    for (int r = 0; r < rounds.Count && mag.rounds < mag.maxRounds; r++)
                    {
                        AmmoScript round = rounds[r];
                        if (round == null || round.ammoType != mag.ammoType) continue;
                        mag.LoadRound(round);
                        loaded++;
                    }
                }
                if (loaded > 0 && Plugin.DebugLog != null && Plugin.DebugLog.Value)
                    Plugin.LogInfo("[DS Tool Extension] auto reload: loaded " + loaded + " round(s).");
            }
            catch (Exception ex)
            {
                Plugin.LogWarning("[DS Tool Extension] auto reload failed: " + ex.Message);
            }
        }

        private void TryTransfer()
        {
            if (IsRemoteClient()) return;   // 主机/单机统一处理所有玩家（含客机的同步副本）
            try
            {
                // 遍历所有穿戴中的子弹带，按各自的主人处理其容器。
                for (int b = 0; b < Item.allItems.Count; b++)
                {
                    Item candidate = Item.allItems[b];
                    if (candidate == null || candidate.id != Plugin.AutoBeltItemId) continue;
                    Container belt = candidate.GetComponent<Container>();
                    if (belt == null) continue;
                    Body body = candidate.GetComponentInParent<Body>();
                    if (body == null) continue;   // 未穿戴

                    Container[] containers = body.GetComponentsInChildren<Container>(true);
                    FillItems(body, containers);

                    // 1) 拆开任意位置的 12 号霰弹盒
                    int unloaded = 0;
                    for (int i = 0; i < Items.Count && unloaded < MaxUnloadPerPass; i++)
                    {
                        Item owner = Items[i];
                        if (owner == null || owner.id != "boxof12gauge") continue;
                        AmmoScript box = owner.GetComponent<AmmoScript>();
                        if (box == null || box.rounds <= 0) continue;
                        while (box.rounds > 0 && unloaded < MaxUnloadPerPass)
                        {
                            box.UnloadRound();
                            unloaded++;
                        }
                    }

                    // 2) 该玩家槽位/穿戴/容器内的子弹与弹夹
                    int moved = 0;
                    for (int i = 0; i < Items.Count; i++)
                    {
                        Item item = Items[i];
                        if (item == null || item == candidate) continue;
                        if (AmmoBeltRules.IsAllowed(item)) moved += MoveToBelt(belt, item);
                    }

                    if ((moved > 0 || unloaded > 0) && Plugin.DebugLog != null && Plugin.DebugLog.Value)
                        Plugin.LogInfo("[DS Tool Extension] ammo transfer: moved " + moved + ", unloaded " + unloaded + ".");
                }
            }
            catch (Exception ex)
            {
                Plugin.LogWarning("[DS Tool Extension] ammo transfer failed: " + ex.Message);
            }
        }

        private static int MoveToBelt(Container belt, Item item)
        {
            if (belt == null || item == null || !belt.CanHoldItem(item)) return 0;
            item.transform.SetParent(null, false);
            item.transform.position = belt.transform.position;
            item.rb.simulated = true;
            belt.LoadItem(item);
            return 1;
        }

        private static void FillItems(Body body, Container[] containers)
        {
            Items.Clear();
            Seen.Clear();
            for (int i = 0; i < body.slots.Length; i++)
            {
                Item item = body.GetItem(i);
                if (item != null && Seen.Add(item.GetInstanceID())) Items.Add(item);
            }
            for (int i = 0; i < body.limbs.Length; i++)
            {
                foreach (Transform child in body.limbs[i].transform)
                {
                    if (child == null) continue;
                    Item item = child.GetComponent<Item>();
                    if (item != null && Seen.Add(item.GetInstanceID())) Items.Add(item);
                }
            }
            for (int c = 0; c < containers.Length; c++)
            {
                Container cont = containers[c];
                if (cont == null) continue;
                for (int i = 0; i < cont.transform.childCount; i++)
                {
                    Transform child = cont.transform.GetChild(i);
                    if (child == null) continue;
                    Item item = child.GetComponent<Item>();
                    if (item != null && Seen.Add(item.GetInstanceID())) Items.Add(item);
                }
            }
        }
    }
}

