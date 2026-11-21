# CageFix v1.5.0

尝试修复 Enhanced Tools 加热笼的开关行为：

- 开启时正常消耗耐久，可作为合成热源。
- 关闭时停止自然腐坏，不再作为合成热源。
- 每只笼子独立处理，不回填已经损失的耐久，不改变原配方消耗。
- 可随正常存档记住开关，并在多人游戏中向主机确认最终状态。

需要安装 Enhanced Tools 与 CUCoreLib。把 `CageFix.dll` 放进游戏的 `BepInEx/plugins/` 或其子目录即可；多人建议主客机都安装相同版本。

不需要安装 DDEN 或 DDEN 公共层。装有 DDEN 管理器时，它会自动出现在“其它模组的设置”，由管理器显示全部配置。
没有管理器时，可关游戏后编辑 `BepInEx/config/com.dden.heatingcagefix.cfg`。

腐坏、热源、记住状态与联机确认各有开关。缺少对应模组或接口时功能会关闭；补齐前置并重启后，可以手动重新开启。

本版通过了构建及离线检查，真实游戏中的灯光、温度、合成、存取和多人同步仍需实测。

独立安装包位于本目录的 `dist/CageFix-v1.5.0.zip`。

维护资料：[AGENTS.md](AGENTS.md)、[修复细节](docs/features.md)、[变更记录](CHANGE.md)。检查工具也保存在本目录。
