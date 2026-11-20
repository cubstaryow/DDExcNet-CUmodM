using System;
using System.Collections.Generic;
using System.IO;
using DDEN.Shared;
using Newtonsoft.Json.Linq;

namespace DDEN.SaveEditor
{
    /// <summary>
    /// 自检：带 -cumodtest 或 BepInEx/config/cumod.selftest 标记时跑真实代码路径
    /// （QoL 存档位枚举 → 读 → 改 → 写 → 再读比对），结果写日志。用副本，不碰真实存档。
    /// </summary>
    internal static class SelfTest
    {
        public static bool Requested
        {
            get
            {
                try
                {
                    if (File.Exists(MarkerPath)) return true;
                    foreach (string arg in Environment.GetCommandLineArgs())
                    {
                        if (string.Equals(arg, "-cumodtest", StringComparison.OrdinalIgnoreCase)) return true;
                    }
                }
                catch { /* ignored */ }
                return false;
            }
        }

        public static string MarkerPath => Path.Combine(BepInEx.Paths.ConfigPath, "cumod.selftest");

        public static void Run(EditorView view, LogChannel log)
        {
            try { if (File.Exists(MarkerPath)) File.Delete(MarkerPath); } catch { /* ignored */ }

            var results = new List<string>();
            string temp = null;
            try
            {
                results.Add($"QoL 存在 = {Qol.Present}");
                results.Add($"存档目录 = {Qol.SaveFolder}");
                results.Add($"当前存档位 = {Qol.CurrentSaveFile}");

                List<string> slots = Qol.Slots();
                results.Add($"QoL.GetSaveFiles() 返回 {slots.Count} 个：{string.Join(", ", slots)}");

                if (slots.Count == 0)
                {
                    log.Info("[SELFTEST] SKIP 没有可测存档");
                    return;
                }

                string source = Path.Combine(Qol.SaveFolder, slots[0]);
                temp = Path.Combine(Qol.SaveFolder, "cumod-selftest.sv");
                File.Copy(source, temp, true);

                JObject original = SaveCodec.Read(temp);
                results.Add($"解压解析 OK，顶层字段 {original.Count} 个");

                var doc = new SaveDocument();
                doc.Load(temp);
                results.Add($"视图：物品 {doc.Items.Count} / 肢体 {doc.Limbs.Count} / 运行设置 {doc.RunSettings.Count} / 字段 {doc.Rows.Count}");
                results.Add($"分组：{string.Join(" / ", doc.Groups)}");

                double before = original["runTime"] != null ? original["runTime"].Value<double>() : 0;
                doc.Doc["runTime"] = before + 1234;
                doc.Dirty = true;
                results.Add("写入：" + doc.Save());

                JObject reread = SaveCodec.Read(temp);
                double after = reread["runTime"].Value<double>();
                bool ok = Math.Abs(after - (before + 1234)) < 0.5;
                results.Add($"回读比对 runTime {before} → {after} ：{(ok ? "PASS" : "FAIL")}");

                // 成就标记：必须原样保留（本编辑器不自动改）
                bool cheatBefore = original["cheatedSave"] != null && original["cheatedSave"].Value<bool>();
                bool cheatAfter = reread["cheatedSave"] != null && reread["cheatedSave"].Value<bool>();
                bool cheatOk = cheatBefore == cheatAfter;
                results.Add($"cheatedSave 保持原值 {cheatBefore} → {(cheatOk ? "PASS" : "FAIL")}");

                // 图标链路（CUCoreLib 注册表 / 游戏资源）
                int iconOk = 0;
                for (int i = 0; i < Math.Min(6, doc.Items.Count); i++)
                {
                    if (DdenUI.ItemIcon(doc.Items[i].Id) != null) iconOk++;
                }
                results.Add($"物品图标可解析 {iconOk}/{Math.Min(6, doc.Items.Count)}");

                results.Add($"管理器已加载 = {DdenHub.ManagerLoaded}，已注册模块 {DdenHub.Modules.Count} 个");
                results.Add($"兼容性扫描项 {Compat.Rows.Count} 条，{Compat.Headline()}");

                bool inWorld = Game.InWorld;
                if (inWorld)
                {
                    doc.Load(temp);
                    results.Add($"实时写回字段 {doc.ApplyToLiveBody()} 个");
                }

                log.Info("[SELFTEST] " + string.Join(" | ", results) + (ok && cheatOk ? " | 结论 PASS" : " | 结论 FAIL"));
            }
            catch (Exception ex)
            {
                log.Error("[SELFTEST] FAIL " + ex);
            }
            finally
            {
                try { if (temp != null && File.Exists(temp)) File.Delete(temp); } catch { /* ignored */ }
                try { if (temp != null && File.Exists(temp + ".cumod.bak")) File.Delete(temp + ".cumod.bak"); } catch { /* ignored */ }
            }
        }
    }
}
