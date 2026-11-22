# 管理器显示接入规范 v1

本文是配置文字、双语、实验性及性能标记的技术契约，配合[管理器接入](cumodm-integration.md)使用。实现依据为`DdenLocalization.cs`、`DdenDocs.cs`、`ExternalConfig.cs`、`DdenUI.cs`和管理器页面路由。规范编号不是模组版本，也不新增联机协议。

## 身份与显示分离

| 数据 | 规则 |
| --- | --- |
| 插件GUID、模块Id、ConfigDefinition.Section／Key | 保持原始身份，不随语言修改；不能用显示名称查找配置或决定行为 |
| 配置值 | 直接编辑原ConfigEntry，保留类型、校验、SettingChanged及保存策略；翻译不得改值 |
| 标签、分区标题、短说明、提示、模块标题／状态 | 仅在绘制时取对应语言，不修改源数据 |
| 用户输入、玩家名、物品ID、文件路径、异常细节 | 原样保留，不做词语替换；游戏内容名称仍由游戏及其模组本地化 |
| 状态与等级 | 使用下述布尔／枚举标记；文字与颜色由管理器决定，不从翻译后的说明反推状态 |

语言代码仅为`zh-CN`、`en`，默认中文。管理器标题栏及自身`UI/Language`切换立即生效，保存在管理器本机配置；管理器、MPfix、性能跟踪器共用`DdenLocalization.Language`。不改变游戏语言，不作为主机权威配置下发。无管理器时，独立窗口标题栏仍可切换当前进程的显示语言，此入口本身不另写语言配置。

找不到译文时保留原文，不显示空白占位，不自动翻译第三方文字。翻译应匹配完整原文／格式模板，不对拼接后的用户文字做词语替换。`{0}`等参数索引、数字格式和富文本标签须保持有效。资源加载一次；语言切换只使文字／布局缓存失效，不清除性能样本、事件、统计基线或功能状态。

## 配置元数据

| L1字典字段 | 类型／合法值 | 用途 |
| --- | --- | --- |
| `label` | string，可省略 | 配置显示名；缺省使用原Key |
| `description` | string，可省略 | 一行短说明及提示中的“功能”；缺省保留原说明／已注册完整说明 |
| `problem` | string，可省略 | 提示中的“修复的问题” |
| `method` | string，可省略 | 提示中的“解决方案” |
| `experimental` | `true`／`false`，不区分大小写 | 实验性真实状态；显式false优先于旧的“是”前缀 |
| `experimentalNote` | string，可省略 | 实验性备注，不能用来决定是否实验性 |
| `performance` | `none`／`low`／`medium`／`high`，不区分大小写 | 性能影响等级；不是实际测量结果或零开销承诺 |
| `performanceNote` | string，可省略 | 性能影响备注，不包含等级前缀 |
| `needsRestart` | `true`／`false`，不区分大小写 | 是否提示需要重启；此标记不会替模组实现重启或重新初始化 |

字段名按表中大小写提供。状态／等级缺省或非法时视为未指定，按兼容规则回退；不得因错误值自动视为false或none。备注可传空字符串清空，不要在备注开头重复等级。

| 标准等级 | 中文 | English |
| --- | --- | --- |
| `none` | 无 | None |
| `low` | 低 | Low |
| `medium` | 中 | Medium |
| `high` | 高 | High |

例如`performance=low`、`performanceNote=仅在打开界面时`显示为`性能影响：低，仅在打开界面时`；英文为`Performance: Low, Only when opening the UI`。实验性同样显示`实验性：是，需多人实测`／`Experimental: Yes, Multiplayer testing needed`。等级与布尔状态由管理器翻译；模组只需提供备注译文。

元数据按配置项首次访问时缓存，模组不应在这个接口里做扫描、生成物品、网络请求或修改配置。动态可用性由`DdenUnavailable`／策略接口提供；动态页面文字在页面绘制时更新。重新扫描接入方时会创建新的配置块。

