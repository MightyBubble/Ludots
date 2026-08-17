# ord-02 reference · 订单规则与打断

> 现状参考。第一性需求见 [ord-02 PRD](../prd/ord-02-rules.md)；配置说明见 [ord-02 配置说明](../config/ord-02-rules.md)。

## 1. 现状快照

- 规则形状：`orderTypeKey` + `blockedActiveOrderTypeKeys` + `interruptsActiveOrderTypeKeys`，三字段必填（数组可空）；两 fixed 数组各上限 8；引用须唯一且已注册。
- 裁决（OrderSubmitter）：Submit 先分派排队态（`allowQueuedMode=false` → RejectedByRule；排队数达上限 → RejectedQueueFull）与即时态。
- 即时态三步：阻止表命中活动单类型 → RejectedByRule；打断判定（无活动单可打断；同型看 `canInterruptSelf`；跨型查表，无规则=不可打断）；打断链先 `TryPrepareActivationBlackboard` 预写，再 `FinalizeActive(Cancelled, Interrupted)`，`clearQueueOnActivate` 清队，`SetActiveDirect`+提交黑板。
- 不可打断落同型策略：Queue 满 DropOldest 释放最老 / Replace 清同型 / Ignore 拒收。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 规则 DTO 三字段 | src/Core/Gameplay/GAS/Orders/OrderTypeConfigLoader.cs:52-57 |
| fixed 容量 8/8 | src/Core/Gameplay/GAS/Orders/OrderRuleRegistry.cs:8-9 |
| 引用唯一/已注册校验 | OrderTypeConfigLoader.cs:632-657,669-679 |
| 排队/即时分派 | src/Core/Gameplay/GAS/Orders/OrderSubmitter.cs:100-116 |
| 阻止/打断判定 | OrderSubmitter.cs:129-136,599-622 |
| 打断链 | OrderSubmitter.cs:144-184 |
| 清队/激活 | OrderSubmitter.cs:178-184 |
| 同型策略退化 | OrderSubmitter.cs:191-240 |
| 真实规则数据 | mods/LudotsCoreMod/assets/GAS/order_types.json（orderRules 段） |

**相关文档**：[ord-02 PRD](../prd/ord-02-rules.md) · [ord-01 reference](ord-01-types.md)
