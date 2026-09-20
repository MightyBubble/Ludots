# 修行进度

守望者身上的进度节点排成一行。

> 状态：🟢（G12）——查询图写出类型化集合袋；面板只消费。Showcase：`panel_progression_nodes`。

> **合同**：[进度节点袋](../query-graph-collection-outputs.md) · 收集节点见 Graph 画廊 [QueryCollectProgressionNodes](../../reference/graph-node-op-wiki/QueryCollectProgressionNodes.md)

![验收截图](artifacts/acceptance/panel_progression_nodes/screens/001_progression_nodes.png)

## 玩家 30 秒

进场只见到一块「修行进度」。名单上能认出「名册修行」——当前挂在守望者身上的进度节点。

## 作者写法

| 项 | 值 |
|----|----|
| 面板 id | `panel.collection.progression` |
| 集合 destination | `ProgressionNodeCollection` |
| 收集 op | `QueryCollectProgressionNodes` |
| Showcase | `panel_progression_nodes` |

## 边界

- 只列实体身上的进度节点；未挂上的进度树节点不进袋。
- 一场一块面板。

## 验收（玩家视角）

```gherkin
Feature: 修行进度单场
  Scenario: 进场点名修行节点
    Given 我启动 panel_progression_nodes
    When 地图加载完成
    Then 屏幕上只有一块「修行进度」
    And 名单含「名册修行」
```

## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $panel_progression_nodes --adapter raylib
```
