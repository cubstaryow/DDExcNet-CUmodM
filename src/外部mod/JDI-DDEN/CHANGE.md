# JDI-DDEN 变更记录

## v1.5.0 · 2026-11-22

- 跟随 DDEN 管理器中英双语切换：新增 `DdenSetLanguage` / `DdenLocalize`，翻译配置说明、禁用原因、模块标题与状态；
- 未知文字保留原文，翻译不改变配置身份或取值。

## v1.4.0 · 2026-11-21

- 作为独立外部模组收入仓库 `src/外部mod/JDI-DDEN/`；
- DDEN L1 接入保持软依赖：`DdenConfig/DdenTitle/DdenSummary`，不实现 `IDdenModule`、不提供 `DdenDraw`，由管理器自动渲染可编辑配置；
- 程序集/插件名 `JDI-DDEN`、GUID `com.dden.jdi`、版本 1.4.0 保持一致。

## v1.3.0

- 原 `DDEN JDI` 改名 `JDI-DDEN`，版本 1.3.0；
- 修复 DDEN「单独设置页」改不了配置的问题：移除静态 `DdenDraw`，改为交出 `DdenConfig`。

## v1.2.0

- 增加 DDEN L1 接入（DdenConfig/DdenTitle/DdenSummary/DdenDraw），配置 `General/Enabled`、`Multiplayer/AllowClients`。

## v1.1.0

- 主机处理请求时先按 `id == "stonefruitclosed"` 过滤，减少 syncInfo 查找；
- 缓存 `stonefruitopen` 预制体。

## v1.0.0

- 独立实现「工具撬石果」：单机/主机本地执行 `DoOpen`；客机取 syncId 通过 Together 频道 `unwey.stonefruit.open` 请求主机执行；
- 替代原版 StonefruitOpener。
