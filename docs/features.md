# 当前功能、证据与限制 · v1.7.5

面向维护者。玩家入口见 [README](../README.md)，检查方法见 [development.md](development.md)。以下以当前源码为准；Together反编译用于理解上游行为，不复制其实现，也不随源码分发。

## 1. 管理器与公共层

六个DDEN产物是管理器、MPfix、存档编辑器、性能跟踪器、UnitListMgr 和 Shared 公共层；仓库另有独立外部模组ET-Fix和JDI-DDEN，它们不包含在DDExcNet发布包中。

`DdenHub`登记内部模块，管理器显示功能页与通用配置页。路由包括 `config:<id>`（内部配置）、`ext:<index>`（外部功能）和 `extconfig:<index>`（外部配置）。总览“配置”和全部配置“单独看”始终打开配置项，外部自绘页另有入口；总览和配置页按内容高度滚动。

外部插件用public static L1桥交配置，无需Shared引用；`DdenUnavailable(ConfigEntryBase)`可返回禁用原因，`DdenSummary`最多每秒刷新一次。协议见 [接入文档](cumodm-integration.md)。

控件由 `DdenUI` 自绘；滚动、裁剪与输入隔离共用Shared实现。`UiGuardPatch`拦截游戏菜单的直接鼠标点击，`DdenBlocker`处理其它界面遮罩。默认快捷键F8，冲突后实际键可变化；F11是已有环境中的一次结果，不是固定默认值。

`DdenGate.ExpectedMp`当前为5.5.4。`VersionOk`实际只比较联机组件版本；`ExpectedGame`为展示参考，Unity 2022.3.62f3是引擎版本。忽略检查只允许继续尝试，不是兼容性证明。

## 2. 无芯片规则同步

**核心现象**：主机开启无芯片模式，客机依旧有芯片，双方实际规则不同。主机关闭后客机仍无芯片也属于状态不一致；不能把这个功能概括成“HUD变了、名牌没变”。

游戏的 `WorldGeneration.unchippedMode`、运行设置和Together玩家状态并非只要其中一个变了就全部更新。MPfix读取主机值，用进局快照、主机变更广播及客机请求同步。`ApplyLocal`在世界存在时调用 `world.SetUnchipped`，同时镜像 `WorldMP+RunUnchippedLock`并调用本地 `ScavPlayer.SetUnchipped`；世界未就绪时先写运行设置。

| 开关／动作 | 当前代码行为 |
| --- | --- |
| `无芯片同步/启用（Enabled）` | 控制自动同步；手动同步按钮仍可在模块总开关允许时请求／应用 |
| `ForceHostUnchipped` | 收到状态时强制应用，并允许本地漂移纠正；关闭后首次／变化值仍会应用，手动／周期请求也会应用，不能写成“完全不采用主机” |
| `OverrideIndividualRule` | 将Together `UnchippedIsIndividual`置false，使该局规则以主机为准；不是仅隐藏名牌或语音 |
| `WatchdogSeconds` | 默认30秒，将本地世界状态与**最近收到的主机值**核对并纠正；0关闭这一项，不关闭其它同步路径 |
| `TickClient`主动请求 | 开始作为客机时请求，并每20秒再问主机；不是按30秒发一次网络请求，也不是无稳定期请求 |
| `AnnounceToOtherPlayers` | 发生本地纠正时写控制台提示；不改变同步逻辑 |

依赖Together与CUCoreLib。源码入口：`MPfixPlugin.RegisterChannels/CaptureState/OnHostState/ApplyLocal/TickHost/TickClient`、`DDEN.Shared.Mp.HostUnchipped`。无芯片同步和“采用权威主机配置”是两个独立功能，后者关闭不等于前者关闭。

## 3. 近战伤害：主机伤害字段补算

### 3.1 原因与角色来源

客机有本地命中预测，随后以主机结算为准，因此先减血再回升可能是两端伤害值不一致后的覆盖，不能仅凭跳字判定实际扣了多少。

Together 5.5.4的 `ServerReceiver_PlayerBodyAttack`通过发送者ID取得 `plr/pb`，对 **plr.body/pb.body，即该客机在主机上的角色副本** 运行 `BetterUseItem`或空手Attack，并调用 `CalculateAttackPower(pb.body, ...)`。不是使用主机本人的种族／力量。

远端重跑设置 `ForceOnlyRetrieveAttackInfo`，攻击被提前截取，没有跑完原版 `Body.Attack`给 `damage/structuralDamage`乘 `attackdamage`的步骤。故主机少了世界伤害倍率；补这个遗漏不代表两端种族、力量、装备和身体状态的副本已经完全一致。

