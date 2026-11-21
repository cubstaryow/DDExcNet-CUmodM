# agent-lead · DSH 协作者记录

## 2026-11-21 · ET-HeatingCageFix → ET-Fix（dev）

**任务**：把 ET 无电池修复合并进 `src/外部mod/ET-HeatingCageFix`，目录/程序集/插件名统一为 `ET-Fix`，并同步编译脚本与文档。

**改动**：目录重命名为 `ET-Fix`；新增 `ETBattery.cs`（无电池可用 + 装电池点灯）；`Plugin.cs` 增加「EnhancedTools 无电池」配置并安装补丁；`CUmod.slnx`、`scripts/build.sh`、`tests/HeatingCageFixTests`、README/AGENTS/CHANGE/docs 同步；版本 1.7.0。

**验证**：`dotnet build src/外部mod/ET-Fix/ET-Fix.csproj -c Release -p:GameDir=...` 通过（0 警告 0 错误）。

**待办 / 交接**：推送由仓库主人用 GitHub Desktop 执行；真实游戏内仍需验收无电池攻击、装电池点灯、加热笼关闭停腐坏/热源、存档与多人开关。
