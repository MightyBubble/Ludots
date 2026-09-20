# ord-01 reference · 订单类型

> 现状参考。第一性需求见 [ord-01 PRD](../prd/ord-01-types.md)；配置说明见 [ord-01 配置说明](../config/ord-01-types.md)。

## 1. 现状快照

- 表 `GAS/order_types.json` 三段：`orderBlackboardKeys`（可空须显式）/ `orderTypes`（非空）/ `orderRules`（可空须显式）；加载先 Clear 两注册表 → 键 → 两遍式编号（整数→语义按字典序取最小空闲，从 1 起）→ 类型 → 规则。
- `orderTypeId` 整数须 >0 且 <256、重复抛错；语义串必须与条目 key 逐字相同。`maxQueueSize`/`queuedModeMaxSize` 0..8；`bufferWindowMs` ≤0 永不过期；三键组必填语义串（`"none"`→-1，须已注册，数字拒收）；`validationGraph` `"none"`→0；五键组当且仅当 `instantComplete=true`。
- `orderBlackboardKeys` 值必须 `true`；内置键禁重声明；自定义键编号从 10000 起。
- 必备类型：`castAbility`/`moveTo`/`attackTarget`/`stop` 由 game.json 常量声明并 fail-fast 校验；另取 `chainPass`/`chainNegate`/`chainActivateEffect` 与 `castAbility.Start`/`castAbility.End`。
- 类默认值字段与加载器上限不一致（`QueuedModeMaxSize=16`），但加载路径逐字段显式赋值、默认值不可达。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 三段 DTO / 加载时序 | src/Core/Gameplay/GAS/Orders/OrderTypeConfigLoader.cs:59-64,80-137 |
| 序列化（严格 camelCase+Disallow） | OrderTypeConfigLoader.cs:14-19 |
| 字段 DTO / 转换 | OrderTypeConfigLoader.cs:21-41,139-181 |
| id 校验与语义分配 | OrderTypeConfigLoader.cs:348-369,469-480 |
| 黑板键段校验 | OrderTypeConfigLoader.cs:389-420,443-467 |
| 三键组解析 | OrderTypeConfigLoader.cs:482-519 |
| validationGraph / 五键组绑定 | OrderTypeConfigLoader.cs:521-561,183-213 |
| 类默认值 | src/Core/Gameplay/GAS/Orders/OrderTypeConfig.cs:23-43 |
| 键注册表 / 自定义起点 | src/Core/Gameplay/GAS/Orders/OrderBlackboardKeyRegistry.cs:13,117-124 |
| 加载调用点 / 必备校验 | src/Core/Engine/GameEngine.cs:870-874,1336-1357 |
| 真实数据 | mods/LudotsCoreMod/assets/GAS/order_types.json；mods/LudotsCoreMod/assets/game.json constants |

**相关文档**：[ord-01 PRD](../prd/ord-01-types.md) · [ord-02 reference](ord-02-rules.md)
