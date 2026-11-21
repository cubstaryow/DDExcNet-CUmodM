# 功能详解：现象 · 诱发原因 · 修复路线

> 这份文档是给**想了解原理、或者要接手维护**的人看的，里面全是实现细节、类名、反编译行号。
> 面向玩家的说明在 [../README.md](../README.md)；给别的模组接入用请看 [cumodm-integration.md](cumodm-integration.md)。
> 下面所有结论都对着 **Together / CasualtiesMP 5.5.4** 与 **CUCoreLib 1.1** 的反编译逐点核对过，
> 反编译产物在本地 `decomp/`（不进仓库）。

---

## 一、管理器（`DDExcNet-CUmodM.dll`）

### 1.1 一个窗口承载所有界面

`DdenHub` 是模块注册中心：每个插件在 `Awake` 里 `DdenHub.Register(this)` 并实现 `IDdenModule`
（`Id / DisplayName / Version / Ready / Summary / ConfigEntries / DrawPage / DrawSettings`）。
管理器侧边栏列 `DdenHub.Modules`，内容区调 `module.DrawPage(area)`；配置页一律走 `DdenUI.ConfigRow`，
按 `ConfigEntry` 自动渲染，模块不需要自己写界面。管理器不在时，模块用 `DdenUI.DrawStandalone` 自己开小窗。

**页面路由**（踩过的坑）：页面名除了 `overview / compat / config / about`，还有两种带前缀的形式 ——
`config:<模块id>`（单模块配置页）、`ext:<下标>`（外部模组页）。
`DrawContent` 里**必须先判前缀再 `DdenHub.Find`**：早期漏了 `config:` 这一支，
`Find("config:mpfix")` 返回 null → 静默落回总览，玩家看到的就是“点了【单独看】没反应”。

### 1.2 控件全部自绘

Unity 默认皮肤（`GUI.skin.button / toggle / textField`）是浅灰圆角贴图：贴在深色面板上突兀，
缩放时发糊，和 DDEN 的直角像素风格也不搭。现在统一成「填色块 + 1px 描边 + 直角」：

| 控件 | 实现 | 细节 |
| --- | --- | --- |
| 按钮 | `DdenUI.Button` | 自绘底与描边，`highlight` 用强调色；点击由一层透明 `GUI.Button(...GUIStyle.none)` 吃掉 |
| 复选框 | `DdenUI.CheckBox / CheckBoxLabeled` | 18px 方框；对勾用**三段短横**拼像素勾，不依赖字体里有 `✓` |
| 输入框 | `DdenUI.TextField` | 底/描边/左侧强调条自绘；`FieldStyle` 只保留文字与光标 |
| 数值框 | `DdenUI.NumberField` | 复用 `TextField`，`double.TryParse`（InvariantCulture） |

⚠️ 坑：`GUIStyle` **没有** `background` 属性（编译不过）。要清底图得逐个清
`normal / hover / active / focused.background`，只清 `normal` 仍会画出白框。

### 1.3 其它模组的设置自动接管（零引用）

`DdenExternal.Scan()` 遍历 `Chainloader.PluginInfos` 的每个程序集，用反射找 `DdenConfig / DdenConfigEntries /
DdenConfigFile`（三选一，可以是属性/字段/无参方法）+ 可选的 `DdenTitle / DdenSummary / DdenDraw(Rect)`。
对方**不需要引用 `DDEN.Shared.dll`**。每个交配置的插件算一个 `Block`，入口有三处：
侧边栏「其它模组的设置」、总览卡片、全部配置页的【单独看】—— 都指向 `ext:<i>`。
那一页的规则：有 `DdenDraw` 就画它的自定义页；没有就把它的 `ConfigEntry` 按同样的两行式渲染成配置列表。
实测（2026-11-20）自动认出了 `QoL-EMI-RecipeRowFix`、`ET-NoBatteryFix` 这类第三方修复插件。

### 1.4 兼容性总控（`Compat`）

