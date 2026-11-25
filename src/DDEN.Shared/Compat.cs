using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace DDEN.Shared
{
    public enum CompatLevel
    {
        Ok,
        Info,
        Warn,
        Problem
    }

    public sealed class CompatRow
    {
        public string Name;
        public string Version;
        public CompatLevel Level;
        public string Note;

        public string LevelText
        {
            get
            {
                switch (Level)
                {
                    case CompatLevel.Ok: return "<color=#8cd97c>兼容</color>";
                    case CompatLevel.Info: return "<color=#9ad>已适配</color>";
                    case CompatLevel.Warn: return "<color=#f0a>需注意</color>";
                    default: return "<color=#f66>冲突</color>";
                }
            }
        }
    }

    /// <summary>
    /// 兼容性总控：所有“别的模组可能和我们打架”的判断都集中在这里，
    /// 由 DDExcNet-CUmodM 统一展示与处理，模块本身只问结果、不各自造轮子。
    /// </summary>
    public static class Compat
    {
        /// <summary>Loaded instances, not files or merely advertised metadata, satisfy a dependency.</summary>
        public static bool PluginLoaded(string guid)
        {
            try { return Chainloader.PluginInfos.TryGetValue(guid, out var info) && info?.Instance != null; }
            catch { return false; }
        }
        public static Version PluginVersion(string guid)
        {
            try { return PluginLoaded(guid) ? Chainloader.PluginInfos[guid].Metadata.Version : null; }
            catch { return null; }
        }

        // 认识的邻居（按 GUID 精确 + 名称关键词兜底）
        private static readonly Dictionary<string, string> KnownByGuid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["net.cucorelib"] = "前置",
            ["CasualtiesMP"] = "联机",
            ["org.bepinex.plugins.qol_unknown"] = "QoL",
            ["com.dden.cumodm"] = "本系列",
            ["com.dden.mpunchippedsync"] = "本系列",
            ["com.dden.saveeditor"] = "本系列",
            // 历史命名（升级前的自己）
            ["com.cumod.unchippedsync"] = "本系列(旧)",
            ["com.cumod.saveeditor"] = "本系列(旧)"
        };

        private static readonly (string keyword, string label)[] KnownByName =
        {
            ("qol", "QoL"),
            ("together", "Together"),
            ("casualtiesmp", "Together"),
            ("cucore", "CUCoreLib"),
            ("emi", "EMI"),
            ("enhancedtools", "EnhancedTools"),
            ("autosort", "AutoSort"),
            ("skinsync", "SkinSync"),
            ("translation", "汉化"),
            ("汉化", "汉化"),
            ("cpuoptim", "CPU优化"),
            ("magazine", "弹药合并"),
            ("titanium", "钛矿"),
            ("stonefruit", "石果"),
            ("whatdidipress", "空投仓优化"),
            ("saveandquit", "保存退出"),
            ("dden", "本系列")
        };

        private static readonly List<CompatRow> _rows = new List<CompatRow>();
        private static readonly List<string> _hotkeyConflicts = new List<string>();

        public static IReadOnlyList<CompatRow> Rows => _rows;
        public static IReadOnlyList<string> HotkeyConflicts => _hotkeyConflicts;

        public static void Rescan()
        {
            _rows.Clear();
            _hotkeyConflicts.Clear();

            ScanPlugins();
            ScanDuplicateAssemblies();
            ScanPatchStacks();
        }

        // ===== 1. 已装插件清单 =====
        private static void ScanPlugins()
        {
            try
            {
                foreach (KeyValuePair<string, PluginInfo> pair in Chainloader.PluginInfos)
                {
                    PluginInfo info = pair.Value;
                    string guid = pair.Key;
                    string name = info?.Metadata?.Name ?? guid;
                    Version version = info?.Metadata?.Version;

                    CompatLevel level = KnownByGuid.TryGetValue(guid, out string kind)
                        ? (kind == "本系列(旧)" ? CompatLevel.Warn : CompatLevel.Info)
                        : LooksKnown(name) ? CompatLevel.Info : CompatLevel.Ok;

                    string note = kind ?? string.Empty;
                    if (kind == "本系列(旧)")
                    {
                        note = "旧版本留下的同名模组：功能和新版重复，建议删掉旧文件";
                    }
                    else if (kind == "QoL")
                    {
                        note = "存档位和自动存档由它负责；我们写存档前会先确认它没在自动保存";
                    }
                    else if (kind == "联机")
                    {
                        note = "“有无芯片”会跟它保持一致；我们的改动排在它之后执行";
                    }
                    else if (kind == "前置")
                    {
                        note = "联机功能靠它；版本太旧的话，部分修复不会生效";
                    }

                    _rows.Add(new CompatRow
                    {
                        Name = name,
                        Version = version?.ToString() ?? "-",
                        Level = level,
                        Note = note
                    });
                }
            }
            catch (Exception ex)
            {
                _rows.Add(new CompatRow { Name = "插件枚举", Version = "-", Level = CompatLevel.Problem, Note = ex.Message });
            }
        }

        private static bool LooksKnown(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string lower = name.ToLowerInvariant();
            return KnownByName.Any(k => lower.Contains(k.keyword));
        }

        // ===== 2. 旧文件/重复程序集 =====
        private static void ScanDuplicateAssemblies()
        {
            try
            {
                string plugins = Paths.PluginPath;
                if (string.IsNullOrEmpty(plugins) || !Directory.Exists(plugins)) return;

                List<string> ours = Directory.GetFiles(plugins, "DDEN.*.dll", SearchOption.AllDirectories)
                    .Concat(Directory.GetFiles(plugins, "DDExcNet-*.dll", SearchOption.AllDirectories))
                    .ToList();

                foreach (string legacy in Directory.GetFiles(plugins, "CUmod.*.dll", SearchOption.AllDirectories))
                {
                    _rows.Add(new CompatRow
                    {
                        Name = Path.GetFileName(legacy),
                        Version = "-",
                        Level = CompatLevel.Problem,
                        Note = "旧版本文件还在插件目录里：会和新版同时加载，界面会出现两份，请删掉它"
                    });
                }

                // 同一程序集出现在多个子目录（例如 plugins/ 与 plugins/DDEN/ 各一份）
                foreach (IGrouping<string, string> group in ours.GroupBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase))
                {
                    if (group.Count() <= 1) continue;
                    _rows.Add(new CompatRow
                    {
                        Name = group.Key,
                        Version = "-",
                        Level = CompatLevel.Problem,
                        Note = "同一个文件出现在多个目录：" + string.Join(" | ", group.Select(p => Shorten(p, plugins)))
                    });
                }
            }
            catch (Exception ex)
            {
                _rows.Add(new CompatRow { Name = "文件扫描", Version = "-", Level = CompatLevel.Warn, Note = ex.Message });
            }
        }

        private static string Shorten(string full, string root)
        {
            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return full.Substring(root.Length).TrimStart('\\', '/');
            return Path.GetFileName(full);
        }

        // ===== 3. 补丁栈（谁还改了同一个方法）=====
        private static void ScanPatchStacks()
        {
            AddPatchRow("无芯片模式切换", AccessTools.Method(typeof(WorldGeneration), "SetUnchipped"));
            AddPatchRow("存档保存", AccessTools.Method(typeof(SaveSystem), "SaveGame"));
            AddPatchRow("存档读取", AccessTools.Method(typeof(SaveSystem), "TryLoadGame"));
            AddPatchRow("画面主循环", AccessTools.Method(typeof(PlayerCamera), "Update"));
        }

        private static void AddPatchRow(string label, MethodBase method)
        {
            if (method == null) return;
            try
            {
                Patches info = Harmony.GetPatchInfo(method);
                if (info == null) return;

                List<string> owners = info.Owners?.ToList() ?? new List<string>();
                if (owners.Count == 0) return;

                bool crowded = owners.Count >= 3;
                _rows.Add(new CompatRow
                {
                    Name = label,
                    Version = owners.Count + " 个模组同时改它",
                    Level = crowded ? CompatLevel.Warn : CompatLevel.Info,
                    Note = string.Join(", ", owners)
                });
            }
            catch (Exception ex)
            {
                _rows.Add(new CompatRow { Name = label, Version = "-", Level = CompatLevel.Info, Note = "读取失败：" + ex.Message });
            }
        }

        // ===== 4. 快捷键冲突 =====

        /// <summary>扫描所有插件的 KeyboardShortcut 配置，找出和 key 冲突的持有者。</summary>
        public static List<string> FindHotkeyOwners(KeyCode key, string selfGuid)
        {
            var owners = new List<string>();
            if (key == KeyCode.None) return owners;

            try
            {
                foreach (KeyValuePair<string, PluginInfo> pair in Chainloader.PluginInfos)
                {
                    if (string.Equals(pair.Key, selfGuid, StringComparison.OrdinalIgnoreCase)) continue;
                    ConfigFile cfg = pair.Value?.Instance?.Config;
                    if (cfg == null) continue;

                    foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> cfgPair in cfg)
                    {
                        ConfigEntryBase entry = cfgPair.Value;
                        if (!(entry.BoxedValue is KeyboardShortcut sc)) continue;
                        if (sc.MainKey == key && !sc.Modifiers.Any())
                        {
                            owners.Add($"{pair.Value.Metadata.Name}:{entry.Definition.Key}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("FindHotkeyOwners 失败：" + ex.Message);
            }
            return owners;
        }

        /// <summary>给一个配置项找一个不冲突的键：先保留原键，冲突则换空闲 F 键并写回配置。</summary>
        public static KeyCode ResolveHotkey(ConfigEntry<KeyboardShortcut> entry, string selfGuid, bool autoFix)
        {
            if (entry == null) return KeyCode.None;
            KeyCode current = entry.Value.MainKey;
            List<string> owners = FindHotkeyOwners(current, selfGuid);
            if (owners.Count == 0) return current;

            string line = $"{current} 已被 {string.Join(" / ", owners)} 占用";
            if (!_hotkeyConflicts.Contains(line)) _hotkeyConflicts.Add(line);
            Log.Warn("快捷键冲突：" + line);

            if (!autoFix) return current;

            foreach (KeyCode candidate in new[] { KeyCode.F7, KeyCode.F10, KeyCode.F11, KeyCode.F6, KeyCode.F5, KeyCode.Insert })
            {
                if (candidate == current) continue;
                if (FindHotkeyOwners(candidate, selfGuid).Count != 0) continue;
                entry.Value = new KeyboardShortcut(candidate);
                Log.Info($"已把 {entry.Definition.Key} 自动改为 {candidate}（避免与其它模组抢键）。");
                _hotkeyConflicts.Add($"已自动改键：{entry.Definition.Key} → {candidate}");
                return candidate;
            }
            return current;
        }

        public static KeyCode ResolveManagerHotkey(ConfigEntry<KeyboardShortcut> entry, string selfGuid, bool autoFix)
        {
            if (entry == null) return KeyCode.None;
            var shortcut = entry.Value;
            var chosen = ManagerHotkeyPolicy.Choose(shortcut.MainKey, shortcut.Modifiers.Any(), autoFix,
                key => FindHotkeyOwners(key, selfGuid).Count != 0);
            if (chosen != shortcut.MainKey)
            {
                entry.Value = new KeyboardShortcut(chosen);
                Log.Info("Manager hotkey -> " + chosen);
            }
            return chosen;
        }

        // ===== 5. 运行时守卫 =====

        /// <summary>QoL 正在自动存档：此时写盘会互相踩，先别写。</summary>
        public static bool IsQolAutosaveBusy()
        {
            try
            {
                Type t = Reflect.Type("QoL_Unknown.SaveManager");
                if (t == null) return false;
                object busy = Reflect.GetStatic(t, "autosaveInProgress");
                return busy is bool b && b;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>控制台是否已有同名指令（避免和别的模组抢指令名）。</summary>
        public static bool ConsoleHasCommand(string name)
        {
            try
            {
                if (ConsoleScript.Commands == null) return false;
                return ConsoleScript.Commands.Any(c => string.Equals(c?.name, name, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>当前是否处于联机（Together 在跑）：联机时改档只在本地生效，需要提示。</summary>
        public static bool InMultiplayer()
        {
            return Mp.IsRunning;
        }

        /// <summary>给界面用的一句话总结。</summary>
        public static string Headline()
        {
            int problems = _rows.Count(r => r.Level == CompatLevel.Problem);
            int warns = _rows.Count(r => r.Level == CompatLevel.Warn);
            if (problems > 0) return $"<color=#f66>{problems} 项冲突需要处理</color>（{warns} 项需注意）";
            if (warns > 0) return $"<color=#f0a>{warns} 项需注意</color>";
            return "<color=#8cd97c>未发现冲突</color>";
        }
    }
}
