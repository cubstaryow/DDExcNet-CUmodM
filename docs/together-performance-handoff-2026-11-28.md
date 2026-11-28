# Together低帧与多线程优化交接单

更新时间：2026-11-28（Asia/Hong_Kong）。交接对象：接手排查与代码评审的agent。

## 1. 用户目标与当前结论

用户反馈：同一存档中，开启Together服务器后low帧明显恶化；停止服务器后明显恢复。空服务器也有此现象；此前用户报告单人约120 FPS，加入2客机时可降到约20 FPS且不稳定。

希望得到切实有效的优化方案，并询问能否让游戏/Together更多地使用其他CPU核心。此前已交接给其他agent只读评审，随后用户回传评审继续此任务。本文按时间保留调查过程；**当前代码以第11节为准：旧线程与保存等优化已撤除，新模拟范围对照默认保持原版，尚无性能收益实测**。

**根因尚未定位，不能宣称已解决。必须保留以下纠正：**

- ChatGPT曾依据CPU优化模组在主机状态下唤醒距离翻倍、活动对象增多，过度强调CPU优化策略是主因。用户随后移除CPU优化模组作对照，开启MP仍然low帧差、停服仍然恢复。因此CPU优化模组不是该现象的必要条件，不能继续按“问题就是CPU优化”推进。
- 用户确认CPU优化确实减少活动物体处理。CPU优化模组的统计涵盖其跟踪对象；性能跟踪器的“物品348（启用23）”是另一口径，不能将两者或物品总数直接比较为每帧处理量。
- 目前稳定触发条件是MP运行状态；可能是Together服务器维护、模拟范围/休眠、同步采样、固定步集中处理、分配/GC、其他模组的MP分支或它们的交互。尚无同一长帧的完整归因证据。
- 多核是需要拆分数据和算法的架构改造。Unity已经有渲染/工作线程，但大量脚本回调仍串行运行。CPU亲和性只决定线程在哪些核心上运行，不能把一个串行调用自动拆成并行任务。

## 2. 工作区与约束

- 仓库：`L:\github\game\CUmod`，分支`dev`，本次读取HEAD为`4ada7af`。
- 游戏：`L:\SteamLibrary\steamapps\common\Casualties Unknown`。
- 当前Together：5.5.5，程序集MVID严格匹配`b3b58466-cc0d-42b6-910c-d744dd135db2`。
- 先读[根协作入口](L:/github/game/CUmod/AGENTS.md)、[个人协作说明](L:/github/game/CUmod/agent/agent-cubstaryow.md)、[源码约束](L:/github/game/CUmod/src/AGENTS.md)。不要覆盖其他协作者已有改动。
- ChatGPT负责代码、修复、必要编译测试和校对；DeepSeek默认负责发布打包/推送/部署及文案同步。交接记录允许写CHANGE。不要擅自提交、推送或部署。
- 不操作游戏进程，不修改真实存档或cfg，不复制第三方实现进src，不改游戏目录里的第三方DLL。性能跟踪器基础模块保持零Harmony；增强模块只允许纯计时，不能用其改变游戏逻辑。
- Windows写L盘需工具提权；不要把权限错误当作仓库异常。Python可用`D:\Python\python.exe`。

## 3. 日志证据：区分两次运行

### A. 先前主机加1客机的深层导出

原件：`L:\SteamLibrary\steamapps\common\Casualties Unknown\BepInEx\config\DDEN\perf-20261128T040152299Z.log`。

共74个基础窗口、72个深层窗口；记录里是主机加1客机，不是用户所述2客机20FPS那一场的证明。平均约49.1 FPS，主线程约20.39ms，GPU约5.76ms。

| 计时项 | 加权平均约ms/帧 |
| --- | ---: |
| Together.Plugin.Update | 2.890 |
| Together.BagSleep.Tick | 2.261 |
| Together.Plugin.FixedUpdate | 1.656 |
| Together.BagSleep.WriteHost | 1.627 |
| Item.HandleDecay | 1.569 |
| Together.Net.DoUpdate | 1.320 |
| SyncInfo.WriteObjectIntoPacket | 0.720 |
| SyncInfoGameObjectTracker.FlushFrameSamples | 0.571 |
| TransportSteamworks._Update_PollMessages | 0.538 |
| Together.ChunkSleep.Tick | 0.322 |