启动时扫两遍（`Awake` 一次、`Start` 一次，后者等别的插件都就位）：已装插件、Harmony 补丁栈、
旧 dll 残留、快捷键抢占。冲突处置：

- **抢键**：`Compat.ResolveHotkey` 发现目标键已被别的模组用掉，就换一个空闲 F 键并写回配置
  （实测 F8 与 SkinSync 冲突 → 自动改 F11）。
- **补丁栈**：同一方法上叠了多个模组的补丁会在兼容性页标出来（例如 `SaveSystem.SaveGame` 上的
  CasualtiesMP / QoL / CUCoreLib 三方栈），只提示不动手。

### 1.5 输入隔离（这游戏的菜单按钮不走 uGUI）

主菜单按钮是 `AdaptiveButton.Update()` 里**直接读 `Input.GetMouseButtonDown(0)`** 再调 `Clicked()`，
既不过 EventSystem，也不吃 uGUI 遮罩 —— 所以“关 EventSystem”“挂 uGUI 遮罩”都拦不住它。
现方案两层：① `UiGuardPatch` 给 `AdaptiveButton.Clicked` 加 Harmony 前缀，只要 DDEN 面板可见就 `return false`；
② `DdenBlocker` 的 uGUI 遮罩管游戏内其它界面。面板可见性由 `DdenHub.SetPanelVisible(owner, visible)` 维护。

### 1.6 关于页与图标

`_page = "about"`。图标是 `src/DDExcNet.CUmodM/Resources/dragon_cloud.png`（16×16 像素图），
由 csproj 的 `<EmbeddedResource Include="Resources\*.png" />` 打进 dll，运行时按资源名后缀匹配读出、
`Texture2D.LoadImage` 解出，**`filterMode = Point`**（Bilinear 会把像素糊成一团），按整数倍放大使用。
加载成功会打 `[UI] 内嵌图标已加载：16×16` —— 没有这行就是资源没打进去。

---

## 二、多人修复（`DDEN.MPfix.dll`）

每一项都按「玩家看到什么 / 为什么会这样 / 我们怎么修 / 开关与失败行为」写。

### 2.1 无芯片模式不同步（HUD 变了，名牌没变）

**现象**：主机把「无芯片」关掉，客机 HUD 变了但名牌 / 附近语音还按无芯片显示，或者反过来。

**原因**：Together 应用运行设置时只写 `runSettings`，不写 `WorldGeneration.unchippedMode`；
它的 `WorldMP+RunUnchippedLock` 又管着“谁能改”，客机本地值与主机并不天然一致。

**修法**：主机裁决为唯一真值，三条通道下发 ——
① 进局同步（`Mp.RegisterSyncModule` 快照）；② 主机变更时广播（postfix `WorldGeneration.SetUnchipped`）；
③ 客机主动请求。客机侧 `ApplyLocal` 必须**三件一起做**：
`world.SetUnchipped` + 镜像 `RunUnchippedLock`（`ApplyingSynced/Locked/GlobalUnchipped`）+ `ScavPlayer.SetUnchipped`。
漏一个就会出现“世界是无芯片，但名牌还是旧的”。默认每 30 秒自动核对一次（`WatchdogSeconds`，旧配置 0.5 自动迁移）。

### 2.2 联机合成提示“无法生成物品”

**现象**：联机合成模组加的东西，客机上提示无法合成 / 无法生成物品；“过一层又能合成”。

**原因**：Together 在主机侧重写了一份生成流程：`PlayerCamera_TryCraft_MultiplayerPatch.RecipeResult_SpawnResult`
→ `Utils.Create` → `Instantiate(Resources.Load(id))`。模组物品是运行时从 AssetBundle / 文件加载的，
不在 Unity `Resources` 里 → `Resources.Load` 返回 null → `CraftReason.SpawnFailed`。
Together 还会按 attempt 缓存失败回执，同一会话重试仍然失败 —— 这就是“换层/重连之后又能做”。

