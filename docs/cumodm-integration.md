# DDExcNet CUmodM 兼容 / 接入文档

> 面向**别的模组作者**：你的模组怎么和这个管理器共存、怎么把配置交给它渲染、怎么避免互相打架。
> 本文所有接口都来自实际代码（`src/DDEN.Shared/*.cs`、`src/DDExcNet.CUmodM/*.cs`），不是设想。

管理器 = `DDExcNet-CUmodM.dll`（GUID `com.dden.cumodm`，默认热键 F11）。
它只做四件事：**统一功能界面**、**统一配置界面**、**兼容性总控**、**联机桥/存档编解码等公共能力**。

---

## 0. 接入深度一览（挑一个就行）

| 级别 | 你要做的事 | 需要引用 DDEN.Shared 吗 | 你能得到什么 |
| --- | --- | --- | --- |
| **L0 什么都不做** | 无 | 否 | 出现在「兼容性」页的插件清单/补丁栈里；管理器不会动你的配置 |
| **L1 交出配置（推荐给大多数模组）** | 在插件程序集里加 1 个成员（见 §1） | **不需要** | 你的所有 `ConfigEntry` 出现在「全部配置」页，按插件分组、两行式渲染、可直接改值并写回 cfg；**并且自动获得一个独立配置页**（侧边栏「外部模组」、总览卡片、全部配置页的【单独看】三个入口都进得去） |
| **L2 注册成 DDEN 模块** | 引用 `DDEN.Shared.dll`，实现 `IDdenModule`（见 §2） | 需要 | 额外获得：侧边栏入口、自己的功能页、自己的配置页、总览卡片、状态行 |
| **L3 用公共能力** | 调用 `Compat` / `Mp` / `Game` / `DdenUI` / `Log` / `SaveCodec`（见 §3） | 需要 | 快捷键仲裁、联机频道、日志通道、统一皮肤、存档编解码、物理保险等 |

---

## 1. L1：把配置交给管理器（**零引用**，反射约定）

### 1.1 你只需要写这些

在你的插件程序集里，任意一个 **public 类型**上放一个 **public static** 成员：

```csharp
using BepInEx.Configuration;

namespace MyMod
{
    public static class DdenBridge
    {
        // 三选一：交 ConfigFile / 交条目集合 / 交单个条目
        public static ConfigFile DdenConfig => MyPlugin.Instance.Config;

        // 可选：显示名（不给就用插件名）
        public static string DdenTitle => "我的模组";

        // 可选：总览里的一行状态
        public static string DdenSummary => MyPlugin.Ready ? "就绪" : "降级";

        // 可选：自定义一整页（管理器把内容区矩形交给你，你想画什么画什么）
        public static void DdenDraw(UnityEngine.Rect area) { /* 用 GUI.* 画 */ }
    }
}
```

### 1.2 管理器认的成员名

| 成员 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `DdenConfig` / `DdenConfigEntries` / `DdenConfigFile` | `ConfigFile`、`IEnumerable<ConfigEntryBase>`、`ConfigEntryBase`、或它们的 public static 属性/字段/无参方法 | **是（三选一）** | 交出哪些配置 |
| `DdenTitle` | `string`（属性/字段/无参方法） | 否 | 「全部配置」页里的分组标题 |
| `DdenSummary` | `string`（同上） | 否 | 标题右侧的一行状态 |
| `DdenDraw` | `public static void DdenDraw(Rect area)` | 否 | 自定义页面内容；**不给也行** —— 不给就直接把你的 `ConfigEntry` 画成配置列表 |

扫描规则（`DdenExternal.Scan()`）：
- 遍历 `Chainloader.PluginInfos` 里**每个已加载插件**的程序集，跳过 DDEN 自己的 dll；
- 只看 `public` 类型；逐个类型找上面 4 个成员，找到第一个「交了配置」的类型就用它（每个插件最多一份）；
- 任何反射异常都被吞掉并写 Debug，**不会影响你的模组**；
- 扫描时机：管理器 `Start()`（所有插件 Awake 之后）。**改完代码重启游戏才生效**。

### 1.3 渲染规则（你不需要适配，但值得知道）

