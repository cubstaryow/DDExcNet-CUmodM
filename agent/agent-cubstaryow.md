# cubstaryow 协作者维护说明 · v1.6.0

本文件属于cubstaryow协作者使用的agent维护说明。公共协作入口见 [根目录agent.md](../agent.md)，其他协作者使用自己的说明，不将本文件自动视为所有人的个人工作约定。文中未作链接的源码路径以仓库根目录为基准。

先读 [玩家入口](../README.md)、[文档索引](../docs/README.md)、[当前功能](../docs/features.md) 与 [构建检查](../docs/development.md)。描述与代码冲突时先核对源码，记录证据，不沿用历史结论。

## 工作边界

- 当前版本 **1.6.0**；props和各插件BepInPlugin同步。五个DDEN产物（含Shared）＋独立ET-HeatingCageFix，不把Unity引擎版本当游戏版本。
- Git操作以用户授权及仓库工作流程为准；在 `dev`提交经过检查的相关文件，获准后普通推送 `origin/dev`。不force push、不改remote、不替用户合并master、打tag或创建Release。不要把一次任务的授权推广到无关任务。
- 写入L盘需依照工具权限申请执行；不要把工具权限失败误判为仓库bare或擅自修改git配置。保护用户已有改动，不用reset/clean丢弃内容，不用无差别add把无关文件带入提交。
- 不提交 `bin/obj/dist/artifacts/decomp/backup`或商业游戏／第三方依赖。反编译仅阅读结论，不复制他人实现。
- **发布包只含 DDEN 五个组件**（公共层 / 管理器 / 多人修复 / 性能跟踪器 / 存档编辑器）；独立外部模组「ET-Fix（ET 加热笼修复）」不随包发布，本机部署仍会带上它供联调，但不进 dist。
- 不停止用户游戏。部署前确认已退出；只处理列明的自有文件，不创建插件备份，不碰其他模组，不通配删除plugins目录。不在未授权时改真实存档／配置。
- 源码及脚本MPL-2.0，资源／名称／世界观按LICENSE-ASSETS；保留归属。世界观文案放资源文件，不放源码字符串。

## 目录与契约

`src/AGENTS.md`规定DDEN模块约束；独立外部模组在 `src/外部mod/<名称>/`，ET修复读 [专属AGENTS](../src/外部mod/ET-HeatingCageFix/AGENTS.md)。DDEN内部模块通过Shared `IDdenModule/DdenHub`接入，外部L1通过public static桥，不强制引用公共层。

管理器入口必须先识别 `config:`、`ext:`、`extconfig:`再找内部模块。外部“配置”始终显示配置项，自定义功能页另有入口。列表高度按实际内容计算，滚动成对结束并在异常时清理。

## 描述必须准确

1. 无芯片核心问题是“主机开无芯片，客机仍有芯片”；同步实际规则与本地玩家，不是仅改HUD／名牌。30秒watchdog核对缓存值；客机另有20秒主动请求。
2. 非激进伤害修复只补主机漏乘的世界伤害倍率。主机重跑使用攻击客机的远端角色状态，不使用主机自己的种族／力量，不保证状态副本相同。
3. 激进仅作用于已开启的分支。方块碎后上报并收敛差值；单位／角色报告附在原攻击消息中，主机校验且只结算一次。不能写“所有敌人／所有伤害已修好”。
4. 区分构建、离线模拟、菜单绘制与真实多人验收。未测项明确写出，不承诺零开销或必定恢复。
5. README与界面面向玩家；技术细节进入docs。旧结论归入history并标过时，不与当前手册并列；当前状态见CHANGE与description-audit。

## 实现不变量

- 无芯片：主机真值来自 `Mp.HostUnchipped`；客机一起更新world、RunUnchippedLock与本地ScavPlayer。
- 伤害：`BlockDamageRelax`仅补hostOwnDamage后交回原判定；不要恢复已删除的BlockDamageRatio或工具耐久同步。单位AttackInfo复制后改字段，不把世界倍率乘到击退。
- 合成：CCL正常结果优先；作用域内主机结果为空才兜底一次，不抢前缀生成、不克隆现存物品。娱乐赠品独立。
- 穿戴：确认请求玩家和物品归属、候选唯一；不回落到主机LocalBody、不复制／重建。物理保险只管实际丢弃，不全场扫描、不挪位置。
- 离场卡顿：**已剔除，不再尝试**（客机离场导致其他客机卡顿的问题无法修复）。`GhostItemRelay` 与 `RelayGhostItems` 已从 MPfix 移除，源码备份在 `decomp/backup-ghostitems/`；不要用按坐标猜副本、冻结/抬升/全场搜索等旧思路重做。
- 名称：只改显示，不改SteamID／逻辑名／存档身份；只用游戏字库，缺字 `?`，不加系统字体兜底。
- 主机权威配置：临时生效层，不覆盖本机cfg；仅主客机均采用才锁客机，退出恢复。缺依赖限制本机和主机临时值。
- 编辑器：只改用户字段，未知节点原样保留；items/itemComponents成对处理；保存检查外部修改及QoL忙，原子写＋备份。cheatedSave仅显式修改，不能宣称保证平台成就。
- 性能：零Harmony补丁；步／帧只反映调度，准确计时缺失显示不可用；详细统计默认关，手动导出日志，等待／数量是线索而非根因证明。

详细原理、构建命令和验收清单分别在features、development、各工具中，不在本文件重复存旧文档。双语切换目前仅有方案，未实现，见 [localization-plan](../docs/localization-plan.md)。
