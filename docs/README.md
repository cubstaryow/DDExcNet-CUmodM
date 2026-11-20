# DDEN / CUmod —— Casualties Unknown 模组套件

工作区：`L:\github\game\CUmod`　游戏：`L:\SteamLibrary\steamapps\common\Casualties Unknown`

> 别的模组怎么接入（交出配置 / 注册模块 / 用公共能力）：见 [cumodm-integration.md](cumodm-integration.md)。

---

## 0. 一套四件（部署在 `BepInEx/plugins/DDEN/`）

| 文件 | 作用 |
| --- | --- |
| `DDExcNet-CUmodM.dll` | **统一管理器**：功能界面 + 全部配置界面 + 兼容性总控 |
| `DDEN.MPfix.dll` | 多人相关修复合集：无芯片同步 + 联机合成/物品生成兜底 + 物品网络登记 |
| `DDEN.SaveEditor.dll` | 可视化存档编辑器（图标/血条点选，QoL 多存档位） |
| `DDEN.Shared.dll` | 公共层（模块契约、UI 皮肤、兼容层、反射、存档编解码） |

BepInEx 会递归扫描 `plugins` 子目录，已实测各 dll 从 `plugins/DDEN/` 正常加载。
`scripts/build.sh` 会自动部署并删除旧命名残留（`plugins/CUmod.*.dll`）。

```bash
cd L:/github/game/CUmod
./scripts/build.sh                            # 构建 + 部署（先关游戏，否则 dll 被锁）
                                              # 默认只部署 Shared / CUmodM / MPfix，**不带存档编辑器**
./scripts/build.sh Debug --with-saveeditor    # 需要存档编辑器时显式加参数
```

- 若之前部署过 `DDEN.SaveEditor.dll`，默认构建会**主动把它撤下来**（正常游戏不带它）；
- 部署**不再自动生成备份**（2026-11 移除）：需要留旧版就在部署前自己复制一份
  `BepInEx/plugins/DDEN/`。

---

## 1. 管理器 `DDExcNet-CUmodM`

- 快捷键 **F11**（默认 F8，检测到与 `Skin Sync Mod:RescanSkins` 冲突后**自动改键并写配置**）。
- 控制台：`dden`。
- 侧边栏：**总览 / 兼容性 / 全部配置 / 各模块页**；每个模块的配置页由管理器统一渲染（也支持模块自绘）。
- 窗口位置记忆在 `PlayerPrefs`；`GUI.depth = -1000`，画在其它模组窗口之上；缩放可调。
- 模块契约（`DDEN.Shared.IDdenModule`）：

```csharp
string Id / DisplayName / Version / Summary / bool Ready;
void DrawPage(Rect area);        // 功能界面
bool DrawSettings(Rect area);    // 返回 false → 管理器用 DdenUI 自动渲染 ConfigEntries
IEnumerable<ConfigEntryBase> ConfigEntries { get; }
```

新增自己的模组：实现这个接口 + `DdenHub.Register(this)`，界面与配置就自动进管理器；
管理器缺席时模块用 `DdenUI.DrawStandalone` 退化成独立小窗（快捷键各自配置）。

---

## 2. 兼容性总控（集中在管理器，不在各模块里各写一套）

`DDEN.Shared.Compat` 提供、管理器“兼容性”页展示：

| 检测项 | 做法 | 已实测结果 |
| --- | --- | --- |
| 已装模组清单 | 扫 `Chainloader.PluginInfos`，按 GUID/名称关键词归类（前置/联机/QoL/…） | 23 条 |
| **快捷键抢键** | 扫所有插件的 `KeyboardShortcut` 配置，冲突则报警并按需自动改到空闲 F 键 | F8 被 SkinSync 占用 → 自动 F11 |
| **补丁栈** | `Harmony.GetPatchInfo` 看同一方法上有哪些 owner | `SaveSystem.SaveGame`: CasualtiesMP + QoL + CUCoreLib；`PlayerCamera.Update`: 5 个 owner |
| **旧 dll / 重复程序集** | 扫 `CUmod.*.dll` 残留、同名 dll 多目录同时存在 | 部署脚本自动清理 |
| **QoL 自动存档互斥** | 写档前查 `QoL_Unknown.SaveManager.autosaveInProgress` | 忙时拒绝写并提示 |
| **点击穿透** | 面板显示期间禁用场景里全部 EventSystem（`DdenUI.InputGate`，引用计数），关闭即恢复；快捷键带 0.25s 去抖 | 面板开：点不到开始/退出；面板关：正常交互 |
| **联机写档风险** | `Mp.IsRunning` → 提示“只改本地文件，主机世界不受影响” | — |
| **控制台指令撞名** | 注册前 `Compat.ConsoleHasCommand` | `dden` / `saveedit` |