## L1：无需引用Shared

下列可选成员放在交出`DdenConfig`的同一个public类型中，均为public static方法，参数和返回类型须完全匹配：

```csharp
IDictionary<string, string> DdenConfigMetadata(ConfigEntryBase entry);
string DdenLocalize(string original, string language);
void DdenSetLanguage(string language);
```

`DdenLocalize`处理配置标签、分区标题、说明、备注、禁用原因，以及该桥接类型的模块标题与状态。它收到原文与上述语言代码，未知文字返回原文或null，空译文也回退原文；不应在翻译函数中产生业务副作用。自定义`DdenDraw`内的文字仍由接入方绘制，可用`DdenSetLanguage`保存语言并更新自己的文字缓存。管理器在识别桥接时通知一次，此后只在语言实际变化时通知；通知不等于要求每帧刷新或重载配置。

完整最小示例（`MyPlugin`是接入方自己的插件类型）：

```csharp
using System.Collections.Generic;
using BepInEx.Configuration;

public static class DdenBridge
{
    public static ConfigFile DdenConfig => MyPlugin.Instance.Config;
    public static string DdenTitle => "我的模组";
    public static string DdenSummary => "就绪";
    private static string _language = "zh-CN";

    public static IDictionary<string, string> DdenConfigMetadata(ConfigEntryBase entry)
    {
        if (entry.Definition.Section != "修复" || entry.Definition.Key != "Enabled") return null;
        return new Dictionary<string, string>
        {
            ["label"] = "启用修复",
            ["description"] = "按需修复此问题。",
            ["problem"] = "特定操作失败。",
            ["method"] = "只在失败时处理。",
            ["experimental"] = "true",
            ["experimentalNote"] = "需多人实测",
            ["performance"] = "low",
            ["performanceNote"] = "仅在打开界面时",
            ["needsRestart"] = "false"
        };
    }

    private static readonly Dictionary<string, string> English = new Dictionary<string, string>
    {
        ["我的模组"] = "My mod", ["就绪"] = "Ready", ["修复"] = "Fixes",
        ["启用修复"] = "Enable fix", ["按需修复此问题。"] = "Applies the fix on demand.",
        ["特定操作失败。"] = "A specific action fails.",
        ["只在失败时处理。"] = "Handles only failed actions.",
        ["需多人实测"] = "Multiplayer testing needed",
        ["仅在打开界面时"] = "Only when opening the UI"
    };
    public static string DdenLocalize(string original, string language)
        => language == "en" && original != null && English.TryGetValue(original, out var en) ? en : original;

    public static void DdenSetLanguage(string language) { _language = language; }
    // 可选DdenDraw内用_language选择文字；无功能页时省略DdenDraw。
}
```

缺失的新方法不会影响旧配置接管。方法签名错误按未提供处理；方法运行异常有隔离与Debug记录，文字回退原文，标记回退旧规则。首次元数据读取失败也会缓存为无新元数据，避免每帧重试。翻译方法抛异常后该配置块会停用此翻译回调，直到重新扫描，避免持续逐帧异常。接入方不应利用接口异常作为动态可用性信号。

## L2：Shared类型化接口

内部模块可使用`DdenDocs.AddStructured`登记配置的完整说明：

```csharp
DdenDocs.AddStructured(section, key, what, fixes, how,
    isExperimental: true,
    performanceImpact: DdenPerformanceImpact.Low,
    experimentalNote: "需多人实测",
    performanceNote: "仅在打开界面时",
    needsRestart: false);
```

Section／Key必须匹配原配置。`DdenPerformanceImpact.Unspecified`表示未指定，不能替代none。模组可将英文完整原文映射放入内嵌JSON，使用`DdenTextCatalog`和`DdenLocalization.Register(Config, catalog)`；卸载时Unregister。嵌入`localization.en.json`时设置`WithCulture=false`，不要让资源被拆进独立语言卫星程序集。显示模板通过`catalog.Format(FormattableString)`格式化，源元数据保持原文。

