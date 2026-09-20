# 编队档案

队员名下嵌着各自的技能格，不是全场技能大杂烩。

> 状态：🟢（G12）——查询图写出类型化集合袋；面板只消费。Showcase：`panel_roster_nested`。

> **合同**：[单位详情嵌技能栏](../query-graph-collection-outputs.md) · 收集节点见 Graph 画廊 [QueryCollectAbilitySlots](../../reference/graph-node-op-wiki/QueryCollectAbilitySlots.md)

![验收截图](artifacts/acceptance/panel_roster_nested/screens/001_roster_nested.png)

## 玩家 30 秒

进场只见到一块「编队档案」。「名册守望者」和「名册学徒」各自成行；展开行下能看到各自的技能格（守望者有火球与闪现一类）。

## 作者写法

| 项 | 值 |
|----|----|
| 面板 id | `panel.collection.roster` |
| 外层袋 | 实体名册 |
| 嵌套袋 | `AbilitySlotCollection`（`QueryCollectAbilitySlots`） |
| Showcase | `panel_roster_nested` |

共享宿主放种子与面板；本场薄入口只改 `startupMapId`。

## 边界

- 技能挂在队员名下，不做成「全场技能一览」第二块面板。
- 一场只开编队档案。

## 验收（玩家视角）

```gherkin
Feature: 编队档案单场
  Scenario: 队员名下嵌技能
    Given 我启动 panel_roster_nested
    When 地图加载完成
    Then 屏幕上只有一块「编队档案」
    And 名单含「名册守望者」与「名册学徒」
    And 队员行下能看到各自的技能格
```

## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $panel_roster_nested --adapter raylib
```
