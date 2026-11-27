# DDExcNet 1.8.5 — 安装 / Installation

本包为八个 DDExcNet 组件：公共层、管理器、MPfix、性能跟踪器、可选深层诊断、可选存档编辑器、可选单位清单管理器与龙灼线束。
现有 DLL / GUID / 配置仍使用 DDEN 名称。ET-Fix、JDI-DDEN、DS Tool Extension 是独立外部模组，不含在本包中。

## 中文

1. 先装好 **BepInEx 5（win_x64）**，启动一次游戏再关掉，让它生成 `BepInEx/` 目录。
2. 把包里的 dll **一起**放进 `<游戏目录>\BepInEx\plugins\DDEN\`，替换同名文件；不要旧的一份、新的一份同时留着。
3. `DDEN.Shared.dll` 是其它组件的依赖，必须保留。深层诊断、存档编辑器、单位清单管理器、性能跟踪器可按需删掉，其余功能不受影响。
4. 启动游戏，按管理器快捷键（默认 **F11**）打开面板，或在控制台输入 `dden`。与其他模组撞键时管理器会自动换键并保存，以界面显示为准。
5. 联机：主机与每一位客机装同一版本。多人修复需要 Together / CasualtiesMP **5.5.5**；无芯片同步、合成补救、商人快照、主机权威配置还需要 CUCoreLib。
6. 卸载：删掉 `BepInEx/plugins/DDEN/` 里的 dll 即可，不写注册表、不留后台服务。存档编辑器会在你改过的存档旁留 `.cumod.bak` 备份。

## Nexus 分发包

N 网：主包含 DDEN 公共层 / 管理器 / 多人修复 / 性能跟踪器 / 深层诊断 / 存档编辑器 / 单位清单管理器；
**龙域武库（DDEN.LoongNova.dll）单独一个包**，需要时另装，避免重复安装。

## 实验性模块 / Experimental

存档编辑器（DDEN.SaveEditor）、单位清单管理器（DDEN.UnitListMgr）为实验性模块：**主动使用导致存档损坏概不负责**，用前请自行备份存档。
深层诊断（DDEN.NetPerfTracker.Enhanced）同为实验性、默认关闭，只用于排查掉帧，不建议长期开启。

## English

1. Install **BepInEx 5 (win_x64)**, launch the game once and close it so the `BepInEx` folder is created.
2. Put all DLLs into `<game folder>\BepInEx\plugins\DDEN\` and replace same-named files. Do not keep an old copy next to a new one.
3. `DDEN.Shared.dll` is required by the other components. The deep-diagnostics, save editor, unit list manager and performance tracker DLLs are optional; removing them leaves everything else working.
4. Launch the game and press the manager hotkey (default **F11**), or type `dden` in the console. If the key clashes with another mod the manager picks a free one and saves it — use what the compatibility page shows.
5. Multiplayer: host and every client must run the same build. The multiplayer fixes need Together / CasualtiesMP **5.5.5**; chip-rule sync, craft-spawn recovery, trader snapshots and host authority config also need CUCoreLib.
6. Uninstalling: delete the DLLs from `BepInEx/plugins/DDEN/`. No registry entries, no background services. The save editor keeps `.cumod.bak` copies next to saves you edited.

Experimental modules: the save editor and the unit list manager are experimental — using them is at your own risk and we are not responsible for corrupted saves. The deep-diagnostics plugin is experimental and off by default; it only measures, and is not meant to stay enabled.

## 校验 / Verification

```
SHA256(DDExcNet-v1.8.5.zip) = 2c2b0956853e07c081e786f5bcc7e65113619b922f05281f8e2005c3b10aba9a
大小 = 394.9 KB
```