**修法**：给 `Utils.Create(string, Vector2, float)` 加前缀，`Resources` 找不到时只走 CUCoreLib 两条正路：
① `CustomInstantiate.ResolvePrefab(id)` 确认带 `Item` 组件 → `Instantiate`；② `CustomInstantiate.InstantiateReturn`。
两条都不通就**放弃兜底交回原版**（该配方该失败就失败），打一条
`[CraftFix] 生成失败 '<id>'：…（含注册表是否命中）`（同 id 30 秒去重）。
**绝不克隆现存物品、绝不做任何 Destroy** —— 克隆容器会把内容物一起复制（凭空刷物品）。
生成成功后在主机侧 `Server_EnsureItemIsNetworkRegistered` 补登记。

**客机永不本地生成**（`Mp.IsRunning && !Mp.IsServer` 时前缀直接返回 true）：商人购买 / 合成 / 水晶掉落
在客机上也会跑一遍本地预测，客机自己造出来就会和主机同步来的那份重复（典型症状：**买东西变成两份**）。

### 2.3 穿戴兜底

客机穿戴后延迟 1.5 秒请求主机，最多三次。主机只找这名玩家自己的物品或附近活跃、无主的自由物品，
同类候选不唯一就保留现状，不拿储物容器、其他玩家或离线存档里的装备来猜。
由原联机穿戴方法处理并验证，再广播状态；不重建、不复制、不主动丢出。
次数上限在主机实际执行，每局重置；开关关闭、断联与换层取消旧请求和回包，发送失败也计入有限重试。
默认关闭。

### 2.4 丢弃物品的碰撞保险

仅在玩家实际丢弃物品时补开碰撞体，不挂被动恢复、不移动物品、不改刚体类型。
记录待丢物品使用每次调用独立的 Harmony 状态，避免嵌套丢弃覆盖另一件物品。
重叠检查改为检查缓冲区内全部碰撞体，不能碰巧先遇到自身或地面就漏掉旁边的实体。
只处理自由物品；与其他实体重叠或判断不完整时保留现状，不新增持续扫描/重试。

### 2.5 已删除：装备贴合 / 贴图残留 / 孤儿物品（2026-11）

这三节原来分别写的是「装备浮在角色头顶」「身上挂着装备贴图其实没穿」「物品卡在角色身上」。
**它们对应的模块已整体删除**：`WornItemSnap`、`WearableVisualFix`、`OrphanItemFix`，以及只服务它们的
`NearbyBodies` 缓存和 7 个开关（`物品物理/SnapWornItems`、`物品物理/SnapMaxPerSession`、
`联机修正/CleanStaleWearableVisuals`、`联机修正/FixOrphanItems`、`联机修正/MaxOrphanFixPerSession`、
`性能/ItemSnapScanSeconds`、`性能/WearableVisualScanSeconds`）。

**为什么删**：这三件"病"本来就是早期修复自己造成的（重复修复改了物品父级与 localPosition 导致错位/残留/孤儿），
其中"贴图残留"从未被证实存在（是上一轮 agent 的推测）。用巡检去猜谁改了父级，风险大于收益 ——
真出现浮空先查是谁改了父级，别再叠一套会动物品的修复。
### 2.6 挖块与近战：基础伤害重算 / 激进上报

三个分支开关分别控制方块、箱子/植物/动物（BuildingEntity）与角色/肢体（Body/Limb，含同路径的敌人与 PVP）。
`AggressiveConverge` 是总的激进模式开关，只对两端均已开启的对应分支生效；默认关闭。

| 分支开关 | 激进关闭 | 激进开启 |
| --- | --- | --- |
| SyncBlockBreak | 主机估算补乘世界伤害倍率，再走 MP 原判定 | 同左，另保留客机碎块上报、主机校验补齐的兜底 |
| SyncBuildingDamage | 主机代理客机时，复制攻击参数补乘伤害字段 | 随原攻击消息上报客机伤害与力量，主机校验后在原攻击中结算一次 |
| SyncBodyDamage | 同上，覆盖 DoPVPMeleeDamage | 同上，原来的目标、距离、阵营与攻击频率校验继续执行 |

