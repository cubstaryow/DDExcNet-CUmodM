# DDExcNet 1.7.5 — 安装 / Installation

本包为六个DDExcNet组件：公共层、管理器、MPfix、可选存档编辑器、可选性能跟踪器与可选物品工具（UnitListMgr）。现有DLL/GUID/配置仍使用DDEN名称。ET-Fix与JDI-DDEN独立发布，不含在本包中。

## 中文

1. 已安装BepInEx 5（Windows Mono）。关闭游戏后，将普通ZIP中的DLL放入BepInEx/plugins/DDEN/。Nexus版则将BepInEx文件夹合并到游戏根目录，两种布局二选一，不重复安装。
2. 多人修复目标Together / CasualtiesMP 5.5.4，相关同步功能还需兼容CUCoreLib。缺少前置的功能停用；管理器、编辑器、跟踪器可不使用多人功能。
3. 主机与各客机使用同版。默认F8打开管理器，冲突后看实际配置；控制台输入dden也可打开。
4. 更新替换同名自有文件，保留DDEN.Shared.dll；编辑器、跟踪器和物品工具按需选装。不需要清空配置，不删除用户存档。

v1.7.1已移除并放弃玩家离场物品剔除广播。原方案实测不能解决其他客机掉帧，旧RelayGhostItems配置不再生效；退出背包处理交由原版Together流程，本包不再提供此项修复。

无芯片修复针对主机启用后客机仍有芯片的实际规则差异。当前工作树只保留主机漏算世界倍率的三分支补算，激进上报及穿戴兜底已移除，仍有覆盖边界；名称修复只用游戏字体，缺字显示?。管理器、MPfix 和性能跟踪器支持中英文切换。构建和离线检查不等于真实多人验收。

卸载只移除本包安装的准确DLL，不删除其他模组或存档。

## English

Install BepInEx 5 for Windows Mono. Close the game and copy the six DLLs from the standard archive into BepInEx/plugins/DDEN/. For the Nexus archive, merge its BepInEx folder into the game root. Use one archive layout only. Existing DDEN configuration identities remain usable.

MPfix targets Together / CasualtiesMP 5.5.4. Synchronization-dependent features require compatible CUCoreLib. Use matching DDExcNet versions across peers. Keep DDEN.Shared.dll; the editor, tracker, and UnitListMgr are optional. Open with F8 unless conflict resolution selected another key, or enter dden in the console.

Version 1.7.1 removes and abandons the disconnected-player item-removal broadcast repair after user testing found it ineffective. RelayGhostItems is no longer used. Inventory handling on exit/reconnect is left to Together. This package does not fix the remaining disconnect-related frame drops.

The manager, MPfix, and performance tracker support Chinese and English. The current working tree retains basic host-side world-multiplier repairs only; aggressive reporting and wearable recovery have been removed. Coverage limits remain. Game fonts are used for name repair; missing glyphs appear as ?. Local builds and offline checks are not real multiplayer validation.

Source, documentation and issues: https://github.com/cubstaryow/DDExcNet-CUmodM
Code: MPL-2.0 (LICENSE). Branding/artwork: separate LICENSE-ASSETS terms. Third-party attribution: NOTICE. No game files, external dependencies, ET-Fix, JDI-DDEN, configurations, saves or decompilation backups are bundled.