# DS Tool Extension 变更记录

## v1.2.10 · 2026-11-25

- 修复与 JDI-DDEN 的 DDEN 配置键冲突：cfg section 由 General 改为 DS Tool Extension（DdenDocs 全局以 section/key 为键，General/Enabled 冲突导致 JDI 的条目被覆盖为实验性）。
- 旧 cfg（General/Enabled、General/DebugLog）回到默认值（Enabled=true、DebugLog=false），功能无影响。

## v1.2.9 · 2026-11-25

- 稀有度定稿：三件物品去掉世界散布（WorldSpawnPerChunk 移除），改为仅从 ContainerCrate/DropCapsule/CapsuleContainer 与三个商人池低概率获得（SpawnFrequency=1，最稀档）；不含医疗箱/食品箱/尸体。

## v1.2.8 · 2026-11-25

- 三件物品设置低概率世界生成（WorldSpawnPerChunk=0.001，不再注入随机战利品池），降低刷新率。

## v1.2.7 · 2026-11-24

- 性能：DrillLoopSound 静态缓存 drillLoop clip；Update 在静默时早退；物品不在玩家槽位时禁用组件，Plugin 每 1 秒兜底重新检查（Parent 变化未触发也能恢复）。

## v1.2.6 · 2026-11-23

- 性能优化：DrillLoopSound 缓存 InventorySlot/本地 Body，状态每 0.2 秒评估一次；BatteryEncumbranceReduction 每 0.5 秒检查一次，减少每帧 GetComponent 与容器字段写入。

## v1.2.5 · 2026-11-23

- 循环音效调试日志改为配置开关（General/DebugLog，默认关闭），减少长期游玩时的日志开销。

## v1.2.4 · 2026-11-23

- 多人音效调整：本地玩家手持钻机正常播放；其他玩家的钻机在 12 米内以 0.5 音量播放，远处静音。

## v1.2.3 · 2026-11-23`n`n- 修复多人下客机使用钻机时主机也会播放钻机循环音效的问题：只让本地玩家手里的物品发声。`n`n## v1.2.2 · 2026-11-23

- 实验性特种腿包增加双配方（INT 14）：
  - ET 版：特种腿包、钛板 ×3、钛棒、加工铜、电路板（≥80%）、一捆导线 ×2、柔性玻璃 ×2、塑料块 ×2、生化流体 35mL、工程化 1、可改造 4、可切割 1；
  - 普通版：腿包、钛板 ×5、钛棒 ×2、加工铜 ×2、电路板（≥80%）×2、一捆导线 ×2、柔性玻璃 ×3、塑料块 ×2、生化流体 45mL、可切割 1、可捶打 1。

## v1.2.1 · 2026-11-23

- 实验性特种腿包减重改为：没电/没电池不减负重，有电池且有电时减 95%（`encumberanceMult = 0.05`）；
- `Container.EncumbranceReduction` 不再作为减重来源（组件每帧覆盖）。

## v1.2.0 · 2026-11-23

- 新增第三件装备：实验性特种腿包（`Experimentalspeciallegpack`）：
  - 容器容量 11.5 / 单件 4.5 / 基础减负 0.05；装 Large 电池（300）；
  - 与重力袋一致：有电时容器负重倍率 0，没电/没电池恢复 1；不爆炸、耐久归零不销毁；
  - 可穿戴（ThighF）、护甲 0.35、受击耐久损失 0.15；无品质；
  - 图标：`images/Experimental special leg pack.png`。

## v1.1.1 · 2026-11-23

- 实验性等离子束发射器增加配方（INT 17，同钻机）：
  - 普通版：等离子切割器、加工铜、中型电池（≥25%）、电路板（≥80%）、一捆导线 ×4、铁钉 ×2、厚钛板、钛板 ×2、生化流体 50mL、可捶打 1、可切割 1；
  - ET 版：可捶打 1 → 工程化 1，可切割 1 → 可改造 4，其余不变。

## v1.1.0 · 2026-11-23

- 新增第二件装备：实验性等离子束发射器（`Experimentalplasmaemitter`），等离子切割器升级版；
- 钻机配方材料调整：加工铜 → 厚钛板（`titaniumslab`）、废料块 → 钛棒（`titaniumrod`），普通/ET 两版同步；
- 等离子束发射器数值：damage 30 / structural 30 / metalMoreDamage / attackCooldownMult 0.4 / distance 6.5 / cooldown 0.07，耗电为切割器一半，保留肢体切割；
- 图标：`images/Experimental plasma emitter.png`。

## v1.0.2 · 2026-11-23

- 增加双配方（INT 17，同增强钛镐）：
  - 普通版：重型钻机 ×1、加工铜 ×1、中型电池（≥25%）×1、电路板（≥80%）×2、一捆导线 ×3、铁钉 ×1、废料块 ×1、生化流体 50mL、可捶打 1、可切割 1；
  - ET 版（装 EnhancedTools 时）：可捶打 1 → 工程化 1，可切割 1 → 可改造 4，其余材料不变。
- 按 `com.a4kk.enhancedtools` 是否存在二选一注册；加载顺序由软依赖保证。

## v1.0.1 · 2026-11-23

- 第一件装备：实验性钛合金钻机（`ExperimentalTi-alloydrill`），CCL 自定义物品；
- 数值：damage 125 / structuralDamage 150 / attackCooldownMult 0.3 / distance 4.5 / cooldown 0.12 / rotateAmount 6 / 重量 4 / 价值 60；
- 耗电为重型钻机的一半（`DrainCharge(0.00119047625)`），Large 电池（300）；
- 循环音效 `drillLoop`（重型钻机同款，手持且攻击时播放）；
- 中英文本走 `zh-CN.json` / `EN.json`；
- DDEN L1 接入：实验性标记 + `DdenLocalize` 双语。
