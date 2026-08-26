# 背包堆叠

同类药剂只露出一枚图标和总数。

> 状态：🟢（G12）——查询图写出类型化集合袋；面板只消费。Showcase：`panel_inventory_aggregate`。

> **合同**：[物品实例袋 + aggregate](../query-graph-collection-outputs.md) · 收集节点见 Graph 画廊 [QueryCollectInventoryItems](../../reference/graph-node-op-wiki/QueryCollectInventoryItems.md)

![验收截图](artifacts/acceptance/panel_inventory_aggregate/screens/001_inventory_aggregate.png)

## 玩家 30 秒

进场只见到一块「背包堆叠」。「试炼药剂」只占一行，旁边写着 ×3——三瓶同类被收成一格，不是三行重复。

## 作者写法

| 项 | 值 |
|----|----|
| 面板 id | `panel.collection.inventory` |
| 集合 destination | `ItemInstanceCollection` + aggregate 展示 |
| 收集 op | `QueryCollectInventoryItems` |
| Showcase | `panel_inventory_aggregate` |

## 边界

- 聚合发生在投影层；袋里仍是真实物品实例。
- 显示名优先读物品定义的 DisplayName，不拿裸 registry key 糊弄玩家。

## 验收（玩家视角）

```gherkin
Feature: 背包堆叠单场
  Scenario: 同类药剂合成一行
    Given 我启动 panel_inventory_aggregate
    When 地图加载完成
    Then 屏幕上只有一块「背包堆叠」
    And 能看到「试炼药剂」
    And 总数显示为 3
```

## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $panel_inventory_aggregate --adapter raylib
```
