# JDI-DDEN 技术细节

## 背景

原版 `StonefruitOpener` 在本地直接 `Instantiate` 开口石果并 `DropItem/PickUpItem`。单机可用，但联机时 Together 是主机权威，客机本地生成的新物品没有网络登记，会被当成未登记物品删除或回滚——表现为「客机拖工具没反应 / 石果打开又被同步回去」。

## 实现

`Plugin.TryPerformInventoryAction_Prefix`（`HarmonyPriority(800)`）：

1. 只处理「拖拽物带 `tool` 标签 + 目标是 `stonefruitclosed`」的情况；
2. 单机 / 主机（`!Net.IsRunning || Net.IsServer`）：本地 `DoOpen`；
3. 客机（`Net.IsClient`）：
   - 取目标物品的 `SyncInfo.syncId`；
   - 用 `Multiplayer.CreateNamedWriter("unwey.stonefruit.open")` 写入 `(ushort)syncId`，`Net.Client_Send(DeliveryMethod.ReliableUnordered, ...)` 发给主机；
   - 返回 `false` 阻止本地逻辑，等主机同步结果。

主机 `ServerOpen(ScavPlayer, NetDataReader)`：

1. 读 `ushort syncId`；
2. 遍历 `Item.allItems`，先按 `id == "stonefruitclosed"` 过滤，再比对 `syncId`；
3. 找到后调用 `DoOpen(plr.body, item)`。

`DoOpen(Body, Item)` 顺序：

1. `body.stamina -= 6f`；
2. 播放 `drop` 音效；
3. 记录 `body.SlotOf(item)`；
4. `item.condition = 0f`；
5. 生成 `stonefruitopen`（缓存预制体）；
6. `item.TryGetParentContainer(out cont)`：有容器则 `DropItem` 后 `cont.LoadItem(open)`；否则 `DropItem` + `PickUpItem(open, slot, false)`，放不下再 `AutoPickUpItem(open)`。

## 配置

- `General/Enabled`：总开关；
- `Multiplayer/AllowClients`：客机是否允许请求主机（关闭时客机拖拽不处理）。

## 约束

- 不改 `Stats.usable` / `condition` 的语义（`condition=0` 只用于被打开的封闭石果）；
- 客机不本地生成未登记物品；
- 频道名、消息格式（`ushort syncId`）保持稳定；
- Together / CasualtiesMP 缺失时，单机路径仍可用，多人请求路径不启用。

## 验收

- 单机：工具拖到封闭石果 → 体力 -6、石果变开口；
- 主机：同上；
- 客机（主机在线）：同上，且主机端看到一致结果；
- 关闭 `AllowClients` 后客机拖拽无反应；
- 无 Together：插件加载（软依赖），单机撬石果正常。
