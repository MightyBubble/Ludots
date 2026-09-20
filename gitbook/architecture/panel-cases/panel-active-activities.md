# 进行中的活动

挂在守望者名下的活动被点名。

> 状态：🟢（G12）——查询图写出类型化集合袋；面板只消费。Showcase：`panel_active_activities`。

> **合同**：[活动实例袋](../query-graph-collection-outputs.md) · 收集节点见 Graph 画廊 [QueryCollectActiveActivities](../../reference/graph-node-op-wiki/QueryCollectActiveActivities.md)

![验收截图](artifacts/acceptance/panel_active_activities/screens/001_active_activities.png)

## 玩家 30 秒

进场只见到一块「进行中的活动」。名单上能认出「名册集会」——当前挂着的活动，不是活动总表。

## 作者写法

| 项 | 值 |
|----|----|
| 面板 id | `panel.collection.activities` |
| 集合 destination | `ActivityInstanceCollection` |
| 收集 op | `QueryCollectActiveActivities` |
| Showcase | `panel_active_activities` |

## 边界

- 只列进行中的活动实例。
- 与差事分场；一场一块面板。

## 验收（玩家视角）

```gherkin
Feature: 进行中的活动单场
  Scenario: 进场点名集会
    Given 我启动 panel_active_activities
    When 地图加载完成
    Then 屏幕上只有一块「进行中的活动」
    And 名单含「名册集会」
```

## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $panel_active_activities --adapter raylib
```
