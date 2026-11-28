# Together低帧与多线程优化交接单

更新时间：2026-11-28（Asia/Hong_Kong）。交接对象：接手排查与代码评审的agent。

## 1. 用户目标与当前结论

用户反馈：同一存档中，开启Together服务器后low帧明显恶化；停止服务器后明显恢复。空服务器也有此现象；此前用户报告单人约120 FPS，加入2客机时可降到约20 FPS且不稳定。

希望得到切实有效的优化方案，并询问能否让游戏/Together更多地使用其他CPU核心。此前已交接给其他agent只读评审，随后用户回传评审继续此任务。本文按时间保留调查过程；**性能证据见第13节；当前实现见第14节：用户要求暂缓其他优化，只重新试验检查点后台保存并添加独立保存间隔控制，改名MPreducer。范围与诊断逻辑沿用0.2.1，原收发接管仍撤除，新增保存开关默认关闭**。

**模拟范围已成为本次实测支持的显著贡献因素，尚未证明能解释全部卡顿、多人差距或AMD问题。必须保留以下纠正：**

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

## 11. 撤除无效优化并加入独立对照（2026-11-28，0.2.0初次交付）

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

## 12. 模拟范围实测与诊断兼容修正（2026-11-28，18:21轮次）

用户反馈“实测模拟距离会显著改善主线程压力”。已核对本次 `LogOutput.log` 及18:21:45导出的 `perf-20261128T102145357Z.log`，并保存原始快照与计算结果至 `artifacts/diagnostics/range-validation-20261128/`。日志为BepInEx5.4.23.6、Together5.5.5、MTOMP0.2.0；仍是i5-12500H／RTX3050 Laptop。本轮没有更新或操作游戏目录。

### 实测支持范围缩小减少模拟负担

本次主机大厅members=1，未见客机加入，网络消息与流量均为0；全程同一层0，地图1024×1024、16×16区块。顺序为半径1→2→4→1，线程观察保持开启，CPU优化与Comforts也在场。因此这是现有模组组合下的无人加入主机验证，不是2客机或AMD验收。

最强的同位置证据：原日志1469–1472行，坐标(1.1,149.6)从半径4切回1时，CPU优化统计中的活动对象由706／699降到159，随后降至96，跟踪总数保持2206。已只读核对该DLL，`objects`分子是跟踪集合中 `GameObject.activeSelf=true` 的数量，分母是Entries.Count；它不是活动刚体／动态刚体计数。其`chunks`数TilemapRenderer.enabled，与MTOMP模拟布尔表是两个指标。MTOMP模拟区块另记录为9→20→72／81→9，表计数峰值约0.02ms。

旧模式日志没有时间戳，只能用相邻带时间的行夹定边界：4→1发生于perf相对时间T363.472–373.475之间，不能把此区间内每秒采样强行归到某个半径。下面只比较完全避开该区间的窗口，按帧数／实际墙钟加权：

| 指标 | 9×9末段 | 回到3×3后 |
| --- | --- | --- |
| 相对时间范围 | T352.073–362.121 | T375.191–401.335 |
| 有效窗口／帧数／时长 | 10／867／10.048秒 | 26／3098／26.144秒 |
| FPS | 86.29 | 118.50 |
| 平均帧间隔 | 11.590ms | 8.439ms |
| Unity主线程帧墙钟 | 10.932ms | 6.646ms |
| Windows主线程CPU占用 | 90.50% | 77.99% |
| 推算CPU时间／渲染帧 | 约10.49ms | 约6.58ms |
| GPU阶段 | 2.466ms | 1.682ms |
| 帧率限制等待 | 0.645ms | 1.785ms |

主线程帧墙钟约下降39%，推算CPU时间／帧约下降37%；这些是本次选定窗口的观测差异，不能包装成固定收益率。角色在9×9阶段和回到3×3后均有移动，期间还有挖掘、倒地与界面操作，区间长度不同；同位置活动数突变与随后帧率恢复共同支持范围的影响。10秒滚动low1中位约37.89→59.80，后段完全排除前一范围滚动历史后约60.37；它不是每个阶段整体计算的1%low。恢复出现在诊断抑制实际生效之前。

同组深层计时（sum TotalMs／sum Frames，父子不能相加）：