例如世界x3，某次客户端最终预期333，只有在其它状态一致且主机漏的恰好只是x3时，主机估算才可能约为111；不能从“333跳字”反推本次必然扣999再恢复。应对照主机记录与目标状态。

### 3.2 三条分支的补算位置

| 分支 | 源码 | 做什么／不做什么 |
| --- | --- | --- |
| 方块 `SyncBlockBreak` | `BlockDamageRelax.Prefix` | 只把 `hostOwnDamage`补乘世界倍率，然后继续Together `ChooseBlockDamage`；不改原比例判定、上报上限或拒绝退路 |
| 单位 `SyncBuildingDamage` | `BuildingDamageRelax` → `UnitDamageRepair.Repair(1,...)` | 对主机处理远端近战的AttackInfo作副本，补伤害与结构伤害字段；原MP继续处理命中、生命、动物反应和掉落 |
| 角色／肢体 `SyncBodyDamage` | `UnitDamageRepair.Repair(2,...)` | 同样只修主机远端近战伤害字段；不把世界伤害倍率额外乘到力量或击退 |

`AttackDamageMultiplier`按世界实例缓存1秒。有限且大于0的世界倍率适用，包括低于1；读不到、非正或非有限值按1。缓存意味着运行中修改倍率最多约1秒后反映。

这里只补主机的伤害字段，不是事后给目标再扣一笔，也不新增伤害报告。`BlockDamageRatio`已删除，不再放宽Together的判定。修复仍可能因状态差异、原上限／校验和预测时序不足以消除回弹。

例如世界x3、客机跳字333，仅在其余状态一致且主机恰好只漏乘x3时，主机修前估算才可能约111。不能据跳字推定实际扣999再回升；应核对主机结算和目标状态。

当前工作树已移除AggressiveConverge、BlockBreakMinFraction及穿戴兜底两个配置，原伤害附加报告和wearfix频道不再使用。队列／身份索引辅助类型仅剩离线检查引用，不表示旧机制仍在运行。

## 4. 合成补救（实验性）与娱乐分开

Together主机的原合成结果经 `Utils.Create`生成；CCL已有正常模组物品兜底。HarmonyX执行多个前缀，旧版再次实例化会留下两个不同对象，网络登记幂等无法消掉这种重复。

当前合成补救为**实验性**功能（配置行与提示框都标注「实验性 · 需多人实测」）：失效模板重建有冷却，仅在模板仍有注册资料且确实失效时重建。`UtilsCreateCompatPatch`前缀只观察购买ID；`CraftSpawnScope`限定原生主机非液体合成结果。后缀只在结果仍为空、正确作用域及开关开启时调用一次CCL完整生成入口。准确空资源异常单独识别；其它构造异常不吞，不用失败永久负缓存。正常结果不替换，不兜底客机、单机、购买或世界生成，不克隆现存物品。失败保留原失败／材料回滚。

“道生一 一生二”是独立的默认关闭玩法。主机购买额外给一件，普通生成20%额外一件；赠品走正常创建及网络登记，商人赠品保持满弹药。不能因正常生成两份就一概归因合成补救，先核对娱乐开关。

## 5. 物品与商人

- **穿戴兜底在v1.7.1之后的当前工作树中已剔除。** `WearGhostFix`（客机穿上后请主机补穿 + 广播）连同 `穿戴兜底/Enabled`、`MaxPerSession` 两个配置
  与频道 `ddn.mp.wearfix` 已移除，源码备份在 `decomp/backup-aggressive-wear/`。装备显示不一致时不再由本模组介入。
- `ItemPhysicsGuard`：仅实际 `Body.DropItem/DropWearable`时处理自由物品；不移动、不改刚体模式、不做全场轮询。重叠或判断不足时保留现状，不能承诺所有穿墙都修好。
- `TraderSnapshotFix`：默认关。开启后，客机打开商人时请求Together重发商品快照；不是在本地自行扣款或造货。`TraderUIQuietFix`仅对可确认的模组空资源误报静音。
- **玩家离场物品剔除广播：v1.7.1 已移除并放弃修复。** 用户实测该方案不能解决客机退出后其他客机持续掉帧的问题，不能再将它列为本模组的有效功能。
  `GhostItemRelay`、`RelayGhostItems` 配置及 `ddn.mp.ghostitems.v2` / `ddn.mp.ghostitems.verify.v2` 收发入口已经移除；旧 cfg 中残留该键也不生效。主机权威配置不会重新启用它。
  原功能与恢复说明仅备份在本地 `decomp/backup-ghostitems/`，不编译、不打包、不提交；此备份不表示计划恢复该功能。`NetworkObjectIndex` 仍为未接入运行流程的辅助类型与离线检查输入，不会触发物品剔除。
  MPfix 不再介入玩家退出时的背包处理，相关行为交回原版 Together 的保存／重连流程。本次不判断掉帧的唯一根因，也不宣称该问题已经解决。实际丢弃碰撞保险是另一项功能（只处理亲手丢出的物品），不因名称中有“幽灵”而删除。

