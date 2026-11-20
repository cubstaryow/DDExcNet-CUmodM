using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DDEN.Shared
{
    /// <summary>小工具：世界 / 玩家 / 面板通用判断。</summary>
    public static class Game
    {
        public static WorldGeneration World => WorldGeneration.world;

        public static bool InWorld
        {
            get
            {
                try
                {
                    WorldGeneration w = WorldGeneration.world;
                    return w != null && w.worldExists;
                }
                catch { return false; }
            }
        }

        public static Body LocalBody
        {
            get
            {
                try { return PlayerCamera.main != null ? PlayerCamera.main.body : null; }
                catch { return null; }
            }
        }

        public static void Alert(string text, bool important = false)
        {
            try
            {
                if (PlayerCamera.main != null) PlayerCamera.main.DoAlert(text, important);
            }
            catch (Exception ex)
            {
                Log.Debug("Alert 失败：" + ex.Message);
            }
        }

        public static void Console(string text)
        {
            try
            {
                ConsoleScript console = ConsoleScript.instance;
                if (console != null)
                {
                    // LogToConsole 是 private instance 方法，走反射，避免和 QoL/CUCoreLib 抢补丁点
                    object ok = Reflect.InvokeInstance(console, "LogToConsole", text);
                    if (ok != null) return;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Console 输出失败：" + ex.Message);
            }
            Debug.Log("[CUmod] " + text);
        }
    }

    /// <summary>反射式读写 Body/Limb 的字段（存档 JSON ↔ 运行时对象）。</summary>
    public static class LiveFields
    {
        private static readonly Dictionary<string, FieldInfo> BodyCache = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
        private static readonly Dictionary<string, FieldInfo> LimbCache = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);

        private static FieldInfo Resolve(Type type, string name, Dictionary<string, FieldInfo> cache)
        {
            if (type == null) return null;
            if (cache.TryGetValue(name, out FieldInfo fi)) return fi;
            fi = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (fi != null) cache[name] = fi;
            return fi;
        }

        public static bool ApplyNumber(object target, Dictionary<string, FieldInfo> cache, string name, double value)
        {
            FieldInfo fi = Resolve(target?.GetType(), name, cache);
            if (fi == null) return false;
            try
            {
                object boxed;
                if (fi.FieldType == typeof(float)) boxed = (float)value;
                else if (fi.FieldType == typeof(int)) boxed = (int)Math.Round(value);
                else if (fi.FieldType == typeof(double)) boxed = value;
                else if (fi.FieldType == typeof(long)) boxed = (long)Math.Round(value);
                else if (fi.FieldType == typeof(short)) boxed = (short)Math.Round(value);
                else if (fi.FieldType == typeof(byte)) boxed = (byte)Math.Max(0, Math.Round(value));
                else return false;

                fi.SetValue(target, boxed);
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug($"写回 {name} 失败：{ex.Message}");
                return false;
            }
        }

        public static bool ApplyBool(object target, Dictionary<string, FieldInfo> cache, string name, bool value)
        {
            FieldInfo fi = Resolve(target?.GetType(), name, cache);
            if (fi == null || fi.FieldType != typeof(bool)) return false;
            try { fi.SetValue(target, value); return true; }
            catch (Exception ex) { Log.Debug($"写回 {name} 失败：{ex.Message}"); return false; }
        }

        public static Dictionary<string, FieldInfo> BodyFields => BodyCache;
        public static Dictionary<string, FieldInfo> LimbFields => LimbCache;
    }
}
