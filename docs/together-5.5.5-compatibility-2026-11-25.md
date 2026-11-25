# Together 5.5.5 与 CUmod 兼容性报告

> 后续实现（2026-11-25）：MPfix 1.8.0已将环境目标更新为5.5.5并删除客机FixInvSync补丁；补齐bodyName的悬停／医疗显示与中文字体路径。管理器2.1.0、Shared2.0.1同步配置入口和日志通道。下文是修改前的兼容性审计证据，不再代表这些遗漏仍未处理；真实游戏及多人验收仍未执行。


日期：2026-11-25。目标游戏：Casualties Unknown 1.0.2。

**结论：现有 MPfix 1.7.6 不能直接视为已经适配 Together 5.5.5。插件加载接口总体仍在，但默认版本闸门会阻止功能执行；即使绕过总闸门，旧客机重试补丁仍被 MVID 限制阻止，名称显示也有明确的覆盖缺口。不得仅改版本常量后宣称兼容。**

本报告不修改 MPfix、Together、游戏配置或真实存档。用户另行提出的管理器／物品工具提示缩放问题单独处理，不构成对 5.5.5 的适配放行。

## 1. 检查基线与证据等级

| 对象 | 本次检查值 |
| --- | --- |
| 用户下载源码 | `decomp/casualties-together-5.5.5.zip`；包内 `Directory.Build.props:6` 声明 `ModVersion=5.5.5` |
| 源码 ZIP SHA-256 | `876A2E3B453B0728F3D839C2310D597B963B035DF8DB7A68ACEA7B7D6A9C9D7D` |
| 已安装 Together DLL | `5.5.5.0`；MVID `b3b58466-cc0d-42b6-910c-d744dd135db2` |
| 已安装 MPfix | `1.7.6.0`；MVID `e819af00-aff6-450a-a4af-46e87d38fcea` |
| 当前源码组件 | Shared 2.0.0／API v1；MPfix 1.7.6；跟踪器 2.3.0；增强 0.2.0-alpha；存档编辑器 2.0.0-alpha；LoongNova 1.0.0 |
| 语言配置只读检查 | Together `Localization/Language=zh-CN`；管理器 `UI/Language=Chinese`；管理器 `IgnoreEnvVersion=false` |

源码解压到工作区临时目录供只读检查，没有写入 `decomp`。以下 `U/` 指 ZIP 根目录内的 `ScavPrototypeMP/`。版本标签相同不证明源码和二进制逐位相同；本次另用 Mono.Cecil 核对已安装 DLL 的关键签名、字段、读名调用和嵌入语言资源，不执行游戏方法。

用户实测作为独立证据保留：**MP 中文仍有问题；悬停玩家姓名、医疗面板相关姓名仍会中文异常、空白或显示数字／SteamID。** 发布说明的“已修复”不能覆盖这些实测结果。

## 2. 必须优先处理的阻断与缺口

### 2.1 MPfix 总闸门仍只认可 5.5.4

- [DdenGate.cs](../src/DDEN.Shared/DdenGate.cs) 的 `ExpectedMp` 为 `5.5.4`，`VersionOk` 按 Together 版本字符串比较。
- [MPfixPlugin.cs](../src/DDEN.MPfix/MPfixPlugin.cs) 的 `Active` 包含 `!DdenGate.Blocked`；阻断时会自动关总开关并记录恢复标记。
- 因而当前 5.5.5 与默认配置组合会阻止修复执行。**“补丁已挂接”日志不等于修复已生效**：部分钩子先安装，再由运行时开关旁路。
- `ExpectedGame=0.1.0.2` 是旧展示参考，与用户目标 1.0.2 的表示不同；目前真正阻断的是 Together 版本，不是此游戏版本字符串。

结论：需要在实际逐项适配后更新闸门；“忽略版本”不是兼容性修复，也不宜作为整套功能已安全可用的替代证明。

### 2.2 旧客机 FixInvSync 补丁不能直接移植

[InventoryRetryFix.cs](../src/DDEN.MPfix/InventoryRetryFix.cs) 只允许旧 MVID `41a77b40-e022-4cc8-9d10-999992f155c4`。新 DLL 的 MVID 不同，独立补丁会拒绝安装；即使总闸门被忽略，该限制仍有效。这是预期的保护行为。