**代码证据与边界**：MP 取得远端攻击参数时通过 `ForceOnlyRetrieveAttackInfo` 跳过游戏 `Body.Attack`，
因此缺少 `attackdamage` 乘法。基础修复覆盖合法的低于 1 倍与高于 1 倍设置，不改客机/主机自己的攻击。
这只能消除倍率遗漏；工具、身体状态与预测同步时序的其他差异仍可能造成回弹，不能承诺全修。
`AttackInfo` 为引用类型，复制后改 `damage/structuralDamage`，不污染共享模板，也不让世界伤害倍率放大击退。

**激进方块路径**：

- 命中时只捕获块编号，碎掉后记录轻量值；不在命中回调中创建 JSON 或发送消息。
- 0.35 秒攒批，每批最多 64 块，每帧最多发送一批；满批只通知调度器，不在同一帧连续发送。
- 消息仍兼容旧的单条 `{x,y,block,total}` 与 `{items:[...]}`；底层保持可靠发送，发送失败保留队列等待重试。
- 主机每批只解析一次发送者，在收到时校验 12 格距离；执行时再次检查块编号和原伤害记录身份。
- 真正补伤害按 FIFO 分帧处理，每帧最多 16 块，累计达到 1.5ms 后暂停。单块原版破坏流程不能中途抢占。
- 仍要求主机已有对应伤害记录，且已损失血量达到 `BlockBreakMinFraction`（默认 50%）；只补差值。
- 主机已经破坏的方块直接跳过；保留原版掉落/破坏/地图广播。不扫描全地图、不直接删块。
- 删除进程累计 3000 次后永久停报的限制。重复坐标保留 5 秒去重，过期自动清理；换层、断联或关闭开关清空待处理状态。
- 排队预算耗尽只延后处理，不丢弃已接纳任务。持续生产速度高于消费速度会增加队列和确认延迟，需实测。

**激进单位/身体路径**：

- 目前 MP 5.5.4 的可靠攻击包附加 24 字节版本化二进制尾部（分支、伤害、结构伤害、力量）。
  不增加独立包，不走 JSON/GZip/Base64，也不另补一次单位伤害，因此没有两条通道先后顺序导致的双扣。
- 同一次原生服务端攻击在完成目标与距离校验后，消费对应分支上报一次；检查有限值、范围与主机估算的可信比例。
  非法/不可信/没有尾部时退回基础重算，不让一次坏报告影响后续攻击。
- 两端对应分支开关与激进开关均须开启。旧客机没有尾部时使用基础重算；旧主机忽略尾部但不会获得新修复。
- 保留原 MP 的阵营规则、护甲与附加伤害逻辑。它不是“禁止回血”；真实回血继续有效。
- `BuildingEntity` 的原版近战动物 BoxCast 独立分支仍由 MP 原有重跑流程处理，并非所有生物都必经 `DamageBuilding`。
  本次不能据此宣称所有敌人的所有攻击类型均已覆盖。

**验证与限制**：`dotnet run --project tools/DamageRepairTests -c Release` 验证格式、无效数据、可信边界、
一万条队列任务的顺序/不丢失、时间预算与清空。`cumod.selftest` 还检查实际安装的传输类读写以及读指针不变。
启动与界面检查不能替代双端联机验收；帧率收益、身体血量预测覆盖和高速连续挖掘需在两端安装新版后实测。

### 2.7 客机付钱，商人却不给货

**原因**：商人库存由主机维护，客机看到的是某次快照。列表一不同步，
`TraderCommit.HandleBuy` 用 `{index, itemId, value}` 三者在主机校验就会失败 → 扣钱不发货。

**修法**：客机每次打开交易菜单（postfix `PlayerCamera.ToggleTradeMenu`）向主机要一次全量快照，
走 Together 自己的 `TraderSync.SendTraderState(knetid)`（发包内含 items 列表），不自己序列化任何东西。

