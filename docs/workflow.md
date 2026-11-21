# 提交、推送与本地发布 · v1.7.1

## 分支和授权

`dev`用于当前开发，`master`用于已验收内容；远端为`origin`，操作前核对实时状态。本轮用户授权文档校对、v1.7.1本地构建和普通推送`origin/dev`，不包含合并master、强推、创建tag或线上Release。

只暂存本次相关文件，不使用无差别add、reset/clean，不覆盖其他协作者工作。反编译、备份、bin/obj/dist和真实存档均不进提交。`agent/`内其他协作者说明不擅自改写。

## 只构建、不部署

```powershell
dotnet build DDExcNet.slnx -c Release -m:1 -nodeReuse:false -p:DeployOnBuild=false
```

`DDExcNet.slnx`只含五个自有发布组件；`CUmod.slnx`还含独立ET-Fix与JDI-DDEN。外部模组保持各自版本，不因DDExcNet版本提升而重发。

`scripts/build.sh`会部署五个DDEN组件和两个外部模组，不能用于本轮“游戏不动、不部署”的任务。本轮不启动／停止游戏，不改插件、配置、存档或自检标记。检查程序只使用模拟输入和临时样本。

## 本地打包

版本基准为Directory.Build.props的DdenVersion，同时核对四个BepInPlugin字面版本。目前1.7.1，不使用旧包哈希或文件名代替新构建。

```powershell
pwsh scripts/release.ps1
```

默认以DeployOnBuild=false构建五个组件，并生成`dist/DDExcNet-v1.7.1.zip`和`dist/RELEASE-v1.7.1.md`。包内恰为五个DLL、README、LICENSE、LICENSE-ASSETS与NOTICE；README来自`docs/release/INSTALL.md`，不带损坏的仓库相对链接。不含ET-Fix、JDI-DDEN、第三方依赖、游戏文件、配置、存档或decomp备份。

已显式完成本轮构建后，可用`-SkipBuild`打包同一批Release产物。脚本核对程序集版本；交付前还须检查插件元数据、ZIP字节和SHA-256。`-Upload`或`-OpenPage`是另外的线上操作，本轮不使用。

Nexus参考资料保存在本地`dist/deliverables/`，不提交Git。保留v1.7.0历史资料，另提供1.7.1更新；其主页不得继续宣传离场物品剔除。资料ZIP不是Main File，实际安装包从本轮最终普通ZIP转换，五个DLL须逐字节一致。

## 已撤回功能与历史记录

用户实测玩家离场物品剔除广播无效，v1.7.1移除并放弃该项修复。原功能仅备份在`decomp/backup-ghostitems/`；不编译、不打包、不提交，也不把它列入后续验收。原版背包保存／重连流程由Together负责。

旧版本发布包、测试结果和文案仅供追溯。保留历史记录并加过时提示，不将旧功能描述套用到当前版本。ET改名历史及独立安装说明见各自模块文档。