5.5.5 本身已改变相关逻辑：`U/Optimizinator/Fixes/FixInvSync.cs:313` 起的重试循环增加了 `IsDestroyed`、`LooseByte`、`LeaveGear.OwnerMissing` 检查；源码明确防止无效容器被反复重建。原 Pending／Wait／Fp／Until 结构和 foreach 仍在，已安装 DLL 也仍有一个符合旧注入形状的 Pending 循环，**但结构相似不能证明旧调度策略仍有必要或语义相容**。

建议：先关闭旧客机补丁，实测上游对离场、重连、父容器稍后到达的处理。只有确认仍积压时，才重新审查预算和去重策略；不要只替换 MVID。

### 2.3 名称修复有 bodyName 与医疗面板覆盖缺口

现有 [PlayerNameRepair.cs](../src/DDEN.MPfix/PlayerNameRepair.cs) 对列出的展示方法替换 `ScavPlayer.playerName` 字段和 `NetBody.playerName` getter，**没有通用替换 `NetBody.bodyName`，也没有覆盖以下入口**：

| 用户看到的位置 | 5.5.5 的实际路径 | 当前覆盖情况 |
| --- | --- | --- |
| 鼠标悬停玩家 | `U/Content/NetBody.cs:837`，`DoMouseHoverTooltip` 直接读 `bodyName` | 方法未列入现有名称补丁 |
| 打开他人医疗面板的提示 | `U/UI/UIBullshit.cs:2211`，`UIInGame._UpdateCheckInteractKeybinds` 将 `targetpb.bodyName` 插入 `plrint_req_woundview` | 方法未列入名称补丁；该 bodyName getter 也未被替换 |
| 医疗面板标题 | `U/SyncSystems/Medical.cs:1571`，`PlayerCamera_ToggleWoundView_MultiplayerPatch.WoundView_SetTargetBody` 写 `wv.nameText` | 当前未覆盖该 TMP 标签 |
| 医疗面板查看者列表 | 同文件 `GeneratePlayerList`（约1216、1276行），`WoundView_UpdateView_MultiplayerPatch` 直接写 `plr.playerName` | 方法不在现有 DisplayReads 清单；字体准备也未覆盖该标签 |

这些读名调用已与已安装 5.5.5 DLL 交叉核对。因此，即使玩家头顶或队伍名单正常，也不能推断悬停和医疗面板正常。

还有两个独立问题：

1. `U/Player.cs:553` 的 `ScavPlayer.ApplyNameAndColor` 仍按 `Retro GamingPix` 字库替换缺字；`U/Helpers/AIGeneratedShit.cs:20` 会将缺失字符替换为 `?`。这不是完整 Unicode 名称修复。MPfix 在部分入口保存原始名字，但未覆盖的 bodyName／医疗标签仍可能使用被处理后的文本。
2. `U/Net/SteamworksNet.cs:566` 在 Steam 昵称未获取到时返回 SteamID 字符串；连接和 persona 回调也存在先传 SteamID 的路径。`bodyName` getter（`NetBody.cs:1037`）只对 null 回退，空字符串仍会原样返回；null时的另一路数字回退是同步ID，不一定是SteamID。**字体问题、缺失名字、合法数字ID回退必须分别处理**，不能用一次换字体解释所有现象。

后续适配应统一“仅展示”的姓名解析并覆盖这些入口，保持 SteamID、连接身份、逻辑名和存档身份不变；沿用只借用游戏字体、缺字明确降级的项目规则。不能全局改写 `bodyName` 来影响非玩家 NPC 和逻辑查询。

### 2.4 中文本地化不等于中文玩家姓名

`U/Content/Lang.cs` 新增了独立语言配置、预览和保存处理；本机配置确实为 `zh-CN`。但它不自动修复玩家名字来源，也不保证所有 TMP／IMGUI 标签有中文字形。管理器的中文设置属于 DDEN 自身，不能代替 Together 的语言选择。

对源码和已安装 DLL 资源均作检查：EN 有660项，zh-CN有727项，均无空值；中文仍缺少两项新增规则键：

- `casualtiestogether_rule_name_RagdollImpact`
- `casualtiestogether_rule_desc_RagdollImpact`

这两项会落到英文回退。它们能证明中文资源并未完全对齐，但**不能据此认定用户所有中文异常只有这一个原因**。其它中文 UI 现象还需按具体界面区分语言键、渲染字库和持久化状态；用户已报告的现象继续列为未解决。

## 3. MPfix 功能逐项核对

以下“接口仍在”仅表示签名／字段可解析，不代表当前版本闸门已放行，也不代表完成实机联机验收。