### 2.8 娱乐开关「道生一 一生二」

主机上给 `Together.TraderCommit.RunBuy(trader, actor, job)` 挂前后缀；购买窗口内由 `Utils.Create` 前缀
记下生成的 id，后缀在主机侧用同一个 id 再造一件给买家。默认关闭，只主机需要开。

复制那件必须走完联机登记流程，否则**买家根本看不到它**（主机多一件幽灵）：
① `Server_EnsureItemIsNetworkRegistered`（= 原版 RunBuy 反编译 142460 那句）→
② 弹药量**保持满弹匣**（用户口径：商人卖出来就是满的；原版 `TryPurchase_rewritten:144184` 会随机 50%~100%，
这里故意不抄那一句）→ ③ `AutoPickUpItem`，拾不进背包就 `SetParent(null)` 留在脚下 →
④ `SafeNewGO` 把状态推给客机。模组物品还依赖 `联机合成/FixModItemCraftSpawn` 开着。
窗口用**时间戳**而不是裸 bool：原方法抛异常时 postfix 不执行，bool 会一直挂着。

### 2.9 已删除：工具耐久同步

`DurabilityFix` 与配置键 `联机修正/SyncToolDurability` 已整体移除（2026-11）—— 联机模组自身已修好这条。
它原来要同时猜“主机重跑使用动作时有没有扣过”，双扣/漏扣都比原版更难调，收益不值。
配置里若还留着那一行无害（BepInEx 不认识的键会被忽略）。

---

### 2.10 客机退出后，其余客机掉帧（v1.5.0 内修订）

**旧方案缺陷**：解绑后只剩物品种类与坐标，1.2 秒后再匹配 1.5 米内无主物品。
副本下落、仍挂在容器、靠近本地玩家时会漏清理；同种正常物品也可能被错认。
全场搜索并逐项比对还会给收到消息的客机制造瞬时开销。失败批次先清队列也不能重试。

**当前机制**：

1. `DetachSyncKeepMesh` 前记录实体 ID、Version、资源 ID 与准确对象；后置确认原生解绑成功，且确实在 `SAVESTATE_` 下。
2. 0.1 秒攒批，每批最多 64 项，可靠发送；失败保留批次。快速重连已恢复的背包不再发送旧清单。
3. 客机在 `SyncInfo.CreateObject` 时维护身份索引，随物品销毁移除，不做全场物品搜索，也不依赖坐标或距离。
4. 广播仅作为通知。因 CUCoreLib 允许客机请求转发广播，客机必须再向主机请求核验。
   主机只批准自己记录且仍保存在离线背包中的身份；否定请求也明确回包，避免留下无回复请求。
5. 客机收到对应请求的确认后按准确身份排队，每帧最多 8 件、累计 1ms 后暂停。
   排队绑定具体对象及原同步记录，ID 换代、网格重新绑定、换层、断联或开关关闭会取消旧任务。
6. 持有中的物品不删；包含子物品的容器等待叶子先清理，10 秒后仍有未确认内容物则保留并计数。
   不把未知内容物卸到地上，不冻结、不挪位置，也不删除主机真实背包。
7. `InternalEntity.Destroy()` 对远端客机实体可能是 no-op。主机确认后使用原生内部实体退出流程，
   再删除准确的空副本，避免仅删画面后 `CreateObject` 重建。
   此特殊退出期间跳过旧物品状态重放/销毁效果；其他正常销毁继续走原流程。

频道改为 `ddn.mp.ghostitems.v2` 与 `ddn.mp.ghostitems.verify.v2`，版本仍为 **1.5.0**；
主机与其他客机都须替换本次修订后的包，旧的 1.5.0 按位置清理处理器不接新通知，避免误删。
日志 `[遗留物品]` 汇总实际清理、保留和排队数量，不把“发送成功”当成“客机已清干净”。

**其余修复的复查结果**：

