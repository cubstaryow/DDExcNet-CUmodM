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

### 2.3 穿上装备，主机那边还在掉 / 物品跟着人跑

**现象**：客机穿上装备，主机画面里那件装备掉在地上，甚至跟着客机移动；有时穿上了贴图却怪怪的。

**原因**：主机 `ServerReceiver__ItemWearing`（反编译 136218）四道门任一不过就 decline：
① 物品必须在 `NetObjectRegistry` 登记表里（`TryGetItem`）—— 模组物品最容易栽在这；
② `Stats.wearable`；③ **物品与玩家在主机的权威坐标上相距 ≤14m**（客机看到的是自己那件，主机那件可能还在柜台）；
④ `CheckIfBodyCanStealThisItem`。decline 之后主机眼里它仍是自由物品（有物理就往下掉），
而客机每帧把“我身上这件”的位置推给主机 → 那件自由物品就跟着客机跑。

**修法**：客机穿上后 1.5s 起**最多问主机 3 次**（间隔 1.5s），经 CUCoreLib 通道 `ddn.mp.wearfix` 带 `{id}`；
主机用 sender-aware 处理器拿 clientId → `ScavPlayer.GetBodyFromClientId` 找身体 →
按 id 找那件（同 id + wearable + **不属于别人** + 距该玩家 ≤14m，取最近）→ 补登记 →
`Together.ItemSync.Body_ForceWearWearable` → `body.GetWearable(id) == item` 校验 →
**`SafeNewGO` 把新状态推给所有客机**（Together 自己在 136245 也是这么做的，少了这步主机改对了客机看不到）。
默认不重建、不复制、不掉落；每局上限 20 次，异常只记 Debug 不会停整个兜底。
**铁律：解析不到发送者绝不回落成 `Game.LocalBody`** —— 那会把客机的物品穿到主机自己身上，直接放弃并留痕。
只有在主机确实找不到时，才由（默认关闭的）`RebuildIfHostLacksItem` 走“脱下来 + 请主机重建”。

### 2.4 丢东西穿墙 / 掉到地图外

**原因**：Together 的 `HeldQuiet` 在 `Awake` 里把物品所有 `Collider2D` 关掉并记下当时状态，`Restore()` 写回；
若挂上那一刻碰撞体本来是关的（物品在背包里、或模组物品后加的碰撞体不在名单里），写回后仍然没有碰撞，
而 `HeldSeat` 已经把 `rb.simulated = true` → **无碰撞 + 有物理 = 穿过墙块往下掉**。

**修法**：① postfix `HeldQuiet.Restore`，恢复后**只补碰撞体**；② 只在 `Body.DropItem / Body.DropWearable`
（真正的丢出动作）后补开碰撞体与物理。**三条铁律**：只处理自由物品（无父级、不在容器里）；
只“打开”不“改写”；**任何位移副作用一律删除**（早期版本会上移 0.5~3m，位移残留在局部坐标里 →
表现为“还穿在身上却浮在角色上方”）。**不要**挂到 `Container.UnloadItem / Utils.Create` 上——
那两个时机一局跑上千次，会把手/包里的物品物理打开。

### 2.5 装备浮在角色头顶附近

**原因**：正常穿上/手握时局部坐标恒为 0（`Body.WearWearable` 与 `HeldSeat` 都这么设），
一旦明显非零就说明被别的东西改过（旧版物理保险的位移、同步写回、别的模组）。

**修法**：`WornItemSnap` —— 穿戴后立即校验 + 每 2 秒扫附近角色，`localPosition` 超过 0.6 单位就归零，
并把 `parent / localPos / localScale / worldPos` 打进日志（定位到底是谁改的）。
只改 `localPosition`，**不动 rotation（slotRotation）与 scale（HeldSeat 的肢体补偿）**；每局上限 100 次。

### 2.6 挖块：血量掉一截又回升、挖掉了又长回来

**现象**：客机挖方块，先掉一段血，紧接着血量回涨一部分；客机那边方块消失了，主机判定没挖掉，
chunk 同步把原 tile 推回来 → “挖了又出现”。

**原因（三条叠加）**：

1. **主机那份伤害天生少了世界伤害倍率**（真凶）。原版 `Body.Attack` 第一件事就是
   `atk.damage / atk.structuralDamage *= WorldGeneration.GetRunSettingFloat("attackdamage")`
   （Assembly-CSharp 3546-3547）。客机那次 Attack 真跑完整流程，所以上报值**含倍率**；
   而主机在 `ServerReceiver_PlayerBodyAttack` 里先把 `ForceOnlyRetrieveAttackInfo` 置真再走
   `ItemSync.BetterUseItem`，Together 的 `Body.Attack` 前缀抓到 `LastAttackInfo` 就直接 `return false` ——
   **3546/3547 在主机从没执行过**。于是 `BlockDamageAuthority.ChooseBlockDamage(hostOwn, …)` 的 `hostOwn`
   少一个 `attackdamage`（`CalculateAttackPower` 里也没有它，只算 armPowerMult/bonusStrength/STR/爪）。
2. **比例门槛**：原版 `ChooseBlockDamage`（126046）只接受 `clientReported ≤ hostOwn * 3`，
   倍率或模组工具一放大就整段被丢，主机用自己那份小值落地 → 客机看到“掉一截又回升”，累不到 0 就永远挖不穿。
3. **中继把裂纹掰回去**：`ClientReceiver__PlayerDamageBlockRelay`（43901）拿 `主机累计 - 本地累计` 当差值
   喂 `DamageBlock`，可能是负数 → 裂纹往回跳。