| 功能 | 5.5.5 检查结果 | 判断／建议 |
| --- | --- | --- |
| 无芯片同步 | `WorldMP.RunUnchippedLock`、`ScavPlayer.SetUnchipped`、运行设置入口仍在；`ApplyRunSettings` 仍主要写设置字典 | 保留适配候选；不能从本次更新记录推断此缺陷已消失，需主客机规则对照 |
| 主机权威配置 | 使用自有 CCL 频道、运行时覆盖层；原生角色／连接读取接口仍在 | 暂未发现接口破坏；大厅开局、继续旧档、换图／重连需实测恢复本机设置 |
| 方块伤害倍率 | `ChooseBlockDamage(hostOwnDamage, clientReportedDamage, clientId)` 仍在 | 接口兼容候选，勿把新敌人生命／经验修复等同于世界倍率问题解决 |
| 单位、动物、角色近战倍率 | `DamageBuilding` 和 `DoPVPMeleeDamage` 的参数类型及 Harmony 注入名 `attacker/atk/armstrength` 保留；远端仍有 `ForceOnlyRetrieveAttackInfo` 提前返回，`CalculateAttackPower` 未补世界 attackdamage | 当前补倍率理由仍存在；需x1／x3等条件检验，避免与其它补丁重复乘算 |
| 模组物品模板／合成兜底 | `RecipeResult_SpawnResult(int,RecipeResult,Body)` 和 `rres` 参数仍在；CCL 模板边界既有检查通过 | 作用域可解析；上游新增主机合成智力收益并不等于该兜底应删除，也不能绕过正常合成结果 |
| 商人商品表补发 | `TraderSync.SendTraderState(knetid)` 保留 | 不与迷你桶可出售修复等同；保持默认关闭，按需验证 |
| 商人误报过滤 | `TraderPinLog.LogRefreshFault(PlayerCamera)` 保留 | 接口可用；仍须区分 CCL 可解析物品与真实模板故障，不能笼统压制错误 |
| 实际丢弃物品物理保险 | 钩子仍为游戏 `Body.DropItem(int)`／`DropWearable(Item)` | 上游 Shift+拖包行为改变，必须验证修复没有把正常装包／移动误当丢弃；仍不应全场补碰撞 |
| 名称显示 | 主要签名仍在，但上表所列展示路径未覆盖 | 明确存在功能缺口，优先适配 |
| 客机重试预算／去重 | 独立 MVID 保护拒绝5.5.5；上游已变更离场与死对象处理 | 暂不放行，先验证上游修复 |
| 娱乐额外物品 | `TraderCommit.RunBuy(trader,actor,job)` 与所需参数名保留 | 结构兼容候选，默认关闭；需重验交易只额外生成一次及网络登记 |

## 4. 其余组件与上游新功能

| 范围 | 检查结论 |
| --- | --- |
| 基础跟踪器2.3.0／增强0.2.0-alpha | 既有44个计时入口在本机5.5.5／游戏／LES／CPU优化程序集仍满足签名检查；只读诊断可保留，但挂接不是性能改善证明 |
| 主机物理补步同步优化 | `U/SyncSystems/ObjectSync.cs:248` 在物理步标记水晶、持有物与清醒松散物体；`FlushFrameSamples:330` 集中写入，`U/Net/Net.cs:700` 在 LES 更新前调用。旧 `FixedUpdate` 时间不再代表全部写包工作；现有 `Net.DoUpdate`、`SyncInfo.WriteObjectIntoPacket` 仍能观察，建议未来单列 FlushFrameSamples，不额外套跳步原型 |
| IK／液体优化 | `FixSimRange.cs:188` 有本地3×3的IK门控；`GeneralPatches.cs:607` 有液体休眠／范围过滤。现有探针可观测；与额外CPU剔除模组的组合仍需对照，不能重复应用上游原型 |
| Steam超时／timedcommand | 由 Together 的服务器配置／命令负责；未发现 DDEN 覆盖它们。超时增加不等于解决 CPU 长帧或同步队列积压 |
| movetool／变换同步 | 属于上游新增变换协议；物品工具目前负责生成与属性查询，没有声明完整编辑该协议。大小、旋转后的拾取、枪械／腰带与存档回读需专门验收 |
| 自定义地图／命名 debug 保存 | 上游使用 `mp_debug_save/<name>`、`PreserveSave` 等独立会话状态；现有存档编辑器 `SaveDocument.BuildTargets` 主要枚举 QoL 槽位与 `.mp` bundle，**不应宣称自动支持新命名地图目录**。未测试该目录的编辑／往返保存，禁止用普通多人存档流程覆盖它 |
| LoongNova1.0.0 | 所用枪械校验、合并、退弹、拉栓、射击伤害7个关键入口和8个伤害上下文字段在5.5.5仍在；没有发现这些绑定被移除。该结论不是全模块实测认证：需验证上游炮塔／敌人同步改动下的伤害、重复命中、能量、掉落与飞行状态 |
| 皮肤、清洁、盖革计数、瓶液、钻孔舱、藤蔓、钛片、小游戏、陷阱、情绪及布娃娃 | 主要由上游处理；本轮未发现 MPfix 针对这些同一问题的全面替换，因此不能称存在已证明的冲突，也不能将上游发布说明当作 DDEN 已验收。原生皮肤与独立 Skin Sync 等外部模组的组合不在冻结外部代码的修改范围内 |