| 入口 | 9×9 ms／帧 | 3×3 ms／帧 | 解释 |
| --- | --- | --- | --- |
| Item.Update | 0.5904 | 0.1497 | 调用／帧120.40→22.56 |
| Item.HandleDecay | 0.2588 | 0.0747 | 含其他补丁时间，不与父项相加 |
| LiquidAffect.FixedUpdate | 0.2382 | 0.0527 | 活动物品相关工作减少 |
| SyncInfoGameObjectTracker.FixedUpdate | 0.2661 | 0.0998 | 调用／帧174.28→35.54；按固定步折算约301→84，减少不只是FPS改变所致 |
| FlushFrameSamples | 0.1849 | 0.0493 | 状态采样／处理量下降 |
| SyncInfo.WriteObjectIntoPacket | 0.2316 | 0.0817 | 序列化开销下降，不等于真的发出了数据 |
| Net.DoUpdate | 0.2381 | 0.0848 | 含子调用 |
| Steam传输更新 | 0.00859 | 0.00813 | 基本未变 |
| Steam消息轮询 | 0.00083 | 0.00084 | 基本未变 |
| ChunkSleep.Tick | 0.1976 | 0.2012 | 管理入口自身未变快 |

因此本次支持的是**更少对象继续执行更新与同步采样**，不是收发线程改善，也不是ChunkSleep管理函数本身明显加速。范围3×3只还原旧版一个条件；结合已确认的5.5.3→5.5.4扩大范围，它是版本性能回退的实测支持因素，不能据此排除其他回归。

周期注册与诊断的按帧均值会随FPS升高被摊薄：HostRegister墙钟开销约4.65→5.08ms／秒、单次峰约10.22→11.10ms；FindTheBugReports约3.12→3.28ms／秒、峰约6.45→7.18ms。它们没有在本轮得到有效优化。Checkpoint仍见约17.7／16.6ms单次峰，不能称后台保存或周期卡顿已经解决。

基础395条记录中的脚本／2D固定阶段均为不可用；DEEP_FRAMES的最近256帧只覆盖导出末约2.17秒，长帧上下文又是选择样本，不能用它们拼出无偏的两组物理平均或P95/P99。导出后的约426ms长帧包含约417ms IMGUI，后续仍见1109ms长帧；范围改善不代表所有尖峰消失。

### 修正验证工具的误判

原日志194行显示 `FindTheBugReports.Tick <- com.dden.perfdeep/Before` 被当成陌生补丁，半径对照与导出前 `skippedScans` 始终0。用户配置虽然请求暂停诊断，但有效状态是保留原诊断；导出后才出现922／2422等跳过计数。不能把配置值true当成已执行优化，也不能把导出后混合状态与之前直接比较。

根因是MTOMP白名单写成 `DDEN.Diagnostics.CallStamp`，真实增强模块使用 `DDEN.NetPerfTracker.Enhanced.CallStamp`；旧夹具恰好伪造同一个错误命名空间，未覆盖两模块真实接口。0.2.1-alpha改为从实际DeepHooks所属程序集取得状态类型并严格核对参数，保留owner／方法声明类型限制与未知补丁回退；新增真实增强DLL契约检查，避免夹具继续偏离产物。

新日志同时记录请求的 `suppressScans`、实际 `effectiveSuppressScans`、Unity `engineTime` 与 `frame`；实际状态变化也触发模式记录。`engineTime`是Unity启动后秒数，换算perf相对T时减去其头部 `engineTimeAtStart`，不要直接当作同一个时间轴。本次只修验证兼容与记录，不改模拟半径默认值或世界更新逻辑。

### 下一步优先级

优先继续验证3×3／5×5的玩法边界：两名客机同区／分散、屏外机关、远端战斗、移动跨区与换层，确认所有玩家必要模拟仍有效。改善明确后再考虑让非必要屏外对象使用更低成本的维护策略；不能直接停整个服务器更新或任意减少伤害消息。诊断扫描需要在修正版下单独比较，本轮不能判定其收益。AMD平台仍需自己的型号与同场景记录，不能从本次Intel空服结果外推。