父子项含子调用，不能相加。数值是此前运行的热点线索，不是新插件收益。第一段安装计时钩子可能产生初始化长帧，需核对SetupMs、排除探针安装时段。

### B. 最新无CPU优化模组的空服/停服运行

本次读取的`BepInEx\LogOutput.log`最后写入时间为本地2026-11-28 13:27:26，长度53352字节；加载列表没有CpuOptimizationAdv。运行中MoreThreadOnMP已启用。

日志原始时间`05:26:45.799`记录`Network Shutdown Reset called`，支持用户在存档内停服的操作。前后收发统计均为0包/s，说明此段没有远端流量压力，不能归因为大量收发包。

| 日志位置/时段 | 原记录 | 解释限度 |
| --- | --- | --- |
| 停服前，行519 | `game 7.00/124.25ms` | 扣除Together已计时项后的剩余墙钟平均/最大，不是完整帧，不能换算FPS或定位单个业务函数 |
| 行525 | `Network Shutdown Reset called` | 可用于划分停服时间 |
| 停服后稳定窗口，行585 | `game 5.73/13.76ms` | 同样是剩余墙钟；与用户体验相符，但不是完整帧或严格同镜头、同负载A/B验收 |
| 行480 | `stats native/reused=418833/1675332; idle scans skipped=2376` | 新缓存和空服降频确实运行过，不证明节省了多少CPU时间 |
| 所有新增地形计数 | `hashes computed/hit/miss=0/0/0` | 这次地形Job未承担工作，不能宣称它改善了此段low帧 |

用户最新截图显示跟踪器平均120 FPS、8.3ms，最近窗口最长约10.7ms；物品348、启用23，详细统计关闭，增强模块关闭，MP当时已停止。当前瞬时CPU主线程/GPU数值不能用于倒推停服前那一个长帧。

证据快照保存在本机`L:\github\game\CUmod\artifacts\diagnostics\together-20261128-handoff`，包括最新日志、旧深层导出、MoreThread配置和用户最新截图。该目录已被git忽略，不应提交日志中的用户身份/存档路径。`manifest.json`记录原路径和SHA256。日志原件会被下次启动覆盖，接手应优先看快照。

## 4. 已完成的代码与交付

代码位于[MoreThreadOnMP模块](L:/github/game/CUmod/src/DDEN.MoreThreadOnMP/MoreThreadPlugin.cs)，版本仍`0.1.0-alpha`。总开关默认关闭，用户当前配置Enabled=true；收包、发包、预解压、性能核约束及下面三项均为true，软预算4ms，每连接最多32包；线程显示采样当前为1s。未替用户改cfg。

既有功能：独立Steam收/发线程、后台gzip预处理、主线程软消息预算、性能核亲和性与线程热图。网络回调/Unity对象操作仍在主线程。原Steam投递标志、FIFO、连接租期、背压与故障回退要保留。

上次已落代码的三个优化：

1. **DecayStatsCache**：对原MP腐坏前缀六个Stats读取指令做同次调用局部复用；物品/ID变化重读，不跨帧缓存，不替换原腐坏/耗电/销毁，兼容其他插件跳过腐坏的保护。单人保留原读取。
2. **IdleHostMaintenance**：无远端连接时，仅将BagSleep.WriteHost背包网络扫描改为10Hz；待握手连接也取消降频，收包分发前先补齐一次原状态。没有降低晶体、腐坏、战斗或事务更新频率。
3. **ChunkHashJobs**：32×32地形快照每帧最多复制64块，IJobParallelFor算原协议校验；首次/变化/任务未完成立即走原HashRegion，每次复用前逐格确认地形完全一致。只用NativeArray数字数据；未启用Burst。失败锁存回退，避免每帧重试。当前空服测试全部计数0。

主要文件：

