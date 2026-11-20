# AGENTS.md —— DDEN / CUmod 工作区常驻须知

> 给**下一个接手的人/AI** 看的第一份文件。读完就能独立构建、部署、验证，不用重新考古。
> 设计文档：[docs/README.md](docs/README.md)　社区前置签名：[docs/cucorelib-api.md](docs/cucorelib-api.md)
> **对外接入文档（给别人模组用）**：[docs/cumodm-integration.md](docs/cumodm-integration.md)
> **功能详解（现象 / 诱发原因 / 修复路线）**：[docs/features.md](docs/features.md)
> 面向玩家的介绍：[README.md](README.md)（只讲结果，不讲实现）

## 0. 这是什么

《Casualties Unknown》(Steam appid 4576490) 的 BepInEx 5 模组工作区。**四个 dll + 一个共享库**，统一由管理器承载界面：

| 产物 | 源码 | 作用 |
| --- | --- | --- |
| `DDExcNet-CUmodM.dll` | `src/DDExcNet.CUmodM/` | **管理器**：统一功能界面 + 全部配置界面 + **兼容性总控** |
| `DDEN.MPfix.dll` | `src/DDEN.MPfix/` | 多人修复合集：无芯片同步 + 联机合成/物品生成兜底 + 物品网络登记 + 穿戴兜底 + 物品物理保险 + 挖块收敛 + 商人快照 + 掉线遗留物品清理 + 娱乐（道生一 一生二） |
| `DDEN.SaveEditor.dll` | `src/DDEN.SaveEditor/` | 可视化存档编辑器（图标/血条点选，QoL 多存档位） |
| `DDEN.NetPerfTracker.dll` | `src/DDEN.NetPerfTracker/` | **性能跟踪器**（正式名 NetPerfTracker，调试/观测工具）：管理器里单独一页，帧数/CPU/物理压力曲线 + 世界对象与网络状态的文字读数；同时写 `BepInEx/config/DDEN/perf-*.log`。**不打任何补丁**，随发布包一起发（玩家不用也不会影响游戏，排查掉帧时让他开着就能抓现场） |
| `DDEN.Shared.dll` | `src/DDEN.Shared/` | 公共层：反射、日志、联机桥、存档编解码、**IDdenModule / DdenHub / Compat / DdenUI** |

部署目录：**`<游戏>/BepInEx/plugins/DDEN/`**（已实测 BepInEx 会递归扫描子目录）。

## 1. 环境事实（**已验证，别猜**）

| 项 | 值 | 依据 |
| --- | --- | --- |
| Unity | 2022.3.62f3 / Mono | `globalgamemanagers` |
| 目标框架 | **netstandard 2.1** | `Managed/netstandard.dll` = 2.1.0.0 |
| 加载器 | BepInEx 5 + HarmonyX（`UnpatchAll(id)` 已 error 级过时 → 用 `UnpatchSelf()`） | core 目录 |
| 联机 | Together / CasualtiesMP **5.5.4**（5.5.3 也已校对；GUID `CasualtiesMP`） | 启动日志 `[MPCOMPAT]` |
| 前置 | CUCoreLib 1.1 `net.cucorelib`；QoL 1.1.5 `org.bepinex.plugins.qol_unknown` | dll |
| 工具链 | .NET SDK 10（`CUmod.slnx`，不是 .sln）；`ilspycmd` | `dotnet --list-sdks` |

游戏目录：`L:\SteamLibrary\steamapps\common\Casualties Unknown`
存档目录：`%USERPROFILE%\AppData\LocalLow\Orsoniks\CasualtiesUnknown`

## 2. 构建 / 部署

```bash
cd L:/github/game/CUmod
./scripts/build.sh                            # Debug：部署 Shared / CUmodM / MPfix（**不带存档编辑器**）
./scripts/build.sh Release                    # Release 同上
./scripts/build.sh Debug --with-saveeditor    # 临时把 DDEN.SaveEditor 也部署进去（开发/调试用）
dotnet build CUmod.slnx -v m                  # 只编译
```

- **默认不部署存档编辑器**：正常游戏带共享层 + 管理器 + MPfix + **性能跟踪器**（调试工具，`DDEN.NetPerfTracker.dll`）；
  如果之前部署过 `plugins/DDEN/DDEN.SaveEditor.dll`，脚本会主动把它撤下来（游戏在跑导致文件被锁时会提示关游戏重跑）。
- **`DDEN/` 目录不是我们的私有目录**：里面可能还有别人做的修复 mod
  （实例：`ET-NoBatteryFix.dll`、`QoL-EMI-RecipeRowFix.dll`）。`scripts/build.sh` 用**白名单**
  （`OWNED=(DDEN.Shared / DDExcNet-CUmodM / DDEN.MPfix / DDEN.NetPerfTracker / DDEN.SaveEditor)`）决定写谁、删谁，
  并在结尾打印 `== 保持不动（其它模组 / 非本脚本产物）: …`。
  **任何情况下都不要对 `DDEN/` 做通配删除**（`rm -rf`、`rm *.dll`）；新增产物必须同时加进 OWNED。
