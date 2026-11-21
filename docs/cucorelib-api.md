# CUCoreLib API 速查 · DDEN v1.7.1

> 对照本机CUCoreLib 1.1（GUID `net.cucorelib`）的public接口整理；这是安装版本的阅读记录，不是对未来版本的承诺。含`...`或斜线并列的是速记，不能原样编译。加号 `+` 表示嵌套类型。实际参数／返回值以对应DLL为准。

## 1. 联机：判定 + 频道

`CUCoreLib.Networking.MultiplayerApi`

```csharp
static bool IsAvailable / IsRunning / IsClient / IsServer / IsHost / IsConnected
static Assembly KrokMpAssembly
static Type ResolveKrokMpType(string name)                 // 带兼容别名：KrokoshaCasualtiesMP.* → Together.*
static MemberInfo FindKrokMpMember(Type type, string name)
static MethodInfo FindKrokMpMethod(Type type, string name, Type[] parameterTypes = null)
```

`CUCoreLib.Networking.MultiplayerBridge`（真正发字节的地方，`JToken` 载荷）

```csharp
static void RegisterServerHandler(string channel, Func<JToken, JToken> handler)   // 返回值 = 回给请求方
static void RegisterClientHandler(string channel, Action<JToken> handler)
static bool SendToServer(string channel, object payload = null, bool reliable = true)      // 不回包
static bool RequestServer(string channel, object payload, Action<JToken> onResponse, bool reliable = true)
static bool Broadcast(string channel, object payload = null, bool includeHost = false, bool reliable = true)
static bool SendToClient(uint clientId, string channel, object payload = null, bool reliable = true)
static bool BroadcastEverywhere(...) / BroadcastToPeer(string channel, uint clientId, ...)
```

坑：
- 频道名自己带前缀，别撞 `cucorelib.*`；
- `Broadcast` 只在主机端有效（`!IsServer` 直接返回 false）；
- `BroadcastEverywhere`等转发入口另走`BroadcastRelayed`，可以请求主机转发。不能把“直接Broadcast仅主机可用”推导成收到的任意频道广播都可信；重要状态应向主机请求核验，并关联本次连接／世界／请求。

## 2. 联机：状态快照（晚进主机也能同步）

`CUCoreLib.Networking.MultiplayerSyncRegistry`

```csharp
static void RegisterModule(string key, Func<JObject> capture, Action<JObject> apply = null)
static JObject CaptureSnapshot(string targetLanguage = null)
static void ApplySnapshot(JObject snapshot)
static void ScheduleInitialSnapshot() / RequestInitialSnapshot() / BroadcastSnapshot(bool includeHost=false)
static void QueueHostSnapshotBroadcast()
```

主机 `capture` → 打进快照 → 客机 `apply`。无芯片同步将它用于主机实际规则下发；还另有变更广播及客机20秒主动请求，并非只靠快照。

## 3. 控制台指令

`CUCoreLib.Registries.ConsoleCommandRegistry`

```csharp
static void Register(string name, string description, Command.Action action,
                     Dictionary<int, List<string>> argAutofill = null, params (string,string)[] argDescription)
static void Register(Command command)
```

注意 `action` 的类型是游戏的 `Command.Action`（`delegate void Action(string[] args)`），lambda 要写成 `(string[] args) => { ... }`。
注册后会自动注入 `ConsoleScript.Commands`。

## 4. 存档扩展点

`CUCoreLib.Registries.SaveRegistry`

```csharp
static void RegisterGlobalProvider(string key, ICustomSaveProvider provider)
static void RegisterItemProvider  (string key, IItemSaveProvider provider)
static void RegisterBodyProvider  (string key, IBodySaveProvider provider)
static void RegisterLimbProvider  (string key, ILimbSaveProvider provider)
static void RegisterWorldProvider (string key, IWorldSaveProvider provider)
```

存档里会落成 `save.sv → "CUCoreLib" → { version, global, world, … }`（实测真实存档可见），
自带 `cucorelib.itemRuntime` / `cucorelib.bodyStatuses` / `cucorelib.limbStatuses` / `cucorelib.buildings` 四个内置 provider。

## 5. 内容注册表（做“自己的内容模组”主要用这些）

```csharp
// 物品
ItemRegistry.Register(string id, ItemInfo info, Sprite icon, int spawnFrequency = 1)
ItemRegistry.Register(string id, CustomItemInfo info, Sprite icon = null)
ItemRegistry.TryGetCustomInfo(Item item, out CustomItemInfo info)
ItemRegistry.GetCustomData<T>/SetCustomData/RemoveCustomData/GetAllCustomData     // 物品上的模组私有数据
ItemRegistry.BeginOwnerRegistration(string ownerId)                              // 归属作用域

// 液体
LiquidRegistry.Register(string id, CustomLiquidInfo info)
LiquidRegistry.TryGetCustomInfo(string id, out CustomLiquidInfo info)

// 液体方块（可放置/可淹没）
LiquidTileRegistry.Register(string id, CustomLiquidTileInfo info)
LiquidTileRegistry.Place(string id, Vector2 worldPos) / FloodFill(...)

// 心情/状态图标
MoodleRegistry.AddMoodle(int intensity, Sprite icon, string name, string description,
                         bool critical = false, bool chippedOnly = false, bool important = true,
                         string key = null, float holdSeconds = 0.5f)
MoodleRegistry.RegisterBody<TStatus>(Func<Body,TStatus,StatusMoodleDefinition> buildMoodle)
MoodleRegistry.RegisterLimb<TStatus>(...)

// 本地化
LocaleRegistry.Register(LocaleCategory category, string key, string text)
LocaleRegistry.Register(string category, string key, string text)
```

其余同名注册表：`BuildingEntityRegistry`、`StructureRegistry`、`TileRegistry`、`RecipeRegistry`、`ModOptionsRegistry`（`Register(ModOptionDefinition)`）。
细节请看 `decomp/core/CUCoreLib.decompiled.cs` 对应类，别照抄网上的旧版示例。

## 6. 其它常用

```csharp
CUCoreLib.Helpers.CUCoreUtils         // 通用工具 + FriendlyKeybind
CUCoreLib.Helpers.AssetLoader         // 从模组目录/内嵌资源载图
CUCoreLib.Helpers.CCLBody.BodyScope   // 批量改 Body 字段的作用域（性能）
CUCoreLib.Patches.KrokMpCompatibilityPatches / QoLUnknownCompatibilityPatches   // 现成的兼容层，可以参考写法
```

## 7. 环境层面两个必踩的坑

1. **HarmonyX 版本**：`harmony.UnpatchAll(string id)` 已 `[Obsolete(error:true)]`，卸载用自己的实例 `UnpatchSelf()`。
2. **目标框架必须是 `netstandard2.1`**：游戏 `Assembly-CSharp.dll` 与 CUCoreLib 都编到 netstandard 2.1（`Managed/netstandard.dll` 2.1.0.0）。
   不能为兼容旧教程降到netstandard2.0而忽略现有依赖的框架要求。

这些公开注册API不等于ET-Fix当前的保存实现：该修复是对CCL内置物品提供者Capture/Restore的可选后缀挂接，不是注册一个不存在的独立provider。见 [独立修复细节](../src/外部mod/ET-Fix/docs/features.md)。
