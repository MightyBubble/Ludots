# fx-23 reference · 兑换

> 现状参考。第一性需求见 [fx-23 PRD](../prd/fx-20-exchange.md)；配置说明见 [fx-23 配置说明](../config/fx-20-exchange.md)。

## 1. 现状快照

- loader：Exchange preset 必须 Instant；`_ep.exchangeOperationId`（Int）>0 必需，缺失即抛错并指明键名与类型。
- runtime：HandleExecuteExchange 组 ExchangeExecutionContext(source/target/targetContext/scope) 调 ExchangeRuntime.TryExecute；`_ep.exchangeScopeKey` 缺省走默认作用域；失败 RecordExchangeResult 后 return 不抛。
- 注册为 Unsupported(Exchange)：计划编译 fail-closed（`GAS.EFFECT_PLAN.ERR.UnsupportedOperation`）——可配置不可执行。
- 仓库无 mod 使用 Exchange 效果条目；兑换操作表在多个展示 mod 与集成测试中存在。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| Exchange 组合与参数校验 | src/Core/Gameplay/GAS/Config/EffectTemplateLoader.cs:402-415 |
| ExchangeOperation 类型编译 | EffectTemplateLoader.cs:1348-1377 |
| 兑换处理器 | src/Core/Gameplay/GAS/BuiltinHandlers.cs:673-707 |
| Unsupported(Exchange) 注册 | BuiltinHandlers.cs:79 |
| 计划编译 fail-closed | src/Core/Gameplay/GAS/EffectExecutionPlan.cs:600-603 |
| 兑换运行时 | src/Core/Gameplay/Exchange/ExchangeRuntime.cs |
| 操作注册表 | src/Core/Gameplay/Exchange/ExchangeOperationRegistry.cs |
| 操作表现货 | mods/showcases/gold_market/GoldMarketShowcaseMod/assets/Exchange/operations.json |

**相关文档**：[fx-23 PRD](../prd/fx-20-exchange.md) · [fx-23 配置说明](../config/fx-20-exchange.md)