## 6. 名称与主机权威配置

`PlayerNameRepair`只改本机显示，默认开；不写逻辑玩家名、SteamID、握手或存档身份。`PlayerNameFonts`只借用游戏字体／可用源字体，缺字按Unicode标量替换 `?`，不调用系统字体兜底；局部字体副本停用时恢复，游戏字体资源不销毁。

名称解析统一经过`NameDisplayCache`：按Steam身份共享昵称查询缓存，临时取名失败时保留已恢复的名称，自定义别名按原规则保留。Steam名称事件只使缓存失效；名牌、协作手部名牌、方向提示、玩家列表和聊天在各自原MP显示流程中读取统一结果。名牌在原生LateUpdate／Update内处理已绑定的文字组件，聊天在原MeasureChatLine布局前按需更新编译缓存；稳定时每个组件／消息最多0.25秒检查一次，缓存版本变化时提前检查，Steam查询同身份共享且通常间隔5秒。没有独立名称队列、全玩家轮询或主动MP姓名／颜色刷新，不改地点标记透明度、目标或计时。

聊天历史保存发送者身份与原始名称快照，昵称后来恢复时可更新显示；玩家离场后保留已恢复的历史名称与原自定义别名。无明确玩家身份的系统／纯文字消息不按数字猜测发送者；原MP若之后提供明确玩家引用，再接入统一解析。停用时恢复本机原显示，字体仍只使用游戏字库，缺字显示?；不处理LES实体／RPC异常。真实联机显示仍待验收，离线通过不代表已在游戏中验证。

`UseHostAuthorityConfig`默认关。主机开启才下发值；关闭仅发停用标记。客机自己也开启且收到有效启用结果，才临时采用白名单配置并锁定其它项；总开关、采用选项仍可编辑。主机不锁定，配置处显示“（主机权威模式）”。退出、拒绝或停用恢复最新本地配置，不将主机值持久化到本机cfg。主机变更0.25秒合并、进局／采用时有界请求，稳定时不定期重发。此节节拍不能套用到无芯片同步的20秒请求上。

`FeatureDependencies`限制本地与主机临时配置。所有MPfix功能需要Together；无芯片、合成兜底、权威配置、商人快照／静音还需CCL。基础伤害、名称和娱乐不因缺CCL连带关闭；缺依赖的bool置false，补齐并重启后由用户开启。

## 7. 存档编辑器与性能分析

编辑器以 `DocumentSession/SaveDocument`保存原始JSON工作副本，常用操作在图形分区，未知字段保留。只写用户修改字段；弹药与附加组件按索引成对处理，支持弹膛／弹匣、电池与混合液体。数组错位时停止相应编辑，不猜测重建。已知规格校验容量；未知规格说明限制。图标保持比例，不拉伸图集。

`SaveCodec.Write`按原子方式写盘并生成 `.cumod.bak`，同步QoL元数据，先检查自动存档忙及文件被外部修改。`cheatedSave`仅用户在进度页明确改动时写入；不能把取消该字段写成平台成就必定恢复。实时“同步到角色”只改内存，不等同保存。

性能跟踪器不安装Harmony补丁，只观察；详细场景统计默认关，手动导出才写日志。物理步数是调度，真实计时不可用时标不可用；CPU／GPU／呈现等待提供线索，不给具体调用栈或断言唯一根因。详见 [指标定义](perf-tracker.md)。

## 8. 独立ET加热笼修复与验证边界

[ET-Fix](../src/外部mod/ET-Fix/README.md)独立于DDEN公共层，ET／CCL／Together通过延迟反射，缺少对应接口停用相应功能。具体腐坏、热源过滤、保存与查询协议见 [专属细节](../src/外部mod/ET-Fix/docs/features.md)。

本轮文档核对与改名的验证记录以 [CHANGE.md](../CHANGE.md) 为准。离线检查模拟Unity／传输边界；菜单绘制成功不等于真实多人伤害、名字或加热笼行为已验收。
