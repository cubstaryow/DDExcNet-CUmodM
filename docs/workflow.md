# 提交、推送与本地发布

## 分支和授权

`dev`用于当前开发，`master`用于已验收内容；远端为`origin`，操作前核对实时状态。本轮用户仅授权全项目文档校对（排除外部mod）及普通推送`origin/dev`；不构建、不打包、不部署。ChatGPT默认负责代码修复与文档校对，上传／打包／文案编辑交其他agent；本次push是明确的当次授权，不包含合并master、强推、创建tag或线上Release。

只暂存本次相关文件，不使用无差别add、reset/clean，不覆盖其他协作者工作。反编译、备份、bin/obj/dist和真实存档均不进提交。`agent/`内其他协作者说明不擅自改写。

## 只构建、不部署

```powershell
dotnet build DDExcNet.slnx -c Release -m:1 -nodeReuse:false -p:DeployOnBuild=false
```

`DDExcNet.slnx`只含六个自有发布组件；`CUmod.slnx`还含独立ET-Fix与JDI-DDEN。外部模组保持各自版本，不因DDExcNet版本提升而重发。

`scripts/build.sh`会部署五个DDEN组件以及 ET-Fix、JDI-DDEN 和 DS Tool Extension，不能用于本轮“游戏不动、不部署”的任务。本轮不启动／停止游戏，不改插件、配置、存档或自检标记。检查程序只使用模拟输入和临时样本。

## 本地打包

发布版本默认读取 MPfix 项目的 Version。各组件的程序集版本与产品版本按对应项目独立校验。

```powershell
pwsh scripts/release.ps1
```

默认以DeployOnBuild=false构建六个组件，并生成`dist/DDExcNet-v<版本>.zip`和`dist/RELEASE-v<版本>.md`。包内恰为六个DLL、README、LICENSE、LICENSE-ASSETS与NOTICE；README来自`docs/release/INSTALL.md`，不带损坏的仓库相对链接。不含ET-Fix、JDI-DDEN、第三方依赖、游戏文件、配置、存档或decomp备份。

发布agent显式完成对应修订构建后，可用`-SkipBuild`打包同一批Release产物。脚本核对程序集版本；交付前还须检查插件元数据、ZIP字节和SHA-256。`-Upload`或`-OpenPage`是另外的线上操作，本轮不使用。

最终交付统一放`dist/<版本>/`；现有release.ps1仍将ZIP与说明输出到dist根目录，此差异交发布agent修订／整理，本轮不改脚本。旧Nexus参考资料在`dist/deliverables/`仅作历史，不提交Git、不改写旧包；后续文案需与最终代码一致，不得宣传已移除机制。资料ZIP不是Main File，实际安装包从对应修订最终普通ZIP转换，六个DLL须逐字节一致。

提交文档前核对暂存区。若其中含其他agent的源码删除或修改，用显式文档路径的git commit --only提交，保留原源码暂存状态，不混入本次文档提交。文档中明确标识尚未提交的工作树功能；不能把文档push当作相关源码已发布。

## 已撤回功能与历史记录

用户实测玩家离场物品剔除广播无效，v1.7.1移除并放弃该项修复。原功能仅备份在`decomp/backup-ghostitems/`；不编译、不打包、不提交，也不把它列入后续验收。原版背包保存／重连流程由Together负责。

旧版本发布包、测试结果和文案仅供追溯。保留历史记录并加过时提示，不将旧功能描述套用到当前版本。ET改名历史及独立安装说明见各自模块文档。