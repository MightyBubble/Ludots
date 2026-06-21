# Exchange Operations

Exchange 是 Ludots 中用于“输入结算为输出、效果或状态变化”的中性 Core 语义。

它覆盖商店购买、出售、合成、配方、以物易物和未来 4X 交易，但这些场景名只属于 Mod、配置、UI 和展示文本，不属于 Core 泛化命名。

## 正式结论

Core 名称使用 `Exchange`。

`Exchange` 表示：

```text
在运行时上下文中，按规则验证输入，并原子结算为输出或 GAS 效果。
```

因此：

* 商人购买是 Exchange。
* 出售物品是 Exchange。
* 合成配方是 Exchange。
* 4X 资源转换、外交交易、贸易路线结算也是 Exchange。

Core 不新增 `MerchantRuntime`、`RecipeRuntime`、`TradeRuntime` 或同类平行管线。

## 身份与动态实例

Exchange 使用两个键：

* `operationId`：模板/操作语义，由 `ExchangeOperationRegistry` 注册。
* `scopeKey`：可选运行时作用域，用于动态实例。

查找顺序：

```text
(operationId, scopeKey) 动态定义
operationId 静态定义
```

静态模板只需要 `operationId`。动态配方、动态报价、4X 谈判结果等需要 `operationId + scopeKey`。

`scopeKey` 不是配方本体身份，它只是某个 operation 下的运行时实例索引。这个规则与 progression 的 scope 思路一致，但避免不同 operation 复用同一个 scope 时互相撞名。

当调用方已经传入 `ExchangeOperationKey` 时，查找只以这个 key 为准；`ExchangeExecutionContext.ScopeKey` 只服务 `TryExecute(operationId, context)` 的便捷入口。动态配方、动态报价和 4X 谈判实例都应使用 `operationId + scopeKey`，而不是单独用 scope 表达身份。

## 正式管线

Exchange 必须复用现有基础设施：

* Inventory ECS 是物品、容器、位置的 SSOT。
* GAS 是效果、条件、属性、标签、授能和执行触发管线。
* ConfigPipeline + `config_catalog.json` 是配置入口。
* Mod 和 UI 负责场景命名、展示皮肤和用户操作。

执行顺序：

1. 解析 operation。
2. 先完整校验输入和输出，并按同一次结算内已经计划的物品输出累计预留容器位置。
3. 通过 `InventoryRuntimeService` 消耗、创建或移动物品。
4. 任一物品输出失败时回滚已消耗、已创建和已移动内容。
5. 物品结算成功后再发布 GAS effect request。

## 配置与 GAS

Exchange 配置入口为 `Exchange/operations.json`。

GAS 使用 `Exchange` preset 和 `ExecuteExchange` handler 触发结算：

* `_ep.exchangeOperationId`：必填 operation id。
* `_ep.exchangeScopeKey`：可选 scope key。

这样 ability、effect、requirement 和未来 progression 仍然沿 GAS 体系思考，不绕开正式运行时。

## 内容映射

内容层可以这样命名：

* `item_showcase.buy_ap_ammo`
* `item_showcase.sell_artifact`
* `item_showcase.forge_crimson_gem`
* `strategy.convert_food_to_production`

这些 ID 可以包含场景词，因为它们是内容 ID。Core 类型、枚举和架构文档仍使用 Exchange 的中性词汇。

## 深度材料

* 深度架构：`docs/architecture/exchange_architecture.md`
* 决策记录：`docs/adr/ADR-0003-exchange-operation-scope-key.md`
