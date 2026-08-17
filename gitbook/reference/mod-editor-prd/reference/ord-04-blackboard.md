# ord-04 reference · 订单黑板

> 现状参考。第一性需求见 [ord-04 PRD](../prd/ord-04-blackboard.md)；配置说明见 [ord-04 配置说明](../config/ord-04-blackboard.md)。

## 1. 现状快照

- 四缓冲（容量为各文件首行常量）：Float 32 仅 TryGet/Set、无移除；Int 32 有移除；Entity 16 按（Id,WorldId,Version）三元组重建校验；Spatial 8 条目 ×16 点。安装器只挂 Int/Spatial/Entity 三种。
- 内置键（OrderBlackboardKeys）：`Cast.SlotIndex`=110（Int）、`Cast.TargetEntity`=111（Entity）、`Cast.TargetPosition`=112（Spatial）、`Cast.AbilityId`=113（Int，定义注册但核心无读取方）、`Cast.Facing`=114（Float，扇形朝向消费）；通用四键 `Generic.TargetEntity`=200、`Generic.TargetPosition`=201、`Generic.IntParam`=202、`Generic.FloatParam`=203。自定义键从 10000 起。
- 存储目标五键组（targetKind/targetPosition/targetEntity/hexQ/hexR）与 `instantComplete` 双向绑定（有则必须瞬时、瞬时必须有）；瞬时系统三步：构造收集 → 提交存储目标 → 完成通知。
- 存储目标操作：读取按形态三态（Entity/Point/HexCell，Hex 缺世界点用六角坐标推导）；提交时实体+空间并存抛目标歧义；点/格/实体三写、容量守卫、世界坐标解析齐备。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 四缓冲容量与操作面 | src/Core/Gameplay/GAS/Components/BlackboardIntBuffer.cs、BlackboardFloatBuffer.cs、BlackboardEntityBuffer.cs、BlackboardSpatialBuffer.cs（各文件首行） |
| 缓冲安装 | src/Core/Gameplay/GAS/Orders/OrderBlackboardStateInstaller.cs:19-30 |
| 内置键全表 | src/Core/Gameplay/GAS/Orders/OrderBlackboardKeys.cs:26-87 |
| 键注册表 / 自定义起点 | src/Core/Gameplay/GAS/Orders/OrderBlackboardKeyRegistry.cs:13,117-124 |
| 五键组结构 | src/Core/Gameplay/GAS/Orders/BlackboardStoredTargetKeys.cs:3-31 |
| 五键组绑定校验 | src/Core/Gameplay/GAS/Orders/OrderTypeConfigLoader.cs:183-213 |
| 瞬时单闭环 | src/Core/Gameplay/GAS/Systems/InstantCompleteOrderSystem.cs:39-51,94-108 |
| 存储目标操作 | src/Core/Gameplay/GAS/Orders/BlackboardStoredTargetOps.cs:37-337（推导 :96、歧义 :126-130） |
| Float 消费例 | src/Core/Gameplay/GAS/TargetResolverFanOutHelper.cs:533 |
| 真实键声明 | mods/LudotsCoreMod/assets/GAS/order_types.json（键段） |

**相关文档**：[ord-04 PRD](../prd/ord-04-blackboard.md) · [ord-01 reference](ord-01-types.md)
