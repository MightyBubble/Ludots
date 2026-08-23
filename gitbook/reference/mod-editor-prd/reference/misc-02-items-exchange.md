# misc-02 reference · 物品与兑换

> 现状参考。第一性需求见 [misc-02 PRD](../prd/misc-02-items-exchange.md)；配置说明见 [misc-02 配置说明](../config/misc-02-items-exchange.md)。

## 1. 现状快照

- Items 三表：shapes（id、rows 网格掩码、rotatable→加载期派生 4 旋转）、layouts（id、purpose、width/height、blockedRows、grantsEquipmentBonuses、namedSlots）、definitions（id、displayName、shape 必填、maxStack≤0 归 1、tags、allowedNamedSlots、equipEffects、abilityGrants、mountedContainers）。
- 根表全空（D3）；内容在 showcase mod（fourx_association、diplomacy_trade_gate、gold_market）。
- 消费：InventoryRuntimeService / EquipmentGrantSync。
- Exchange/operations：id、relationshipRequirements、inputs、outputs；依赖 Item 与 Relationship 注册表；LoadIds 两段式；根表空（D3）；ExchangeRuntime 注入效果系统。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| shapes / layouts / definitions 加载 | src/Core/Gameplay/Items/ItemConfigLoader.cs:40,77,120 |
| 兑换加载（两段式） | src/Core/Gameplay/Exchange/ExchangeConfigLoader.cs:43-90 |
| 根表占位 | assets/Items/shapes.json、layouts.json、definitions.json、assets/Exchange/operations.json |
| 样例 | mods/showcases/fourx_association/…/assets/Items/ 与 Exchange/；mods/showcases/diplomacy_trade_gate/…/assets/Items/ |

**相关文档**：[misc-02 PRD](../prd/misc-02-items-exchange.md) · [misc-01 reference](misc-01-progression.md)
