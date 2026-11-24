# agent-lead · DSH 协作者记录

## 2026-11-21 · ET-HeatingCageFix → ET-Fix（dev）

**任务**：把 ET 无电池修复合并进 `src/外部mod/ET-HeatingCageFix`，目录/程序集/插件名统一为 `ET-Fix`，并同步编译脚本与文档。

**改动**：目录重命名为 `ET-Fix`；新增 `ETBattery.cs`（无电池可用 + 装电池点灯）；`Plugin.cs` 增加「EnhancedTools 无电池」配置并安装补丁；`CUmod.slnx`、`scripts/build.sh`、`tests/HeatingCageFixTests`、README/AGENTS/CHANGE/docs 同步；版本 1.7.0。

**验证**：`dotnet build src/外部mod/ET-Fix/ET-Fix.csproj -c Release -p:GameDir=...` 通过（0 警告 0 错误）。

**待办 / 交接**：推送由仓库主人用 GitHub Desktop 执行；真实游戏内仍需验收无电池攻击、装电池点灯、加热笼关闭停腐坏/热源、存档与多人开关。

## 2026-11-21 · JDI-DDEN 入库（dev）

- 在 `src/外部mod/JDI-DDEN/` 新建外部模组：`Plugin.cs`、`JDI-DDEN.csproj`（$(GameDir)/$(BepInExPluginsDir) 引用）、README/AGENTS/CHANGE/docs、LICENSE 等；
- 更新 `CUmod.slnx`、`scripts/build.sh`（OWNED + 部署 `JDI-DDEN.dll`）、`src/AGENTS.md`；
- 编译验证通过；GUID `com.dden.jdi`、版本 1.4.0 不变。

## 2026-11-23 · DS Tool Extension 入库（dev）

- 在 `src/外部mod/DS Tool Extension/` 新建外部模组：`Plugin.cs`、`DrillLoopSound.cs`、`DS Tool Extension.csproj`、`zh-CN.json`、`EN.json`、`images/`、README/AGENTS/CHANGE 与许可文件；
- 更新 `CUmod.slnx`、`scripts/build.sh`、`src/AGENTS.md`；
- 第一件装备：实验性钛合金钻机（CCL 物品，1.0.1）。
