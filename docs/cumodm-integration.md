# DDEN管理器接入约定

面向模组作者，以 `DDEN.Shared/ExternalConfig.cs`、`Hub.cs`、`DdenDocs.cs`及管理器当前代码为准。管理器默认F11，冲突时从F10至F1查找，F12保留给Steam截图。文字、双语、实验性／性能等级及显示样式统一遵循[管理器显示接入规范 v1](manager-ui-contract.md)。

| 模式 | 需要Shared引用 | 用途 |
| --- | --- | --- |
| 不接入 | 否 | 仍可出现在兼容性插件清单，不接管配置 |
| L1反射桥 | 否 | 外部模组交出原始ConfigEntry，由管理器生成配置页，可选自定义功能页 |
| L2内部模块 | 是 | 实现IDdenModule，登记功能、配置、总览状态与健康信息 |
| 使用公共能力 | 是 | 按需使用DdenUI／Compat／Mp／Log／SaveCodec；不是引用后自动获得全部修复 |

## 1. L1：零Shared引用交配置

在插件自己的程序集内提供public类型，配置成员为public static：

```csharp
using BepInEx.Configuration;

public static class DdenBridge
{
    public static ConfigFile DdenConfig => MyPlugin.Instance.Config;
    public static string DdenTitle => "我的模组";
    public static string DdenSummary => MyPlugin.Ready ? "就绪" : "功能受限";

    // 可选：非空原因会使该配置项不可编辑。
    public static string DdenUnavailable(ConfigEntryBase entry)
        => MyPlugin.CanUse(entry) ? null : "缺少所需模组";

    // 可选：自定义功能页；不需要时完全省略。
    // public static void DdenDraw(UnityEngine.Rect area) { ... }
}
```

示例中的MyPlugin是接入方自己的类型，不是Shared现有类型。无管理器时继续正常工作，不能为了L1接管配置再引入Shared硬依赖。

### 支持成员

| 成员 | 约定 |
| --- | --- |
| `DdenConfig`／`DdenConfigEntries`／`DdenConfigFile` | 三种名字择一，值可以是ConfigFile、ConfigEntryBase或条目集合；支持public static属性／字段／无参方法 |
| `DdenTitle` | 可选string，同样支持上述成员形式；缺省使用插件名称 |
| `DdenSummary` | 可选string，最多每秒刷新一次；不在取值时扫描全场或做昂贵工作 |
| `DdenDraw(Rect)` | 可选public static void方法，绘制功能页 |
| `DdenConfigMetadata(ConfigEntryBase)` | 可选public static IDictionary<string,string>方法，提供显示文字、实验性真实状态、性能等级与备注；字段及优先级见[显示规范](manager-ui-contract.md) |
| `DdenLocalize(string,string)` | 可选public static string方法，接收原文和zh-CN／en，未知文字回退原文 |
| `DdenSetLanguage(string)` | 可选public static void方法，接收初次语言及实际语言变化通知，供自定义功能页使用 |
| `DdenUnavailable(ConfigEntryBase)` | 可选public static string方法；null／空表示允许，非空为禁用原因，异常则显示暂不可用 |

扫描在管理器启动时运行：从已加载插件实例获取程序集，跳过Shared本身，逐一检查public类型，取首个交出有效配置的类型。不要依赖交出配置后再添加空引用、卸载插件或改变列表索引仍保持相同路由身份。

修改值直接作用于原ConfigEntry，不是复制一份。是否自动落盘取决于所属ConfigFile的保存策略；接入方仍负责SettingChanged、校验和重建运行对象。界面置灰不会自动替外部模组关闭其补丁；依赖缺失时的功能停用必须由模组自己保证。

### 配置页与功能页

- 独立配置路由：`extconfig:<index>`。总览“配置”和全部配置“单独看”始终进入这里，通用绘制条目。
- 外部功能路由：`ext:<index>`。存在DdenDraw时调用自定义页，否则回退配置绘制；管理器另有“打开界面／功能页”入口。
- 没有DdenDraw也能获得独立配置页；不要用空的DdenDraw冒充接管配置页。
- 条目按原Section分组，值仍使用原Key。配置与列表高度按实际内容计算，长说明通过提示显示；不要求接入方复制管理器控件。全部配置按模组折叠，不折叠Section；单独配置页“返回”进入本模组功能页，无功能页则回总览。侧栏保持对应模组选中，再点击打开功能页，无功能页继续配置。管理器自身配置也可从总览／侧栏进入。

### 可选完整说明

DDEN配置提示含功能、适用问题、实验性和性能影响；既有MPfix保留解决方案说明，其他模块不在面板展示开发实现。新接入使用独立状态／等级和备注字段，管理器决定语言和格式：

