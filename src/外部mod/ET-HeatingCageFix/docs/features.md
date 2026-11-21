# ET-HeatingCageFix 修复细节（v1.6.0）

独立程序集 `ET-HeatingCageFix.dll`，插件GUID沿用 `com.dden.heatingcagefix`；修复在v1.5.0阶段独立实现，没有复制反编译参考DLL的代码，当前v1.6.0文档轮次只改名与说明。
仅引用游戏、BepInEx、Harmony与JSON库；不硬引用DDEN.Shared、EnhancedTools、CUCoreLib或Together。
ET/CCL缺失或接口不匹配时功能关闭，配置桥仍可被管理器发现；缺少Together时仅关闭联机确认。

## 1. 按实例修正腐坏与热源资格

ET加热笼注册缺少其自定义灯光控制器，原`SetActive`无法通过该控制器更新腐坏倍率。
本修复在`HeatingCageFire.Awake/SetActive`后登记实际实例，并在`Item.Start`后处理物品ID最终赋值与Awake先后顺序，再按其`active`字段将`Item.decayMultiplier`设为开1/关0。
原版与Together的腐坏公式都使用该倍率，所以不通过返回false阻挡其它前缀，也不冻结或回填`condition`。
关闭功能时仅恢复仍由本修复控制的倍率；其他模组后续改动不覆盖。模板克隆中已有但尚未初始化的辅助组件会重新绑定。

对非液体、非指定物品的`heatsource`配方请求，给`RecipeItem.GetMatchingItem`提供一份排除了关闭笼子的临时排除列表；
保留原候选池、原排除列表和其它热源，继续由游戏挑选可用材料。`DoesUseItemType`同步处理用途显示。
不改变共享`ItemInfo.qualities`，不影响另一只笼子，不过滤指定加热笼的维修/材料配方；开启后的合成成本仍由原版/MP扣除。
没有删除ET组件，也没有按0.25秒丢弃快速开关输入。

## 2. 保存与联机确认

在CCL的`BuiltInItemRuntimeSaveProvider.Capture/Restore`后，给加热笼的原JObject附加`cagefix:{version:1,on:bool}`，其它字段保留。
非对象型的未知提供者结果保持原样；旧存档没有该键时沿用ET原有默认状态。使用游戏正常保存流程，不自行改写存档文件。
恢复后在CCL的Defer阶段再次应用，避免灯光恢复顺序覆盖；用实例修订号避免延迟恢复覆盖之后发生的玩家切换。

联机使用CCL的两个独立小频道：`cagefix.state.get.v1`只查询主机状态，`cagefix.state.notice.v1`只通知需要重新确认。
保留Together原来的物品使用与校验流程；修复不另行执行一次Toggle。主机回复最终布尔状态，客户端应用SetActive，避免盲目翻转。
通知即使由客机转发也不能直接改状态，只有请求主机的对应回复可应用；主机不接受客户端提交开关值。
请求带随机nonce；回复检查当前连接、世界/层、物品ID与代次、原绑定请求对象，丢弃过期回复。退出或生成世界时不应用。
每包至多128个笼子，查询间隔至少0.5秒，无回复最多3次；稳定状态不轮询、不发送整个CCL定义快照。只遍历已登记笼子。
实际双端的连续切换、晚加入、换层与重连仍需联机验证，不能将离线模拟当作实测通过。

## 3. DDEN配置接管

`HeatingCageFix.DdenBridge`通过public static `DdenConfig/DdenTitle/DdenSummary`交配置，不实现DDEN模块接口、不提供DdenDraw，完全交给通用配置页。
新增可选L1约定`DdenUnavailable(ConfigEntryBase)`，返回原因即可禁用对应配置；桥中所有类型均来自BepInEx，外部模组无需Shared引用。
Shared侧适配为原有配置策略接口，旧模组没有这个成员时保持原行为。外部摘要最多每秒刷新一次。

管理器通用入口与滚动修复见 [DDEN功能文档](../../../../docs/features.md)。

产品／程序集已从CageFix改名，原GUID、cagefix存档节点和频道保持不变。安装只留一份新DLL；旧包和旧参考DLL不是本次构建，不随改名而重新部署。当前检查结论见上级CHANGE.md。
