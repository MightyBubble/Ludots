# fx-23 配置说明 · 兑换

> 配置写法与行为。第一性需求见 [fx-23 PRD](../prd/fx-20-exchange.md)；编辑器需求见 [UXD](../uxd/fx-20-exchange.md)；现状见 [reference](../reference/fx-20-exchange.md)。

## 1. 示例配置

效果条目（教学骨架：preset Exchange + 操作参数；仓库 mod 暂无 Exchange 效果实例），操作本体取真实文件：

```json
{
  "id": "Effect.Market.BuyRelic",
  "presetType": "Exchange",
  "lifetime": "Instant",
  "configParams": {
    "_ep.exchangeOperationId": { "type": "ExchangeOperation", "value": "gold_market.buy_relic" }
  }
}
```

被引用的操作（真实文件 `mods/showcases/gold_market/GoldMarketShowcaseMod/assets/Exchange/operations.json`）：

```json
{
  "id": "gold_market.buy_relic",
  "inputs":  [ { "kind": "AttributeCost", "actor": "Source", "attribute": "Gold", "quantity": 5 } ],
  "outputs": [ { "kind": "CreateItem", "actor": "Source", "purpose": "Stash", "item": "gold_market_relic", "quantity": 1 } ]
}
```

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `presetType: "Exchange"` | 必须 Instant；OnApply 走兑换处理器 |
| `_ep.exchangeOperationId` | 必需，type `ExchangeOperation`、value 为操作表 id（须已注册） |
| `_ep.exchangeScopeKey` | 可选：命名作用域（Int 型键），缺省走默认作用域 |

操作的输入/输出/关系门槛字段属于兑换操作表合同，见 misc-02；本篇只管"效果如何引用操作"。**现状提示**：处理器未通过原子域认证，Exchange 模板启动计划编译即拒（`GAS.EFFECT_PLAN.ERR.UnsupportedOperation`，治理跟踪中，见 spec）。

## 3. 文件结构

效果条目在 `assets/GAS/effects.json`；操作表在 `assets/Exchange/operations.json`（目录登记，合同见 misc-02 与 cfg-04）。

## 4. 运行时加载效果

loader 要求 Exchange 必带正数 `_ep.exchangeOperationId` 并经兑换操作注册表把名字解析为 id；运行期组装上下文调兑换运行时一次结算。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| Exchange 非 Instant | 启动失败，指明效果 |
| 缺 `_ep.exchangeOperationId` 或值 <=0 | 启动失败，报键名与类型 |
| 操作名未注册 | 启动失败，指明键与名字 |
| Exchange 模板进入计划编译 | 启动失败（现状，Unsupported(Exchange)） |
| 运行期兑换业务失败 | 仅记录结果与诊断，不抛错 |

## 6. 实例

- 兑换操作本体：`mods/showcases/gold_market/GoldMarketShowcaseMod/assets/Exchange/operations.json`（buy_relic 等）
- 带关系门槛的操作：`mods/showcases/diplomacy_trade_gate/DiplomacyTradeGateShowcaseMod/assets/Exchange/operations.json`

**相关文档**：[fx-23 PRD](../prd/fx-20-exchange.md) · [fx-17 配置说明](fx-14-config-params.md)