需要自定义展示元数据的模块同时实现`IDdenConfigPolicy`和`IDdenConfigPresentation`，将`DdenConfigMetadata`交给`Metadata(entry)`，翻译由`Localize(original, language)`提供。类型字段对应L1：`DisplayName`、`Description`、`Problem`、`Method`、`IsExperimental`、`ExperimentalNote`、`PerformanceImpact`、`PerformanceNote`、`RestartRequired`。其中布尔字段可空，表示未指定。只修改显示标记不能代替业务策略。

## 特殊显示与编辑策略

| 来源 | 管理器行为 |
| --- | --- |
| 实验性状态 | 通用配置行使用实验性强调色，提示显示本语言状态与备注；不可编辑行优先显示禁用样式 |
| 性能等级／备注 | 提示统一显示等级和备注；不从描述词语猜测新元数据的等级，不据此停用功能 |
| `needsRestart`／`RestartRequired` | 提示明确说明需要重启 |
| `IDdenModuleGate` | 整体禁用时保留MasterKey对应原配置的编辑入口 |
| `IDdenConfigPolicy` | 根据CanEditConfig决定编辑权，ConfigDisplayValue显示实际值，ConfigNotice提供状态说明；主机临时值不覆盖本机cfg |
| `IDdenConfigAvailability`／L1 `DdenUnavailable` | 非空原因置灰并解释缺失依赖等限制；模组负责真正停用对应功能 |
| `IDdenModuleHealth` | 使用Ok／Warn／Severe状态决定健康显示；不要从翻译后的状态文字判断颜色 |

## 兼容优先级

1. 配置块提供的新字段优先，其次使用`DdenDocs.AddStructured`登记的类型化字段。
2. 未提供实验性状态时，继续兼容旧`Doc.Experimental`以“是”或“Yes”开头的检测；直接修改备注不会改变新状态。
3. 未提供性能等级时，保留旧`Doc.Perf`显示；可识别的无／极低／低／中／高及英文等级会规范显示，括号备注保留。无法识别的旧说明原样展示，不猜测等级。
4. 原八参数`DdenDocs.Add(string,string,string,string,string,string,string,bool)`继续保留，已编译调用和按参数类型反射的L1模组不必更新。新接入优先使用元数据桥或AddStructured。
5. 未提供翻译接口／资源时保留原文；不强制外部模组引用Shared，也不修改现有外部模组文件。

## 配置页导航与折叠

全部配置按模块折叠，Section仍仅分组，不单独折叠。折叠键使用模块Id或外部提供类型身份，不使用译名。折叠后重新按可见内容计算滚动高度。

管理器配置使用保留路由`config:@manager`，从总览或侧栏进入；内部`config:<id>`、外部`extconfig:<index>`仍保留。内部IDdenModule必须提供DrawPage；外部DdenDraw可省略。配置页“返回”打开对应功能页，无功能页返回总览。侧栏在配置页仍选中该模组，再点侧栏打开功能页；无功能页继续打开配置。外部列表索引不作为持久化身份。

验证至少覆盖：双语不改变配置身份／值、显式false覆盖旧“是”、缺省新标记兼容旧项、低／中／高转换和备注、缺译回退、原L1反射签名、外部语言通知、折叠高度与返回路径。离线通过不等于已完成游戏内绘制和联机验收。

## 组件版本与适配声明

Shared当前为产品v2.0.0、公共API v1；显示规范v1不等于管理器／Shared产品版本。模块通过可选IDdenModuleVersion分别声明最低管理器、最低Shared、两者已校对版本及API代际；未声明或外置L1配置免检。接口、比较规则与维护位置见[组件版本与适配校对](component-versions.md)。管理器仅提示，不借此改动功能开关。
