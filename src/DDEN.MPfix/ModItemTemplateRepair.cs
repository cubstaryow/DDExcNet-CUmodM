using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DDEN.Shared;
using HarmonyLib;
using UnityEngine;

namespace DDEN.MPfix
{
    /// <summary>Evict only dead CCL templates before construction; never retry a spawned item or protect live templates.</summary>
    internal static class ModItemTemplateRepair
    {
        private static FieldInfo _templates, _registered;
        private static MethodInfo _clear;
        private static readonly Dictionary<string, float> RetryAt = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        [ThreadStatic] private static bool _repairing;

        internal static bool Install(Harmony harmony)
        {
            try
            {
                var factory = Reflect.Type("CUCoreLib.Helpers.CustomInstantiate");
                var registry = Reflect.Type("CUCoreLib.Registries.ItemRegistry");
                var target = AccessTools.Method(factory, "GetOrCreateTemplate", new[] { typeof(string) });
                _templates = AccessTools.Field(factory, "_templateCache");
                _registered = AccessTools.Field(registry, "RegisteredItems");
                _clear = AccessTools.Method(factory, "ClearTemplateCache", new[] { typeof(string) });
                if (target == null || target.ReturnType != typeof(GameObject) || _clear == null
                    || _templates == null || _registered == null
                    || !typeof(IDictionary).IsAssignableFrom(_templates.FieldType)
                    || !typeof(IDictionary).IsAssignableFrom(_registered.FieldType)) return false;
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(ModItemTemplateRepair), nameof(Before)),
                    finalizer: new HarmonyMethod(typeof(ModItemTemplateRepair), nameof(Finish)));
                return true;
            }
            catch (Exception ex) { MPfixPlugin.Log.Exception(ex, "安装物品模板重建"); return false; }
        }

        internal static void Before(string id, out bool __state)
        {
            __state = false;
            if (_repairing || !MPfixPlugin.Active || !MPfixPlugin.CfgFixModItemCraftSpawn.Value || !Mp.IsRunning
                || string.IsNullOrWhiteSpace(id)) return;
            id = id.Trim();
            try
            {
                if (!(_templates?.GetValue(null) is IDictionary templates) || !templates.Contains(id)) return;
                object cached = templates[id];
                // A Unity-destroyed object still exists as a managed reference in the dictionary.
                if (cached != null && !(cached is GameObject)) return;
                if (cached is GameObject live && live != null) return;
                if (!(_registered?.GetValue(null) is IDictionary registered) || registered[id] == null) return;
                float now = Time.unscaledTime;
                if (RetryAt.TryGetValue(id, out float next) && now < next) return;
                if (RetryAt.Count >= 256) RetryAt.Clear();
                RetryAt[id] = now + 10f;
                _repairing = true;
                _clear.Invoke(null, new object[] { id });
                __state = true;
                // Let this same CCL call create and validate the replacement template.
                // No actual item is instantiated by this patch, on either host or client.
            }
            catch (Exception ex) { MPfixPlugin.Log.Warn("[CraftFix] 无法清除失效模板 '" + id + "'：" + ex.GetBaseException().Message); }
            finally { _repairing = false; }
        }

        internal static Exception Finish(string id, bool __state, GameObject __result, Exception __exception)
        {
            if (__state)
            {
                try
                {
                    if (__exception == null && __result != null && __result.GetComponent<Item>() != null)
                        MPfixPlugin.Log.Info("[CraftFix] 已重建失效物品模板 '" + id + "'。");
                    else
                        MPfixPlugin.Log.Warn("[CraftFix] 物品模板重建失败 '" + id + "'："
                            + (__exception?.GetBaseException().Message ?? "CCL 未返回有效物品模板"));
                }
                catch { /* Diagnostics cannot change the original construction outcome. */ }
            }
            return __exception;
        }
    }
}
