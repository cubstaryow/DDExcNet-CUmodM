# DS Tool Extension（DS 工具扩展）

《Casualties Unknown》的工具扩展模组，以 **CUCoreLib（CCL）为前置**，兼容多人，并接入 **DDEN CUmodM** 的 L1 软接入（实验性 + 中英双语）。

## 第一件装备：实验性钛合金钻机

| 项目 | 值 |
|---|---|
| id | `ExperimentalTi-alloydrill` |
| 中文名 | 实验性钛合金钻机 |
| 英文名 | Experimental Ti-alloy drill |
| 中文描述 | 重型钻机的钛合金升级版本，钻头自加热，效率更高 |
| 英文描述 | A titanium-alloy upgrade of the heavy drill. The bit is self-heating for higher efficiency. |
| 分类 / 标签 | tool / tool |
| 重量 | 4 |
| 价值 | 60 |
| damage | 125 |
| structuralDamage | 150 |
| attackCooldownMult | 0.3 |
| distance | 4.5 |
| knockBack | 180（沿用重型钻机） |
| cooldown | 0.12 |
| rotateAmount | 6 |
| 每次攻击耗电 | `DrainCharge(0.00119047625)`（重型钻机的一半） |
| 电池 | Large（maxCharge 300，自带电池） |
| 循环音效 | 重型钻机同款 `drillLoop`（手持且攻击时播放） |
| 合成配方 | 见下方「配方」 |



## 第二件装备：实验性等离子束发射器

| 项目 | 值 |
|---|---|
| id | `Experimentalplasmaemitter` |
| 中文名 | 实验性等离子束发射器 |
| 英文名 | Experimental plasma emitter |
| 中文描述 | 去掉了等离子流的限制模块，采用钛合金重做散热系统以达到在相同时间内可以射出更多的等离子束 |
| 英文描述 | The plasma flow limiter has been removed, and the cooling system has been rebuilt with titanium alloy so that more plasma beams can be fired in the same amount of time. |
| 分类 / 标签 | tool / tool |
| 重量 / 价值 / 识别度 | 2.5 / 60 / 12 |
| damage / structuralDamage | 30 / 30（metalMoreDamage=true） |
| attackCooldownMult / distance / cooldown | 0.4 / 6.5 / 0.07 |
| 每次攻击耗电 | `DrainCharge(0.0005)`（等离子切割器的一半） |
| 可切割肢体 | 保留（同等离子切割器） |
| 电池 | Large（300） |
| 合成配方 | 无（同等离子切割器） |

## 配方

制作等级 **INT 17**（与增强钛镐相同），两种版本按是否安装 EnhancedTools 二选一注册：

**普通版（无 EnhancedTools）**

- 重型钻机 ×1
- 厚钛板（titaniumslab）×1
- 中型电池（≥25%）×1
- 电路板（≥80%）×2
- 一捆导线 ×3
- 铁钉 ×1
- 钛棒（titaniumrod）×1
- 生化流体 50mL
- 可捶打性质 ×1、可切割性质 ×1

**ET 版（安装 EnhancedTools）**

- 材料同上；
- 可捶打性质 ×1 → **工程化性质 ×1**
- 可切割性质 ×1 → **可改造性质 ×4**

## 安装

把整个 `DS Tool Extension` 文件夹放进 `BepInEx/plugins/`，保持以下相对位置：

```
BepInEx/plugins/DS Tool Extension/
├─ DS Tool Extension.dll
├─ zh-CN.json
├─ EN.json
└─ images/Experimental Ti-alloy drill.png
```

前置：**CUCoreLib**（必需）；多人时主机与客机安装相同版本。

## 中英文本

`zh-CN.json` / `EN.json` 按 CCL 的本地化覆盖格式：

```json
{ "item": { "<id>": "名称", "<id>dsc": "描述" } }
```

CCL 在 `Locale.LoadLanguage` 后自动加载（与 DLL 同目录），本插件随后把物品文本更新为当前语言。

## 维护资料

[AGENTS.md](AGENTS.md) · [CHANGE.md](CHANGE.md)

代码采用 [MPL-2.0](LICENSE)，其它内容和第三方归属见 [LICENSE-ASSETS](LICENSE-ASSETS) 与 [NOTICE](NOTICE)。
