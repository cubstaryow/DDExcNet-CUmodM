# 源码局部约束

遵守 [根协作约定](../agent.md)，并按协作者身份阅读 `agent/` 中对应的工作说明。当前v1.6.0，结构与构建见 [development.md](../docs/development.md)。

| 目录 | 职责 |
| --- | --- |
| `DDEN.Shared` | 模块契约、配置绘制、兼容、反射／联机桥、存档编解码与日志 |
| `DDExcNet.CUmodM` | 管理器窗口、路由、输入隔离 |
| `DDEN.MPfix` | 多人修复、可选主机配置与娱乐分支 |
| `DDEN.SaveEditor` | 存档工作副本、图形化编辑与保存 |
| `DDEN.NetPerfTracker` | 只读性能采样／图表／日志，无Harmony补丁 |
| `外部mod/ET-Fix` | 独立ET加热笼修复，专属文档及检查在其目录内 |

DDEN内部模块引用Shared，实现IDdenModule并在DdenHub登记；只把界面交给管理器，管理器缺席时用DdenUI.DrawStandalone兜底。统一DdenUI控件、图标比例、输入隔离与日志通道，生命周期／绘制异常留痕。跨Together／QoL与其它DDEN模块用反射或契约，不硬引用其程序集。

独立外部模组不套用内部模块的Shared依赖要求，参照 [ET修复约束](外部mod/ET-Fix/AGENTS.md)，用L1桥交配置即可。

新增或修改功能时先核对 [features.md](../docs/features.md) 的不变量，更新配置短说明、DdenDocs、玩家说明及相关技术文档，记录已验证和未验证范围。保持cfg键／网络协议／存档字段稳定；显示翻译不得改变逻辑身份。中文／English切换本轮只提方案，不偷偷加入未完成的语言选项。
