# 工作区、构建与检查 · v1.7.1

## 目录职责

```text
README.md                         玩家入口
CHANGE.md                         当前版本修改与验证
agent.md                          多协作者公共协作说明
AGENTS.md                         工具发现入口与简短索引
agent/agent-<协作者标识>.md         不同协作者的agent工作说明
src/DDEN.Shared/                  DDEN公共层
src/DDExcNet.CUmodM/               管理器
src/DDEN.MPfix/                    多人修复
src/DDEN.SaveEditor/               存档编辑器
src/DDEN.NetPerfTracker/           性能跟踪器
src/外部mod/ET-Fix/      独立外部模组，含自身文档／检查／许可
src/外部mod/JDI-DDEN/    独立工具撬石果模组，含自身文档／许可
tools/                            跨模块及DDEN组件的离线检查
scripts/                          本机构建部署和整合发布脚本
docs/                             当前说明、接入协议、计划与历史索引
docs/history/                     明确过时的历史记录
```

`bin/obj/dist/artifacts/decomp/backup`为本地生成／参考目录，忽略而不上传；不为了整理工作区删除用户的历史包、日志或反编译文件。独立模组的检查工具保存在自己的目录中；其余工具仍按模块分组，避免为移动目录制造重复实现。

## 本机环境

- 游戏：`L:\SteamLibrary\steamapps\common\Casualties Unknown`；路径可通过 `Directory.Build.props`或 `-p:GameDir=...`覆盖。
- Unity 2022.3.62f3／Mono是引擎与运行时信息，不是游戏产品版本号。目标netstandard2.1，BepInEx 5、游戏自带HarmonyX和Newtonsoft.Json。
- .NET SDK 10使用 `DDExcNet.slnx` 构建五个发布组件；`CUmod.slnx` 是包含外部模组的整仓开发入口。Shared以外的插件不得硬引用Together或QoL；ET-Fix、JDI-DDEN还不得硬引用DDEN公共层；JDI-DDEN对Together/CCL使用软依赖。
- 当前联机核对目标为Together 5.5.4、CUCoreLib 1.1。只有对本机安装文件及明确测试过的接口作结论，更新后重新检查。

## 只构建

从仓库根目录执行：

```powershell
dotnet build DDExcNet.slnx -c Release -m:1 -nodeReuse:false -p:DeployOnBuild=false
```

`DeployOnBuild`默认false；单纯构建不应写游戏目录。中文路径项目与嵌套检查项目已纳入解决方案／相对引用。五个发布组件版本由 `DdenVersion`统一（外部模组保持各自项目版本），目前1.7.1；各插件 `BepInPlugin`中的字面版本也要同步，不能只改props。

## 部署与改名迁移

`build.sh`是Bash脚本，不能用 `pwsh scripts/build.sh`运行。在Git Bash等Bash环境执行：

```bash
./scripts/build.sh Release
./scripts/build.sh Release --no-saveeditor
```

默认部署五个DDEN产物、ET-Fix及JDI-DDEN；`--no-saveeditor`只撤下已知的自有编辑器文件。**等待用户游戏退出后再部署，不为部署强制结束进程**，不造插件备份，不操作其它作者插件。部署目录为 `BepInEx/plugins/DDEN/`，只准按自有准确文件名处理。

CageFix现名 `ET-Fix.dll`。原GUID `com.dden.heatingcagefix`、cfg、存档键与网络频道保留。部署脚本在DDEN目录移除已知旧名 `CageFix.dll`；若曾在plugins其它位置独立安装，用户需关闭游戏后移除那里那份旧文件。不全场扫描后删除未知DLL。

DDExcNet发布包仅包含五个DDEN产物和四份说明／许可；ET-Fix与JDI-DDEN不随包发布。普通包README取自独立安装说明，避免包内出现不存在的仓库文档链接。配置、存档、第三方依赖和游戏文件不打包；详情见 [工作流程](workflow.md)。

## 离线检查

通用流程先显式构建，再用 `--no-build`运行；这样便于控制串行构建并清楚区分编译与检查结果：

```powershell
dotnet build tools/DamageRepairTests/DamageRepairTests.csproj -c Release -m:1 -nodeReuse:false -p:DeployOnBuild=false
dotnet run --no-build --project tools/DamageRepairTests/DamageRepairTests.csproj -c Release
```

| 工具 | 覆盖范围 |
| --- | --- |
| `DamageRepairTests` | 伤害报告格式／可信边界、队列预算和任务顺序 |
| `MPfixSafetyTests` | 生成兜底、依赖、身份与名称安全约束 |
| `HostAuthorityTests` | 临时配置层、本机恢复与有界同步 |
| `NameFontTests` | 游戏字库策略、Unicode缺字与恢复 |
| `PerfTrackerTests` | 性能采样、分帧清点、告警与日志逻辑 |
| `EditorTests` | 原始字段保留、物品附加属性、容量与保存边界 |
| `src/外部mod/ET-Fix/tests/HeatingCageFixTests` | 腐坏／配方／保存／网络状态与实际L1扫描，Unity和传输边界模拟 |

检查程序可能读取本机已安装程序集，但真实存档测试必须显式说明输入、只读或副本行为。不能默认把用户存档当写入测试夹具。检查依赖的Shared仅用于验证管理器接入，不会成为外部模组的发布依赖。

## 游戏内与多人验收

已有自检入口是 `BepInEx/config/cumod.selftest`、`cumod.uitest`、`cumod.cfgtest`。这些会让插件执行自检或自动翻页面；仅在用户授权的测试实例中使用，完成后清除标记，不能把它们留给日常游戏。

离线通过后仍须真实双端验证：主机开无芯片／客机仍有芯片的复现与同步；x3及低倍率、不同种族／力量的三条近战分支；开关关闭、激进开启／拒绝回退；名称游戏字库。离场物品剔除广播已放弃，不作为待验收功能；外部ET笼子由独立模组维护者单独验收。记录主客机版本、开关、复现步骤与日志；不以菜单无异常代替多人验收。