修正版必要编译件为 `dist/MoreThreadOnMP-v0.2.1-alpha/DDEN.MoreThreadOnMP.dll`，只需替换这一份自有DLL，基础／增强跟踪器未修改。Release构建0错误，1条NU1900漏洞数据服务不可达警告；主离线133项、真实Harmony51项、实际DLL契约25项通过。SHA256 `0D11E55C4E7BA3091230120DA720C938D426022E6F5691D62A94921817F122DA`。未部署、修改真实cfg／存档或第三方DLL，未启动／停止游戏，未提交推送；修正版诊断抑制与范围的组合尚未实机复测。

## 13. 0.2.1空服复测：无需客机即可继续的排查（2026-11-28，18:40轮次）

用户目前没有客机配合。已读取18:40:47导出的 `perf-20261128T104047464Z.log` 与18:41:08更新的普通日志；原始快照、精确切换时序、分组统计与逐帧归因保存在 `artifacts/diagnostics/solo-validation-20261128-1840/`。本节仅校对实测结论，没有修改源码、编译新DLL或操作游戏。

环境为BepInEx5.4.23.6、Together5.5.5、MTOMP0.2.1、基础跟踪器2.3.1、增强0.2.1，仍是i5-12500H／RTX3050 Laptop。建房members=1，47个Together统计窗口均无网络流量，始终第0层。441个增强窗口全部Active；主机期间 `effectiveSuppressScans` 与请求一致，跳过计数持续增长，未再出现纯计时补丁误报冲突。第12节末“尚未实机复测”属于前一轮状态，现已获得这一轮空服结果。

### 范围缩小确实减少对象工作量

模拟半径4→2→1→4依次在engineTime187.831／211.999／417.951切换；perf相对时间需减头部 `engineTimeAtStart=5.559`。固定诊断抑制开启后，物品更新的稳定区间结果为：

| 模拟范围 | Item.Update调用／帧 | Item.Update耗时／帧 |
| --- | --- | --- |
| 9×9 | 86.24 | 0.392ms |
| 5×5 | 30.89 | 0.170ms |
| 3×3 | 15.10 | 0.098ms |
| 返回9×9 | 87.45 | 0.398ms |

这不是每个阶段完整的总CPU对照：早段3×3包含失焦、观察页面负担变化，不能用它证明3×3整体CPU一定低于5×5。9×9与5×5的同位置区间观察开销都约0.02ms／帧，FPS均约118.3，但Unity主线程墙钟6.811→5.796ms、Windows主线程CPU78.66%→66.09%，帧率限制等待1.635→2.642ms；负载降低主要表现为更多等待余量。

原日志同坐标(22.9,222.6)的4→2使CPU优化跟踪集合中活动对象612→190；2→1后收敛至67～74。在(43.3,230.0)从1切回4时，64→618，总登记数2098未变。这里仍指 `activeSelf`，不是动态刚体数。

末段进一步选取保持聚焦、观察开销相近的连续窗口，**保留区间内所有慢帧**，比较切回大范围前后：

| 指标 | 3×3末段 | 返回9×9 |
| --- | --- | --- |
| perf相对T | 390.026–412.147 | 416.161–427.206 |
| 时长／帧数 | 22.121秒／2607 | 11.045秒／1223 |
| FPS | 117.85 | 110.73 |
| Windows主线程CPU | 81.37% | 93.09% |
| 推算线程CPU／帧 | 6.90ms | 8.41ms |
| Unity主线程墙钟／帧 | 6.862ms | 8.600ms |
| 帧率限制等待／帧 | 1.615ms | 0.412ms |
| 观察开销／帧 | 0.0198ms | 0.0206ms |
| Item.Update调用／帧 | 15.13 | 87.36 |
| Item.Update耗时／帧 | 0.0947ms | 0.4098ms |

前段横移2.17单位，从(41.08,229.98)到(43.25,229.98)，后段固定在后者；精确同坐标的前段只有1.001秒／120帧，可佐证物品调用14.57次／帧，但不足以单独比较稳定CPU或周期扫描。同步对象数63→79～83，恢复后的集合也未完全等同最初9×9阶段。因此表内差值是本次观测，不是严格固定场景收益承诺。

相同末段对照中，Steam更新0.00752→0.00749ms／帧、收包0.000744→0.000734ms／帧、ChunkSleep.Tick 0.1893→0.1923ms／帧基本不变；Net.DoUpdate 0.0712→0.0932ms／帧。仍支持优先减少活动对象更新与同步准备工作，不支持重新引入收发线程。最后一次4→2发生在engineTime464.894，活动数616→191，但已晚于perf导出，只能使用普通日志验证，不能混入上述深层对照。