- [SimulationHooks.cs](L:/github/game/CUmod/src/DDEN.MoreThreadOnMP/Integration/SimulationHooks.cs)：三项模拟优化与Harmony入口。
- [ChunkHashJobs.cs](L:/github/game/CUmod/src/DDEN.MoreThreadOnMP/Integration/ChunkHashJobs.cs)：Unity任务及生命周期。
- [ChunkHashCache.cs](L:/github/game/CUmod/src/DDEN.MoreThreadOnMP/Core/ChunkHashCache.cs)：快照验证与排队、空服维护门控。
- [TransportHooks.cs](L:/github/game/CUmod/src/DDEN.MoreThreadOnMP/Integration/TransportHooks.cs)：收发调用点/解码重定向、分发前维护入口。
- [MpThreadRuntime.cs](L:/github/game/CUmod/src/DDEN.MoreThreadOnMP/Integration/MpThreadRuntime.cs)：既有主线程回调调度、队列与回退。

页面新增命中/回退/快照与调度耗时、功能开关，每30秒向现有日志输出累计计数。不要把“计数增长”当作性能收益。

交付DLL：[DDEN.MoreThreadOnMP.dll](L:/github/game/CUmod/dist/MoreThreadOnMP-v0.1.0-alpha/DDEN.MoreThreadOnMP.dll)，78848字节，SHA256：

```text
3722478BAB41AA5588A1504B5C29F83D0F7E7F0781F5D1307C7F1FE82675BE8C
```

本次读取游戏plugins/DDEN中的同名DLL哈希与交付一致，说明用户已安装并运行该版；不是ChatGPT在此次任务中部署的。Shared/管理器保持原版，本轮没改LoongNova或其他模组。

## 5. 验证结果与复现命令

上次代码交付已通过：Release构建0警告/0错误，4096项离线检查，21项真实Harmony模拟路径检查，16项真实Harmony传输检查。本次交接仅写文档/保存证据，没有重跑构建，不能把之前测试说成本次新增优化已验证。

在`L:\github\game\CUmod`执行：

```powershell
dotnet build src/DDEN.MoreThreadOnMP/DDEN.MoreThreadOnMP.csproj -c Release -m:1 -nodeReuse:false -p:DeployOnBuild=false
dotnet run --project tools/MoreThreadTests/MoreThreadTests.csproj -c Release -p:DeployOnBuild=false
& tools/MoreThreadTests/run-simulation-netfx.ps1
& tools/MoreThreadTests/run-hooks-netfx.ps1
```

Unity NativeArray/Job调度的离线流程测试用自有托管夹具；Harmony钩子是真实HarmonyX运行，但不是实机Unity Job调度验收。用户目前运行日志中Job未触发，不能补称已经通过实机Job验证。用户反馈整体low帧问题仍在，之前迭代没有被证明解决主诉。

## 6. 已阅读的上游路径与待验证方向

只能阅读理解，不要把下列第三方实现复制到仓库。

- `Together.Plugin.Update`调用ChunkSleep.Tick、BagSleep.Tick、HeaterMultiplayerRewrite.Tick以及若干优化/名单/晶体处理；FixedUpdate调用BagSleep.WriteHost和FallCatch.Tick。
- `Together.Net.DoUpdate`负责Transport.Update、SyncInfoGameObjectTracker.FlushFrameSamples及LES管理器更新。它不是纯网络发送函数。
- `Together.SyncInfoGameObjectTracker.FixedUpdate/TickSync`安排对象状态采样；FlushFrameSamples还调用EvidenceCrystal.Host，不能因无客机就整体跳过。
- `Together.ChunkSleep.Tick`：EnsureRenderers、WakeFreshChunks、每次最多64项Visit；唤醒预算32，远区复查0.1s加分散时间，冷却1s。EnsureRenderers会调用WorldGeneration.UpdateChunkVisibility。WakeFreshChunks遍历ByChunk后判热区/可见，WakeSlot遍历槽内条目。需要确认是否有重复可见性计算、重复Wake或与游戏更新重叠，不能未经计时就断定其为大头。
- 原MP的Item_HandleDecay_MultiplayerPatch使用物品实际所属Body的速度判定移动腐坏；不能改成主机自己的角色速度。
- CpuOptimizationAdv.CullWorld原实现主机时_prop乘2，扫描策略也不同。它确实可能影响活动范围，但用户无CPU优化对照已经排除其为唯一根因。本机目前该DLL已不在原路径，不能擅自补回或用旧假设调整半径。
- `Together.FindTheBugInator`/`FindTheBugReports`有PlayerLoop计时和约10秒周期输出。日志/字符串构造、其他插件周期任务是否造成尖峰属于待验证假设，尚未做同帧关联。