模块侧只问结论：`Compat.IsQolAutosaveBusy()`、`Compat.ResolveHotkey(...)`、`Compat.ConsoleHasCommand(...)`。

---

## 3. `DDEN.MPfix`（多人修复合集）

一个 dll 里装三件事，界面/配置都在管理器里：

| 分组 | 内容 |
| --- | --- |
| 无芯片同步 | 主机裁决经 快照 / 推送 / 请求 三路下发，客机 watchdog 纠偏（默认 **30s**，芯片状态通常一次同步就够；`ForceHostUnchipped`、`OverrideIndividualRule`、`WatchdogSeconds`） |
| 穿戴兜底 | 客机穿上后主机没认下来时：主机按 id 找到那件物品、补登记并 `Body_ForceWearWearable` 穿上（**不重建/不复制**；`GraceSeconds`、`MaxPerSession`；`RebuildIfHostLacksItem` 默认关） |
| 联机合成 | `FixModItemCraftSpawn`：给 `Utils.Create` 加前缀，模组物品不在 Resources 时用 `CustomInstantiate` 生成，修 “无法生成物品” |
| 物品网络身份 | `RegisterSpawnedItemsOnHost`：兜底生成的物品立刻 `NetObjectRegistry.Server_EnsureItemIsNetworkRegistered`，修“主机看到装备掉下去又回来” |

### 与 Together 版本校对（5.5.3 → 5.5.4，逐点反编译核对）

| 我们的功能 | 5.5.4 现状 | 结论 |
| --- | --- | --- |
| 无芯片同步 | `WorldMP.RunUnchippedLock` / `Util.IsUnchipped` / `BlocksLocalChange` 一字未改；`ApplyRunSettings` 依旧只写 `runSettings`，**不写** `world.unchippedMode` | **仍需要** |
| 联机合成模组物品 | `PlayerCamera_TryCraft_MultiplayerPatch.RecipeResult_SpawnResult` 仍 `Utils.Create(rres.id,…)` → `Resources.Load`；新增的 `Utils.Create` 补丁只有「客户端不生成 glassshards」和「生成期跳过水晶」两条，没有自定义物品兜底 | **仍需要** |
| 物品网络身份 | `ServerReceiver__ItemWearing` 仍以 `TryGetItem(登记表)` 为门控，找不到就 `Server declines itemwearing`；登记只发生在读档 / 小游戏 / 手持等路径，水晶掉落的物品不会自动登记 | **仍需要** |

5.5.4 新增但**不能替代**我们修复的东西：
- `CustomItemBehaviour` 状态同步（`ItemSync`）：只同步模组物品“状态”，不负责“生成”；
- `EvidenceCache` 会给 `Resources.Load` / `CustomInstantiate.ResolvePrefab` 挂诊断补丁：说明它已经知道 CUCoreLib 自定义物品的存在，但生成仍走原版路径；
- `Utils_Create_SkipClientCrystalDuringGen`：只处理「客户端生成期不要造水晶」，与掉落物品登记无关。

插件启动时会打一段 `[MPCOMPAT]` 自检日志，直接把上面结论和被检测到的 Together 版本写进 `LogOutput.log`。

游戏里“有没有芯片”只有 `WorldGeneration.unchippedMode` 一个开关，所有判定（深度表/视野/伤口/心搏骤停/辐照/枪膛）
都读它，而它**没有网络同步**；Together 的主机裁决分支默认关闭（`UnchippedIsIndividual = true`），
且只在生成世界那一刻写一次 → 中途改动客机看不到 = 你看到的“主机无芯片、客机还有芯片”。

修法（全部走 CUCoreLib 公开 API，不 fork Together）：

```
主机 capture() → 联机快照 ──► 客机 apply        （晚进主机也拿得到）
主机 Broadcast(push) ───────► 客机立即应用
主机 RegisterServerHandler ◄── 客机 RequestServer（连上兜底问一次）
应用 = world.SetUnchipped + 镜像 Together.WorldMP+RunUnchippedLock + 同步本地 ScavPlayer
       + 0.5s watchdog 纠偏 + 可选把 UnchippedIsIndividual 置 false
```

配置：`ForceHostUnchipped` / `OverrideIndividualRule` / `WatchdogSeconds` / `AnnounceToHostChat` / `Verbose`。
模块页可直接：按主机裁决纠正一次、重新向主机请求、**主机侧切换开/关**。

---

## 4. `DDEN.SaveEditor`（可视化）