| 路径 | 发现并修正 |
| --- | --- |
| 无芯片 | 总开关关闭时，周期任务、快照、请求与延迟回包仍可能写状态；统一检查 Active，换连接清旧主机缓存 |
| 商人快照 | 关闭交易窗口也请求、主机未检查开关、缺少主机限频；仅打开时请求，主机检查开关/发送者并限频 |
| 穿戴兜底 | 次数只在主机累加却在客机检查、跨局累计；改在主机执行上限并按局清理，拒绝不唯一或储存中的候选 |
| 丢弃碰撞 | 共用静态待处理物品易被嵌套调用覆盖；单个重叠命中也会漏判；改独立调用状态及完整缓冲区检查 |
| 物品登记 | 已销毁的注册条目/跟踪器会被当作已登记；检查存活状态，缓存字段而非缓存可能被替换的字典实例 |
| 购买赠品 | 异常退出会残留购买窗口，0.05 秒全局去重还会漏赠合法快速购买；增加作用域恢复、保留原交易去重 |

**验收边界**：离线检查覆盖 1,000 个身份、ID 代次和对象重绑、旧对象销毁、重置及分帧队列。
游戏自检使用可丢弃测试对象验证“有子物品时保留、叶子可走原生清理”，不修改真实物品。
这些检查不能代替至少三人联机：需测试一名客机携带多层容器退出，比较另一客机退出前后的帧耗时、清醒动态刚体、
遗留物品日志，以及快速重连后背包是否完整。正常掉落、其他人的物品和未知内容物应保留。

性能分析的指标定义与观察者开销控制见 [perf-tracker.md](perf-tracker.md)。物理步／帧不再作为物理压力；
对象清点附带数据年龄和时间范围，避免把旧结果当成断线瞬间的状态。

### 2.11 采用权威主机配置（v1.5.0 内追加）

总开关下的 `采用权威主机配置（UseHostAuthorityConfig）` 默认关闭。总开关与该选项始终由本机控制，不接受远端覆盖；其他MPfix配置（含界面热键与详细日志）参与临时采用。

| 主机选择 | 客机选择 | 结果 |
| --- | --- | --- |
| 开启 | 开启，且收到有效主机确认 | 客机采用主机值并暂时锁定其他配置，显示「（主机权威模式）」；主机配置仍可编辑 |
| 开启 | 关闭 | 客机使用本机配置，不采用、不锁定 |
| 关闭 | 任意 | 主机只发布不启用标记，不携带设置值；客机恢复本机值及正常编辑权限 |
| 退出/更换多人连接 | 任意 | 清除临时值和旧请求，恢复本机配置；下一连接重新确认 |

配置传输独立于修复总开关运行，以保证停用修复时仍能关闭采用、接收停用标记并恢复配置。关闭总开关原有的配置置灰规则保留；总开关和采用选项仍可操作。开启权威选项本身不会限制主机的编辑能力。

实现与发送时机：

- `AuthoritySetting<T>` 分离本机 `ConfigEntry` 与临时生效值；修复读临时值，保存/重载仍读写本机值，不用主机数据覆盖配置文件。因此即使客机中途保存、崩溃或重启，也不会把主机值永久留下；恢复时使用最新本机值。
- 主机在多人连接就绪时发布一次策略标记；设置变化按0.25秒合并，仅实际变更才通知。停用状态下普通设置变化不发送配置值，也不重复发布相同策略。
- 新加入或中途启用采用的客机请求一次当前策略。通知频道 `ddn.mp.config.notice.v1` 只作刷新提示；CUCoreLib可转发广播，不能用广播直接授权修改或锁定。客机经 `ddn.mp.config.get.v1` 向主机请求后，才应用对应回复中的启用标记及值。
- 连接实体管理器、请求序号、随机nonce、主机会话与递增修订号共同隔离旧回包。取消采用、退出、换主机、变为主机或插件停止后，旧回复不能重新覆盖设置。
- 配置按已绑定白名单匹配，先验证完整性、类型、范围和有限值，再一次切换全部生效值，最后通知修复处理状态变化；不把任意网络键名写入配置。
- 没有稳定期轮询或定时全量下发；请求发送失败/超时最多尝试3次，通知失败保留重试，主机按发送者限频。两端需要安装同一修订后的共享层、管理器与MPfix。
- 配置策略通过可选 `IDdenConfigPolicy` 接入全部配置、单模块配置与独立窗口。锁定时显示生效的主机值；两项本机控制仍可编辑，进行中的改键也会取消。Shared保留原绘制方法重载。