## 7. 建议接手顺序（尚未实施）

1. **先审查上一轮优化是否对准问题。** 特别是地形Job在空服完全不触发；Stats读取减少不等于总体主线程明显下降。不要为了继续多线程而增加未证明有收益的工作。
2. **抓同一个长帧的归因。** 对齐MP开始/停止、脚本/物理阶段、固定步次数、GC、分配/日志，以及必要的少量Together入口；避免只比较不同10秒窗口各自的最大值。探针开销和安装首帧应单独剔除。物理步数不是物理CPU耗时。
3. **保留对照条件。** 用户已有无CPU优化空服/停服对照；若继续采集，使用相同存档、位置、镜头、帧率上限、其他模组与面板状态，跳过读档/生成预热，再比较停服、空服、1客机、2客机。记录MoreThread/增强探针开关，不能把它们作为不变背景而忘记测其开销。
4. **选一处已证实热点再改。** 优先考虑同帧去重、变化驱动、批量对象状态采样、纯数据的距离/范围/差异比较或编码压缩；所有优化必须可独立关闭、有命中/耗时证据并可回退。不要贸然降低整个服务器Tick、扩大时间步，或停止客机附近的模拟。
5. **批量中继，而非逐次同步RPC。** 主线程采集必要数字快照→后台计算→主线程核对世界/对象版本后有限应用。每次GetComponent/Transform操作都发回主线程等待，既不能消除其成本，还可能放大等待/死锁；lock也不能让非线程安全Unity API变安全。是否值得拆分必须算上采集、复制、调度、核对和应用成本。

## 8. 未提交内容与交接状态

上轮修改仍未提交。当前涉及：MoreThreadOnMP的csproj、插件入口、TransportHooks、英文资源；新增SimulationHooks、ChunkHashJobs、ChunkHashCache；MoreThreadTests的主程序/csproj/传输夹具以及新增JobFixture、SimulationTests、SimulationFixture、run-simulation-netfx.ps1；CHANGE.md。请以实时git status为准，保留这些改动，不用reset/clean。

本次新增交接单和被git忽略的诊断快照。**没有新增唤醒优化、没有改CPU优化DLL、没有实现“让整个游戏自动多核调度”、没有宣称已解决low帧。** 请先评审证据与架构，再决定下一处改动。


## 9. 回传评审后的更新（2026-11-28）

第4、5、8节描述首次交接时点的代码和交付。用户随后回传只读评审，现已补充逐帧记录并修正MoreThread自身附加开销；当前交付在`L:\github\game\CUmod\dist\Together-frame-diagnostics-20261128`，旧交付目录未覆盖，三份新DLL需配套使用。本次仍未部署到游戏，当前游戏目录中的旧DLL不能当成新版本实测。

