# AGENTS.md —— DDEN / CUmod 工作区常驻须知

> 给**下一个接手的人/AI** 看的第一份文件。读完就能独立构建、部署、验证，不用重新考古。
> 设计文档：[docs/README.md](docs/README.md)　社区前置签名：[docs/cucorelib-api.md](docs/cucorelib-api.md)
> **对外接入文档（给别人模组用）**：[docs/cumodm-integration.md](docs/cumodm-integration.md)

## 0. 这是什么

《Casualties Unknown》(Steam appid 4576490) 的 BepInEx 5 模组工作区。**三个 dll + 一个共享库**，统一由管理器承载界面：

| 产物 | 源码 | 作用 |
| --- | --- | --- |
| `DDExcNet-CUmodM.dll` | `src/DDExcNet.CUmodM/` | **管理器**：统一功能界面 + 全部配置界面 + **兼容性总控** |
| `DDEN.MPfix.dll` | `src/DDEN.MPfix/` | 多人相关修复合集：无芯片同步 + 联机合成/物品生成兜底 + 物品网络登记 |
| `DDEN.SaveEditor.dll` | `src/DDEN.SaveEditor/` | 可视化存档编辑器（图标/血条点选，QoL 多存档位） |
| `DDEN.Shared.dll` | `src/DDEN.Shared/` | 公共层：反射、日志、联机桥、存档编解码、**IDdenModule / DdenHub / Compat / DdenUI** |

部署目录：**`<游戏>/BepInEx/plugins/DDEN/`**（已实测 BepInEx 会递归扫描子目录）。

## 1. 环境事实（**已验证，别猜**）

| 项 | 值 | 依据 |
| --- | --- | --- |
| Unity | 2022.3.62f3 / Mono | `globalgamemanagers` |
| 目标框架 | **netstandard 2.1** | `Managed/netstandard.dll` = 2.1.0.0 |
| 加载器 | BepInEx 5 + HarmonyX（`UnpatchAll(id)` 已 error 级过时 → 用 `UnpatchSelf()`） | core 目录 |
| 联机 | Together / CasualtiesMP 5.5.3，GUID `CasualtiesMP` | cfg |
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

- **默认不部署存档编辑器**：正常游戏只带共享层 + 管理器 + MPfix；如果之前部署过，脚本会主动把
  `plugins/DDEN/DDEN.SaveEditor.dll` 撤下来（游戏在跑导致文件被锁时会提示关游戏重跑）。
- **`DDEN/` 目录不是我们的私有目录**：里面可能还有别人做的修复 mod
  （实例：`ET-NoBatteryFix.dll`、`QoL-EMI-RecipeRowFix.dll`）。`scripts/build.sh` 用**白名单**
  （`OWNED=(DDEN.Shared / DDExcNet-CUmodM / DDEN.MPfix / DDEN.SaveEditor)`）决定写谁、删谁，
  并在结尾打印 `== 保持不动（其它模组 / 非本脚本产物）: …`。
  **任何情况下都不要对 `DDEN/` 做通配删除**（`rm -rf`、`rm *.dll`）；新增产物必须同时加进 OWNED。
- **每次部署自动备份到仓库根目录**（`backup/` 已 git-ignore）：
  `backup/plugins_<时间戳>/*.dll + manifest.txt`（含 md5/大小/配置/是否带编辑器）、
  `backup/latest/`（最近一次副本）、`backup/DDEN-plugins-<时间戳>.zip`（有 PowerShell 时）。

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
  （开关 `联机合成/FixModItemCraftSpawn`、`联机合成/RegisterSpawnedItemsOnHost`）。
  **两条都不行就放弃兜底**（交回原版，该配方该失败就失败）——**绝不克隆现存物品、绝不做任何 Destroy**：
  克隆容器会把内容物一起复制（凭空刷物品），清理子物品又是一次删除，风险远大于收益。
  **失败必须留痕**：会打一条 `[CraftFix] 生成失败 '<id>'：…（含注册表是否命中）`（同 id 30s 去重）。
  **绝对不要加“失败即永久拉黑这个 id”的负缓存**——首次调用可能早于模组注册/资源就绪，一旦拉黑该配方整局都失败。
  注意 Together 会按 attempt 缓存失败 receipt，同一会话重试仍失败，换层/重连才会清 —— 这是“过一层又能合成”的来源。
