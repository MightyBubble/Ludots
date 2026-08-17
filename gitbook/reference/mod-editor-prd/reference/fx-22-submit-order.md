# fx-23 reference · 出生下单

> 现状参考。第一性需求见 [fx-23 PRD](../prd/fx-22-submit-order.md)；配置说明见 [fx-23 配置说明](../config/fx-22-submit-order.md)。

## 1. 现状快照

- loader：submitOrderFromBlackboard 块仅 SubmitOrderFromBlackboard + Instant；source/target 缺省 Source/Target、禁 None；storedTarget 五键（targetKindKey/targetPositionKey/targetEntityKey/hexQKey/hexRKey）全必填且经 OrderBlackboardKeyRegistry 解析（未知即抛）；pointMoveOrderTypeKey/entityOrderTypeKey 必填且需 OrderTypeRegistry；entityOrderIntArg0 必填；submitMode 仅 Immediate/Queued。
- runtime：HandleSubmitOrderFromBlackboard 读五键快照（无目标静默返回）；Point/HexCell → 点移动单、Entity → 实体单（Args.I0）；经 OrderIntake.SubmitAssigned 提交，非接受结果抛错；执行者无 OrderBuffer 抛错；注册为 External(Order) 独占。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 块与 preset 组合校验 | src/Core/Gameplay/GAS/Config/EffectTemplateLoader.cs:485-502 |
| submitOrder 编译 | EffectTemplateLoader.cs:761-867 |
| 五键解析 | EffectTemplateLoader.cs:827-856 |
| 提交模式解析 | EffectTemplateLoader.cs:858-867 |
| 下单处理器 | src/Core/Gameplay/GAS/BuiltinHandlers.cs:743-867 |
| 组单规则 | BuiltinHandlers.cs:806-867（BuildOrderFromStoredTarget） |
| External(Order) 注册 | BuiltinHandlers.cs:81 |
| 黑板键注册表 | src/Core/Gameplay/GAS/Orders/OrderBlackboardKeyRegistry.cs |
| 展示 mod 现货 | mods/showcases/rts_demo/RtsDemoMod/assets/GAS/effects.json |

**相关文档**：[fx-23 PRD](../prd/fx-22-submit-order.md) · [fx-23 配置说明](../config/fx-22-submit-order.md)
