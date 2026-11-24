using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using DDEN.Shared;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DDEN.MPfix
{
    /// <summary>
    /// 联机频道小工具：带“发送者”的服务端处理器注册 + 常用反射（Body 解析 / knetid 构造）。
    /// CUCoreLib 的 sender-aware 重载是 internal，这里靠反射拿到；拿不到就退回无 sender 版本（返回 false）。
    /// </summary>
    internal static class MpChannels
    {
        private static readonly Dictionary<string, Delegate> Kept = new Dictionary<string, Delegate>();

        /// <summary>注册带发送者 clientId 的服务端处理器；成功返回 true。</summary>
        internal static bool TryRegisterSender(string channel, Func<uint, JToken, JToken> handler)
        {
            if (!Mp.CoreReady) return false;
            try
            {
                Type bridge = AccessTools.TypeByName("CUCoreLib.Networking.MultiplayerBridge");
                if (bridge == null) return false;

                MethodInfo reg = null;
                foreach (MethodInfo m in bridge.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (m.Name != "RegisterServerHandler") continue;
                    ParameterInfo[] ps = m.GetParameters();
                    if (ps.Length == 2 && ps[0].ParameterType == typeof(string) && ps[1].ParameterType != typeof(Func<JToken, JToken>))
                    {
                        reg = m;
                        break;
                    }
                }
                if (reg == null) return false;

                Type delType = reg.GetParameters()[1].ParameterType;
                Delegate del = Delegate.CreateDelegate(delType, handler.Target, handler.Method, false);
                if (del == null) return false;

                reg.Invoke(null, new object[] { channel, del });
                Kept[channel] = del;      // 保住委托，避免被 GC
                MPfixPlugin.Log.Info($"[频道] 已注册带发送者的 '{channel}'。");
                return true;
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Debug($"注册带发送者频道 '{channel}' 失败：{ex.Message}");
                return false;
            }
        }

        /// <summary>clientId → 该玩家当前的身体。</summary>
        internal static Body BodyForClient(uint clientId)
        {
            try
            {
                Type scav = AccessTools.TypeByName("Together.ScavPlayer");
                if (scav == null) return null;
                MethodInfo get = AccessTools.Method(scav, "GetBodyFromClientId");
                if (get == null) return null;

                Type idType = get.GetParameters()[0].ParameterType;
                object knet = Activator.CreateInstance(idType, (ushort)clientId);
                return get.Invoke(null, new[] { knet }) as Body;
            }
            catch (Exception ex)
            {
                MPfixPlugin.Log.Debug("解析发送者 Body 失败：" + ex.Message);
                return null;
            }
        }

        /// <summary>uint → Together.knetid。</summary>
        internal static object MakeKnet(uint clientId)
        {
            try
            {
                Type t = AccessTools.TypeByName("Together.knetid");
                if (t == null) return null;
                return Activator.CreateInstance(t, (ushort)clientId);
            }
            catch
            {
                return null;
            }
        }
    }
}
