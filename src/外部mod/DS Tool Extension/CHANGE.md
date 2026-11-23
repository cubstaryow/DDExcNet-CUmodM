# DS Tool Extension 变更记录

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
