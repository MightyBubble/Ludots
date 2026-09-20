# 物品图鉴

已登记物品定义排成名册，没有堆叠实例。

> 状态：🟢（G12）——查询图写出类型化集合袋；面板只消费。Showcase：`panel_item_definitions`。

> **合同**：[物品定义袋](../query-graph-collection-outputs.md) · 收集节点见 Graph 画廊 [QueryCollectItemDefinitions](../../reference/graph-node-op-wiki/QueryCollectItemDefinitions.md)

![验收截图](artifacts/acceptance/panel_item_definitions/screens/001_item_definitions.png)

## 玩家 30 秒

进场只见到一块「物品图鉴」。能认出「试炼药剂」「干粮」——这是说明书名册，没有 ×数量。

## 作者写法

| 项 | 值 |
|----|----|
| 面板 id | `panel.collection.itemDefinitions` |
| 集合 destination | `ItemDefinitionCollection` |
| 收集 op | `QueryCollectItemDefinitions` |
| Showcase | `panel_item_definitions` |

## 边界

- 与背包堆叠分开进场：图鉴不写数量，背包不展示未持有的定义。

## 验收（玩家视角）

```gherkin
Feature: 物品图鉴单场
  Scenario: 进场只看定义名册
    Given 我启动 panel_item_definitions
    When 地图加载完成
    Then 屏幕上只有一块「物品图鉴」
    And 名单含「试炼药剂」与「干粮」
    And 行上没有堆叠数量
```

## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $panel_item_definitions --adapter raylib
```
