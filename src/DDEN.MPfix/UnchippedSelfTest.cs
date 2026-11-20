using System;
using System.Collections.Generic;
using DDEN.Shared;

namespace DDEN.MPfix
{
    /// <summary>-cumodtest 时跑一遍：确认 CUCoreLib 桥、Together 反射面、本地写入通道都真的可用。</summary>
    internal static class UnchippedSelfTest
    {
        public static bool Requested
        {
            get
            {
                try
                {
                    string marker = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "cumod.selftest");
                    if (System.IO.File.Exists(marker)) return true;
                    foreach (string arg in Environment.GetCommandLineArgs())
                    {
                        if (string.Equals(arg, "-cumodtest", StringComparison.OrdinalIgnoreCase)) return true;
                    }
                }
                catch { /* ignored */ }
                return false;
            }
        }

        public static void Run(LogChannel log)
        {
            var r = new List<string>();
            bool pass = true;
            try
            {
                r.Add($"CUCoreLib 联机桥可用 = {Mp.CoreAvailable}");
                r.Add($"联机运行中 = {Mp.IsRunning}（host={Mp.IsServer}, client={Mp.IsClient}）");

                Type lockType = Reflect.Type("Together.WorldMP+RunUnchippedLock");
                r.Add($"RunUnchippedLock 反射面 = {(lockType != null ? "OK" : "MISSING")}");
                if (lockType == null) pass = false;

                Type mpType = Reflect.Type("Together.Multiplayer");
                r.Add($"Together.Multiplayer = {(mpType != null ? "OK" : "MISSING")}");
                if (mpType == null) pass = false;
                else r.Add($"UnchippedIsIndividual = {Mp.UnchippedIsIndividual}");

                if (lockType != null)
                {
                    object oldLocked = Reflect.GetStatic(lockType, "Locked");
                    object oldGlobal = Reflect.GetStatic(lockType, "GlobalUnchipped");

                    Reflect.SetStatic(lockType, "Locked", true);
                    Reflect.SetStatic(lockType, "GlobalUnchipped", true);
                    bool okWrite = Reflect.GetStatic(lockType, "GlobalUnchipped") is bool b && b;

                    Reflect.SetStatic(lockType, "Locked", oldLocked);
                    Reflect.SetStatic(lockType, "GlobalUnchipped", oldGlobal);

                    r.Add($"RunUnchippedLock 写读回环 = {(okWrite ? "PASS" : "FAIL")}");
                    if (!okWrite) pass = false;
                }

                r.Add($"WorldGeneration 可读 = {Game.InWorld}（world={(WorldGeneration.world != null ? "非空" : "null")}）");
                r.Add($"主机裁决读取 = {Mp.HostUnchipped()}");

                log.Info("[SELFTEST] " + string.Join(" | ", r) + (pass ? " | 结论 PASS" : " | 结论 FAIL"));
            }
            catch (Exception ex)
            {
                log.Error("[SELFTEST] FAIL " + ex);
            }
        }
    }
}
