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
./scripts/build.sh              # 构建四件套 → 复制到 plugins/DDEN，并删除旧 CUmod.*.dll 残留
./scripts/build.sh Release
dotnet build CUmod.slnx -v m    # 只编译
```

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
  修复在 `DDEN.MPfix.Utils_Create_ModItemPatch`：给 `Utils.Create` 加前缀，Resources 找不到时用
  `CUCoreLib.Helpers.CustomInstantiate.InstantiateReturn` 生成，并在主机侧补 `NetObjectRegistry.Server_EnsureItemIsNetworkRegistered`
  （开关 `联机合成/FixModItemCraftSpawn`、`联机合成/RegisterSpawnedItemsOnHost`）。
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
  修复在 `DDEN.MPfix.ItemPhysicsGuard`：① Harmony postfix 到 `HeldQuiet.Restore`（反射按名字找类型）恢复后真正打开碰撞体；
  ② `Body.DropItem/DropWearable`、`Container.UnloadItem`、`Utils.Create` 后统一做保险
  （开碰撞体、`Continuous` 防隧穿、压在 Ground 层里就上移脱困）。开关 `物品物理/GuardDropPhysics`。
- **删幽灵物品必须先保内容物**：`WearGhostFix.TryRetireGhost` 先把子物品逐个脱离落地，
  含液体（`WaterContainerItem.stack`）或取不干净就**保留不删**，只有空壳才 Destroy。
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
