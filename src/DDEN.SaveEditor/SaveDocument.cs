using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DDEN.Shared;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DDEN.SaveEditor
{
    internal enum RowKind
    {
        Number,
        Bool,
        Text
    }

    /// <summary>一个可编辑字段 = 一个 JToken 的视图；改它就等于改文档（不重建，保住未知字段）。</summary>
    internal sealed class FieldRow
    {
        public string Group;
        public string Label;
        public JToken Token;
        public RowKind Kind;
        public string Buffer;
        public bool BoolValue;

        public void Refresh()
        {
            if (Token == null) return;
            switch (Kind)
            {
                case RowKind.Bool:
                    BoolValue = Token.Type == JTokenType.Boolean && Token.Value<bool>();
                    break;
                case RowKind.Number:
                    Buffer = Token.Type == JTokenType.Integer || Token.Type == JTokenType.Float
                        ? Convert.ToString(((JValue)Token).Value, CultureInfo.InvariantCulture)
                        : "0";
                    break;
                default:
                    Buffer = Token.Type == JTokenType.Null ? string.Empty : (Token.ToString() ?? string.Empty);
                    break;
            }
        }

        public bool ApplyText(string text)
        {
            if (Token == null) return false;
            if (Buffer == text) return false;
            Buffer = text;

            try
            {
                if (!(Token is JValue jv)) return false;

                if (jv.Type == JTokenType.Integer)
                {
                    if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l)) return false;
                    jv.Value = l;
                    return true;
                }
                if (jv.Type == JTokenType.Float)
                {
                    if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return false;
                    jv.Value = (float)d;
                    return true;
                }
                if (jv.Type == JTokenType.Boolean)
                {
                    if (!bool.TryParse(text, out bool b)) return false;
                    jv.Value = b;
                    return true;
                }
                jv.Value = text;
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug($"写回 {Label} 失败：{ex.Message}");
                return false;
            }
        }

        public bool ApplyBool(bool value)
        {
            if (Token == null || BoolValue == value) return false;
            BoolValue = value;
            if (Token is JValue jv)
            {
                jv.Value = value;
                return true;
            }
            return false;
        }

        public double Number => Token != null && (Token.Type == JTokenType.Float || Token.Type == JTokenType.Integer)
            ? Convert.ToDouble(((JValue)Token).Value, CultureInfo.InvariantCulture)
            : 0d;

        public void SetNumber(double value)
        {
            if (!(Token is JValue jv)) return;
            if (jv.Type == JTokenType.Integer) jv.Value = (long)Math.Round(value);
            else jv.Value = (float)value;
            Refresh();
        }
    }

    internal sealed class ItemView
    {
        public int Index;
        public string Id;
        public string SlotText;
        public string GroupKey;
        public JObject Raw;
        public FieldRow Condition;
        public FieldRow Favourited;
        public FieldRow Label;
        public Sprite Icon;

        public float ConditionValue => Condition != null ? (float)Condition.Number : 1f;
    }

    /// <summary>一组物品：存档里子物品与容器共享 slot/wearSlot，所以按这个键归组就能还原“谁装在谁里面”。</summary>
    internal sealed class ItemGroup
    {
        public string Key;
        public string Label;
        public ItemView Head;
        public readonly List<ItemView> Items = new List<ItemView>();

        public bool IsContainer => Items.Count > 1;
        public int Count => Items.Count;
    }

    internal sealed class TargetRow
    {
        public string Path;
        public string Label;
        public string Kind;
        public bool IsCurrent;
        public bool IsMain;
    }

    /// <summary>一个存档文件的编辑态：目标列表 + JObject + 各类视图。</summary>
    internal sealed class SaveDocument
    {
        public JObject Doc;
        public string Path;
        public bool Dirty;
        public string Status = string.Empty;
        public bool StatusIsError;

        public readonly List<FieldRow> Rows = new List<FieldRow>();
        public readonly List<string> Groups = new List<string>();
        public readonly List<ItemView> Items = new List<ItemView>();
        public readonly List<ItemGroup> ItemGroups = new List<ItemGroup>();
        public readonly List<ItemGroup> VisibleGroups = new List<ItemGroup>();
        public readonly List<LimbView> Limbs = new List<LimbView>();
        public List<FieldRow> RunSettings = new List<FieldRow>();
        public List<FieldRow> Progress = new List<FieldRow>();
        public List<FieldRow> BodyRows = new List<FieldRow>();
        public List<FieldRow> SkillRows = new List<FieldRow>();

        public FieldRow CheatFlag;
        public string ItemFilter = string.Empty;
        public int SelectedItem = -1;
        public int SelectedLimb = -1;

        internal sealed class LimbView
        {
            public int Index;
            public string Label;
            public JObject Raw;
            public FieldRow Skin;
            public FieldRow Muscle;
            public readonly List<FieldRow> Rows = new List<FieldRow>();
        }

        private const int MaxItems = 600;

        // ===== 目标枚举（QoL 多存档位 + 多人 bundle）=====
        public static List<TargetRow> BuildTargets()
        {
            var targets = new List<TargetRow>();
            string folder = Qol.SaveFolder;
            List<string> slots = Qol.Slots();

            foreach (string slot in slots)
            {
                if (string.IsNullOrEmpty(slot)) continue;
                string full = System.IO.Path.Combine(folder, slot);
                if (!File.Exists(full)) continue;

                targets.Add(new TargetRow
                {
                    Path = full,
                    Label = slot,
                    IsMain = true,
                    IsCurrent = Qol.IsCurrentSlot(slot),
                    Kind = "存档位"
                });

                foreach (string playerSave in SaveCodec.ListMpPlayerSaves(full))
                {
                    string slotName = System.IO.Path.GetFileNameWithoutExtension(full);
                    string playerDir = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(playerSave));
                    bool isWorld = string.Equals(playerDir, slotName + ".mp", StringComparison.OrdinalIgnoreCase);

                    targets.Add(new TargetRow
                    {
                        Path = playerSave,
                        Label = isWorld ? slotName + ".mp · 世界存档" : slotName + ".mp · " + ShortenPlayer(playerDir),
                        IsMain = false,
                        IsCurrent = false,
                        Kind = isWorld ? "多人世界" : "多人玩家快照"
                    });
                }
            }

            return targets;
        }

        /// <summary>STEAM_76561198832992872 → …92872，列表里放得下又能区分。</summary>
        private static string ShortenPlayer(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return "?";
            string name = dir.Replace("STEAM_", string.Empty);
            return name.Length <= 5 ? name : "…" + name.Substring(name.Length - 5);
        }

        public bool Load(string path)
        {
            try
            {
                Doc = SaveCodec.Read(path);
                Path = path;
                Dirty = false;
                SelectedItem = -1;
                SelectedLimb = -1;
                Rebuild();
                SetStatus($"已读取 {System.IO.Path.GetFileName(path)}：{Items.Count} 件物品 / {Limbs.Count} 根肢体 / {Rows.Count} 个字段", false);
                return true;
            }
            catch (Exception ex)
            {
                Doc = null;
                Clear();
                SetStatus("读取失败：" + ex.Message, true);
                Log.Exception(ex, "Load save");
                return false;
            }
        }

        private void Clear()
        {
            Rows.Clear(); Groups.Clear(); Items.Clear(); ItemGroups.Clear(); VisibleGroups.Clear();
            Limbs.Clear(); RunSettings.Clear(); Progress.Clear(); BodyRows.Clear(); SkillRows.Clear();
            CheatFlag = null;
        }

        public void SetStatus(string text, bool isError)
        {
            Status = text ?? string.Empty;
            StatusIsError = isError;
        }

        public void Rebuild()
        {
            Clear();
            if (Doc == null) return;

            Progress = CollectScalars(Doc);
            if (Doc["cheatedSave"] is JValue) CheatFlag = MakeRow("进度", "cheatedSave", Doc["cheatedSave"]);

            if (Doc["runSettings"] is JArray rs) BuildRunSettings(rs);
            if (Doc["body"] is JObject body)
            {
                BodyRows = CollectScalars(body);
                if (body["skills"] is JObject skills) SkillRows = CollectScalars(skills, "skills.");
            }

            if (Doc["limbs"] is JArray limbs) BuildLimbs(limbs);
            if (Doc["items"] is JArray items) BuildItems(items);

            Rows.AddRange(Progress);
            Rows.AddRange(RunSettings);
            Rows.AddRange(BodyRows);
            Rows.AddRange(SkillRows);

            var groups = new List<string> { "进度" };
            if (RunSettings.Count > 0) groups.Add("运行设置");
            if (BodyRows.Count > 0) groups.Add("身体");
            if (SkillRows.Count > 0) groups.Add("技能");
            if (Limbs.Count > 0) groups.Add("肢体");
            if (Items.Count > 0) groups.Add("物品");
            Groups.AddRange(groups);
        }

        private static List<FieldRow> CollectScalars(JObject source, string prefix = null)
        {
            var rows = new List<FieldRow>();
            foreach (JProperty prop in source.Properties())
            {
                if (!(prop.Value is JValue jv) || jv.Type == JTokenType.Null) continue;
                FieldRow row = MakeRow("body", (prefix ?? string.Empty) + prop.Name, jv);
                if (row != null) rows.Add(row);
            }
            return rows;
        }

        private void BuildRunSettings(JArray array)
        {
            foreach (JToken entry in array)
            {
                if (!(entry is JObject obj)) continue;
                string key = obj["Item1"]?.ToString();
                JToken value = obj["Item2"];
                if (string.IsNullOrEmpty(key) || value == null) continue;
                FieldRow row = MakeRow("run", key, value);
                if (row != null) RunSettings.Add(row);
            }
        }

        private void BuildLimbs(JArray array)
        {
            for (int i = 0; i < array.Count; i++)
            {
                if (!(array[i] is JObject limb)) continue;
                var view = new LimbView
                {
                    Index = i,
                    Raw = limb,
                    Label = LimbLabel(i)
                };
                foreach (JProperty prop in limb.Properties())
                {
                    if (!(prop.Value is JValue jv) || jv.Type == JTokenType.Null) continue;
                    FieldRow row = MakeRow("limb", prop.Name, jv);
                    if (row == null) continue;
                    view.Rows.Add(row);
                    if (prop.Name == "skinHealth") view.Skin = row;
                    if (prop.Name == "muscleHealth") view.Muscle = row;
                }
                Limbs.Add(view);
            }
        }

        private void BuildItems(JArray array)
        {
            for (int i = 0; i < array.Count && Items.Count < MaxItems; i++)
            {
                if (!(array[i] is JObject item)) continue;

                var view = new ItemView
                {
                    Index = i,
                    Id = item["id"]?.ToString() ?? string.Empty,
                    Raw = item,
                    SlotText = DescribeSlot(item),
                    GroupKey = GroupKeyOf(item),
                    Condition = MakeRow("item", "condition", item["condition"]),
                    Favourited = MakeRow("item", "favourited", item["favourited"]),
                    Label = MakeRow("item", "label", item["label"])
                };
                Items.Add(view);
            }
            BuildGroups();
        }

        /// <summary>同 slot + 同 wearSlot = 同一容器（原版存档把父物品写在子物品前面）。</summary>
        private static string GroupKeyOf(JObject item)
        {
            string slot = item["slot"]?.ToString() ?? "-1";
            string wear = item["wearSlot"]?.ToString() ?? string.Empty;
            return slot + "|" + wear;
        }

        /// <summary>把扁平物品列表还原成“容器 → 内容物”分组。</summary>
        public void BuildGroups()
        {
            ItemGroups.Clear();
            VisibleGroups.Clear();

            ItemGroup current = null;
            foreach (ItemView item in Items)
            {
                if (current == null || current.Key != item.GroupKey)
                {
                    current = new ItemGroup { Key = item.GroupKey, Head = item, Label = item.Id };
                    ItemGroups.Add(current);
                }
                current.Items.Add(item);
            }

            string filter = (ItemFilter ?? string.Empty).Trim();
            foreach (ItemGroup group in ItemGroups)
            {
                bool match = filter.Length == 0 || group.Items.Any(i => i.Id.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
                if (!match) continue;
                VisibleGroups.Add(group);
            }
        }

        private static string DescribeSlot(JObject item)
        {
            string slot = item["slot"]?.ToString() ?? "-";
            string wear = item["wearSlot"]?.ToString();
            return string.IsNullOrEmpty(wear) ? "槽 " + slot : wear;
        }

        /// <summary>筛选变化后重算可见分组（保留整组上下文，方便看出“这条属于哪个背包”）。</summary>
        public void RefreshVisibleItems()
        {
            BuildGroups();
        }

        internal static FieldRow MakeRow(string group, string label, JToken token)
        {
            if (token == null) return null;

            RowKind kind;
            switch (token.Type)
            {
                case JTokenType.Boolean: kind = RowKind.Bool; break;
                case JTokenType.Integer:
                case JTokenType.Float: kind = RowKind.Number; break;
                case JTokenType.String: kind = RowKind.Text; break;
                default: return null;
            }

            var row = new FieldRow { Group = group, Label = label, Token = token, Kind = kind };
            row.Refresh();
            return row;
        }

        private static string LimbLabel(int index)
        {
            try
            {
                Body body = Game.LocalBody;
                if (body != null && body.limbs != null && index < body.limbs.Length)
                {
                    Limb limb = body.limbs[index];
                    if (limb != null && !string.IsNullOrEmpty(limb.shortName)) return limb.shortName;
                }
            }
            catch { /* ignored */ }
            return "#" + index;
        }

        public ItemView FindItem(int index) => Items.FirstOrDefault(i => i.Index == index);

        // ===== 写盘 =====

        public string Save()
        {
            if (Doc == null) return "没有已读取的存档。";

            if (Compat.IsQolAutosaveBusy())
            {
                SetStatus("QoL 正在自动存档，稍等 1 秒再保存（避免互相踩）。", true);
                return Status;
            }

            try
            {
                string backup = SaveCodec.Write(Path, Doc);
                SaveCodec.SyncMeta(Path, Doc);
                Dirty = false;
                string msg = backup != null
                    ? $"已写入 {System.IO.Path.GetFileName(Path)}（备份 {System.IO.Path.GetFileName(backup)}）"
                    : $"已写入 {System.IO.Path.GetFileName(Path)}";
                SetStatus(msg, false);
                Log.Info(msg);
                return msg;
            }
            catch (Exception ex)
            {
                SetStatus("写入失败：" + ex.Message, true);
                Log.Exception(ex, "Save");
                return Status;
            }
        }

        // ===== 物品操作 =====

        public void SetItemCondition(ItemView item, float value)
        {
            item?.Condition?.SetNumber(value);
            Dirty = true;
            SetStatus($"{item?.Id} 耐久 → {value:0.00}", false);
        }

        public void AllItemsFullCondition()
        {
            foreach (ItemView item in Items) item.Condition?.SetNumber(1d);
            Dirty = true;
            SetStatus("已把全部物品耐久设为 100%", false);
        }

        public void DeleteItem(ItemView item)
        {
            if (item == null || !(Doc["items"] is JArray items)) return;
            try
            {
                if (item.Index < 0 || item.Index >= items.Count) return;
                items.RemoveAt(item.Index);

                // itemComponents 与 items 一一对应，必须同步删，否则后面全部错位
                if (Doc["itemComponents"] is JArray comps && comps.Count == items.Count + 1 && item.Index < comps.Count)
                {
                    comps.RemoveAt(item.Index);
                }

                SelectedItem = -1;
                Dirty = true;
                Rebuild();
                SetStatus($"已移除物品 #{item.Index} {item.Id}", false);
            }
            catch (Exception ex)
            {
                SetStatus("移除失败：" + ex.Message, true);
            }
        }

        // ===== 预设 =====

        public void PresetFullHeal()
        {
            if (Doc?["body"] is JObject body)
            {
                SetNumber(body, "bloodOxygen", 100);
                SetNumber(body, "bloodVolume", 100);
                SetNumber(body, "brainHealth", 100);
                SetNumber(body, "consciousness", 100);
                SetNumber(body, "hunger", 100);
                SetNumber(body, "thirst", 100);
                SetNumber(body, "stamina", 100);
                SetNumber(body, "energy", 100);
                SetNumber(body, "temperature", 37);
                SetNumber(body, "radiationSickness", 0);
                SetNumber(body, "sicknessAmount", 0);
                SetNumber(body, "septicShock", 0);
                SetNumber(body, "internalBleeding", 0);
                SetNumber(body, "hemothorax", 0);
                SetNumber(body, "painShock", 0);
                SetNumber(body, "shock", 0);
                SetNumber(body, "traumaAmount", 0);
                SetNumber(body, "strokeAmount", 0);
                SetNumber(body, "venomTotal", 0);
                SetNumber(body, "venomCurrent", 0);
                SetNumber(body, "dirtyness", 0);
                SetNumber(body, "wetness", 0);
                SetNumber(body, "happiness", 100);
            }

            if (Doc?["limbs"] is JArray limbs)
            {
                foreach (JToken t in limbs)
                {
                    if (!(t is JObject limb)) continue;
                    SetNumber(limb, "skinHealth", 100);
                    SetNumber(limb, "muscleHealth", 100);
                    SetNumber(limb, "infectionAmount", 0);
                    SetNumber(limb, "pain", 0);
                    SetNumber(limb, "bleedAmount", 0);
                    SetNumber(limb, "shrapnel", 0);
                    SetBool(limb, "dislocated", false);
                    SetBool(limb, "broken", false);
                    SetBool(limb, "infected", false);
                    SetBool(limb, "dismembered", false);
                }
            }

            MarkDirty("已套用：满治疗效果");
        }

        public void PresetZeroTime()
        {
            if (Doc == null) return;
            SetNumber(Doc, "runTime", 0);
            MarkDirty("已套用：计时归零");
        }

        public void PresetCleanNegative()
        {
            if (Doc?["body"] is JObject body)
            {
                foreach (JProperty prop in body.Properties().ToList())
                {
                    if (!(prop.Value is JValue jv)) continue;
                    if (jv.Type != JTokenType.Float && jv.Type != JTokenType.Integer) continue;
                    double v = Convert.ToDouble(jv.Value, CultureInfo.InvariantCulture);
                    if (v < 0) jv.Value = jv.Type == JTokenType.Integer ? (object)0L : 0f;
                }
            }
            MarkDirty("已套用：身体负值清零");
        }

        private void MarkDirty(string status)
        {
            Dirty = true;
            Rebuild();
            SetStatus(status, false);
        }

        private static void SetNumber(JObject obj, string key, double value)
        {
            if (obj[key] is JValue jv)
            {
                if (jv.Type == JTokenType.Integer) jv.Value = (long)Math.Round(value);
                else if (jv.Type == JTokenType.Float) jv.Value = (float)value;
            }
        }

        private static void SetBool(JObject obj, string key, bool value)
        {
            if (obj[key] is JValue jv && jv.Type == JTokenType.Boolean) jv.Value = value;
        }

        // ===== 实时写回运行时 =====

        public int ApplyToLiveBody()
        {
            Body body = Game.LocalBody;
            if (body == null) return -1;

            int count = 0;
            if (Doc["body"] is JObject bodyJson)
            {
                foreach (JProperty prop in bodyJson.Properties())
                {
                    if (!(prop.Value is JValue jv)) continue;
                    if (jv.Type == JTokenType.Float || jv.Type == JTokenType.Integer)
                    {
                        if (LiveFields.ApplyNumber(body, LiveFields.BodyFields, prop.Name,
                                Convert.ToDouble(jv.Value, CultureInfo.InvariantCulture))) count++;
                    }
                    else if (jv.Type == JTokenType.Boolean)
                    {
                        if (LiveFields.ApplyBool(body, LiveFields.BodyFields, prop.Name, jv.Value<bool>())) count++;
                    }
                }

                if (bodyJson["skills"] is JObject skills)
                {
                    System.Reflection.FieldInfo skillsField = typeof(Body).GetField("skills",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (skillsField != null)
                    {
                        object boxed = skillsField.GetValue(body);
                        if (boxed != null)
                        {
                            foreach (JProperty prop in skills.Properties())
                            {
                                if (prop.Value is JValue jv && (jv.Type == JTokenType.Float || jv.Type == JTokenType.Integer))
                                {
                                    if (Reflect.TrySetInstance(boxed, prop.Name,
                                            Convert.ToDouble(jv.Value, CultureInfo.InvariantCulture))) count++;
                                }
                            }
                            skillsField.SetValue(body, boxed);
                        }
                    }
                }
            }

            if (Doc["limbs"] is JArray limbsJson && body.limbs != null)
            {
                for (int i = 0; i < limbsJson.Count && i < body.limbs.Length; i++)
                {
                    if (!(limbsJson[i] is JObject limbJson)) continue;
                    Limb limb = body.limbs[i];
                    if (limb == null) continue;
                    foreach (JProperty prop in limbJson.Properties())
                    {
                        if (!(prop.Value is JValue jv)) continue;
                        if (jv.Type == JTokenType.Float || jv.Type == JTokenType.Integer)
                        {
                            if (LiveFields.ApplyNumber(limb, LiveFields.LimbFields, prop.Name,
                                    Convert.ToDouble(jv.Value, CultureInfo.InvariantCulture))) count++;
                        }
                        else if (jv.Type == JTokenType.Boolean)
                        {
                            if (LiveFields.ApplyBool(limb, LiveFields.LimbFields, prop.Name, jv.Value<bool>())) count++;
                        }
                    }
                }
            }

            return count;
        }
    }
}
