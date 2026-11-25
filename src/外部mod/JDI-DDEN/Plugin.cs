using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using LiteNetLib;
using LiteNetLib.Utils;
using Together;
using UnityEngine;
using UnityEngine.EventSystems;

namespace JustDragAToolOnIt
{
    [BepInPlugin("com.dden.jdi", "JDI-DDEN", "1.5.2")]
    [BepInDependency("CasualtiesMP", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        internal static Plugin Instance;
        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> AllowClients;

        private const string Channel = "unwey.stonefruit.open";
        private static GameObject _openPrefab;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            Enabled = Config.Bind("General", "Enabled", true, "启用工具撬石果功能。");
            AllowClients = Config.Bind("Multiplayer", "AllowClients", true, "允许客机通过主机完成撬石果（需要主机也装本插件）。");
            try
            {
                Multiplayer.RegisterCustomServerReceiver(Channel, ServerOpen);
            }
            catch (Exception ex)
            {
                Log.LogWarning("[JDI-DDEN] register receiver failed: " + ex.Message);
            }
            new Harmony("com.dden.jdi").PatchAll(typeof(Plugin));
            DdenBridge.DdenDocs();
            Log.LogInfo("[JDI-DDEN] loaded (v1.3, DDEN-aware).");
        }

        [HarmonyPatch(typeof(PlayerCamera), "TryPerformInventoryAction")]
        [HarmonyPriority(900)]
        [HarmonyPrefix]
        private static bool TryPerformInventoryAction_Prefix(PlayerCamera __instance, RaycastResult hit, List<RaycastResult> uiCasts, ref bool __result)
        {
            if (Enabled != null && !Enabled.Value) return true;
            if (__instance == null) return true;
            Item drag = __instance.dragItem;
            if (drag == null || drag.Stats == null || !drag.Stats.HasTag("tool")) return true;
            InvButton button;
            if (!hit.gameObject.TryGetComponent<InvButton>(out button) || button == null || !button.Overlaps(uiCasts)) return true;
            Item target = button.GetItem();
            if (target == null || target.id != "stonefruitclosed") return true;

            if (Net.IsRunning && Net.IsClient)
            {
                if (AllowClients != null && !AllowClients.Value) return true;
                SyncInfo si;
                if (!Util_MPExtensions.TryGetSyncInfo(target, out si) || si == null)
                {
                    Log.LogWarning("[JDI-DDEN] closed stonefruit has no sync id; falling back.");
                    return true;
                }
                try
                {
                    NetDataWriter writer = Multiplayer.CreateNamedWriter(Channel);
                    writer.Put((ushort)si.syncId);
                    Net.Client_Send(DeliveryMethod.ReliableUnordered, writer);
                    __result = true;
                    return false;
                }
                catch (Exception ex)
                {
                    Log.LogWarning("[JDI-DDEN] send request failed: " + ex.Message);
                    return true;
                }
            }

            // 单机 / 主机：本地执行
            if (DoOpen(__instance.body, target)) __result = true;
            return false;
        }

        private static void ServerOpen(ScavPlayer plr, NetDataReader reader)
        {
            try
            {
                if (Enabled != null && !Enabled.Value) return;
                reader.Get(out ushort syncId);
                Item item = null;
                foreach (Item candidate in Item.allItems)
                {
                    if (candidate == null || candidate.id != "stonefruitclosed") continue;
                    SyncInfo si;
                    if (!Util_MPExtensions.TryGetSyncInfo(candidate, out si) || si == null) continue;
                    if ((ushort)si.syncId == syncId) { item = candidate; break; }
                }
                if (item == null) return;
                Body body = plr != null ? plr.body : null;
                if (body == null) return;
                if (DoOpen(body, item)) Log.LogInfo($"[JDI-DDEN] host opened stonefruit for {plr?.name} (syncId={syncId}).");
            }
            catch (Exception ex)
            {
                Log.LogWarning("[JDI-DDEN] ServerOpen failed: " + ex);
            }
        }

        private static bool DoOpen(Body body, Item item)
        {
            if (body == null || item == null) return false;
            body.stamina -= 6f;
            Sound.Play("drop", item.transform.position, false, true, null, 1f, 1f, false, false);
            int slot = body.SlotOf(item);
            item.condition = 0f;
            if (_openPrefab == null) _openPrefab = Resources.Load("stonefruitopen") as GameObject;
            if (_openPrefab == null) return false;
            GameObject go = UnityEngine.Object.Instantiate(_openPrefab, item.transform.position, Quaternion.identity);
            Item open = go.GetComponent<Item>();
            if (open == null) { UnityEngine.Object.Destroy(go); return false; }

            Container cont;
            if (item.TryGetParentContainer(out cont) && cont != null)
            {
                body.DropItem(item);
                cont.LoadItem(open);
            }
            else
            {
                body.DropItem(item);
                body.PickUpItem(open, slot, false);
                if (!body.HoldingItem(open))
                {
                    body.AutoPickUpItem(open);
                }
            }
            return true;
        }
    }