- 明确纠正`game`剩余墙钟口径；停服后仍执行的Tick不能全部算作开服新增工作。周期30秒检查点、2秒诊断扫描、每约5秒多人回调尖峰等是回传评审给出的重点待验证线索，尚未完成同一长帧的实机归因，不作为已经证明的根因。
- 增强模块现用自有PlayerLoop书签提交完整渲染帧，手动导出`DEEP_FRAMES`。保留最近256帧与128帧长帧上下文（前后各4帧）。每帧有Frame/T/Role/LoopMs/FixedSteps/GC0-2/脚本和2D固定阶段时间，以及按Targets索引的Calls/MethodMs/PeakCallMs数组。缺失为-1，父子和阶段计时重叠。
- P95/P99来自所有非预热、非手动导出帧的0.1ms固定直方图；不是从长帧样本算出来。LoopMs为自有帧边界墙钟，GC次数不是耗时；当前没有给出可冒充同帧的GPU或精确GC耗时。诊断提交成本不在LoopMs内，探针本身仍有开销。
- 路由集合稳定时不重复创建/发布列表、字典、peer数组；连接变化、句柄/后端更换和握手前校验仍保留。线程负载采样器在MoreThread总开关关闭后也会停止。不能将总开关关闭等同于完全卸载插件的开销对照。
- 三模块构建通过；新增与既有回归结果见CHANGE最新条目。PlayerLoop只有独立托管图夹具验证，真实Unity帧阶段执行和与其他改循环插件的兼容仍需实机确认。本次没有实现新的世界唤醒策略、多核业务迁移或存档压缩后台化。
- 建议下一次相同场景预热后，停服→空服→停服每段至少2分钟，手动导出；再单独比较MoreThread开关、客机人数。用新帧号对齐检查点/诊断/可见性/距离检查及GC，原4ms软预算仍不能打断单个长消息回调。确认热点后再拆纯数据任务。

本次编译件：
- DDEN.MoreThreadOnMP.dll 79360 bytes SHA256 C3A1EEDD4497883B989D2BF1387ADD942CF5671EDB04BDEF2DF1BFC9609759E8
- DDEN.NetPerfTracker.dll 140800 bytes SHA256 50CAB0EDAACB9BE71953B6E6230131D11F296A9B4BCCAA458665B9055F16EBDB
- DDEN.NetPerfTracker.Enhanced.dll 52224 bytes SHA256 4EB1851D372926B4E1F48499DFBE4765682DBD68DBFC08527CBE175338FA83A0

## 10. 用户反馈无效后的版本回归核查（2026-11-28）

本节补充第9节之后的进展，前面的交付哈希、日志时点不代表本次用户实测。检查点后台保存已另行实现并通过离线检查，用户随后明确反馈**实测没有效果**。据此降低检查点在主诉排查中的优先级，不将压缩次数减少等同于帧率收益。用户补充：MP 5.5.3 时主机性能较好；游戏没有更新，其他模组有为适配5.5.5的更新，少有新增，perf属于新增。社区反馈是调查线索，不能代替本机版本对照。

### 实际发布 DLL 的差异