### 诊断抑制有效，收益主要是对应的周期尖峰

固定9×9、坐标(22.92,222.65)，正常扫描8.039秒与抑制26.121秒的独立对照：Reports.Tick墙钟开销2.962→0.070ms／秒，单次峰6.102→0.052ms；FPS118.67→118.26，物品调用86.25→86.24次／帧。固定3×3再次开关，也复现3.162→0.075ms／秒、峰6.775→0.0027ms。

这证明0.2.1兼容修正确实生效。约3ms／秒的扫描节省不能解释大幅平均主线程CPU变化；仍应把持续模拟负担与周期诊断开销分开。更小范围也未消除建筑注册：末段HostRegister约4.994→5.124ms／墙钟秒，单次峰11.129→10.156ms。

### 剩余长帧与空服下一步

- HostRegister全程215次，平均单次9.095ms、最高14.459ms。留存Frame32460（engine313.9144，perf T308.3554）整帧55.820ms，注册占9.586ms、脚本更新46.323ms；它是贡献项，不能解释整帧。可继续在空服分析全场注册的重复扫描与增量维护边界；它参与实际同步注册，不能像诊断一样直接跳过。
- 检查点仍有13个明显峰值窗口，约每30秒一次；首次85.975ms，其后15.88～27.75ms，多数约16～17ms。不能称保存尖峰已解决；可以继续细分采集／序列化／写入耗时，但现有证据不支持恢复此前无效的后台保存实现。
- perf T285.416所在窗口出现4280ms基础帧间隔，普通日志附近有QoL存档菜单刷新探测，Together阶段记录 `DelayedDynamicFrameRate` 峰3911.56ms。只能作为界面／延迟阶段线索，尚不能证明具体模组责任；不是本次范围切换或导出造成的已确认事件。
- 留存Frame39088／39144，perf T365.321／366.180，整帧268.712／211.586ms；诊断抑制开启，保存与注册耗时几乎为0，GC各代计数均未增加。脚本更新67.244／50.460ms，其余约201.052／159.569ms位于当前四个计时阶段之外；不能把这部分直接命名成物理、GC、CPU运算或I/O。先扩充纯计时覆盖，定位延迟脚本、界面、渲染提交与等待，以及尚未覆盖的脚本回调，再决定优化。
- 具体计时方向先考虑EarlyUpdate、PreUpdate、PreLateUpdate、PostLateUpdate四个顶层阶段，再根据结果补少量回调；保持原节点、顺序与执行逻辑，父子段不相加。此处是后续方案，本轮没有安装新计时钩子。
- DEEP_FRAMES累计47238帧、超过33.3ms共62帧，但逐帧只保留375个唯一帧，其中14个长帧。268ms仅是留存记录最大值；不能拿选择样本代表全部长帧，也不能用混合阶段的累计P95/P99比较各半径。基础帧间隔与增强PlayerLoop头尾计时边界不同，不强行同帧对应4秒停顿。

目前无需等待客机：范围对持续主机负担的贡献、诊断抑制、建筑注册与未知长帧均可独自开服继续定位。若补对照，固定存档位置／镜头与聚焦状态、关闭观察页面且保持采样配置一致，每段至少30秒，先导出再换条件。只有远端战斗、玩家分散、跨区同步与伤害一致性仍需真实多人验收；本轮也不回答AMD平台差异。不恢复已被用户否定的后台保存或收发接管。

## 14. 按用户新指令恢复检查点试验，改名MPreducer（0.2.2-alpha）

用户在第13节之后明确要求重新尝试检查点后台保存、允许降低保存频率，其余方向先放下；同时指定新文件名`DDEN.MPreducer.dll`及描述“让超速的亿堆检测器减速”。本轮按此更新，不推进建筑注册、主循环计时或其他优化，也不恢复收发接管。第13节末不恢复检查点的建议已被本次用户指令取代，不代表此后仍禁止实现。