**修法（先治根再兜底）**：

- `BlockDamageRelax`：给 `ChooseBlockDamage` 加**全程接管**的前缀（不能 `return true` 交回原版，
  否则“被拒绝”那条路仍用没乘倍率的 hostOwn 落地）：`hostOwn *= attackdamage`（读不到/非法值按 1，绝不猜）
  之后再做原版那套有限性 + 2000 硬上限 + 比例判定；比例上限可配 `联机修正/BlockDamageRatio`（默认 6，
  有效下限 3 = 原版）。有倍率时打一条 `[挖块] 世界伤害倍率 attackdamage=…`。
- `BlockDamageSyncFix`：客机本地把方块打到 0 时发 `ddn.mp.blockdmg {x,y,block,total}`；
  主机**先校验**（同一格同一方块 / 发送者 12 格内 / 主机账上这块已掉 ≥ `BlockBreakMinFraction`（默认 50%）血量），
  再**收敛**（`delta = total - 已损失`，只加不减）而不是无脑打满 —— 一镐敲穿钢墙这条路是死的。
  同坐标 5 秒去重、每局 3000 次上限；中继伤害（`ApplyingRelayedDamage`）不回发。
  被拒绝时打 `[挖块] 拒绝收敛：… 主机账上只掉了 …`，按这条日志调门槛。

### 2.7 客机付钱，商人却不给货

**原因**：商人库存由主机维护，客机看到的是某次快照。列表一不同步，
`TraderCommit.HandleBuy` 用 `{index, itemId, value}` 三者在主机校验就会失败 → 扣钱不发货。

**修法**：客机每次打开交易菜单（postfix `PlayerCamera.ToggleTradeMenu`）向主机要一次全量快照，
走 Together 自己的 `TraderSync.SendTraderState(knetid)`（发包内含 items 列表），不自己序列化任何东西。

### 2.8 身上挂着装备贴图，其实没穿

**原因**：穿戴时 `Wearable.CreateSprites` 在肢体下生成纯视觉子物体（`WearableExtension`，`reference` 指向 Item），
正常卸下由 `Body.DropWearable → ClearSprites()` 清理；联机里物品常被 `SetParent(null)` / `HeldSeat` 搬走，
这条路不清理 → 贴图残留。

**修法**：① `DropWearable` 后补一次 `ClearSprites()`（幂等）；② 每 2 秒巡检附近身体的 `WearableExtension`，
`reference` 为空、或它挂的 Item 已不属于该身体，**连续两次（≥1.5s）**成立才销毁那层视觉。
只删视觉子物体，绝不碰真实物品。

### 2.9 有件物品卡在角色身上（能打开，但既不算穿着也不在背包）

**原因**：Together 的 `HeldSeat` 把物品挂到 `InventorySlot`（父级=槽位、localPosition=0）来表现远端的手/背包，
槽位记账错位时物品就一直挂在身体中心，`GetItem(slot)` / `GetAllWearables()` 里都找不到。

**修法**：**只看本地身体**，每 2 秒扫“挂在身体下、父链里没有别的 Item（排除容器内容）、不属于任何槽位/穿戴”的物品，
连续 2.5 秒成立 → 先 `AutoPickUpItem` 归位；拾不进背包就 `SetParent(null)` 放回地面（物品不删）。
每局上限 50。**必须避开穿戴兜底正在处理的物品**（`WearGhostFix.RecentlyRequested` 12 秒窗口），
否则两套修复互相拆台。

### 2.10 娱乐开关「道生一 一生二」

主机上给 `Together.TraderCommit.RunBuy(trader, actor, job)` 挂前后缀；购买窗口内由 `Utils.Create` 前缀
记下生成的 id，后缀在主机侧用同一个 id 再造一件给买家。默认关闭，只主机需要开。

复制那件必须走完联机登记流程，否则**买家根本看不到它**（主机多一件幽灵）：
① `Server_EnsureItemIsNetworkRegistered`（= 原版 RunBuy 反编译 142460 那句）→
② 弹药量**保持满弹匣**（用户口径：商人卖出来就是满的；原版 `TryPurchase_rewritten:144184` 会随机 50%~100%，
这里故意不抄那一句）→ ③ `AutoPickUpItem`，拾不进背包就 `SetParent(null)` 留在脚下 →
④ `SafeNewGO` 把状态推给客机。模组物品还依赖 `联机合成/FixModItemCraftSpawn` 开着。
窗口用**时间戳**而不是裸 bool：原方法抛异常时 postfix 不执行，bool 会一直挂着。

### 2.11 已删除：工具耐久同步

`DurabilityFix` 与配置键 `联机修正/SyncToolDurability` 已整体移除（2026-11）—— 联机模组自身已修好这条。
它原来要同时猜“主机重跑使用动作时有没有扣过”，双扣/漏扣都比原版更难调，收益不值。
配置里若还留着那一行无害（BepInEx 不认识的键会被忽略）。

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
2. 只“打开”不“改写”、只“归位”不“删除”：物理保险不动位置，孤儿/贴图清理不删真实物品，
   幽灵清理先搬内容物、含液体就保留。
3. 失败要留痕且**不加负缓存**：首次调用可能早于资源/注册表就绪，一旦拉黑整局都失败。
4. 联机判定宁可不修也不猜人：解析不到发送者就放弃，绝不回落到本机身体。
5. 挖块不要退回“主机一次打满血量”：那等于取消硬度与工具判定。