- **不做插件备份**（2026-11 按用户要求移除）：`scripts/build.sh` 里原来那套
  `backup/plugins_<时间戳>` + `backup/latest` + zip 已删掉，**agent 部署时也不要再手工造 `backup/`**。
  需要留旧版就在部署前自己复制一份。`.gitignore` 里仍保留 `backup/` 一行（防手滑把 dll 提交进去）。

换机器只改 `Directory.Build.props` 的 `<GameDir>`（或 `-p:GameDir=...`）。
引用白名单：`Assembly-CSharp / UnityEngine.* / BepInEx / 0Harmony / Newtonsoft.Json / CUCoreLib`；
**禁止硬引用 Together、QoL、以及两个模块 dll**（管理器跨模块调用一律走 `IDdenModule` 或反射）。

### ⚠️ 两个必踩的坑

1. **游戏在跑 → `plugins/DDEN/*.dll` 被锁 → `cp` 静默失败**。先
   `powershell -c "Stop-Process -Name CasualtiesUnknown -Force"`，再部署。
2. `strings` 查 .NET 字符串常量要 **`strings -a -el`**（#US 堆是 UTF-16）。

## 3. 架构约定（改代码前必看）

```
插件 Awake:  DdenHub.Register(this)            ← 实现 IDdenModule
管理器:      侧边栏列 DdenHub.Modules，绘制 module.DrawPage(area)
配置页:      module.DrawSettings(area) 返回 false → 管理器用 DdenUI.ConfigRow 自动渲染 ConfigEntries
兜底:        管理器缺席时模块自己开小窗（DdenUI.DrawStandalone）
兼容性:      一律问 Compat.*（快捷键冲突 / QoL autosave / 旧 dll 残留 / 补丁栈）
```

- **日志**：插件里用 `internal static LogChannel Log = DDEN.Shared.Log.For(Logger, "Tag", verbose)`，
  共享层用静态 `Log`（管理器 Awake 里 `Log.Attach` 兜住，否则只进 Player.log 不进 BepInEx 日志）。
- **UI**：只用 `DdenUI`（配色/精灵/条/配置行），保证四个页面观感一致；`GUI.depth = -1000` 画在别人上面。
- **控件一律自绘，别用 `GUI.skin.*` 的原生皮肤**：`GUI.skin.button/toggle/textField` 是浅灰圆角贴图，
  贴在深色面板上很跳、缩放会糊。现成件：`DdenUI.Button`（直角块+描边，highlight 走强调色）、
  `DdenUI.CheckBox / CheckBoxLabeled`（像素风对勾用三段短横拼，不依赖字体有没有 ✓）、
  `DdenUI.TextField`（底/边框自绘，`FieldStyle` 只留文字与光标）、`DdenUI.NumberField`；
  配置行 `ConfigRow` 已经全部走它们。**别在 `GUIStyle` 上写 `background`**——没这个属性，
  要逐个清 `normal/hover/active/focused.background`。
- **管理器页面路由（踩过坑）**：页面名有两种带前缀的形式 —— `config:<模块id>`（单模块配置）、`ext:<下标>`（外部模组页）。
  `DrawContent` 的 switch 里**必须先判前缀再 `DdenHub.Find`**，否则 `Find("config:mpfix")` 返回 null →
  静默落回总览，表现为“点了没反应”。新增任何带前缀的页面都要在 switch 的 default 里补一支。
- **外部模组也有自己的页**：`DdenExternal.Blocks` 里的每一块（只要交了配置）都会出现在
  侧边栏「外部模组（只交配置）」+ 总览卡片 + 全部配置页的【单独看】→ `ext:<i>`。
  那一页：`DdenDraw` 存在就画自定义页，不存在就直接把它交出来的 `ConfigEntry` 画成配置列表。
- **关于页 + 内嵌图标**：`HubPlugin.DrawAbout`（`_page = "about"`）。图标是
  `src/DDExcNet.CUmodM/Resources/dragon_cloud.png`（16×16 像素图），由 csproj 的
  `<EmbeddedResource Include="Resources\*.png" />` 打进 dll，运行时按资源名后缀匹配读出并
  `LoadImage`。**必须 `filterMode = Point`**（Bilinear 会把像素图糊掉），放大按整数倍用。
  加载成功会打 `[UI] 内嵌图标已加载：16×16` —— 没有这行就是资源没打进去。
- **面向玩家的文案里不许出现开发术语**（2026-11 用户要求，逐字遵守）：
  界面文字、配置说明、README 一律用普通玩家能懂的话。不要写类名 / 方法名 / dll 文件名 / 构建参数
  （`--with-saveeditor` 这种）、不要写「16×16 像素图 / Point 过滤 / netstandard 2.1」这类实现细节，
  也不要把内部原因码（`request(manual)`、`watchdog`、`runSettings`）原样显示给玩家 ——
  用 `MPfixPlugin.ReasonText` 这类映射翻成人话（例：`push` → 「主机主动同步」）。
  技术细节统一写进 [docs/features.md](docs/features.md)（现象 / 诱发原因 / 修复路线 + 日志表），
  README 只讲“用起来是什么样”。**新增界面文字前先问自己：玩家看得懂吗、有没有暴露内部结构。**