- **穿戴兜底（联机）**：`DDEN.MPfix.WearGhostFix` —— 客机穿上物品后 1.5s，经 CUCoreLib 频道
  `ddn.mp.wearfix` 把 {id, 位置} 发给主机；主机用**带发送者**的处理器拿到 clientId →
  `ScavPlayer.GetBodyFromClientId` 找到该玩家 body → 按 id + 位置找那件物品（排除别人身上那件）→
  `Server_EnsureItemIsNetworkRegistered` 补登记 → `Together.ItemSync.Body_ForceWearWearable` 穿上 →
  `body.GetWearable(id) == item` 校验。**默认不重建、不复制、不掉落**。
  只有 `RebuildIfHostLacksItem`（默认 false）开启且主机真的找不到时才走“掉地+重建”。
  保守原则：异常/通道失败即本会话禁用并 Warn；每局次数上限。
  无芯片 watchdog 默认 **30s**（旧配置 0.5 会自动迁移）。
- **物品穿墙掉落（联机）**：真凶是 Together 的 `HeldQuiet`（`Internal/HeldQuiet`）——
  `Awake` 里把物品所有 `Collider2D` 关掉并记下当时状态，`Restore()` 再写回；
  若挂上那一刻碰撞体本来就是关的（物品在背包里 / 模组物品后加的碰撞体不在名单里），写回后仍然是关的，
  而 `HeldSeat` 已经把 `rb.simulated = true` → **无碰撞 + 有物理 = 穿过墙块往下掉**。
  修复在 `DDEN.MPfix.ItemPhysicsGuard`：① Harmony postfix 到 `HeldQuiet.Restore`（反射按名字找类型）恢复后**只补碰撞体**；
  ② 只在 `Body.DropItem` / `Body.DropWearable`（真正的丢出动作）后**补开碰撞体 / 打开物理**，**绝不移动物品**。
  **三条铁律**（2026-11 因“主机物品一直掉落/浮空”两次收紧）：只处理 `IsFree` 的自由物品（无父级、不在 Container 里）；
  只“打开”不“改写”；任何位移副作用一律删除（早期版本会上移 0.5~3m，位移会残留在局部坐标 →
  表现为“还穿在身上却浮在角色上方”）。
  **不要**把保险挂到 `Container.UnloadItem` / `Utils.Create` 上——那两个时机一局跑上千次，会把手/包里的物品物理打开。
- **物品贴合修正**：`DDEN.MPfix.WornItemSnap` —— `Body.WearWearable` 后立即校验，且每 2 秒扫一遍附近 Body 的
  穿戴/手持物品；`localPosition` 超过 0.6 单位（正常恒为 0）就归零，并把
  `parent / localPos / localScale / worldPos` 打进日志（用来定位究竟是谁改了局部坐标）。
  只改 localPosition，**不动 rotation（slotRotation）与 scale（HeldSeat 的肢体补偿）**；
  每局最多 `物品物理/SnapMaxPerSession`（默认 100）次后自动停手。
- **“一生二”重复物品（重要）**：合成 / 购买 / 生成在客机上**也会跑一遍本地预测**。
  只要客机的 `Utils.Create` 成功造出模组物品，它自己那份 + 主机同步来的那份就是两份。
  所以 `Utils_Create_ModItemPatch` 的第一道闸是 `if (Mp.IsRunning && !Mp.IsServer) return true;`
  —— **客机永不本地生成**，物品一律以主机为权威。**新增任何生成逻辑前先看这条，别再绕过它**。
- **娱乐开关「道生一 一生二」**：`DDEN.MPfix.DoublePurchase` —— 配置键就叫 `娱乐/道生一 一生二`（默认 false）。
  实现：给 `Together.TraderCommit.RunBuy` 挂前后缀；购买窗口内由 `Utils.Create` 前缀记录 id
  （`DoublePurchase.NoteId`），后缀在主机侧用同 id 再造一件并 `AutoPickUpItem` 给买家。
  这是**故意**的多一份，和上面那条防重复的规则是两回事，别把它当 bug 修掉。
- **商人商品表不一致（客机付钱不发货）**：`DDEN.MPfix.TraderSnapshotFix` —— 客机每次打开交易菜单
  （postfix `PlayerCamera.ToggleTradeMenu`）向主机发 `ddn.mp.tradersnap`；
  主机用**带发送者**的处理器调 `Together.TraderSync.SendTraderState(knetid)`，
  重发完整商人快照（`TraderCommit.Send` 里含 items 列表）。不自己序列化任何东西。
  根因：`TraderCommit.HandleBuy` 用 `{index,itemId,value}` 三元组在主机侧校验，
  客机列表一旦和主机不同步就校验失败 → 扣钱不发货。
