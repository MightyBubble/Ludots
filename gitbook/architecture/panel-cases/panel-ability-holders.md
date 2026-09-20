# 谁会火球

技能格旁挂着会这招的人（source=input 反查）。

> 状态：🟢（G12）——查询图写出类型化集合袋；面板只消费。Showcase：`panel_ability_holders`。

> **合同**：[反查持有者](../query-graph-collection-outputs.md) · 收集节点见 Graph 画廊 [QueryCollectAbilityHolders](../../reference/graph-node-op-wiki/QueryCollectAbilityHolders.md)

![验收截图](artifacts/acceptance/panel_ability_holders/screens/001_ability_holders.png)

## 玩家 30 秒

进场只见到一块「谁会火球」。先看到技能格，格旁嵌着会这招的人——「名册守望者」和「名册学徒」都在持有者名单里。

## 作者写法

| 项 | 值 |
|----|----|
| 面板 id | `panel.collection.holders` |
| 外层袋 | 技能槽 |
| 嵌套袋 | 持有者实体（`source=input`） |
| 收集 op | `QueryCollectAbilityHolders` |
| Showcase | `panel_ability_holders` |

## 边界

- 持有者名单靠 `source=input` 反查，不在面板里写死人名。
- 一场只开这一块面板。

## 验收（玩家视角）

```gherkin
Feature: 谁会火球单场
  Scenario: 技能格旁挂持有者
    Given 我启动 panel_ability_holders
    When 地图加载完成
    Then 屏幕上只有一块「谁会火球」
    And 技能格的嵌套名单含「名册守望者」与「名册学徒」
```

## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $panel_ability_holders --adapter raylib
```