- **性能跟踪器（`DDEN.NetPerfTracker`，正式名 NetPerfTracker）**：调试用的子 mod，管理器侧边栏单独一页「性能分析」。
  **零补丁**（一个 MonoBehaviour，不装 Harmony，最坏情况只是自己不采样），所以线上排查可以放心让它跑着。
  - 采样两档：轻采样（帧数/最差帧/CPU%/物理步每帧数，默认 1 秒）与重采样（数 `Item`/`Body`/`Rigidbody2D`/`Collider2D`、
    反射读 `NetObjectRegistry.SyncRegistry` 与 `ScavPlayer.ClientIdToPlayerDict`，默认 5 秒）；
    **重采样耗时超过 8ms 自动关掉碰撞体计数**（观察者不许成为元凶），每次耗时都写进日志的 `costMs`。
  - 页面：4 张曲线（帧数 / CPU / 物理压力 / 世界对象）+ 文字读数（物品·无主·无主且物理开着、刚体、碰撞体、
    网络对象·已销毁条目、玩家数、targetFrameRate、vSync、timeScale、fixedDt）+ 暂停/清空/写日志/打开目录。
    曲线按像素列降采样，攒几千个点也不会把这一页画卡。
  - 日志：`BepInEx/config/DDEN/perf-<时间戳>.log`，一行一个快照 `key=value`（不走 BepInEx 主日志，那边会被新实例截断）。
  - 用途：抓「一台客机掉线 → 另一台客机巨卡」这类现场 —— 看 `sim`（无主且物理开着）、`deadsync`、`players` 的台阶。
  - 它**随发布包一起发**（用户 2026-11 明确要求：本地输出要包含全部模块，含存档编辑器与性能跟踪器）；
    它不打任何补丁，装不装都不影响游戏，只是排查掉帧时能直接抓现场。
  - ⚠️ `[BepInPlugin(..., "1.3.0")]` 里的版本号读不了 MSBuild 属性：**改 `<DdenVersion>` 时顺手改各插件的 `[BepInPlugin]`**。
- **窗口必须用无皮肤模式**：`GUI.Window(id, rect, Draw, GUIContent.none, GUIStyle.none)` + 自己画标题栏。
  用默认窗框时它会把 y<20 的第一行内容盖住（表现为“文字上下被切掉”）。
- **输入隔离（关键：这游戏的菜单按钮不走 uGUI）**：
  主菜单按钮是 `AdaptiveButton.Update()` 里 **直接读 `Input.GetMouseButtonDown(0)`** 再调 `Clicked()`，
  既不经过 EventSystem，也不吃 uGUI 遮罩 —— 所以“关 EventSystem”和“挂 uGUI 遮罩”都拦不住它。
  现行方案两层：① `DDExcNet.CUmodM.UiGuardPatch` 给 `AdaptiveButton.Clicked` 加 Harmony 前缀，
  只要有 DDEN 面板可见就 return false（日志 `[UIGuard] 面板打开，已拦截菜单按钮点击（Play）`）；
  ② `DdenBlocker` 的 uGUI 遮罩负责游戏内其它 uGUI 界面。两者都受 `UI/ModalBlocker` 开关控制。
  面板可见性用 `DdenHub.SetPanelVisible(owner, visible)` 维护，`DdenHub.AnyPanelVisible` 供补丁判断。
- **图形化肢体**：世界内读原版 `WoundView.limbImages / origPositions / localScale.x`，并把每块贴图
  **Blit+ReadPixels 抓成 PNG** 存到 `BepInEx/config/dden.limbs/`（布局存 `dden.limbs.json`），
  这样主菜单也能用原版美术。
  **默认只按原版 `localScale.x` 镜像，不做额外翻转**（实测这是正确的）；「左翻 / 右翻」只是换体型/角色时的微调开关。
  翻转用**预翻好的贴图副本**实现（IMGUI 的 UV 翻转对整张贴图是恒等、负宽度缩放也不可靠）。
- **标签统一 `clipping = Overflow` 并按宽度截断**：`DdenUI.ElideToWidth(text, width)`；
  Overflow 是为了中文行高不被切，但会横向溢出到邻居控件，凡是定宽列一律先截断。
- **悬停提示别用 `GUI.tooltip`**：那个值会残留，表现为“鼠标后面一直跟着一个黑框”。
  用 `DdenUI.Hover(rect, text)` + 每帧 `DdenUI.BeginFrame()` + 末尾 `DdenUI.DrawTooltip(bounds)`（会钳在窗口内）。
