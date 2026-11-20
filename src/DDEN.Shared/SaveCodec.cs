using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DDEN.Shared
{
    /// <summary>
    /// 游戏存档编解码：save.sv = GZip(ASCII JSON)，与 SaveSystem.Zip/Unzip 完全一致。
    /// 同时处理 QoL 的 *.sv.meta 与 *.mp 多人 bundle。
    /// </summary>
    public static class SaveCodec
    {
        public static string SaveFolder => UnityEngine.Application.persistentDataPath;

        public static JObject Read(string path)
        {
            byte[] compressed = File.ReadAllBytes(path);
            using (var ms = new MemoryStream(compressed))
            using (var gz = new GZipStream(ms, CompressionMode.Decompress))
            using (var reader = new StreamReader(gz, Encoding.ASCII))
            {
                string json = reader.ReadToEnd();
                return JObject.Parse(json);
            }
        }

        public static string ReadJson(string path)
        {
            byte[] compressed = File.ReadAllBytes(path);
            using (var ms = new MemoryStream(compressed))
            using (var gz = new GZipStream(ms, CompressionMode.Decompress))
            using (var reader = new StreamReader(gz, Encoding.ASCII))
            {
                return reader.ReadToEnd();
            }
        }

        public static byte[] Encode(JObject save)
        {
            string json = save.ToString(Formatting.None);
            using (var ms = new MemoryStream())
            {
                using (var gz = new GZipStream(ms, CompressionLevel.Optimal, true))
                using (var writer = new StreamWriter(gz, Encoding.ASCII))
                {
                    writer.Write(json);
                }
                return ms.ToArray();
            }
        }

        /// <summary>原子写：临时文件 → 备份旧档(.cumod.bak) → 顶替。返回备份路径。</summary>
        public static string Write(string path, JObject save)
        {
            byte[] data = Encode(save);
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string tmp = path + ".cumod-tmp";
            File.WriteAllBytes(tmp, data);

            string backup = null;
            if (File.Exists(path))
            {
                backup = path + ".cumod.bak";
                try { File.Copy(path, backup, true); }
                catch (Exception ex) { Log.Warn($"备份失败（继续写入）：{ex.Message}"); backup = null; }
            }

            try
            {
                File.Delete(path);
                File.Move(tmp, path);
            }
            catch
            {
                try { File.Copy(tmp, path, true); File.Delete(tmp); }
                catch (Exception ex) { throw new IOException("写入存档失败：" + ex.Message, ex); }
            }
            return backup;
        }

        public static string GetMetaPath(string savePath) => savePath + ".meta";

        public static JObject TryReadMeta(string savePath)
        {
            string metaPath = GetMetaPath(savePath);
            if (!File.Exists(metaPath)) return null;
            try { return JObject.Parse(File.ReadAllText(metaPath)); }
            catch (Exception ex) { Log.Warn($"读取 meta 失败：{ex.Message}"); return null; }
        }

        /// <summary>改完存档后同步 QoL meta 里会被玩家直接看到的那几个字段。</summary>
        public static void SyncMeta(string savePath, JObject save)
        {
            JObject meta = TryReadMeta(savePath);
            if (meta == null) return;

            try
            {
                if (save.TryGetValue("biome", out JToken biome)) meta["BiomeIndex"] = biome;
                if (save.TryGetValue("runTime", out JToken runTime))
                {
                    meta["IGTSeconds"] = runTime;
                    if (runTime.Type == JTokenType.Float || runTime.Type == JTokenType.Integer)
                    {
                        meta["RTASeconds"] = runTime;
                    }
                }
                meta["Timestamp"] = DateTime.Now.ToString("yyyy/MM/dd HH:mm");
                File.WriteAllText(GetMetaPath(savePath), meta.ToString(Formatting.Indented), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Log.Warn($"同步 meta 失败：{ex.Message}");
            }
        }

        /// <summary>QoL 存档位：slot_1.sv / slot_2.sv / autosave.sv / save.sv。返回按修改时间倒序。</summary>
        public static List<string> ListSaveFiles(string folder)
        {
            try
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return new List<string>();
                return new DirectoryInfo(folder).GetFiles("*.sv")
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Select(f => f.Name)
                    .ToList();
            }
            catch (Exception ex)
            {
                Log.Warn($"枚举存档失败：{ex.Message}");
                return new List<string>();
            }
        }

        /// <summary>多人槽位的伴随目录：slot_1.sv → slot_1.mp（内含每人 save.sv）。</summary>
        public static string GetMpBundleDir(string savePath)
        {
            if (string.IsNullOrEmpty(savePath)) return null;
            string dir = Path.Combine(Path.GetDirectoryName(savePath) ?? string.Empty,
                                      Path.GetFileNameWithoutExtension(savePath) + ".mp");
            return Directory.Exists(dir) ? dir : null;
        }

        /// <summary>列出多人 bundle 里的“每人一份”存档。</summary>
        public static List<string> ListMpPlayerSaves(string savePath)
        {
            var result = new List<string>();
            string bundle = GetMpBundleDir(savePath);
            if (bundle == null) return result;
            try
            {
                foreach (string playerDir in Directory.GetDirectories(bundle))
                {
                    string candidate = Path.Combine(playerDir, "save.sv");
                    if (File.Exists(candidate)) result.Add(candidate);
                }
                string worldSave = Path.Combine(bundle, "save.sv");
                if (File.Exists(worldSave)) result.Insert(0, worldSave);
            }
            catch (Exception ex)
            {
                Log.Warn($"枚举多人 bundle 失败：{ex.Message}");
            }
            return result;
        }
    }

    /// <summary>QoL 多存档位桥（软依赖：没装 QoL 就退回 save.sv）。</summary>
    public static class Qol
    {
        public static bool Present => Reflect.Type("QoL_Unknown.SaveManager") != null;

        public static string CurrentSaveFile
        {
            get
            {
                object v = Reflect.GetStatic(Reflect.Type("QoL_Unknown.SaveManager"), "CurrentSaveFile");
                return v as string ?? "save.sv";
            }
        }

        public static string SaveFolder
        {
            get
            {
                object v = Reflect.GetStatic(Reflect.Type("QoL_Unknown.SaveManager"), "SaveFolder");
                return v as string ?? SaveCodec.SaveFolder;
            }
        }

        public static List<string> Slots()
        {
            Type t = Reflect.Type("QoL_Unknown.SaveManager");
            if (t != null)
            {
                object raw = Reflect.InvokeStatic(t, "GetSaveFiles");
                if (raw is List<string> list) return list;
            }
            return SaveCodec.ListSaveFiles(SaveFolder);
        }

        /// <summary>某个槽位是否是当前正在玩的档（决定能否“实时应用”）。</summary>
        public static bool IsCurrentSlot(string fileName)
        {
            return string.Equals(CurrentSaveFile, fileName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
