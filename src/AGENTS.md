# 源码局部约束

遵守 [根协作约定](../agent.md)，并按协作者身份阅读 `agent/` 中对应的工作说明。DDEN组件版本独立，产品版本与API代际分别维护，见[版本契约](../docs/component-versions.md)；结构与构建见 [development.md](../docs/development.md)。

| 目录 | 职责 |
| --- | --- |
| `DDEN.Shared` | 模块契约、配置绘制、兼容、反射／联机桥、存档编解码与日志 |
| `DDExcNet.CUmodM` | 管理器窗口、路由、输入隔离 |
| `DDEN.LoongNova` | 龙域武库（LoongNova，龙化装备包）：龙灼线束、龙息单元、重力腰带、龙化工具／配方与CCL联机适配 |
| `DDEN.MPfix` | 多人修复、可选主机配置与娱乐分支 |
| `DDEN.SaveEditor` | 存档工作副本、图形化编辑与保存 |
| `DDEN.NetPerfTracker` | 只读性能采样／图表／日志，无Harmony补丁 |
| `DDEN.NetPerfTracker.Enhanced` | 独立可选深层诊断，承载Together／LES／物体更新的纯计时Harmony钩子，默认关闭 |
| `外部mod/ET-Fix` | 独立ET加热笼修复，专属文档及检查在其目录内 |
| `外部mod/JDI-DDEN` | 独立工具撬石果修复，专属文档及检查在其目录内 |
| `外部mod/DS Tool Extension` | 独立工具扩展（CCL 物品），专属文档在其目录内 |

DDEN内部模块引用Shared，实现IDdenModule并在DdenHub登记；所有功能页交给管理器，不再提供独立窗口或独立面板按键。物品生成轮盘是操作入口，保留快捷键。详细日志开关只在管理器绑定；有性能跟踪器时统一使用跟踪器日志通道。统一DdenUI控件、图标比例、输入隔离与日志通道，生命周期／绘制异常留痕。跨Together／QoL与其它DDEN模块用反射或契约，不硬引用其程序集。

独立外部模组不套用内部模块的Shared依赖要求，参照 [ET修复约束](外部mod/ET-Fix/AGENTS.md)，用L1桥交配置即可。

新增或修改功能时先核对 [features.md](../docs/features.md) 的不变量，更新配置短说明、DdenDocs、玩家说明及相关技术文档，记录已验证和未验证范围。保持cfg键／网络协议／存档字段稳定；显示翻译不得改变逻辑身份。管理器、MPfix与性能跟踪器已支持中文／English，显示规范见docs/manager-ui-contract.md；保持旧模块及L1外部配置免检兼容。