- **联机合成“无法生成物品”**：Together 主机侧自己实现了一份 spawn
  （`PlayerCamera_TryCraft_MultiplayerPatch.RecipeResult_SpawnResult` → `Utils.Create` → `Resources.Load(id)`），
  模组物品不在 Resources 里 → `CraftReason.SpawnFailed`（客户端文案 `craft_deny_spawnfailed`）。
  修复在 `DDEN.MPfix.Utils_Create_ModItemPatch`：给 `Utils.Create` 加前缀，Resources 找不到时只走 CUCoreLib 两条正路：
  ① `ResolvePrefab`（先确认带 `Item` 组件）再 `Instantiate` → ② `CustomInstantiate.InstantiateReturn`；
  **客机一律不本地生成**（`Mp.IsRunning && !Mp.IsServer` 时直接返回 true）：商人购买 / 合成 / 水晶掉落
  在客机上也会跑一遍“本地预测”，让客机成功造出模组物品就会和主机同步来的那份重复
  （典型症状：**客机买商人东西买两份**）。客机保持原版行为，生成物一律由主机权威下发。
  生成后在主机侧补 `NetObjectRegistry.Server_EnsureItemIsNetworkRegistered`
  （开关只剩 `联机合成/修“无法生成物品”（FixModItemCraftSpawn）`；登记已改为无条件且幂等，不再有开关）。
  **两条都不行就放弃兜底**（交回原版，该配方该失败就失败）——**绝不克隆现存物品、绝不做任何 Destroy**：
  克隆容器会把内容物一起复制（凭空刷物品），清理子物品又是一次删除，风险远大于收益。
  **失败必须留痕**：会打一条 `[CraftFix] 生成失败 '<id>'：…（含注册表是否命中）`（同 id 30s 去重）。
  **绝对不要加“失败即永久拉黑这个 id”的负缓存**——首次调用可能早于模组注册/资源就绪，一旦拉黑该配方整局都失败。
  注意 Together 会按 attempt 缓存失败 receipt，同一会话重试仍失败，换层/重连才会清 —— 这是“过一层又能合成”的来源。
- **穿戴兜底（联机）**：`DDEN.MPfix.WearGhostFix` —— 客机穿上物品后 1.5s 起**最多问主机 3 次**（间隔 1.5s），
  经 CUCoreLib 频道 `ddn.mp.wearfix` 带 {id, 上报位置}；主机用 `MpChannels.TryRegisterSender` 拿 clientId →
  `ScavPlayer.GetBodyFromClientId` 找该玩家 body → 按 id 找那件（同 id + wearable + 不属于别人 + **距该玩家 ≤14m**
  （与 Together 的 `ServerReceiver__ItemWearing` 距离门一致，取最近）→ `Server_EnsureItemIsNetworkRegistered` 补登记 →
  `Together.ItemSync.Body_ForceWearWearable` → `body.GetWearable(id) == item` 校验 →
  **`NetObjectRegistry.SafeNewGO` 把新状态推给所有客机**（Together 自己在反编译 136245 也是这么做的，
  少了这一下主机的修正在客机看不到，症状仍是“物品跟着人跑”）。**默认不重建、不复制、不掉落**。
  **铁律：绝不把“解析不到发送者”回落成 `Game.LocalBody`** —— 那会把客机的物品穿到主机自己身上；
  解析不到就 `Fail("no-body")`。发送者通道注册失败 → 本会话整块停手并 Warn（宁可不修，不猜人）。
  记录类异常只记 Debug，**不因此禁用整个兜底**；只有频道/`Body_ForceWearWearable` 找不到才停。
  **没有“重建”这条路了**（`RebuildIfHostLacksItem` 与 `wearrespawn` 频道已删除）：主机找不到就保持现状，绝不复制。
  无芯片 watchdog 默认 **30s**（旧配置 0.5 会自动迁移）。
- **物品穿墙掉落（联机）**：真凶是 Together 的 `HeldQuiet`（`Internal/HeldQuiet`）——
  `Awake` 里把物品所有 `Collider2D` 关掉并记下当时状态，`Restore()` 再写回；
  若挂上那一刻碰撞体本来就是关的（物品在背包里 / 模组物品后加的碰撞体不在名单里），写回后仍然是关的，
  而 `HeldSeat` 已经把 `rb.simulated = true` → **无碰撞 + 有物理 = 穿过墙块往下掉**。
  修复在 `DDEN.MPfix.ItemPhysicsGuard`：① Harmony postfix 到 `HeldQuiet.Restore`（反射按名字找类型）恢复后**只补碰撞体**，
  **且只管 `物品物理/被动恢复碰撞范围（GuardRadius）`（默认 5 米，约等于刚丢出去的距离）以内的物品** —— 远处恢复碰撞对玩家没意义，纯属多花开销；
  ② 只在 `Body.DropItem` / `Body.DropWearable`（真正的丢出动作）后**补开碰撞体 / 打开物理**，**绝不移动物品**。
  **三条铁律**（2026-11 因“主机物品一直掉落/浮空”两次收紧）：只处理 `IsFree` 的自由物品（无父级、不在 Container 里）；
  只“打开”不“改写”；任何位移副作用一律删除（早期版本会上移 0.5~3m，位移会残留在局部坐标 →
  表现为“还穿在身上却浮在角色上方”）。
  **不要**把保险挂到 `Container.UnloadItem` / `Utils.Create` 上——那两个时机一局跑上千次，会把手/包里的物品物理打开。
