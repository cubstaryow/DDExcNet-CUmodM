# 文案核对与归纳 · v1.6.0

本次核对README、发布正文、技术说明、工作流程、维护约束、外部接入文档和独立ET修复说明。依据是当前源码及本机上游行为阅读；历史实测只归属于当时版本，不扩展为本次验收。

| 问题 | 统一后的描述 | 代码证据／归属 |
| --- | --- | --- |
| 无芯片被描述成HUD变了、名牌没变，掩盖主问题 | 主机开无芯片，客机仍有芯片；修的是实际规则和玩家状态不同步，关闭也能同步恢复 | `MPfixPlugin.ApplyLocal`，world／RunUnchippedLock／ScavPlayer |
| OverrideIndividualRule被写成单纯显示选项 | 关闭Together个人无芯片规则，统一跟随主机；不只是外观 | `TickHost/ApplyLocal`设置 `Mp.UnchippedIsIndividual` |
| 0.5秒旧巡检、30秒请求、“无稳定期请求”混写 | 旧0.5默认迁移30；30秒是本地对缓存值核对，客机另有20秒请求 | `MigrateOldWatchdog/TickClient`；与主机配置同步节拍分开 |
| 不开激进也声称按同一套算法完全重算、保证不跳血 | 基础只补主机漏掉的世界伤害倍率，原判定仍在；不能消除所有状态差异 | `BlockDamageRelax.Prefix`、`UnitDamageRepair.Repair` |
| 主机结算被误解为用主机自身种族／力量 | 用发送者对应的客机角色在主机上的副本；副本仍可能与客机当前值不同 | Together `TryGetPlayerAndNetBodyFromClientId` → `plr.body/pb.body` → `CalculateAttackPower` |
| 所有分支的激进机制写成独立发包、事后补扣 | 方块碎后批量上报、补差；单位／角色在原攻击包附加报告、原结算用一次，拒绝则基础回退 | `BlockDamageSyncFix`、`UnitDamageRepair`、`AttackReport` |
| “动物／敌人全部修好”与“无条件采用客机值” | 按具体近战分支，报告有可信度限制；其它攻击路径不能推定覆盖 | `DamageBuilding/DoPVPMeleeDamage`挂点及 `AttackReport.Credible` |
| “伤害333、世界x3”被直接解释为扣999 | 跳字不足以推导权威伤害；需看两端攻击字段、力量及目标实际状态 | 主机权威结算与客机预测，见features示例 |
| 方块基础仍写自定义比例或3000次停报 | 当前原生比例／上限保留；旧BlockDamageRatio和进程累计停报限制已删除 | `BlockDamageRelax`与当前队列 |
| 物品生成修复被写成总是再造一件 | 正常结果优先，主机合成作用域内失败才兜底一次；娱乐赠品独立 | `CraftSpawnScope/UtilsCreateCompatPatch/DoublePurchase` |
| 物理保险仍写HeldQuiet被动恢复、生成时扫描 | 只处理实际DropItem／DropWearable，自由物品且检查足够时补碰撞；不挪位置 | `ItemPhysicsGuard` |
| 离场就删除物品，或主机背包也清掉 | 主机确认离线保存身份后，只清其它客机对应副本；未经确认与正常物品保留 | `GhostItemRelay/NetworkObjectIndex` |
| 把物理步／帧当物理压力、等待当GPU根因 | 调度、准确计时、对象状态与观察者开销分开；等待／数量只能提供线索 | `PhysicsTiming/FrameStages/ObjectCensus/PerfAlerts` |
| Unity 2022.3被当成游戏版本 | 引擎版本与产品版本分开；当前门槛实际只核对Together 5.5.4 | `DdenGate.VersionOk/ExpectedGame` |
| F11被写成默认值 | 默认F8，冲突后看本机实际配置；F11只是过去测试结果 | 管理器Config.Bind与 `Compat.ResolveHotkey` |
| “四个／五个／六个”组件数量不一 | 五个DDEN产物含Shared，另有独立ET-Fix，共六个DLL | `CUmod.slnx`、发布产物映射 |
| 编辑器默认不部署／不发布与当前脚本矛盾 | 完整构建部署默认含编辑器；`--no-saveeditor`可排除，发布包仍含它 | `scripts/build.sh`、`scripts/release.ps1` |
| 提示所有DLL必须同目录，独立外部模组也必须装Shared | DDEN组件需要可加载的Shared；ET修复单独可用，不强制引用DDEN | 生产项目引用与L1桥 |
| 有DdenDraw时“配置”只显示状态页 | 配置路由extconfig，功能路由ext；“配置／单独看”始终到配置项 | `HubPlugin.DrawContent/DrawExternalPage` |
| CUCoreLib广播说明自相矛盾 | 直接Broadcast仅主机；Everywhere等转发接口另有路径，因此广播来源仍要校验 | `MultiplayerBridge.Broadcast/BroadcastEverywhere` |
| 注册返回值与发送回执混为一谈 | CCL注册方法返回void，Shared现有bool封装不能单独证明是否已注册；本轮仅核正文档，封装语义列为后续检查项 | `Mp.RegisterClientHandler/RegisterServerHandler`与底层签名 |
| 存档取消cheatedSave被写成保证恢复成就 | 只改保存字段，不承诺游戏平台已授予或被拦截的成就状态 | 编辑器与保存边界 |
| 根CHANGE与维护说明积累互相矛盾的“本次” | 当前CHANGE短记录；旧对话整份移入history并标为过时 | 文档目录、history，不把旧哈希当新包 |
| v1.5.0与CageFix旧名仍写成当前 | 当前v1.6.0；产品／程序集／路径统一ET-Fix，旧GUID／存档／频道保留兼容 | 项目、BepInPlugin、脚本、独立说明 |
| 双语方案被误认为本版已有功能 | 仅方案，没有语言开关或完整英译的实现承诺 | localization-plan，本次不加UI |

## 后续维护规则

玩家README和发布正文只讲目的、选择、效果边界；features讲代码路径，development讲可执行操作，接入文档讲真实接口。修复触及配置短说明时同步DdenDocs，但保留原Section／Key，避免破坏cfg及主机配置白名单。

发现旧结论时更新当前权威入口；历史记录只加过时标记，不冒充当前说明。测试记录写清版本、工具、是否模拟和真实联机是否执行；本轮实际结果见 [CHANGE.md](../CHANGE.md)。
