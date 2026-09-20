# 进行中的差事

挂在守望者名下的差事被点名。

> 状态：🟢（G12）——查询图写出类型化集合袋；面板只消费。Showcase：`panel_active_tasks`。

> **合同**：[任务实例袋](../query-graph-collection-outputs.md) · 收集节点见 Graph 画廊 [QueryCollectActiveTasks](../../reference/graph-node-op-wiki/QueryCollectActiveTasks.md)

![验收截图](artifacts/acceptance/panel_active_tasks/screens/001_active_tasks.png)

## 玩家 30 秒

进场只见到一块「进行中的差事」。名单上能认出「巡夜差事」——当前挂着的任务，不是任务图鉴墙。

## 作者写法

| 项 | 值 |
|----|----|
| 面板 id | `panel.collection.tasks` |
| 集合 destination | `TaskInstanceCollection` |
| 收集 op | `QueryCollectActiveTasks` |
| Showcase | `panel_active_tasks` |

## 边界

- 只列进行中的任务实例；未接取的不进袋。
- 与「进行中的活动」分场演示，禁止大合集墙。

## 验收（玩家视角）

```gherkin
Feature: 进行中的差事单场
  Scenario: 进场点名巡夜
    Given 我启动 panel_active_tasks
    When 地图加载完成
    Then 屏幕上只有一块「进行中的差事」
    And 名单含「巡夜差事」
```

## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $panel_active_tasks --adapter raylib
```