- **物品贴合 / 贴图残留 / 孤儿物品：三个模块已整体删除（2026-11，用户口径）**。
  `WornItemSnap`、`WearableVisualFix`、`OrphanItemFix` 连同 `NearbyBodies`（原本只服务这两个巡检）
  以及开关 `物品物理/SnapWornItems`、`物品物理/SnapMaxPerSession`、`联机修正/CleanStaleWearableVisuals`、
  `联机修正/FixOrphanItems`、`联机修正/MaxOrphanFixPerSession`、`性能/ItemSnapScanSeconds`、
  `性能/WearableVisualScanSeconds` 全部移除。原因：这三件事**是早期修复自己引入的副作用**
  （重复修复导致的错位/残留/孤儿），而且“贴图残留”从未被证实存在——是上一个 agent 的推测。
  **不要再加回来**：它们会去改物品的父级与 localPosition，一旦判断错就是"身上物品乱飞/被挪走"，
  比它想解决的问题更糟。真出现“装备浮空/贴图残留”，先查是谁改了父级，别用巡检去猜。
- **掉线后另一台客机掉帧（`GhostItemRelay` 主机点名，2026-11 第五版）**：
  **先读 MP 自己的退场流程**（decomp/mp554）：主机 `ScavPlayer.OnDestroy` → `PlayerCheckpoint.WriteOne` →
  `new PlayerSavedState(body)`：物品 `SetParent` 到 `SAVESTATE_<nb>` → `NetObjectRegistry.DetachSyncKeepMesh(go)` → 容器 `SetActive(false)`。
  **`DetachSyncKeepMesh` 是 `if (!Net.IsServer) return;`，只改主机注册表、不给客机发任何消息** ——
  客机那份副本于是没人管：`HeldSeat.Detach/Release` 把它 `SetParent(null)` 并设回 Dynamic + `simulated = true`，
  碰撞体却停在"被抓时"的关闭状态 → 一路下坠、永远 sleep 不了（实测 82 件 / 336 刚体 / 2.55 步每帧 / 19fps）。
  注意客机自己的 `SyncRegistry` 仍有这些条目 → `IsRegistered` 为真，所以"按注册状态判断"的写法会全部放过它们。
  - **主修 `GhostItemRelay`**（按用户口径：主机点名、客机执行）：Harmony postfix 到
    `Together.NetObjectRegistry.DetachSyncKeepMesh(GameObject)`（反射挂，因为 Together 不能硬引用），
    记下 `{item.id, 坐标}`，1.2 秒攒批后用 CUCoreLib `MultiplayerBridge.Broadcast("ddn.mp.ghostitems", …)` 广播；
    客机 `RegisterClientHandler` 收到后按 **id + 1.5m 内** 精确命中本地"无主副本"，调
    `NetObjectRegistry.SafeDestroyObject`（MP 自己的销毁通道：先卸容器内容物 → 摘注册表 → Destroy）删掉。
    **不用任何"是不是在下坠/还登不登记"的猜测**；开关 `联机修正/RelayGhostItems`（默认开）。
  - **启发式已删除**（用户口径 2026-11）：`GhostItemCleaner` 连同 `CleanGhostItems` / `CleanGhostItemsMaxPerSession`
    一起删掉。理由：判据不稳（物品可能真的掉很久、还有浮空机制），不靠猜。
  - **卡顿应急已删除**（用户口径 2026-11）：`PerfRescue` 连同 `卡顿应急/*` 四个开关一并删掉。
    原因：它把无主物品冻成运动学并关掉碰撞体，客机侧看起来就是**物品往天上飘**（很诡异），
    而且它只是在掩盖症状。**别再恢复**：卡顿要么靠"主机点名清理"根治，要么不动。
  - `NetPerfTracker` 的「活跃刚体」只数 `Dynamic`（运动学/静态刚体不算压力），指标才反映真实开销。
- **现在的周期节拍**：`WearGhostFix.Tick()`、`GhostItemRelay.Tick()`（主机攒批广播）；
  `[巡检]` 每 60 秒汇总一次（平均 ≥0.5ms 或峰值 ≥2ms 才写），状态页常驻显示 `巡检开销`。
- **“一生二”重复物品（重要）**：合成 / 购买 / 生成在客机上**也会跑一遍本地预测**。
  只要客机的 `Utils.Create` 成功造出模组物品，它自己那份 + 主机同步来的那份就是两份。
  所以 `Utils_Create_ModItemPatch` 的第一道闸是 `if (Mp.IsRunning && !Mp.IsServer) return true;`
  —— **客机永不本地生成**，物品一律以主机为权威。**新增任何生成逻辑前先看这条，别再绕过它**。