- **客机挖块后又长回来**：`DDEN.MPfix.BlockBreakFix` —— 客机挖块是本地乐观结算
  （`WorldGeneration.DamageBlock`），是否“落地”取决于主机
  `BlockDamageAuthority.ChooseBlockDamage(hostOwn, clientReported, clientId)` 的校验；
  主机没打碎时 chunk 同步会把原 tile 推回来，看起来就是“挖掉了又出现”。
  修法：客机本地把方块打成 0 的瞬间发 `ddn.mp.blockbreak {x,y}`，主机按自己方块表的剩余血量
  用一次足量 `DamageBlock` 打碎（走原版流程，掉落/音效/广播都正常），再靠主机的 SetBlock 广播对齐所有客机。
  同坐标 5s 去重、每局 3000 次上限。
- **工具耐久同步（实验，默认关闭）**：`DDEN.MPfix.DurabilityFix` —— 耐久扣减在物品 `useAction` 里，
  联机攻击由主机重跑 `ItemSync.BetterUseItem`，主机那次 `Attack` 不成立时耐久不降 → 客户端值被覆盖。
  客机量差值发 `ddn.mp.tooldura`，主机补扣；并观测 `BetterUseItem` 前后变化做去重（1.5s 内主机已扣过就跳过）。
  **注意**：用户实测更常见的是“方块重生”而不是耐久闪烁，所以 `联机修正/SyncToolDurability` 默认 **false**，
  只有确认存在耐久不同步时才开。
- **删幽灵物品必须先保内容物**：`WearGhostFix.TryRetireGhost` 先把子物品逐个脱离落地，
  含液体（`WaterContainerItem.stack`）或取不干净就**保留不删**，只有空壳才 Destroy。
- **身上挂着装备贴图但其实没穿**：`DDEN.MPfix.WearableVisualFix` —— 穿戴时 `Wearable.CreateSprites`
  会在肢体下生成纯视觉子物体（`WearableExtension`，`reference` 指向 Item），正常卸下由
  `Body.DropWearable → ClearSprites()` 清理；但联机里物品常被 Together 用 `SetParent(null)` / `HeldSeat` /
  同步搬走，这条路不清理 → 贴图残留。
  修法：① `Body.DropWearable` postfix 再补一次 `ClearSprites()`（幂等）；
  ② 每 2 秒巡检附近身体的 `WearableExtension`：`reference` 为空、或它挂的 Item 已不属于该身体 →
  **连续两次巡检（≥1.5s）都成立**才销毁这层视觉（只删视觉子物体，绝不碰真实物品）。
  开关 `联机修正/CleanStaleWearableVisuals`。
- **物品卡在玩家中心（能打开、但不算穿戴也不在背包）**：`DDEN.MPfix.OrphanItemFix` ——
  Together 的 `HeldSeat` 把物品挂到 `InventorySlot`（父级=槽位、localPosition=0）来表现远端的手/背包，
  槽位记账错位时物品就一直挂在身体中心，`GetItem(slot)` / `GetAllWearables()` 里却找不到。
  修法：**只看本地身体**，每 2 秒扫“挂在身体下、父链里没有别的 Item（排除容器内容）、不属于任何槽位/穿戴”的物品，
  连续 2.5 秒成立 → 先 `AutoPickUpItem` 归位；拾不进背包就 `SetParent(null)` 放回地面（物品不删）。
  每局上限 `联机修正/MaxOrphanFixPerSession`（默认 50），开关 `联机修正/FixOrphanItems`。
- **配置界面必须是两行一条**：上行「键 + 控件」，下行说明（`DdenUI.ConfigRow`，行高 44）。
  “全部配置”页必须把**所有模块的配置**都列出来（按模块分块、按 Section 分组），不能要求先选模块才看得到。
  说明放不下时用 `DdenUI.Hover` 让鼠标悬停看全文。
- **外部模组接入（给别人用）**：`DDEN.Shared.DdenExternal` 在管理器 `Start()` 扫描所有已加载插件程序集，
  认出约定成员（`DdenConfig` / `DdenTitle` / `DdenSummary` / `DdenDraw(Rect)`）后把它们的配置渲染进「全部配置」页。
  **对方不需要引用 `DDEN.Shared.dll`**。文档：[docs/cumodm-integration.md](docs/cumodm-integration.md)；
  端到端验证用 `tools/TestExternalConfig/`（编译后丢进 plugins 看一眼再删，别常驻）。
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
- 界面冒烟：overview / compat / config / 两个模块页 / 两个配置页 / 编辑器五标签页 PASS。

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

## 8. Git

本仓库**已 `git init` 并做了首次提交，没有远端、没有 push**。用户会自己建 GitHub 仓库再推。
不要自行 `git push`、不要改 remote、不要提交 `decomp/`、`bin/`、`obj/`。
