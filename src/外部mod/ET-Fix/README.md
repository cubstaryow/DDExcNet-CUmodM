# ET-Fix · ET 加热笼修复 + 无电池修复 v1.7.4

尝试修正 Enhanced Tools 加热笼的开关行为：

- 开启时正常消耗耐久，可作为合成热源。
- 关闭时停止自然腐坏，不再作为通用合成热源。
- 每只笼子独立处理，不回填已损失耐久，不改变原配方消耗。
- 可随正常存档记住开关，并在多人游戏中向主机确认最终状态。

## 无电池修复（增强钛砍刀 / 增强钛镐）

- 没有电池时也能使用（不改 `condition`，仍显示 0%）；
- 装上电池后自动重新点亮工具灯；
- 可通过配置「额外物品 id」追加其它物品。

需要安装 Enhanced Tools 与 CUCoreLib；联机确认还需Together。缺少对应模组或接口时关闭相应功能；补齐前置并重启后可手动重新开启。

## 安装与升级

将新版 `ET-Fix.dll`放进游戏的 `BepInEx/plugins/`或其子目录。**旧 `CageFix.dll`必须先移除，只保留一份**，不要把旧文件简单改名后当作新版。多人建议各端相同版本。

原配置文件仍是 `BepInEx/config/com.dden.heatingcagefix.cfg`，无需因改名清空设置。原存档开关字段与联机频道保留。

不需要安装DDEN或DDEN公共层。装有DDEN管理器时自动出现在“其它模组的设置”，由管理器绘制全部配置；没有管理器时可关闭游戏后修改上述cfg。

腐坏、热源、记住状态、联机确认各有开关。构建与离线检查不能代替真实游戏中的灯光、温度、合成、存取、连续切换和多人验收，这些仍需实测。

## 维护资料

[AGENTS.md](AGENTS.md) · [技术细节](docs/features.md) · [CHANGE.md](CHANGE.md) · [检查工具](tests/HeatingCageFixTests/HeatingCageFixTests.csproj)

构建输出使用新名称；本目录 `dist/`内若仍有CageFix旧名历史包，它不代表这次构建，不应当作新版安装。代码采用 [MPL-2.0](LICENSE)，其它内容和第三方归属见 [LICENSE-ASSETS](LICENSE-ASSETS) 与 [NOTICE](NOTICE)。本模组不分发游戏或依赖模组文件。