- **娱乐开关「道生一 一生二」**：`DDEN.MPfix.DoublePurchase` —— 配置键就叫 `娱乐/道生一 一生二`（默认 false）。
  实现：给 `Together.TraderCommit.RunBuy(trader, actor, job)` 挂前后缀（窗口用**时间戳**不是裸 bool：
  原方法抛异常时 postfix 不执行，bool 会一直挂着）；窗口内由 `Utils.Create` 前缀记录 id
  （`DoublePurchase.NoteId`），后缀在主机侧用同 id 再造一件给买家。
  **复制那件必须走完联机的登记流程，否则买家根本看不到它**（主机多一件幽灵、客机什么都没有）：
  ① `Utils_Create_ModItemPatch.RegisterOnHost`（= 原版 `RunBuy` 反编译 142460 那句）→
  ② 弹药量**保持满弹匣**（用户口径：商人卖出来就是满的）——原版 `TryPurchase_rewritten:144184` 会随机 50%~100%，
  复制那件**故意不抄这一句**，别“修正”成随机；
  ③ `AutoPickUpItem`；拾不进背包就 `SetParent(null)` 留在脚下（已登记，客机收得到）→
  ④ `SyncNow`（`NetObjectRegistry.SafeNewGO`）推状态。
  模组物品还依赖 `联机合成/FixModItemCraftSpawn` 开着，否则 `Utils.Create` 造不出来（只记 Debug）。
  这是**故意**的多一份，和上面那条防重复的规则是两回事，别把它当 bug 修掉。
- **商人商品表不一致（客机付钱不发货）**：`DDEN.MPfix.TraderSnapshotFix` —— 客机每次打开交易菜单
  （postfix `PlayerCamera.ToggleTradeMenu`）向主机发 `ddn.mp.tradersnap`；
  主机用**带发送者**的处理器调 `Together.TraderSync.SendTraderState(knetid)`，
  重发完整商人快照（`TraderCommit.Send` 里含 items 列表）。不自己序列化任何东西。
  根因：`TraderCommit.HandleBuy` 用 `{index,itemId,value}` 三元组在主机侧校验，
  客机列表一旦和主机不同步就校验失败 → 扣钱不发货。
- **客机挖块「血量跳 / 挖了又长回来」**：`DDEN.MPfix.BlockDamageSyncFix` + `BlockDamageRelax`。
  病根三条：
  ① **主机那份伤害天生少了世界伤害倍率**（“血量掉一截又回升”的真凶）—— 原版 `Body.Attack` 第一件事就是
  `atk.damage/atk.structuralDamage *= GetRunSettingFloat("attackdamage")`（Assembly-CSharp 3546-3547）；
  客机那次 Attack 真跑完整流程所以上报值含倍率，而主机在 `ServerReceiver_PlayerBodyAttack` 里先置
  `ForceOnlyRetrieveAttackInfo=true` 再走 `BetterUseItem`，Together 的 `Body.Attack` prefix 抓到
  `LastAttackInfo` 就 `return false` —— **3546/3547 在主机从没执行过**，于是
  `ChooseBlockDamage(hostOwn=…)` 少一个 attackdamage；倍率 >3 时客机数字每次被拒 → 永远挖不动 → 方块重生。
  （`CalculateAttackPower` 里也没有这个倍率，只算 armPowerMult/bonusStrength/STR/爪。）
  ② 原版比例门槛 `clientReported ≤ hostOwn*3` 对模组工具仍偏紧。
  ③ 中继会把客机裂纹“掰”回主机数值 —— `ClientReceiver__PlayerDamageBlockRelay`（43901）拿
  `主机累计 - 本地累计` 当差值喂 `DamageBlock`，可能是负数 → 裂纹往回跳。
  修法分两步，**先治根再兜底**：
  - `BlockDamageRelax`：给 `ChooseBlockDamage` 加**全程接管**的前缀（不再 `return true` 交回原版，
    否则“被拒绝”那条路仍用没乘倍率的 hostOwn 落地、血量照样跳）：
    `hostOwn *= attackdamage`（`GetRunSettingFloat` 读不到/非法值 → 按 1，绝不猜）后再做原版那套
    有限性 + 2000 硬上限 + 比例判定；比例上限可配 `联机修正/BlockDamageRatio`（默认 6，有效下限 3 = 原版）。
    有倍率时打一条 `[挖块] 世界伤害倍率 attackdamage=…` 便于确认补丁真的生效。
  - `BlockDamageSyncFix`：客机本地把方块打到 0 时发 `ddn.mp.blockdmg {x,y,block,total}`；
    主机**先校验**（同一格同一方块 / 发送者 12 格内 / 主机账上这块已掉 ≥`BlockBreakMinFraction`（默认 0.5）血量），
    再**收敛**到客机的累计值（`delta = total - 已损失`，只加不减）而不是无脑打满 —— 一镐敲穿钢墙这条路不通。
    发送者 12 格、同坐标 5s 去重、每局 3000 次上限；中继伤害（`ApplyingRelayedDamage`）不回发。
  **不要退回“主机一次打满血量”的老做法**：那等于取消硬度与工具判定。