初次交付只改程序集／DLL与界面名称，用户随后要求源码也改名，现目录为`src/DDEN.MPreducer`、项目为`DDEN.MPreducer.csproj`、入口为`MPreducerPlugin.cs`，命名空间及嵌入资源名前缀同步为`DDEN.MPreducer`；开发解决方案和离线检查引用已同步。插件GUID、模块ID与cfg身份仍保留，版本0.2.2-alpha。替换安装时只保留新DLL，移除旧`DDEN.MoreThreadOnMP.dll`，不要并存。描述同时进入页面、英文资源与程序集描述元数据。前文旧源码路径仅记录历史时点，当前构建命令以新项目路径为准。

新增两项独立对照，均受模块总开关控制且默认关闭：

| 配置 | 默认值 | 行为 |
| --- | --- | --- |
| 实验线程/CheckpointSaveWorker | false | 恢复原检查点处理合并与纯数据后台压缩／临时写入 |
| 检查点保存/ExtendIntervalEnabled | false | 仅替换已核验的定时轮转阈值 |
| 检查点保存/IntervalSeconds | 60 | 开启延长间隔时生效，可设30～300秒；页面提供30／60／120／300秒 |

真实5.5.5的Tick先累积`unscaledDeltaTime`，达到30秒后轮流选择**一名**有效玩家调用`WriteOne(false)`。间隔补丁只替换`_wait < 30f`中的唯一阈值，保留原累计、玩家选择与其他分支，不重置已累积时间；关闭、非主机或未知补丁时用30秒。多人时同一玩家的定时周期近似有效人数×间隔，不能把60秒描述成所有玩家一分钟各保存一次。手动、换层与离场保存仍即时走原入口。

后台实现从本机历史**自有**最终0.1.0产物及纯Core源稿恢复，没有复制第三方业务实现。主线程保留角色采集、原JSON处理、Together／CCL／QoL回调及ASCII中间语义，将同次保存的重复压缩与文件更新合并成每个目标最终JSON。后台只处理不可变字符串及自有临时文件；主线程核验会话、修订与文件SHA指纹后逐文件替换。仍有采集／JSON／指纹／最终提交成本，不承诺解决持续低帧。

恢复时补齐：准确核对实际perfdeep状态类型与间隔补丁，未知补丁回退；仅撤销自身具体方法；生命周期限定所属主线程；初始化失败释放worker；捕获中关闭／卸载延到保存finalizer排空，防止中途丢弃虚拟文件；worker的Drain可以接回已取出但尚未完成的结果。读取、后续保存、转移、停服与关闭屏障先完成在途保存。文件冲突、旧会话及部分提交不重放覆盖；只有目标仍完整可核验时才尝试一次原保存回退。两目标逐文件原子，不是整体事务；任意第三方绕过已列入口的直接文件访问仍不在保证范围。

请求／实际后台状态、实际轮转秒数、采集／完成／合并／回退／冲突计数和采集／后台准备／提交／屏障峰值均可从页面或模式日志核对。可先保持其他条件不变，只开60秒间隔，再保持60秒单开后台处理，避免把少保存一半误认为每次保存更快。此次没有修改真实cfg／存档，没有部署或操作游戏；真实保存恢复与性能收益需后续实测。

Release构建通过，0错误，1条NU1900漏洞元数据源不可达警告；主离线237项、检查点专项194项（真实DLL契约75／writer27／Harmony夹具92）、原回归DLL契约25／Harmony51项通过，88条英文资源与占位符核对通过。测试只在自有夹具／临时文件运行，真实游戏DLL只读元数据。交付`dist/MPreducer-v0.2.2-alpha/DDEN.MPreducer.dll`，99328 bytes，SHA256 `76FDD88899BE341D7FCBD171478F0DB662F3DD54B14C9BC32B8079FF11590A96`；本轮未提交推送。

随后源码目录、项目及内部名称统一改名后，重新运行上述237／194／25／51项检查全部通过，12个开发解决方案项目路径有效；Release构建0警告／0错误，本次恢复依赖时不查询NuGet漏洞元数据。版本和运行逻辑保持0.2.2-alpha，已更新同一交付路径的DLL，99328 bytes，**当前SHA256为`665578BAB6680963D4E56F7B582C8ED9D67D4D42A8985CEFA828C3C5B4442AB9`**，上一段哈希属于改源码名称之前的初次交付。未部署或操作游戏。
