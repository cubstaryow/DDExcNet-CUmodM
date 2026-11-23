# DS Tool Extension 维护说明

本目录是独立外部模组，继承仓库操作与授权边界；DDEN 内部模块的 Shared 日志、接口和 UI 要求不适用于这里。保持程序集/插件名 `DS Tool Extension`、GUID `com.dstool.extension`、版本 **1.1.1**。不得为接管配置而强制引入 DDEN 公共层。

## 文件入口

- `Plugin.cs`：生命周期、CCL 物品注册、配置与 public static `DdenBridge`（`DdenConfig/DdenTitle/DdenSummary/DdenSetLanguage/DdenLocalize/DdenDocs`）。
- `DrillLoopSound.cs`：重型钻机同款循环音效（`drillLoop`），手持且攻击时播放。
- `zh-CN.json` / `EN.json`：CCL 本地化覆盖（`item` 分节，`<id>` 与 `<id>dsc`）。
- `images/Experimental Ti-alloy drill.png`：物品图标（30×30）。
- [README.md](README.md)：玩家说明；[CHANGE.md](CHANGE.md)：变更记录。
- `bin/`、`obj/`、`dist/` 为本地生成文件，不提交。

## 依赖与配置

- 硬引用 CUCoreLib（必需）；游戏、BepInEx、HarmonyX 由仓库 `Directory.Build.props` 提供；多人为 CCL/Together 既有行为。
- 无 DDEN 时仍可用 BepInEx 配置；已有 DDEN 时通过 L1 桥交配置与双语文本，不实现 `IDdenModule`、不提供 `DdenDraw`。
- 物品注册走 `ItemRegistry.Register`；图标走 `AssetLoader.LoadSpriteFromPluginFolder`；文本走 CCL 的 `LocaleRegistry` + Lang JSON。

## 不变量

1. 物品 id 保持 `ExperimentalTi-alloydrill`；改名需同时改 Lang JSON 的键。
2. 数值以 README 表为准；`useAction` 的 `physicalSwing=false`、`staminaUse=0`、`piercing=false`、`attackAnim=null`、`swingSounds` 空与重型钻机一致。
3. 循环音效只在本端手持且攻击时播放，不向其它端广播；音量/音调渐弱逻辑与原生 `heavydrill` 一致。
4. 中英文本只走 `zh-CN.json` / `EN.json`；`Locale.LoadLanguage` 后更新 `Item.GlobalItems` 的 `fullName/description`，不改配置身份。
5. DDEN 配置页当前标记为实验性（`是 · 尚需游戏内验证`）；双语通过 `DdenLocalize`，不改变 cfg 键。

## 构建与检查

从本目录执行，继承仓库 `Directory.Build.props`；换游戏路径使用 `-p:GameDir=...`：

```powershell
dotnet build "DS Tool Extension.csproj" -c Release -m:1 -nodeReuse:false -p:DeployOnBuild=false
```

真实游戏验收需覆盖：物品出现、攻击数值/耗电、循环音效、中英切换、多人主机/客机。

本文件保留独立模组的技术约束。协作方式见 [根目录 agent.md](../../../agent.md)，个人工作约定按协作者身份选择 `agent/` 中的对应文件。
