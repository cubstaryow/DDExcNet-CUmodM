using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DDEN.SaveEditor
{
    internal static class EditorLabels
    {
        private static readonly Dictionary<string, string> Known = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["bloodOxygen"]="血氧", ["bloodVolume"]="血量", ["brainHealth"]="脑部健康", ["consciousness"]="意识",
            ["hunger"]="饱食", ["thirst"]="水分", ["stamina"]="体力", ["energy"]="精力", ["temperature"]="体温（℃）",
            ["radiationSickness"]="辐射病", ["sicknessAmount"]="疾病程度", ["septicShock"]="脓毒性休克", ["internalBleeding"]="内出血",
            ["hemothorax"]="血胸", ["painShock"]="疼痛休克", ["shock"]="休克", ["traumaAmount"]="创伤", ["strokeAmount"]="中风程度",
            ["venomTotal"]="累积毒素", ["venomCurrent"]="当前毒素", ["dirtyness"]="脏污", ["wetness"]="潮湿", ["happiness"]="心情",
            ["skinHealth"]="皮肤健康", ["muscleHealth"]="肌肉健康", ["infectionAmount"]="感染程度", ["pain"]="疼痛", ["bleedAmount"]="出血",
            ["shrapnel"]="弹片", ["dislocated"]="脱臼", ["broken"]="骨折", ["infected"]="感染", ["dismembered"]="截断",
            ["condition"]="耐久", ["favourited"]="收藏", ["label"]="自定义名称", ["runTime"]="游戏内计时（秒）",
            ["rounds"]="剩余子弹数", ["maxRounds"]="弹匣容量", ["roundsInMag"]="弹匣内子弹数", ["magCapacity"]="弹匣容量",
            ["roundInChamber"]="弹膛状态", ["hasMag"]="装有弹匣", ["safe"]="保险开启", ["racked"]="枪机拉开",
            ["lastRacked"]="此前枪机状态", ["triggerPressed"]="扳机已按下", ["firingPinStruck"]="击针已释放",
            ["maxCharge"]="电池总容量", ["batteryType"]="电池型号", ["batteryWasFavourited"]="电池收藏标记",
            ["liquidId"]="液体种类", ["amount"]="数量（mL）", ["swatch"]="配色序号",
            ["cheatedSave"]="禁用成就", ["level"]="层数", ["biomeIndex"]="区域序号", ["seed"]="世界种子", ["alive"]="存活",
            ["strength"]="力量", ["resilience"]="韧性", ["intelligence"]="智力", ["STR"]="力量", ["RES"]="韧性", ["INT"]="智力",
            ["strengthExp"]="力量经验", ["resilienceExp"]="韧性经验", ["intelligenceExp"]="智力经验", ["skills.STR"]="力量等级",
            ["skills.RES"]="韧性等级", ["skills.INT"]="智力等级", ["handSlot"]="当前手位", ["health"]="健康", ["selectedCharacter"]="角色种族"
        };
        private static readonly Dictionary<string, string> ItemNames = new Dictionary<string, string>(StringComparer.Ordinal);
        private static string _language;
        private static readonly Dictionary<string, RunSetting> Settings = new Dictionary<string, RunSetting>(StringComparer.Ordinal);
        private static int _settingCount = -1;
        private static string Plain(string value) => Regex.Replace(value ?? "", "<[^>]*>", "");
        internal static string Item(string id)
        {
            if (_language != Locale.currentLangName) { _language = Locale.currentLangName; ItemNames.Clear(); }
            if (ItemNames.TryGetValue(id ?? "", out var name)) return name;
            try
            {
                name = Plain(global::Item.GetItem(id)?.fullName);
                if (string.IsNullOrWhiteSpace(name) || name == id) name = Plain(Locale.GetItem(id));
            }
            catch { name = id; }
            if (string.IsNullOrWhiteSpace(name) || name.StartsWith("MISSING", StringComparison.OrdinalIgnoreCase)) name = id;
            // Do not negatively cache: mod registrations and translations may still be loading.
            if (name != id && name != null) ItemNames[id] = name;
            return name ?? "未知物品";
        }
        internal static RunSetting Setting(string key)
        {
            try
            {
                var list = RunSettings.settingTypes;
                if (list == null) return null;
                if (_settingCount != list.Count) { _settingCount = list.Count; Settings.Clear(); }
                if (Settings.TryGetValue(key, out var cached)) return cached;
                var setting = list.FirstOrDefault(s => s.name == key);
                if (setting != null) Settings[key] = setting;
                return setting;
            }
            catch { return null; }
        }
        internal static string Field(FieldRow row)
        {
            if (row.Group == "run")
            {
                try { string name = Locale.GetOther("runset" + row.Label); if (!string.IsNullOrWhiteSpace(name) && name != "runset" + row.Label) return Plain(name); }
                catch { }
            }
            return Known.TryGetValue(row.Label, out var label) ? label : row.Label;
        }
        internal static string Help(FieldRow row)
        {
            string help = "";
            if (row.Group == "run")
            {
                try { string key = "runset" + row.Label + "dsc"; string text = Locale.GetOther(key); if (text != key) help = Plain(text); } catch { }
                if (Setting(row.Label) is RunSettingFloat f) help += $"\n游戏范围：{f.limits.min:0.###} ～ {f.limits.max:0.###}";
            }
            if (row.Label == "bloodVolume") help += "血量使用游戏内部数值，100 为正常基准，并非毫升。";
            if (row.Label == "cheatedSave") help += "开启后，此存档不计入成就。编辑器不会自动修改此项。";
            return (help.Length > 0 ? help.Trim() + "\n" : "") + "字段：" + row.Label;
        }
        internal static string Limb(int index)
        {
            string[] names = { "头部", "胸部", "腹部", "左上臂", "右上臂", "左前臂", "右前臂", "左手", "右手", "左大腿", "右大腿", "左小腿", "右小腿", "左脚", "右脚" };
            return index >= 0 && index < names.Length ? names[index] : "部位 " + (index + 1);
        }
    }
}