验证：`tools/HostAuthorityTests` 以真实BepInEx配置文件和模拟双端时钟/传输检查保存重载、恢复、发送时机、失效回包、伪造通知、无回复与重复数据。游戏内自检覆盖实际配置白名单与全部类型序列化。真实双机的进局、设置变更与断线重连仍须实测。

---
## 三、存档编辑器（`DDEN.SaveEditor.dll`，可选组件）

零 patch：读 `*.sv`（`GZip(ASCII JSON)`，与 `SaveSystem.Zip/Unzip` 同构）→ 按 `slot|wearSlot` 归组渲染
（原版把父物品写在子物品前，子物品显示在容器头下面）→ 改的只是你点过的那个字段。
写盘只走 `SaveCodec.Write` + `SyncMeta`（临时文件 → `.cumod.bak` → 顶替，再刷 `.sv.meta` 的
`BiomeIndex/IGTSeconds/RTASeconds/Timestamp`），写前查 `Compat.IsQolAutosaveBusy()`。
**`cheatedSave`（禁用成就）绝不自动写入**，只由「进度/成就」页手动切。
删除物品时 `items` 与 `itemComponents` 必须同步删。
图形化肢体：世界内读原版 `WoundView.limbImages / origPositions / localScale.x`，把每块贴图
`Blit + ReadPixels` 抓成 PNG 存 `BepInEx/config/dden.limbs/`，主菜单也能用原版美术。

---

## 四、怎么确认它在干活

| 日志关键字 | 含义 |
| --- | --- |
| `[MPCOMPAT] Together/CasualtiesMP = 5.5.4` | 校对过的联机版本；没校对过会 Warn |
| `[CraftFix] 物品 '<id>' 不在 Resources，已用模组资源生成` | 合成兜底生效 |
| `[CraftFix] 生成失败 '<id>'：…` | 两条正路都不通（会写明注册表是否命中） |
| `[穿戴兜底] 主机已认出并让 '<id>' 正确穿到玩家身上` | 穿戴修好了 |
| `[穿戴兜底] 主机没找到可穿的那件 '<id>'：同 id N 件 / 可穿 N 件 / 最近 d=…` | 没修好 —— 看是没登记、在容器里、还是太远 |
| `[挖块] 世界伤害倍率 attackdamage=…` | 倍率补乘生效（最该先看这条） |
| `[挖块] 采纳客机伤害 / 客机数字仍不可信 / 拒绝收敛` | 挖块三条通道各自的判定结果 |
| `[道生一 一生二] 商人购买 '<id>'：额外奉送一份` | 娱乐开关生效 |
| `[CFG] 单独查看配置：…` | 单模块配置页真的画出来了（不是落回总览） |
| `[UI] 内嵌图标已加载：16×16` | 内嵌资源打进去了 |

---

## 五、红线（改代码前先读）

1. 客机永不本地生成物品（合成 / 购买 / 水晶）—— 一切以主机为准，否则必然双份。
2. 只“打开”不“改写”：物理保险不动位置、不删物品，
   幽灵清理先搬内容物、含液体就保留。
3. 失败要留痕且**不加负缓存**：首次调用可能早于资源/注册表就绪，一旦拉黑整局都失败。
4. 联机判定宁可不修也不猜人：解析不到发送者就放弃，绝不回落到本机身体。
5. 挖块不要退回“主机一次打满血量”：那等于取消硬度与工具判定。