- 一条配置 = **两行**：上行「键 + 控件」，下行说明（`ConfigEntry.Description`）；说明过长会省略，鼠标悬停看全文；
- 按 `ConfigEntry` 的 **Section** 自动分组（`▸ 无芯片同步` 这种小节标题）；
- 控件：`bool` → 开关；`int/float` → 输入框；`KeyboardShortcut` → 点一下然后按键；
- 改值直接写 `ConfigEntryBase.BoxedValue` → BepInEx 立刻落盘到你的 cfg；
- **热重载类配置（需要重建对象的）请自己在 `SettingChanged` 里处理**，管理器只负责改值。

### 1.4 注意

- 不要把**别的模组**的 `ConfigEntry` 交出来 —— 只交自己的；
- 成员的 `public static` 类型必须是**你自己的程序集**里的（否则可能被反射到别人的类型上）；
- 如果你的配置项里有敏感值（token 之类），别交出来。

---

## 2. L2：注册成 DDEN 模块（需要引用 `DDEN.Shared.dll`）

### 2.1 引用方式

```xml
<Reference Include="DDEN.Shared">
  <HintPath>$(GameDir)\BepInEx\plugins\DDEN\DDEN.Shared.dll</HintPath>
  <Private>false</Private>   <!-- 关键：不要把 Shared 复制进你的输出，否则会加载出第二份静态状态 -->
</Reference>
```

### 2.2 最小实现

```csharp
[BepInPlugin("com.you.mymod", "My Mod", "1.0.0")]
[BepInDependency("com.dden.cumodm", BepInDependency.DependencyFlags.SoftDependency)]
public class MyPlugin : BaseUnityPlugin, IDdenModule
{
    public string Id => "mymod";
    public string DisplayName => "我的模组";
    public string Version => Info.Metadata.Version.ToString();
    public bool Ready => true;                       // 依赖缺失时给 false（总览会标“降级”）
    public string Summary => Ready ? "运行中" : "缺少前置";
    public IEnumerable<ConfigEntryBase> ConfigEntries => Config.Entries();

    private void Awake()
    {
        // 注册进管理器（管理器没装时这个调用照样安全）
        DdenHub.Register(this);
        // 若你也有自己的窗口：管理器在跑时不要再自己开
        if (!DdenHub.ManagerLoaded) { /* 自己开小窗兜底 */ }
    }

    // 功能页：管理器把内容区矩形给你
    public void DrawPage(Rect area) { /* GUI.* */ }

    // 配置页：返回 false = 让管理器用通用渲染器列 ConfigEntries
    public bool DrawSettings(Rect area) => false;
}
```

### 2.3 `IDdenModule` 契约表

| 成员 | 必须 | 说明 |
| --- | --- | --- |
| `Id` | ✅ | 小写唯一；管理器用它做页面路由（`DdenHub.OpenModule("mymod")`） |
| `DisplayName` | ✅ | 侧边栏 / 总览显示名 |
| `Version` | ✅ | 显示用 |
| `Summary` | ✅ | 一行状态（总览卡片） |
| `Ready` | ✅ | 依赖是否满足；false 时卡片标红 |
| `DrawPage(Rect)` | ✅ | 功能页 |
| `DrawSettings(Rect)` → `bool` | ✅ | 返回 `true` 表示已自绘；`false` 交给管理器渲染 `ConfigEntries` |
| `ConfigEntries` | ✅ | 参与「全部配置」页的条目，一般 `Config.Entries()` |

### 2.4 管理器缺席时

- `DdenHub.ManagerLoaded == false` → 你应当降级成自己的小窗；
- 共享层已经提供了这套兜底：
  ```csharp
  DdenUI.DrawStandalone(rect, this, ref scroll, showConfigTab: true);
  ```
  它自己处理标题栏、滚动、配置页，风格和管理器一致。

---

## 3. L3：公共能力（都在 `DDEN.Shared`）

### 3.1 兼容性（先问，别自己猜）

