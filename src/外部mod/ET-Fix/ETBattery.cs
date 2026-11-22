using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace HeatingCageFix
{
    /// <summary>
    /// 原 ET-NoBatteryFix 的修复逻辑，并入 ET-Fix：
    ///  - 增强钛砍刀/钛镐没有电池时也能攻击（不改 condition，仍显示 0%）；
    ///  - 装上电池后重新点亮工具灯（只处理本地玩家的物品，避免同步到所有端都跑检测）。
    /// </summary>
    internal static class ETBattery
    {
        private const float RestoreCondition = 0.001f;   // 能点亮的最小耐久
        private const float ReenableWindow = 1.8f;       // 装电池后的校正窗口（秒）
        private const float ReenableStep = 0.3f;         // 窗口内检查间隔
        private const int MaxSteps = 6;

        private static readonly HashSet<string> TargetIds = new HashSet<string>();
        private static readonly HashSet<string> Reported = new HashSet<string>();
        private static readonly HashSet<int> Reenabling = new HashSet<int>();
        private static readonly Dictionary<Type, MethodInfo> SetLightCache = new Dictionary<Type, MethodInfo>();
        private static readonly Dictionary<Type, MethodInfo> EnableLightCache = new Dictionary<Type, MethodInfo>();

        internal static void Install(Harmony harmony)
        {
            try
            {
                RebuildTargets();
                harmony.PatchAll(typeof(ETBattery));
                if (Plugin.Instance != null) Plugin.Instance.Config.SettingChanged += (_, __) => RebuildTargets();
            }
            catch (Exception ex)
            {
                Plugin.Error(ex, "无电池修复安装失败");
            }
        }

        private static void RebuildTargets()
        {
            TargetIds.Clear();
            TargetIds.Add("enhancedmachete");
            TargetIds.Add("enhancedpickaxe");
            var extra = Plugin.ExtraIds;
            if (extra != null && !string.IsNullOrWhiteSpace(extra.Value))
            {
                foreach (var part in extra.Value.Split(','))
                {
                    var id = part.Trim();
                    if (id.Length > 0) TargetIds.Add(id);
                }
            }
        }

        private static bool Active => Plugin.NoBatteryEnabled == null || Plugin.NoBatteryEnabled.Value;

        [HarmonyPatch(typeof(Body), "UseItemInHand")]
        [HarmonyPriority(800)]
        [HarmonyPrefix]
        private static bool UseItemInHand_Prefix(Body __instance)
        {
            if (!Active) return true;
            if (__instance == null || !__instance.conscious) return true;
            int slot = __instance.handSlot;
            if (!__instance.HoldingItem(slot)) return true;
            Item item = __instance.GetItem(slot);
            if (item == null || !TargetIds.Contains(item.id)) return true;
            ItemInfo stats = item.Stats;
            if (stats == null) return true;
            // 每个物品只记一次，避免每次攻击拼字符串
            if (Reported.Add(item.id))
                Plugin.LogInfo($"[ET-Fix] [无电池] {item.id} condition={item.condition:0.###} usable={stats.usable} usableLMB={stats.usableWithLMB} useAction={(object)stats.useAction != null}");
            if ((object)stats.useAction != null && stats.usableWithLMB)
            {
                stats.useAction(__instance, item);
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(BatteryItem), "LoadBattery")]
        [HarmonyPostfix]
        private static void LoadBattery_Postfix(BatteryItem __instance)
        {
            if (!Active) return;
            if (Plugin.ReenableLight != null && !Plugin.ReenableLight.Value) return;
            if (__instance == null) return;
            Item item = __instance.GetComponent<Item>();
            if (item == null || !TargetIds.Contains(item.id)) return;
            if (!__instance.hasBattery) return;

            // 联机时 BatteryItem.LoadBattery 会在主机/各端被同步调用；只处理“本地玩家自己的物品”，
            // 避免一个人装电池、所有端都起身跑点灯检测。
            if (!IsLocalPlayerItem(item)) return;

            // condition 为 0 时 EnhancedTools 的 EnableLight/Update 会拒绝点亮：装电池后先恢复一个可点亮的最小耐久。
            if (item.condition <= 0f)
            {
                item.condition = RestoreCondition;
                Plugin.LogInfo($"[ET-Fix] [无电池] {item.id}: condition restored to {RestoreCondition:0.###} before enabling light.");
            }

            if (Reenable(item))
                Plugin.LogInfo($"[ET-Fix] [无电池] {item.id}: light re-enabled after battery load.");

            // 客机：主机同步可能覆盖本地 lightOn；在短窗口内多次校正，而不是只补一次。
            int id = item.GetInstanceID();
            if (Plugin.Instance != null && Reenabling.Add(id))
                Plugin.Instance.StartCoroutine(ReenableWindowRoutine(item, id));
        }

        /// <summary>物品是否属于本地玩家（手/背包/装备）。单机或无法判断时按 true 处理。</summary>
        private static bool IsLocalPlayerItem(Item item)
        {
            try
            {
                Body local = PlayerCamera.main != null ? PlayerCamera.main.body : null;
                if (local == null) return true;
                Body owner = item.transform.GetComponentInParent<Body>();
                return owner == null || owner == local;
            }
            catch
            {
                return true;
            }
        }

        /// <summary>窗口内每 0.3 秒检查一次（最多 6 次）；只在“有电池且耐久可点亮”时校正，避免强制常亮。</summary>
        private static IEnumerator ReenableWindowRoutine(Item item, int id)
        {
            int applied = 0;
            try
            {
                // 组件只查一次，后续复用；避免每步 GetComponents 分配。
                MonoBehaviour cage = FindLightControl(item);
                if (cage == null) yield break;
                for (int step = 0; step < MaxSteps; step++)
                {
                    yield return new WaitForSecondsRealtime(ReenableStep);
                    if (item == null || item.battery == null || !item.battery.hasBattery) yield break;
                    if (item.condition <= 0f) continue;
                    if (cage == null) { cage = FindLightControl(item); if (cage == null) break; }
                    if (ApplyLightOn(cage)) applied++;
                }
            }
            finally
            {
                Reenabling.Remove(id);
            }
            if (applied > 0)
                Plugin.Debug($"[ET-Fix] [无电池] {item?.id}: light re-enabled in window (x{applied}).");
        }

        private static MonoBehaviour FindLightControl(Item item)
        {
            if (item == null) return null;
            var comps = item.GetComponents<MonoBehaviour>();
            for (int i = 0; i < comps.Length; i++)
            {
                var c = comps[i];
                if (c != null && c.GetType().Name == "CustomLightControl") return c;
            }
            return null;
        }

        /// <summary>找到 CustomLightControl 并点亮；优先绕过 condition 检查的 SetLightState(true)，反射结果按类型缓存。</summary>
        internal static bool Reenable(Item item)
        {
            var cage = FindLightControl(item);
            return cage != null && ApplyLightOn(cage);
        }

        private static bool ApplyLightOn(MonoBehaviour c)
        {
            if (c == null) return false;
            var type = c.GetType();
            if (!SetLightCache.TryGetValue(type, out var set))
            {
                set = type.GetMethod("SetLightState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                SetLightCache[type] = set;
            }
            if (set != null)
            {
                set.Invoke(c, new object[] { true });
                return true;
            }
            if (!EnableLightCache.TryGetValue(type, out var enable))
            {
                enable = type.GetMethod("EnableLight", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                EnableLightCache[type] = enable;
            }
            if (enable != null)
            {
                enable.Invoke(c, null);
                return true;
            }
            return false;
        }
    }
}