已只读下载官方 [5.5.3](https://github.com/creaturefeaturelarry/casualties-together/releases/tag/v5.5.3)、[5.5.4](https://github.com/creaturefeaturelarry/casualties-together/releases/tag/v5.5.4)、[5.5.5](https://github.com/creaturefeaturelarry/casualties-together/releases/tag/v5.5.5) fullgame 包与源码。证据在被git忽略的 `artifacts/together-version-compare/`，包括各版本DLL、相关类型反编译和方法差异JSON；不提交或复制第三方实现进src。当前安装5.5.5的SHA256与官方下载一致。

**源码 tag 与发布 DLL 不完全同步。** 源码标签3→4几乎没有业务变化，实际发布DLL却有明显差异；以下以实际DLL为准。

| 核对项 | 5.5.3 发布 DLL | 5.5.4／5.5.5 发布 DLL |
| --- | --- | --- |
| `FixSimRange.Mark` 单中心范围 | x/y各±1，3×3 | x/y各±4，9×9 |
| `FindTheBugInator.Tick` | 空方法 | 调用 `FindTheBugReports.Tick` |
| `FindTheBugReports` | 不存在 | 开服后每2秒扫描，包括非活动的BoughtItem／BatteryItem，特定层另扫生物／机关并生成日志 |
| `LayerBuildingStream.HostRegister` 周期入口 | 无该新增调用 | 主机约每1.9秒全场查找炮台、吸血生物、BlueHeart，逐对象核查组件与同步注册 |
| 自带10秒计时报告 | 已存在 | 仍存在，不能误称整个计时器均为新版新增 |

- **首要候选是模拟范围扩大。** `SharedMain.Update → _UpdatePlayerVisibleChunks → FixSimRange.Rebuild → Mark`构表；主机`ChunkSleep.Hot/RendererReady`读取该表决定唤醒，`WorldInSimRange`及建筑刚体Dynamic/Static门控也消费此范围。每名玩家单窗口标记上限由9增至81；边界裁剪、玩家窗口重叠及SimKeep决定实际数量，**不是实测CPU开销增加九倍**。该路径可以解释没有远端玩家时主机仍增加工作量，也可能放大多人分散后的负担，但需实机证明。客机虽构表，ChunkSleep.Hot的客机分支仍按本地±1，不能直接套用主机结论。
- `SimKeep`三版方法逻辑未变：按游戏时间保留10秒，过期或对象销毁时清理。它的Stamp复用Mark，因此保留对象周边的标记也跟随扩大；不能误称SimKeep本身为新版新增或已证实泄漏。
- 第二候选为新增诊断扫描，适合检查2秒节奏的尖峰与分配。仅关闭日志输出并不能消除扫描和字符串构造。5.5.5已含FlushFrameSamples以及LiquidAffect／IKHandle相关修订，不能直接套用针对5.5.4的issue #78原型或跳过整段物理更新。
- 第三候选为`NetObjectRegistry.Update → _ObjectSyncUpdateLoopUniversal(true) → LayerBuildingStream.HostRegister`的周期注册扫描。真实5.5.5实现每次进行三次含非活动对象的全局查找，空服主机也执行；5.5.3→4新增调用已核实。当前增强模块50个目标没有覆盖NetObjectRegistry.Update／HostRegister，这部分只落在ScriptUpdate余项；下一轮应优先补这两个纯计时入口，再决定是否适合改成增量注册。同步注册属于业务行为，不能与纯诊断扫描一起直接禁用。
- 三份发布包中LiteEntitySystem与CUCoreLib分别具有相同SHA256，暂不支持将这两项依赖的发布包版本变化作为该回归解释；这不排除其他更新模组的交互。

MVID：5.5.3 `e6e6fcbb-c4f4-44e2-9650-a39b65e0c024`；5.5.4 `41a77b40-e022-4cc8-9d10-999992f155c4`；5.5.5 `b3b58466-cc0d-42b6-910c-d744dd135db2`。关键证据为各版本`KrokV5Optimization.FixSimRange.cs`的Mark、5.5.5的`Together.SharedMain.cs`、`Together.ChunkSleep.cs`及`Together.SimKeep.cs`。

### 下一轮对照边界

优先在同一5.5.5及相同模组环境中分别检验模拟窗口与纯诊断扫描，两项独立改变，保留主机及每名远端玩家的必要模拟；范围回退可能影响屏外机关和客机附近行为，不能直接作为已确认安全的正式修复。测量实际模拟区块、活动Body／动态刚体、固定步数、脚本／2D物理阶段和P95／P99，并用长帧对应的调用记录判断持续开销与周期尖峰。另保留新增perf及MoreThread关闭／移出测试副本的对照，关闭面板不等于停止全部采样。若跨版本对照，使用存档副本并核对适配模组版本；当前MoreThread的MVID校验不允许直接用于5.5.3，不应为试验粗暴移除校验。

现有跟踪器的一个操作注意：MP开始／停止或换世界会清空deep记录，**每段结束时必须先导出，再切换开服状态**。计时均含子调用，`Inclusive=false`标签不代表已经扣除了子项；GC次数不是GC耗时。

本轮最新普通日志最后写入16:58:14，唯一联机角色为client，不能用它反驳用户主机实测或充作主机A/B。它确有持续bag诊断输出，但没有5.5.3基线；最新perf导出仍为12:01的旧窗口数据，没有本轮DEEP_FRAMES。本轮没有部署、启动／停止游戏、修改真实cfg／存档，也没有新增模拟范围或唤醒补丁。

## 11. 撤除无效优化并加入独立对照（2026-11-28，当前代码）

用户确认：检查点试验无收益，原收发线程还会造成丢伤害，明确要求删除。MoreThreadOnMP现为0.2.0-alpha，显示名改为“联机性能验证（MTOMP）”；GUID与总开关键保留，旧收发等配置不再绑定。此前0.1.0的说明与编译件只保留作历史，本节取代第4、5、8、9节的当前功能描述。

### 清理与验证边界

- 删除收发接管、包队列、主线程消息预算、预解压、检查点后台保存、Unity地形校验Job、腐坏参数缓存、空服扫描降频，以及旧CPU亲和性控制。Together恢复原收发与业务处理路径；这不代表已经验证解决所有伤害不同步。旧流程对应测试也已撤除，历史日志与旧交付目录保留。
- 可选线程观察只读本进程线程时间，默认关闭；不开额外业务工作线程，不绑定核心。新增验证总开关仍默认关闭。即便原cfg曾启用总开关，新条件仍默认半径4、保留诊断、关闭线程观察；原cfg未被工具修改。
- `回归验证/HostSimulationRadius` 为4／2／1，即9×9／5×5／3×3。只替换已核对Mark的四个范围常量，原玩家遍历、相机回退、边界裁剪与SimKeep均保留。**3×3仅复现旧版这一项范围条件，不等于完整回退到5.5.3。** 缩小范围会改变屏外模拟；分散玩家、远处机关、生成／掉落行为需要实机检查。
- `回归验证/SuppressDiagnosticScans` 默认关闭。启用后暂停FindTheBugReports.Tick及纯诊断NoteDecay，清空其Bags计数与BagItems旧引用；物品腐坏业务、HostRegister同步注册和其他服务器更新照常。直接只停Tick会失去诊断容器的原清理机会，该边界已在复审中修正。关闭后恢复原诊断，历史计数重新积累。
- 两项只对正在开服的主机生效，保持5.5.5实际MVID限制，分别降级；陌生二进制、签名或冲突补丁不强行适配。改选项时即时审计，运行中新装补丁最多一秒后被检查。关闭／退出时撤回自有补丁并恢复原半径；重算仍调用原Rebuild，实际唤醒受原ChunkSleep预算约束。未知第三方补丁与真实Unity生命周期不能只靠夹具保证。
- 每秒读取一次原模拟布尔表，显示已标记区块数和表采样峰值；没有新增场景对象扫描。区块计数不是活跃刚体数量。界面“已跳过诊断入口”为Tick调用次数，不是每两秒一次的实际扫描批次数。状态字符串只在条件变化／30秒报告时构建；表计数、补丁审计与可选线程观察仍有自身开销。
- 增强跟踪器从50扩到53个目标，追加NetObjectRegistry.Update、_ObjectSyncUpdateLoopUniversal(bool)、LayerBuildingStream.HostRegister。仅纯计时，精确匹配bool重载，不改参数／异常／返回流程；旧版本不存在的HostRegister显示不可用。基础跟踪器继续零Harmony补丁。

### 实机对照步骤

新编译件位于 `dist/Together-regression-validation-20261128/`，三份自有DLL配套使用，沿用现有Shared2.0.1与管理器2.1.0。替换旧DLL后重启游戏，确保旧程序集及亲和性设置已经退出；本轮没有自动部署或操作游戏进程。

使用同一存档副本、相同位置／镜头、人数与模组版本。先读档预热，保持线程观察关闭、perf与增强开关相同。建议每组至少2分钟；每次条件变化稳定后在跟踪器点“清空”，避免分位数混入前一组。**每组先导出日志，再清空、停服或换层**。基线与试验各重复一次；半径恢复后等待对象状态稳定，必要时重新载入同一副本再比，不能把切换当帧当作收益。

| 组别 | MTOMP总开关 | 模拟范围 | 诊断扫描 | 用途 |
| --- | --- | --- | --- | --- |
| 0 | 关闭 | 原版 | 原版 | 测MTOMP验证本身的附加开销 |
| A | 开启 | 9×9 | 保留 | 两个条件共用基线 |
| B | 开启 | 3×3 | 保留 | 单独验证模拟范围；需要时补5×5 |
| A2 | 开启 | 9×9 | 保留 | 回到基线，核查负载漂移 |
| C | 开启 | 9×9 | 跳过 | 单独验证诊断扫描 |

先做空服，再按相同条件做2客机；多人分别同区和分散是不同场景，分别记录。比较基础帧率／low帧、DEEP_FRAMES的P95/P99、脚本／2D固定阶段、固定步数量、表中区块数，以及新加注册路径。2D固定阶段仍不是精确物理解算器耗时，父子计时不能相加。单独再做perf及增强均关闭的基线，检查新增观察器是否影响现象；仅关闭面板不能视作关闭采样。

判读：B稳定改善持续负担且A2恢复，支持范围扩大是贡献因素；C主要改善周期长帧，支持诊断扫描贡献；HostRegister与长帧同帧显著重合，才继续考虑事件驱动／增量注册。同步注册不能简单跳过。若两项均无收益，应继续按同帧脚本、物理、等待／GC证据找热点，不再延续后台保存方向。

### AMD／Intel线索

用户回忆在MP5.5.3相同环境下，Intel主机带2客机可上百FPS，AMD约20FPS；本轮没有可对齐的历史AMD原始记录。检查现存日志、诊断快照与仓库记录，找到的硬件证据只有 `artifacts/diagnostics/together-20261128-handoff/perf-host-one-client-20261128T040152299Z.log` 第11行：i5-12500H、16逻辑处理器、RTX3050 Laptop，且该记录为MP5.5.5主机加1客机，不能冒充5.5.3平台对照；未清理这些证据。

- 在实际5.5.3的MP／LES托管IL中未发现AMD／Intel品牌分支、核心数裁剪或设置线程亲和性路径。这只排除了已检查的托管代码，不涵盖Unity／Steam原生运行时、驱动与系统调度，也不能证明两台机器运行成本相同。
- MTOMP旧亲和性只按最高EfficiencyClass选核，未使用CoreIndex／共享末级缓存／NUMA或SchedulingClass；它不是通用AMD拓扑策略，现已撤除。但MTOMP本来只适配5.5.5，不能据此解释用户在5.5.3就遇到的差距。
- 调度与跨缓存域延迟是**待测假设**，不是“AMD必慢”的证据。多CCX产品的跨域共享写入可能增加成本，部分产品的核心偏好也会动态变化；具体是否适用依赖CPU型号。AMD官方建议避免硬亲和性干扰调度与电源管理，见[官方GDC指南第28–30、59页](https://gpuopen.com/download/GDC2024_AMD_Ryzen_Processor_Software_Optimization.pdf)。CPU拓扑字段含义见[Microsoft CPU Set说明](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-system_cpu_set_information)。不根据品牌自动绑核、关SMT或修改系统电源设置。
- 下一份AMD主机记录先补具体型号、系统、实际MP及模组版本；同存档副本下比较停服／空服／2客机。主线程CPU时间与脚本热点同涨时优先查串行工作量；墙钟很高但主线程CPU不高时再分辨调度等待、帧率限制、GPU或I/O，必要时另采WPR线程调度轨迹。需要跨CCD验证时必须先确认该型号确有多缓存域，再单变量比较，不能从100对20直接推断。
- MTOMP这两个运行逻辑开关不用于5.5.3；旧版只做相同观察条件的性能采样与匹配入口纯计时，并核对不可用项。5.5.3平台差异与5.5.3→5.5.5版本回退是两条独立问题线索。

### 离线验证

MTOMP Release构建通过，0错误；NuGet漏洞元数据源不可达产生NU1900警告，不是代码编译失败。增强与基础跟踪器Release构建通过。MTOMP主离线检查130项（含20项真实DLL只读契约）、真实Harmony40项；增强账本／桥／IL检查375项，真实Harmony当前目标35项、旧目标缺失36项；62条MTOMP英文本地化及格式占位符核对通过。删除路径、默认原版配置、异常／参数不变、独立回退、退出恢复、未知补丁与诊断历史维护均有覆盖。

所有运行夹具都使用自有模拟方法；真实第三方DLL只读检查元数据与IL。未启动／停止／部署游戏，未修改真实存档、cfg或第三方文件，未提交推送。**没有本轮真实FPS或伤害一致性验收；以上通过项不能作为性能收益证明。**