## 5. 已做检查与边界

- ZIP版本、归档哈希、已安装 DLL版本／MVID核对。
- 关键类型、字段、方法返回值、参数类型及 Harmony 按名注入参数的只读检查。
- 已安装 DLL 中悬停、医疗提示、医疗标题对 `bodyName` 的读取，以及玩家名缺字替换、帧末集中写包调用的 IL 交叉验证。
- 现有 `PerfEnhancerTests`：241项通过，含44入口签名与安全边界；`MPfixSafetyTests`：85项通过，含名称规则、依赖与生成边界。**这些检查未覆盖新医疗标签的完整渲染，也没有授权绕过MVID限制。**
- 本轮没有启动／停止游戏，没有执行自定义地图命令，没有改真实配置、存档或第三方 DLL；没有进行真实多人、屏幕渲染或性能收益验收。

## 6. 后续适配与验收优先级

1. 先补齐展示层姓名来源与字库覆盖：悬停、医疗提示、医疗标题和查看者列表均须测试中文、混合名、空名、Steam未返回昵称、重连和改名。测试“看别人”与“被别人查看”两种方向。
2. 复核5.5.5的LeaveGear／FixInvSync效果；在旧补丁关闭的条件下测试退场、重建、容器延迟抵达，再决定是否保留旧预算补丁。不要恢复已移除的离场广播清理方案。
3. 重验无芯片、伤害倍率、合成、商人与丢弃。完成后再更新目标版本和文档；不能通过“忽略版本”代替这一步。
4. 性能对照保持世界、位置、人数、物品密度和剔除开关一致。先测纯Together及必要前置，再逐项加入其它模组；区分新的集中写包、IK／液体开销和客机容器积压。
5. 命名 debug 地图另做保存格式／入口适配；在确认前，存档编辑器继续限定已支持的保存来源。

本报告区分了“已确认阻断”“接口仍可解析”“存在覆盖缺口”和“尚未实测”；不能把其中任一项简写成整套组件已经全面兼容5.5.5。

## 7. 用户追加的独立屏幕提示缩放

管理器2.0.1与物品工具0.1.1-alpha完成了源码修订，Shared仍为2.0.0／API v1，最低管理器要求未提高，没有新增硬引用：

- 管理器的环境版本／模块版本提示，显式按管理器Scale缩放框体、边距和字号，按实际屏幕宽度限制显示范围，并在所有退出路径恢复GUI矩阵和绘制层级。原源码及已安装2.0.0 DLL本来就有矩阵缩放，因此不能把用户的版本条现象简单归因于“完全没乘缩放”；新版消除了该独立绘制路径对窗口矩阵缩放的依赖，实机效果仍需确认。
- 物品工具独立的已生成／失败提示，原先确实使用固定像素，现改为只读获取管理器UI/Scale；无管理器时回退1。面板内部提示继续沿用窗口矩阵，不重复缩放。
- 管理器与物品工具Release构建均0警告／0错误；ManagerLocalizationTests 1336项、UnitListTests 411项通过。GUI输入属于模拟，没有启动游戏验证4K、鼠标悬停或真实提示绘制。
- 独立DLL交付到`dist/Manager-v2.0.1/DDExcNet-CUmodM.dll`与`dist/UnitListMgr-v0.1.1-alpha/DDEN.UnitListMgr.dll`，未部署到游戏。此UI修订不改变Together版本闸门，不代表MPfix已经适配5.5.5。