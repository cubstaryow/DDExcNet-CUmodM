# 提交、推送与本地发布 · v1.6.0

## 分支和授权

`dev`用于当前开发，`master`用于已验收内容。当前远端为 `origin`；每次操作前核对实际状态，不把文档当作实时分支状态。

用户本次已授权整理完成后push：检查改动、运行适当验证、仅暂存本次文件并提交，然后普通推送 `origin/dev`。不要把此授权延伸为自动合并master、force push、改远端、创建tag或发布Release。遇到远端新增提交先检查差异，不能覆盖它。

不要默认 `git add -A`收进全部内容，也不用 `reset --hard/clean`解决工作区冲突。文档、代码、资源与生成产物分清；反编译、依赖、bin/obj/dist与真实存档不进提交。

## 本地构建与验收

先按 [development.md](development.md)只构建并执行相关离线检查，再按需要在获得授权的游戏实例内验收。`build.sh`用Bash运行，不用PowerShell解释脚本内容。默认部署全部五个DDEN产物和独立ET-HeatingCageFix；只想不带编辑器用 `--no-saveeditor`。

原生／Harmony执行、视觉效果与真实双机联机需独立记录。主机开启无芯片后客机规则一致、不同角色近战与x3倍率、退出／重连物品、缺依赖禁用、主机配置恢复等均不能靠菜单自检替代。

## 打包与发布

版本基准是 `Directory.Build.props`中的DdenVersion；同步各BepInPlugin版本与文档。当前1.6.0，不能用旧包文件名或旧哈希代表本次构建。

从仓库根目录执行：

```powershell
pwsh scripts/release.ps1
```

默认构建Release，组装 `dist/DDEN-v1.6.0.zip`与发布说明；不上传。包内是五个DDEN产物、`ET-HeatingCageFix.dll`及README／LICENSE／LICENSE-ASSETS／NOTICE，不含游戏依赖、配置和存档。发布正文来自 [release-page.md](release-page.md)。

脚本需要本地git历史生成变更说明；`-Upload`才会创建线上Release并上传，`-OpenPage`打开发布页面。它们是独立动作，本次push授权不自动包含线上发布。桌面工具能力可能变化，不用它的限制推断发布已经发生。

本地构建需要商业游戏及已安装前置的程序集；不能把这些依赖上传到仓库或公共CI。完整包应在本地检查文件名、版本、依赖和SHA-256。不要承诺固定包大小。

## 改名升级

CageFix改为 **ET-HeatingCageFix**，旧cfg的GUID仍为 `com.dden.heatingcagefix`，原存档键和频道不变。安装时删除旧 `CageFix.dll`，只保留新DLL；脚本只清理DDEN目录内已知旧名，其他独立安装位置手动核对。改名不意味着改变修复算法。

旧包可保留追溯，但它们包含旧名、旧文档和旧版本，不能直接更名后宣称为新包。历史记录见 [history](history/conversation-v1.5.0.md)。
