# 工作区、构建与检查

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
src/DDEN.MPreducer/               联机性能对照与检查点保存（原MoreThreadOnMP）
src/外部mod/ET-Fix/      独立外部模组，含自身文档／检查／许可
src/外部mod/JDI-DDEN/    独立工具撬石果模组，含自身文档／许可
tools/                            跨模块及DDEN组件的离线检查
scripts/                          本机构建部署和整合发布脚本
docs/                             当前说明、接入协议、计划与历史索引
docs/history/                     明确过时的历史记录
```

`bin/obj/dist/artifacts/decomp/backup`为本地生成／参考目录，忽略而不上传；不为了整理工作区删除用户的历史包、日志或反编译文件。独立模组的检查工具保存在自己的目录中；其余工具仍按模块分组，避免为移动目录制造重复实现。

项目根目录为L:\github\game\CUmod；反编译参考放decomp，文档放docs，agent为协作说明目录。最终交付文件统一放dist/<版本>/（现有版本目录形如dist/DDExcNet-v1.7.2/），bin/obj仅为构建中间目录。当前发布脚本仍会将ZIP和发布说明写到dist根目录，后续由发布agent调整或整理；不能把目录约定写成脚本已完成的行为。

## 本机环境

- 游戏：`L:\SteamLibrary\steamapps\common\Casualties Unknown`；路径可通过 `Directory.Build.props`或 `-p:GameDir=...`覆盖。
- Unity 2022.3.62f3／Mono是引擎与运行时信息，不是游戏产品版本号。目标netstandard2.1，BepInEx 5、游戏自带HarmonyX和Newtonsoft.Json。
- .NET SDK 10使用 `DDExcNet.slnx` 构建六个发布组件；`CUmod.slnx` 是包含外部模组的整仓开发入口。Shared以外的插件不得硬引用Together或QoL；ET-Fix、JDI-DDEN还不得硬引用DDEN公共层；外部模组的具体依赖由各自项目说明维护，本次不作校对。
- 当前联机核对目标为Together 5.5.5、CUCoreLib 1.1。只有对本机安装文件及明确测试过的接口作结论，更新后重新检查。

## 只构建

从仓库根目录执行：

```powershell
dotnet build DDExcNet.slnx -c Release -m:1 -nodeReuse:false -p:DeployOnBuild=false
```

独立MPreducer不加入上述六组件发布包；单独构建使用`dotnet build src/DDEN.MPreducer/DDEN.MPreducer.csproj -c Release -m:1 -nodeReuse:false -p:DeployOnBuild=false`，开发解决方案`CUmod.slnx`也已同步新项目路径。

`DeployOnBuild`默认false；单纯构建不应写游戏目录。中文路径项目与嵌套检查项目已纳入解决方案／相对引用。DDEN组件均在各自csproj维护独立版本，不再使用DdenVersion统一属性：Shared 2.0.1（API v1）、管理器2.1.0、MPfix 1.8.0、性能跟踪器2.3.1、增强0.2.1-alpha、存档编辑器2.0.1-alpha、UnitListMgr 0.1.2-alpha、LoongNova 1.0.1。Alpha的加载器声明使用数字版2.0.0，发行元数据及模块显示保留alpha；外部模组版本不变。产品版本、两个适配最低要求和API代际见[版本契约](component-versions.md)。

## 部署与改名迁移

`build.sh`是Bash脚本，不能用 `pwsh scripts/build.sh`运行。在Git Bash等Bash环境执行：

```bash
./scripts/build.sh Release
./scripts/build.sh Release --no-saveeditor
```

`scripts/build.sh` 默认部署五个DDEN产物以及 ET-Fix、JDI-DDEN 和 DS Tool Extension。UnitListMgr 随发布包提供；`--no-saveeditor`只撤下已知的自有编辑器文件。**等待用户游戏退出后再部署，不为部署强制结束进程**，不造插件备份，不操作其它作者插件。部署目录为 `BepInEx/plugins/DDEN/`，只准按自有准确文件名处理。

CageFix现名 `ET-Fix.dll`。原GUID `com.dden.heatingcagefix`、cfg、存档键与网络频道保留。部署脚本在DDEN目录移除已知旧名 `CageFix.dll`；若曾在plugins其它位置独立安装，用户需关闭游戏后移除那里那份旧文件。不全场扫描后删除未知DLL。

DDExcNet发布包仅包含六个DDEN产物和四份说明／许可；ET-Fix与JDI-DDEN不随包发布。普通包README取自独立安装说明，避免包内出现不存在的仓库文档链接。配置、存档、第三方依赖和游戏文件不打包；详情见 [工作流程](workflow.md)。

## 离线检查

通用流程先显式构建，再用 `--no-build`运行；这样便于控制串行构建并清楚区分编译与检查结果：

```powershell
dotnet build tools/DamageRepairTests/DamageRepairTests.csproj -c Release -m:1 -nodeReuse:false -p:DeployOnBuild=false
dotnet run --no-build --project tools/DamageRepairTests/DamageRepairTests.csproj -c Release
```

| 工具 | 覆盖范围 |
| --- | --- |
| `DamageRepairTests` | 当前仅检查保留的队列／身份索引辅助类型；不再验证已移除的AttackReport或实际伤害倍率结算 |
| `MPfixSafetyTests` | 生成兜底、依赖、身份与名称安全约束 |
| `HostAuthorityTests` | 临时配置层、本机恢复与有界同步 |
| `ManagerPolicyTests` | F11向前避让、F12保留、真实BepInEx旧配置清理、统一日志出口／详细程度、模块默认开关和实际版本声明 |
| `ChineseHookTests/run-netfx.ps1` | 使用真实Harmony和独立假方法验证中文Get的只读引用参数、UI标签出参和字体回调；不运行游戏 |
| `NameFontTests` | 游戏字库策略、Unicode缺字与恢复 |
| `PerfEnhancerTests` | 完整帧计时账本、缺失方法、异常结束／嵌套计时、反射桥及本机Together入口；构建增强模块后运行 |
| `PerfEnhancerHookTests` | 用Windows .NET Framework运行真实Harmony假方法检查：调用顺序、异常原对象传播、其他补丁跳过决定、仅撤下自身钩子；运行 `tools/PerfEnhancerHookTests/run-netfx.ps1`，不使用CoreCLR `dotnet run` |
| `PerfTrackerTests` | 性能采样、分帧清点、告警与日志逻辑，切换语言保留事件和原始日志证据 |
| `ManagerLocalizationTests` | 双语目录／格式参数、原配置身份及控件、规范化显示标记、旧L1反射签名、外部语言桥、导航与折叠高度；GUI边界模拟 |
| `EditorTests` | 原始字段保留、物品附加属性、容量与保存边界 |
| `src/外部mod/ET-Fix/tests/HeatingCageFixTests` | 腐坏／配方／保存／网络状态与实际L1扫描，Unity和传输边界模拟 |

检查程序可能读取本机已安装程序集，但真实存档测试必须显式说明输入、只读或副本行为。不能默认把用户存档当写入测试夹具。检查依赖的Shared仅用于验证管理器接入，不会成为外部模组的发布依赖。

## 游戏内与多人验收

已有自检入口是 `BepInEx/config/cumod.selftest`、`cumod.uitest`、`cumod.cfgtest`。这些会让插件执行自检或自动翻页面；仅在用户授权的测试实例中使用，完成后清除标记，不能把它们留给日常游戏。

离线通过后仍须真实双端验证：主机开无芯片／客机仍有芯片的复现与同步；x3及低倍率、不同种族／力量的三条近战分支；名称游戏字库。离场物品剔除广播已放弃，不作为待验收功能；外部ET笼子由独立模组维护者单独验收。记录主客机版本、开关、复现步骤与日志；不以菜单无异常代替多人验收。
