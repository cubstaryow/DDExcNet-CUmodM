# ET-HeatingCageFix 维护说明

本目录是独立外部模组，继承仓库操作与授权边界；DDEN内部模块的Shared日志、接口和UI要求不适用于这里。保持程序集/插件名 `ET-HeatingCageFix`、GUID `com.dden.heatingcagefix`、版本 **1.6.0**。不得为接管配置而强制引入DDEN公共层。

## 文件入口

- `Plugin.cs`：生命周期、依赖检查、配置与public static `DdenBridge`。
- `CageRuntime.cs`：登记单只笼子、原生开关、腐坏倍率与恢复修订号。
- `CagePatches.cs`：热源匹配和CCL物品保存/恢复后缀。
- `CageRules.cs`、`CageWire.cs`：保存字段与严格网络消息校验。
- `CageNetwork.cs`：主机状态查询、通知合并、过期回包隔离与有界重试。
- [README.md](README.md)：玩家说明；[docs/features.md](docs/features.md)：证据与实现；[CHANGE.md](CHANGE.md)：独立变更记录。
- `tests/HeatingCageFixTests/`：离线检查。`bin/`、`obj/`、`dist/`为本地生成文件，不提交。

## 依赖与配置

- 只硬引用游戏、BepInEx、HarmonyX与JSON；ET、CUCoreLib、Together使用延迟反射，缺失时停用对应功能并显示原因。
- 无DDEN时仍可用BepInEx配置；已有DDEN时通过 `DdenConfig/DdenTitle/DdenSummary/DdenUnavailable` 交配置，不实现 `IDdenModule`，不提供 `DdenDraw`。
- 桥的类型只使用BepInEx已有类型。可选DDEN说明注册只访问已加载Shared，不主动加载它。
- 测试项目引用Shared是为核验外部扫描，不代表发布DLL依赖Shared。接入协议见 [管理器文档](../../../docs/cumodm-integration.md)。

## 不变量

1. 仅对单只加热笼写 `decayMultiplier`，开1/关0；不冻结、不回填 `condition`，保留正常损伤与合成消耗。关闭修复时只恢复仍由自己控制的倍率。
2. 不改共享 `ItemInfo.qualities`。只从通用非液体热源匹配中排除关闭的笼子；保留其它热源、原始候选池、原排除表和指定加热笼维修配方。
3. ET Awake/SetActive和Item.Start共同处理初始化顺序及模板克隆，不删除原组件，不丢弃快速切换。
4. 保存只在CCL现有提供者结果中附加版本化 `cagefix` 字段，未知字段保留；不直接写存档。延迟恢复必须检查实例修订号。
5. 联机保留原生使用流程，不再执行第二次Toggle。客户端只查询主机最终布尔状态；通知不能直接改状态，主机不接受客机上报开关值。
6. 回复必须检查nonce、连接、世界/层、ID/代次、原请求对象。每包至多128项、查询间隔至少0.5秒、无回复最多3次；稳定时不轮询，不扫描全场，不发送完整CCL快照。
7. 生命周期异常留痕；卸载只恢复自己持有的改动。反编译只读结论，不复制实现；原 `decomp/CageFix.dll` 是参考文件，不修改或部署。

## 构建与检查

从本目录执行，继承仓库 `Directory.Build.props`；换游戏路径使用 `-p:GameDir=...`：

```powershell
dotnet build ET-HeatingCageFix.csproj -c Release -m:1 -nodeReuse:false -p:DeployOnBuild=false
dotnet build tests/HeatingCageFixTests/HeatingCageFixTests.csproj -c Release -m:1 -nodeReuse:false -p:DeployOnBuild=false
dotnet run --no-build --project tests/HeatingCageFixTests/HeatingCageFixTests.csproj -c Release
```

检查自动构建ET-HeatingCageFix和作为集成验证对象的Shared。DLL检查路径由构建元数据提供，不依赖执行目录。需本机游戏及ET/CCL安装文件；不随模组分发这些依赖。

77项离线检查覆盖真实业务逻辑及改名后的程序集／插件身份，但Unity、传输与反射边界使用模拟，不能当作Unity/Mono实际补丁执行或双机验收。游戏内检查开关、灯光/温度/腐坏、热源合成、两只不同状态笼子、存取、连续切换、晚加入、换层和重连。

本文件保留独立模组的技术约束。协作方式见 [根目录agent.md](../../../agent.md)，个人工作约定按协作者身份选择 `agent/` 中的对应文件。部署、推送等操作遵守用户授权；仅处理准确列出的自有文件，不造插件备份，不碰其他模组。

改名必须保留原GUID／cfg／存档键／频道，旧CageFix.dll与新ET-HeatingCageFix.dll不能同时安装；namespace为HeatingCageFix，配置桥全名为HeatingCageFix.DdenBridge。
