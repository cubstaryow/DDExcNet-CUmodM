# DS Tool Extension 变更记录

## v1.2.2 · 2026-11-23

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