`F11` 打开管理器 → 存档编辑器页（管理器缺席时 `F9` 独立小窗）。

```
[工具条] 读取 / 保存 / 满治疗 / 计时归零 / 负值清零 / 同步到角色 / 刷新 / 筛选
[左 290] 存档位（QoL 多存档 + *.mp 内每人快照，当前档标记）
[中]     标签页：物品 · 肢体 · 身体 · 运行设置 · 进度/成就
[右 310] 上下文检查器
```

- **物品页**：图标网格（图标来自 `CUCoreLib.Registries.ItemRegistry.TryGetIcon` → 模组注册资源 + 游戏原资源，
  回退 `Resources.Load<Sprite>`），每格下面一条耐久条，颜色随耐久；点选后右侧滑杆/数字精修、
  收藏开关、标签、**单件移除**（同步删 `itemComponents` 对应项，避免错位）。
- **肢体页（图形化）**：默认是**人体图**——世界内直接读原版 `WoundView` 的部位图标与位置（`limbImages / origPositions`，
  索引与 `body.limbs` 一一对应），把同一套美术按同样构图缩放画出来；退出世界后布局缓存在
  `BepInEx/config/dden.limbs.json`，主菜单用同一构图（无美术时用状态色块）。颜色 = 皮肤/肌肉里较差的一项，
  悬停看具体数值，点击选中后右侧逐字段精修（骨折/脱臼/感染/截断/疼痛/出血…）。右上角“列表”可切回 15 格血条视图。
- **身体 / 运行设置**：144 / 51 项数值与开关，两列滚动列表。
- **进度/成就**：顶层字段 + 一张显眼的卡片——**是否禁用成就（cheatedSave）**。
  编辑器**绝不自动写这个字段**；勾选＝把该档标记为作弊档，取消＝恢复计入成就。自检会比对改档前后该值不变（PASS）。
- QoL 兼容：写盘后同步刷 `.sv.meta`（`BiomeIndex/IGTSeconds/RTASeconds/Timestamp`），QoL 存档列表显示不会过期；
  写盘前查 QoL 是否正在自动存档；写入原子化并留 `*.sv.cumod.bak`。
- “同步到角色”：把 body / limbs / skills 的数值直接打进**内存里的** `Body`（不写文件、不重载世界）。

---

## 5. 开发环境

- `Directory.Build.props`：netstandard2.1、本地程序集引用（`Private=false`）、`DdenPluginsDir`、`DeployOnBuild`。
- `CUmod.slnx` 四个项目；`scripts/build.sh` 一键构建部署。
- 自检标记（不看启动参数）：
  - `BepInEx/config/cumod.selftest` → 数据自检（QoL 存档位、读写回环、图标、模块数、兼容条目）
  - `BepInEx/config/cumod.uitest` → 界面冒烟（自动打开窗口、轮播全部页面 + 编辑器五个标签页，异常会被捕获写日志）

实测基线（2026-11-20）：

```
[CUmodM] 管理器就绪（F11 开关）；快捷键冲突 F8 → F11
[MPUnchipped] SELFTEST … RunUnchippedLock 写读回环 = PASS | 结论 PASS
[SaveEditor] SELFTEST … 126 物品 / 15 肢体 / 51 运行设置 / 144 字段
             图标解析 6/6 | cheatedSave 保持原值 PASS | 模块 2 个 | 结论 PASS
[CUmodM] [UITEST] PASS：所有页面绘制完成，无异常
```

---

## 6. 排障

| 现象 | 处理 |
| --- | --- |
| 日志没有 `Loading [DDEN…]` | 四个 dll 必须同在 `plugins/DDEN/`；`DDEN.Shared.dll` 少了会 TypeLoadException |
| 管理器打不开 | 看 `com.dden.cumodm.cfg` 里的 `ToggleKey`（可能被自动改键了） |
| 快捷键仍冲突 | 兼容性页会列出占用者；关掉 `AutoFixHotkeyConflicts` 则只警告不改键 |
| 写档被拒 | 多半是 QoL 正在自动存档，等 1 秒重试 |
| 存档写坏 | 同目录 `*.sv.cumod.bak` 改名回去 |
| 面板盖住别的模组 | 本管理器 `GUI.depth=-1000` 恒在上层；想临时收起按快捷键 |

---

## 7. Git

本仓库已在根目录 `git init` 并完成首次提交，**没有远端、没有 push**。
`.gitignore` 排除 `bin/ obj/ decomp/ artifacts/ *.cumod.bak`。
建好 GitHub 仓库后自行：

```bash
git remote add origin <url>
git push -u origin master
```