```csharp
DdenDocs.AddStructured(section, key, what, fixes, how,
    isExperimental: false, performanceImpact: DdenPerformanceImpact.Low,
    performanceNote: "仅在打开界面时", needsRestart: false);
```

键必须与原ConfigEntry的Section／Key一致。L1模组不硬引用这个API，优先提供DdenConfigMetadata和DdenLocalize。原八参数DdenDocs.Add仍保留，兼容已有反射注册与旧前缀检测；详见[字段、示例与兼容优先级](manager-ui-contract.md)。需要重启的设置明确写出，不让玩家误以为即时生效。

## 2. L2：内部DDEN模块

生产项目引用Shared，实现 `IDdenModule`：

```csharp
string Id { get; }
string DisplayName { get; }
string Version { get; }
bool Ready { get; }
string Summary { get; }
IEnumerable<ConfigEntryBase> ConfigEntries { get; }
void DrawPage(UnityEngine.Rect area);
bool DrawSettings(UnityEngine.Rect area);
```

在Awake调用 `DdenHub.Register(this)`。DrawSettings返回false时交给管理器通用配置绘制；管理器缺席可用DdenUI.DrawStandalone，不要在管理器已有窗口时另开同内容窗口。

可选接口：

| 接口 | 用途 |
| --- | --- |
| `IDdenModuleGate` | ModuleEnabled与完整MasterKey，让模块停用时主开关仍可编辑 |
| `IDdenModuleHealth` | Health与HealthNote，报告错误／终止状态 |
| `IDdenConfigPolicy` | 逐项可编辑策略、实际显示值及配置通知，例如主机临时覆盖 |
| `IDdenModuleVersion` | 可选声明最低管理器、最低Shared、两者已校对版本及Shared API代际；免检与比较规则见[版本契约](component-versions.md) |
| `IDdenConfigPresentation` | 与配置策略一起提供规范化元数据及显示翻译，供通用配置页绘制 |
| `IDdenConfigAvailability` | 对禁用项提供原因 |

参与环境核对的模块显式 `DdenGate.RegisterParticipant(id)`，自行尊重Blocked。当前VersionOk仅比较Together 5.5.5，Blocked还要求已安装Together且未选择忽略；不比较Unity版本，也不能把忽略开关当作测试通过。

## 3. 公共能力与安全边界

- UI使用实际存在的DdenUI控件，如Button、CheckBox、TextField、DrawSprite、DrawConfigGrouped；滚动容器成对退出，图标按比例绘制，提示用Hover而非残留的GUI.tooltip。不要照旧文档调用不存在的Toggle。
- 日志用 `Log.For(Logger, tag, verbose)`建立通道；共享层静态Log由管理器Attach。生命周期、绘制和异步回调异常应留痕并限制影响。
- Compat提供依赖清单、抢键检测与QoL忙检查。快捷键占用来自当前环境，不把历史F8／F9／F10／F11分配写死。
- Mp是CUCoreLib反射桥，没有依赖时保持可加载。底层RegisterServerHandler／RegisterClientHandler返回void；Shared现有bool封装不能直接证明底层是否注册成功，不能把它等同发包回执。Broadcast／Request的发送结果也不代表远端已执行。重要状态请求主机验证，检查连接、世界与请求对应关系。
- SaveCodec提供读写／元数据同步。保存前检查QoL自动存档、外部文件变化，只改用户字段并保留未知内容；不把“同步到角色”当成写盘。
- 跨Together、QoL或其他DDEN模块用契约／反射，不硬引用；独立L1模组也不需要复制Shared的全部能力。

## 4. 不要重复叠加旧修复

CCL已负责正常注册与生成模组物品；MPfix仅在主机原生合成结果失败时补救，不能再写一个生成前缀覆盖已有结果。物理保险只在实际丢弃处理，不在HeldQuiet.Restore／生成时被动扫描，不挪物品。

主机开无芯片而客机仍有芯片、三条近战分支的补倍率路径等，详见 [功能说明](features.md)。不要把“已注册物品”理解为所有联机兼容问题自动得到保证。

## 5. 样板与排查

[ET-Fix](../src/外部mod/ET-Fix/README.md)展示完整L1零Shared引用配置桥、依赖禁用及可选说明；其检查工具覆盖实际扫描。旧`tools/TestExternalConfig`手工探针在当前工作树已移除，不再作为现有检查工具或运行步骤。

配置没出现时检查public static、类型public、返回值和初始化时机；页面存在但改值无效时检查模组自己的生效逻辑；配置被禁用时检查可用性原因。扫描或绘制成功不能替代功能及真实多人验收。

后续双语扩展目前只是 [计划](localization-plan.md)，本版没有承诺语言提供者接口；不能在此文档里伪造一个已实现约定。