    // DDEN CUmodM 的 L1 接入（零引用，反射约定）。
    public static class DdenBridge
    {
        public static ConfigFile DdenConfig => Plugin.Instance != null ? Plugin.Instance.Config : null;

        public static string DdenTitle => "JDI-DDEN（工具撬石果）";
        private static string _language = "zh-CN";
        public static void DdenSetLanguage(string language)
        {
            _language = string.Equals(language, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "zh-CN";
        }
        public static string DdenLocalize(string original, string language)
        {
            if (string.IsNullOrEmpty(original)) return original;
            if (!string.Equals(language, "en", StringComparison.OrdinalIgnoreCase)) return original;
            return DdenText.En(original);
        }


        public static IDictionary<string, string> DdenConfigMetadata(ConfigEntryBase entry)
        {
            if (entry == null) return null;
            var fields = new Dictionary<string, string>();
            switch (entry.Definition.Key)
            {
                case "Enabled":
                    fields["label"] = "启用";
                    fields["experimental"] = "false";
                    break;
                case "AllowClients":
                    fields["label"] = "客机撬石果";
                    fields["experimental"] = "false";
                    break;
                default:
                    return null;
            }
            return fields;
        }

        public static void DdenDocs()
        {
            System.Reflection.Assembly shared = null;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.GetName().Name == "DDEN.Shared") { shared = a; break; }
            }
            var type = shared?.GetType("DDEN.Shared.DdenDocs");
            var method = type?.GetMethod("Add", new[] { typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(bool) });
            if (method == null || DdenConfig == null) return;
            foreach (var pair in DdenConfig)
            {
                bool clients = pair.Key.Key == "AllowClients";
                method.Invoke(null, new object[]
                {
                    pair.Key.Section, pair.Key.Key,
                    clients ? "允许客机通过主机完成撬石果（需要主机也装本插件）。" : "启用工具撬石果功能。",
                    clients ? "客机无法撬开石头果实。" : "无法用工具撬开石头果实。",
                    clients ? "客机拖拽工具到石头果实上时由主机代为完成。" : "拖拽任意带 tool 标签的工具到石头果实上即可撬开。",
                    "否",
                    "无",
                    false
                });
            }
        }

        public static string DdenSummary =>
            Plugin.Enabled != null && !Plugin.Enabled.Value ? "已禁用" : "运行中";
    }
    internal static class DdenText
    {
        internal static string En(string zh)
        {
            if (zh == null) return null;
            switch (zh)
            {
                case "启用": return "Enable";
                case "客机撬石果": return "Client stonefruit opening";
                case "JDI-DDEN（工具撬石果）": return "JDI-DDEN (Tool-opens-Stonefruit)";
                case "启用工具撬石果功能。": return "Enable the tool-opens-stonefruit feature.";
                case "允许客机通过主机完成撬石果（需要主机也装本插件）。": return "Allow clients to crack stonefruit through the host (host must install this too).";
                case "已禁用": return "Disabled";
                case "运行中": return "Running";
                default: return zh;
            }
        }
    }

}
