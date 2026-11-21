# JDI-DDEN（工具撬石果）

- 插件名：`JDI-DDEN`
- GUID：`com.dden.jdi`
- 版本：1.4.0
- 公司：MornJor
- 文件：`JDI-DDEN.dll`

替代原版 `StonefruitOpener` 的独立实现：把带 `tool` 标签的物品拖到背包/容器里的封闭石果（`stonefruitclosed`）上，即可打开。

## 功能

- 扣 6 点体力；
- 封闭石果消失，生成开口石果（`stonefruitopen`）；
- 开口石果放回原容器/背包（放不进就自动拾取）；
- 单机、主机、客机都生效。

## 多人

- 客机：拦截拖拽，取石果的 `SyncInfo.syncId`，通过 Together 频道 `unwey.stonefruit.open` 请求主机；
- 主机：按 syncId 找到物品，在自己的端执行转换，Together 自动同步；
- 单机/主机本地：直接执行。

需要 Together / CasualtiesMP（软依赖）。主机和客机都装这一份。

## 安装

1. 从 `BepInEx/plugins` 删除旧的 `工具撬开石果-StonefruitOpener.dll`（以及旧版 `StonefruitMPFix.dll` / `Just drag a Tool on it.dll`）；
2. 放入 `JDI-DDEN.dll`；
3. 重启游戏。

## 配置

配置交给 DDEN 管理器渲染（`F8` →「其它模组的设置」，分组 `JDI-DDEN（工具撬石果）`）；没有管理器时编辑 `BepInEx/config/com.dden.jdi.cfg`。

| 配置项 | 默认 | 说明 |
|---|---|---|
| `General/Enabled` | true | 总开关 |
| `Multiplayer/AllowClients` | true | 允许客机请求主机撬石果（主机也要装本插件） |

## 维护资料

[AGENTS.md](AGENTS.md) · [技术细节](docs/features.md) · [CHANGE.md](CHANGE.md)

代码采用 [MPL-2.0](LICENSE)，其它内容和第三方归属见 [LICENSE-ASSETS](LICENSE-ASSETS) 与 [NOTICE](NOTICE)。