| API | 用途 |
| --- | --- |
| `Compat.Rescan()` | 重扫插件清单 / 旧 dll / 补丁栈 |
| `Compat.ResolveHotkey(entry, selfGuid, autoFix)` | 你的热键被别人占用时自动换成空键并写回配置 |
| `Compat.FindHotkeyOwners(key, selfGuid)` | 谁占了这个键 |
| `Compat.IsQolAutosaveBusy()` | QoL 正在自动存档 → 你别写盘 |
| `Compat.ConsoleHasCommand(name)` | 游戏控制台指令是否已存在（避免撞名） |
| `Compat.InMultiplayer()` / `Compat.Headline()` | 联机状态 / 一行摘要 |

### 3.2 联机（CUCoreLib 桥，**不要自己写 LiteNetLib**）

| API | 用途 |
| --- | --- |
| `Mp.IsRunning` / `Mp.IsServer` / `Mp.IsClient` | 状态 |
| `Mp.RegisterServerHandler(channel, token => reply)` | 主机处理请求并回包 |
| `Mp.RegisterClientHandler(channel, token => {})` | 客机收广播 |
| `Mp.RequestServer(channel, payload, resp => {})` | 客机请求主机 |
| `Mp.Broadcast(channel, payload, includeHost)` | 广播 |
| `Mp.RegisterSyncModule(key, capture, apply)` | 快照式状态同步（像我们的无芯片那样） |

> **频道命名规范**：`<你的前缀>.<用途>`，例如 `ddn.mp.wearfix`、`cumod.unchipped`。
> 不要用裸名字，避免和 CUCoreLib / Together / 其它模组撞。

### 3.3 游戏对象

| API | 用途 |
| --- | --- |
| `Game.InWorld` / `Game.World` / `Game.LocalBody` | 常用状态 |
| `Game.Alert(text)` / `Game.Console(text)` | 屏幕提示 / 控制台输出 |
| `LiveFields.ApplyNumber(...)` / `ApplyBool(...)` | 运行时直接写游戏字段（带缓存） |

### 3.4 UI

| API | 用途 |
| --- | --- |
| `DdenUI.ConfigRow(rect, entry)` / `DrawConfigGrouped(area, entries)` | 复用管理器的两行式配置渲染（**推荐**，保证观感一致） |
| `DdenUI.Button / Toggle / Label / Section / Fill / Frame` | 统一皮肤的基础件 |
| `DdenUI.Hover(rect, text)` + `DdenUI.BeginFrame()` + `DdenUI.DrawTooltip(bounds)` | 悬停提示（**别用 `GUI.tooltip`**，会残留成跟鼠标的黑框） |
| `DdenUI.ElideToWidth(text, width)` | 中文行高 + 定宽列截断 |

### 3.5 日志

```csharp
internal static LogChannel Log = DDEN.Shared.Log.For(Logger, "MyMod", verbose);
Log.Info(...); Log.Warn(...); Log.Exception(ex, "...");
```
统一前缀方便 grep：`[MyMod] ...`。共享层内部用静态 `Log`（管理器 `Awake` 里 `Log.Attach` 兜底）。

### 3.6 存档

`SaveCodec.Read / Write / SyncMeta`：`*.sv = GZip(ASCII JSON)`，写盘走「临时文件 → `.cumod.bak` → 顶替」。
写之前**必须**查 `Compat.IsQolAutosaveBusy()`。

---

## 4. 必须遵守的五条（否则就是“打架”）

1. **不要和管理器抢窗口**：`DdenHub.ManagerLoaded` 为 true 时不要再自己开 `GUI.Window`。
2. **热键走仲裁**：`Compat.ResolveHotkey(...)`，别硬编码 F8/F9/F10/F11（它们已被占：F8 存档编辑器 / F9 自动排序 / F10 MPfix 兜底 / F11 管理器）。
3. **不硬引用别人的 dll**（Together / QoL / 我们的模块）：跨模组调用一律反射或走 `IDdenModule`。
4. **Harmony 补丁要能撤**：`new Harmony(guid).UnpatchSelf()`，并对顺序敏感的打 `[HarmonyAfter("CasualtiesMP","net.cucorelib")]`。
5. **异常不能冒到 Unity**：所有 `Update/OnGUI/协程` 都包 try/catch，走 `Log.Exception`。

---

## 5. 联机场景：这些坑已经有主了，别重复造

Together/CasualtiesMP **5.5.3 / 5.5.4** 已验证存在下面三个问题，`DDEN.MPfix` 已修，你的模组**不需要再修一遍**：