- **工具耐久同步：已整体删除（2026-11）**。`DDEN.MPfix.DurabilityFix` 与 `联机修正/SyncToolDurability`
  都已移除 —— MP 自身已修好这条，我们不再叠一套（配置里若还留着 `SyncToolDurability` 那行是无害残留）。
  **不要再加回来**：它要同时猜“主机重跑有没有扣过”，双扣/漏扣都比原版更难调。
- **删幽灵物品必须先保内容物**：`WearGhostFix.TryRetireGhost` 先把子物品逐个脱离落地，
  含液体（`WaterContainerItem.stack`）或取不干净就**保留不删**，只有空壳才 Destroy。
- **配置界面必须是两行一条**：上行「键 + 控件」，下行说明（`DdenUI.ConfigRow`，行高 44）。
  “全部配置”页必须把**所有模块的配置**都列出来（按模块分块、按 Section 分组），不能要求先选模块才看得到。
  说明放不下时用 `DdenUI.Hover` 让鼠标悬停看全文。
- **外部模组接入（给别人用）**：`DDEN.Shared.DdenExternal` 在管理器 `Start()` 扫描所有已加载插件程序集，
  认出约定成员（`DdenConfig` / `DdenTitle` / `DdenSummary` / `DdenDraw(Rect)`）后收进管理器。
  **对方不需要引用 `DDEN.Shared.dll`**。文档：[docs/cumodm-integration.md](docs/cumodm-integration.md)；
  端到端验证用 `tools/TestExternalConfig/`（编译后丢进 plugins 看一眼再删，别常驻）。
  入口有三处，别只留「全部配置」：侧边栏「外部模组（只交配置）」、总览卡片、全部配置页的【单独看】，
  三个都指向 `ext:<下标>` 页（有 `DdenDraw` 画自定义页，没有就把它的 `ConfigEntry` 画成配置列表）。
  实测能自动认出 `QoL-EMI-RecipeRowFix` / `ET-NoBatteryFix` 这类别人做的插件并给它们生成独立配置页。
- **授权边界（2026-11 定，别越界）**：**代码 = MPL-2.0**（根目录 `LICENSE`）；
  **图标 / 名称 / 世界观文本 = 保留所有权利**（`LICENSE-ASSETS`）；第三方归属与「不复制别人代码」的说明在 `NOTICE`。
  规矩：① 新增任何「资源」（png、字体、世界观文案、专有名词）必须同步登记到 `LICENSE-ASSETS`；
  ② **不要把世界观文案写成 .cs 里的字符串** —— MPL 的「软件」会连带覆盖它，文案放 `Resources/*.txt`
  由代码读取（例：`about.txt` 的 key=value 解析）；③ 借阅外部代码前先确认对方许可；
  ④ 反编译只用于阅读结论，不复制代码，`decomp/` 永不提交；⑤ 新增源码文件不必加版权头（`LICENSE` 已覆盖），
  但**不要**把别人的版权声明删掉。
- **不要在模块里重复造兼容判断**，也别各自写自己的弹窗系统。
- **存档编辑红线**：只改用户点过的字段；`cheatedSave`（禁用成就）**绝不自动写入**，仅由“进度/成就”页手动切换；
  写盘只走 `SaveCodec.Write` + `SyncMeta`（原子 + `.cumod.bak`），写前查 `Compat.IsQolAutosaveBusy()`。
- 不碰别人放在 plugins 下的 dll；不 git push（本地仓库由用户自行建远端）。

## 4. 验证流程（三件套，全部真实运行）

```bash
touch "<游戏>/BepInEx/config/cumod.selftest"   # 数据自检（用副本，跑完自删）
touch "<游戏>/BepInEx/config/cumod.uitest"     # 界面冒烟（自动轮播全部页面/标签页）
touch "<游戏>/BepInEx/config/cumod.cfgtest"    # 直接停在“全部配置”页（截图/核对排版用）
# 启动游戏 → 等 8~40 秒 → 看日志
grep -nE "SELFTEST|UITEST|Loading \[DDEN|Loading \[DDExcNet" "<游戏>/BepInEx/LogOutput.log"
```

已验证基线（2026-11-20）：
- 4 个 dll 从 `plugins/DDEN/` 加载成功；
- 管理器快捷键 F8 与 SkinSync 冲突 → 自动改 F11 并写配置；
- 兼容性扫描 23 条，识别出 `SaveSystem.SaveGame` 上 CasualtiesMP / QoL / CUCoreLib 三方补丁栈；
- 存档自检：126 物品 / 15 肢体 / 51 运行设置 / 144 字段，图标解析 6/6，`cheatedSave` 保持原值 PASS；
- 界面冒烟（`cumod.uitest`，2026-11-20 已含新页）：总览 / 兼容性 / 全部配置 / **关于** / 模块页 / **单模块配置页** /
  **每个外部模组页 `ext:<i>`** 全部 PASS，日志见 `[CFG] 单独查看配置：…` 与 `[UI] 内嵌图标已加载：16×16`。
  冒烟只判“绘制不抛异常”+窗口没被关掉 —— 加了新页面记得把页名加进 `UiSmokeTest` 的 `pages`。

