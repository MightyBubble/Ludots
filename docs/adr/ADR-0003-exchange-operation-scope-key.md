# ADR-0003 Exchange Operation 与 Scope Key 身份模型

本记录说明 Ludots 为什么采用 `operationId + scopeKey` 表达动态 Exchange 实例，并把商店、合成、配方和未来 4X 交易统一收敛到中性 `Exchange` 语义。

## 1 背景

PR #81 已经把 item/equip/backpack foundation 合入 main，Inventory ECS 成为物品实例、容器和位置的真相来源。后续需求要求同一套能力支持：

* 购买与出售。
* 物品合成与配方。
* 动态生成的报价。
* 未来 4X 游戏常见的资源转换、外交交易和贸易路线结算。

如果 Core 直接采用 `Merchant`、`Vendor`、`Forge`、`Recipe`、`Trade` 等场景词，会导致泛化能力被某个玩法皮肤污染，并诱导后续创建平行 runtime。

同时，动态配方和动态交易需要运行时实例身份。只使用一个裸 `scopeKey` 会让不同 operation 在同一 scope 下发生碰撞，也会让 scope 被误认为 operation 本体身份。

## 2 决策

采用中性 Core 名称 `Exchange`。

`Exchange` 表示：

> 在运行时上下文中，按规则验证输入，并原子结算为输出、效果或状态变化。

Exchange operation 的身份模型为：

* `operationId`：注册后的操作语义或模板身份。
* `scopeKey`：可选的运行时实例作用域。

动态定义查找顺序为：

```text
(operationId, scopeKey)
operationId
```

也就是说，静态模板只靠 `operationId`；动态报价、动态配方、4X 谈判等使用 `(operationId, scopeKey)` 覆盖静态模板或表达运行时实例。

`scopeKey` 不单独作为正式 operation identity。

如果调用方传入 `ExchangeOperationKey`，则该 key 是唯一查找身份；`ExchangeExecutionContext.ScopeKey` 只用于 `TryExecute(int operationId, context)` 这个便捷入口派生 key。动态配方、动态报价或 4X 谈判实例必须把“模板/操作语义”放在 `operationId`，把“运行时实例作用域”放在 `scopeKey`，两者共同索引。

## 3 备选方案

### 3.1 使用 Merchant / Recipe / Trade 等多个 Core runtime

拒绝。

这些名字是玩法皮肤，不是稳定架构语义。多个 runtime 会复制库存结算、回滚、条件、效果发布和配置管线。

### 3.2 只使用 scopeKey

拒绝。

裸 scope 无法表达“这是哪个 operation 的实例”，并且容易让不同 operation 在同一实体、同一地图或同一 UI 上下文中碰撞。

### 3.3 所有动态内容都落回静态配置

拒绝。

4X 谈判、随机报价、动态市场和运行时生成配方需要运行时实例化；强行写回静态配置会破坏 Mod/config SSOT 和运行时确定性边界。

## 4 影响

* Core 新增 `src/Core/Gameplay/Exchange/`。
* Exchange operation 通过 `ConfigPipeline` 加载 `Exchange/operations.json`。
* GAS 新增 `Exchange` preset 和 `ExecuteExchange` handler。
* Inventory 继续作为 item/container/location ECS SSOT。
* Showcase 的购买、出售和合成流程迁移到 Exchange，不再手写私有结算逻辑。
* 架构护栏测试禁止 Core Exchange 采纳场景词，并检查热路径中明显的 LINQ/临时集合模式。

## 5 后续约束

* Core public 类型、枚举和架构名继续使用 `Exchange`、operation、input、output、context、settlement 等中性词。
* `Merchant`、`Vendor`、`Forge`、`Recipe`、`Market`、`TradeRoute`、`DiplomacyDeal` 等只能出现在内容 ID、配置、Mod/UI 展示或领域示例中。
* 动态 Exchange 正式路径必须优先使用 `(operationId, scopeKey)`。
* Exchange 输出校验必须按同一次结算内的累计 placement reservation 判断，不能让多个输出分别基于初始容器状态通过。
* Exchange 不保存第二份库存真相，不绕过 GAS effect pipeline。

## 6 相关文档

* Exchange 深度架构：见 [../architecture/exchange_architecture.md](../architecture/exchange_architecture.md)
* Item / Equip / Backpack 架构：见 [../architecture/item_inventory_equipment_architecture.md](../architecture/item_inventory_equipment_architecture.md)
* GitBook 正式说明：见 [../../gitbook/architecture/exchange-operations.md](../../gitbook/architecture/exchange-operations.md)
