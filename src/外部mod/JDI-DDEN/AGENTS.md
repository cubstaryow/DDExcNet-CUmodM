# JDI-DDEN 维护说明

本目录是独立外部模组，继承仓库操作与授权边界；DDEN 内部模块的 Shared 日志、接口和 UI 要求不适用于这里。保持程序集/插件名 `JDI-DDEN`、GUID `com.dden.jdi`、版本 **1.5.2**。不得为接管配置而强制引入 DDEN 公共层。

## 文件入口

- `Plugin.cs`：生命周期、依赖检查、配置与 public static `DdenBridge`（`DdenConfig/DdenTitle/DdenSummary`，**不提供 `DdenDraw`**）。
- `docs/features.md`：技术细节（网络频道、客机请求/主机执行、单机路径）。
- [README.md](README.md)：玩家说明；[CHANGE.md](CHANGE.md)：变更记录。
- `bin/`、`obj/`、`dist/` 为本地生成文件，不提交。

## 依赖与配置

- 硬引用游戏、BepInEx、HarmonyX；Together / CasualtiesMP、LiteNetLib 为编译期引用，运行时按软依赖处理（缺失时插件不启用多人请求路径）。
- 无 DDEN 时仍可用 BepInEx 配置；已有 DDEN 时通过 `DdenConfig/DdenTitle/DdenSummary` 交配置，不实现 `IDdenModule`，不提供 `DdenDraw`。
- 桥的类型只使用 BepInEx 已有类型。可选 DDEN 说明注册只访问已加载 Shared，不主动加载它。

## 不变量

1. 不改原版 `Stats.usable` / `condition`；只在 `PlayerCamera.TryPerformInventoryAction` 前缀里处理"工具拖到封闭石果"这一条路径。
2. 单机 / 主机本地直接执行 `DoOpen`；客机只发请求，不在本地生成未登记物品。
3. 客机请求走 Together 自定义频道 `unwey.stonefruit.open`，消息只含 `ushort syncId`；主机校验后按 syncId 找物品。
4. `DoOpen` 顺序保持：扣体力 → 音效 → 记录槽位 → `condition=0` → 生成 `stonefruitopen` → 优先放回原容器，否则 `DropItem` + `PickUpItem`（失败回退 `AutoPickUpItem`）。
5. 开关状态与配置只影响本插件；不修改原版物品定义、不新增网络频道格式、不改存档字段。
6. 改名/升级必须保留 GUID、cfg 键和网络频道；旧插件（StonefruitOpener 等）不得与本插件同时安装。

## 构建与检查

从本目录执行，继承仓库 `Directory.Build.props`；换游戏路径使用 `-p:GameDir=...`：

```powershell
dotnet build JDI-DDEN.csproj -c Release -m:1 -nodeReuse:false -p:DeployOnBuild=false
```

暂无离线测试项目；真实游戏验收需覆盖：单机撬石果、主机撬石果、客机撬石果（主机在线）、无 Together 时降级。

本文件保留独立模组的技术约束。协作方式见 [根目录 agent.md](../../../agent.md)，个人工作约定按协作者身份选择 `agent/` 中的对应文件。