| 现象 | 根因（反编译确认） | 我们的处理 |
| --- | --- | --- |
| 合成模组物品报「无法合成：无法生成物品」 | `RecipeResult_SpawnResult` 用 `Utils.Create` → `Resources.Load(id)`，模组物品不在 Resources | `Utils_Create_ModItemPatch`：Resources 缺失时走 `CUCoreLib.Helpers.CustomInstantiate` |
| 主机看到客机装备“掉下去又回来” | `ServerReceiver__ItemWearing` 以 `TryGetItem(登记表)` 为门控 | `WearGhostFix`：客机请求 → 主机按 id 找到物品 → 补登记 → `Body_ForceWearWearable` |
| 丢东西穿墙往下掉 | `Together.HeldQuiet` 把碰撞体关掉、`Restore` 又把“关”写回，而 `rb.simulated` 已被打开 | `ItemPhysicsGuard`：`HeldQuiet.Restore` 后强制开碰撞体 + 掉落/生成时物理保险 |

**你自己新增物品时**只要走 `CUCoreLib.Registries.ItemRegistry.Register(...)`，上面这些兜底就自动覆盖你。
自己 `new GameObject` 的野路子不会被覆盖 —— 那种请自行 `NetObjectRegistry.Server_EnsureItemIsNetworkRegistered`。

---

## 6. 现在的“配置归属”清单

管理器的「全部配置」页会列出：**每个 DDEN 模块** + **管理器自身** + **每个交出配置的外部模组**。

| 分组 | 谁在管 | 典型项 |
| --- | --- | --- |
| 多人修复（`mpfix`） | DDEN.MPfix | 无芯片同步（Enabled / ForceHostUnchipped / OverrideIndividualRule / WatchdogSeconds / AnnounceToOtherPlayers）、联机合成（FixModItemCraftSpawn / RegisterSpawnedItemsOnHost）、穿戴兜底（Enabled / GraceSeconds / MaxPerSession / RebuildIfHostLacksItem）、物品物理（GuardDropPhysics）、UI（StandaloneKey）、Debug（Verbose） |
| 存档编辑器（`saveeditor`） | DDEN.SaveEditor | UI / Debug |
| 管理器自身 | DDExcNet-CUmodM | ToggleKey / Scale / AutoFixHotkeyConflicts / ScanOnStart / ModalBlocker / BlockerDim / Verbose |

---

## 7. 故障排查

| 现象 | 检查 |
| --- | --- |
| 我的配置没出现在「全部配置」 | 成员是不是 `public static`？类型是不是 `public`？名字拼写（`DdenConfig`）？改完有没有重启（扫描只在启动时） |
| 出现了但值改了没生效 | 你的配置本来就是“启动时读取一次”的类型 → 在 `SettingChanged` 里自己重建对象 |
| 「单独看」按钮不出现 | 没提供 `public static void DdenDraw(Rect)` |
| 我的热键失效 | 被别的模组先注册了 → 用 `Compat.FindHotkeyOwners` 查，再 `ResolveHotkey` 换键 |
| 管理器没加载 | 管理器也是可选前置：`DdenHub.ManagerLoaded == false` 时必须能独立工作（用 `DdenUI.DrawStandalone` 兜底） |
| 日志里没有我的行 | 用 `Log.For(Logger, "MyMod")`，并确认 `Log.Attach` 已由管理器完成（或你自己的插件里先 `DDEN.Shared.Log.Attach(Logger, "MyMod")`） |

---

## 8. 附：可复制的样板

`src/DDEN.SaveEditor/` 是最小的“完整模块”样板（注册 + 功能页 + 配置页 + 自检），
`src/DDEN.MPfix/` 是“联机 + 频道 + 反射调 Together”的样板。
新增模块的最快路径：

```bash
cp -r src/DDEN.SaveEditor src/DDEN.MyMod
# 改 csproj: AssemblyName / RootNamespace / Version
# 改插件: [BepInPlugin("com.dden.mymod", ...)] + 实现 IDdenModule + DdenHub.Register(this)
dotnet sln CUmod.slnx add src/DDEN.MyMod/DDEN.MyMod.csproj
./scripts/build.sh
```