已知无害噪声：`[Error : Together] Failed to patch: MovementCourse_Start_MultiplayerPatch`（装模组前就有）。

启动游戏用 `app_launch`（清 `ELECTRON_RUN_AS_NODE`）；注意游戏会二次自启，pid 会变、`LogOutput.log` 会被新实例截断。

## 5. 本机 bash 怪癖

| 现象 | 绕法 |
| --- | --- |
| 无 `python` / `node` / `gzip` / `jq` / `perl` | 用 `dotnet`、PowerShell，或直接编辑文件 |
| `tasklist //FI` 报错 | `powershell -c "Get-Process CasualtiesUnknown"` |
| 大文件不要 `cat/sed` | 用 `read` / `ops_inspect_local_file` |

PowerShell 解存档：

```powershell
$in=[IO.File]::ReadAllBytes($p)
$gz=New-Object IO.Compression.GZipStream((New-Object IO.MemoryStream(,$in)),[IO.Compression.CompressionMode]::Decompress)
(New-Object IO.StreamReader($gz,[Text.Encoding]::ASCII)).ReadToEnd()
```

## 6. 存档 / 数据

- `*.sv = GZip(ASCII JSON)`（与 `SaveSystem.Zip/Unzip` 同构）；改档只碰已知键，未知 JToken 原样保留。
- QoL 存档位：`slot_N.sv` + `slot_N.sv.meta` + `slot_N.mp/`（含 `STEAM_<id>/save.sv` 每人快照）。
- 写盘：临时文件 → `.cumod.bak` → 顶替；再刷 `.sv.meta` 的 `BiomeIndex/IGTSeconds/RTASeconds/Timestamp`。

## 7. 反编译参考（只读、不进 git）

```bash
ilspycmd -o decomp/game --nested-directories "<Managed>/Assembly-CSharp.dll"
ilspycmd -o decomp/core --nested-directories "<plugins>/CUCoreLib.dll"
ilspycmd -o decomp/mp   --nested-directories "<plugins>/Together/CasualtiesMP.dll"
ilspycmd -o decomp/qol  --nested-directories "<plugins>/QoL Unknown 1.1.5 - 实验剪切版.dll"
```

| 符号 | 位置 |
| --- | --- |
| `WorldGeneration.unchippedMode/SetUnchipped`、`SaveSystem.SaveGame/Zip/Unzip`、`Item.GlobalItems` | `decomp/game/Assembly-CSharp.decompiled.cs` |
| `Together.WorldMP+RunUnchippedLock`、`Util.IsUnchipped`、`MultiplayerGameRules` | `decomp/mp/CasualtiesMP.decompiled.cs` |
| `QoL_Unknown.SaveManager/SaveFileStore/SaveMetadata` | `decomp/qol/…` |
| `CUCoreLib.Networking.* / Registries.* / Helpers.AssetLoader` | `decomp/core/CUCoreLib.decompiled.cs` |

## 8. Git / 分支 / 发布

**已 `git init` 并有远端：`origin = https://github.com/cubstaryow/DDExcNet-CUmodM.git`，默认分支 `master`。**

- **分支模型（2026-11 定）**：`master` = 发布分支（只放验收过的），`dev` = 日常工作分支。
  **agent 每次会话的改动先提交并 push 到 `dev`**；用户验收通过后再由用户合并到 `master`。
  详见 [docs/workflow.md](docs/workflow.md)。
- **不要直接往 master 提交功能改动**，也不要替用户做合并/打 tag；`git push origin dev` 是例行动作，
  但**不要** force push、不要改 remote、不要删别人的分支。
- **发布**：`pwsh scripts/release.ps1` 打包到 `dist/`（已 git-ignore）；`-Upload` 走 gh CLI 或 `GITHUB_TOKEN`
  自动建 Release；用户用的是 **GitHub 桌面版**，它**不能发 Release 也不能建 tag** —— 所以手动发版走
  `-OpenPage`（复制发布说明 + 打开网页）再拖 zip 上传。版本号**两处**要一起改：`Directory.Build.props` 的
  `<DdenVersion>`（csproj 的 `<Version>` 用它）与各插件的 `[BepInPlugin(..., "x.y.z")]`（特性里读不了 MSBuild 属性）。
- **CI 构建不了**，别尝试加 Actions：编译需要游戏本体的 `Assembly-CSharp.dll` / `UnityEngine.*` 与
  `0Harmony.dll`，这些是商业游戏安装文件，不能进仓库也不能传 CI —— 只能本地构建再上传产物。
- 不提交 `decomp/`、`bin/`、`obj/`、`dist/`、`backup/`、`artifacts/`。
