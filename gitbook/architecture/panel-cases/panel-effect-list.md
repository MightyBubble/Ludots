# 效果条

身上正在生效的 buff，带着剩余时间。

> 状态：🟢（G12）——查询图写出类型化集合袋；面板只消费。Showcase：`panel_effect_list`。

> **合同**：[效果实例袋](../query-graph-collection-outputs.md) · 收集节点见 Graph 画廊 [QueryCollectActiveEffects](../../reference/graph-node-op-wiki/QueryCollectActiveEffects.md)

![验收截图](artifacts/acceptance/panel_effect_list/screens/001_effect-strip.png)

## 玩家 30 秒

进场只见到一块「效果条」。行上是当前挂在身上的效果显示名，并带剩余时间——和「效果图鉴」的说明书不同。

## 作者写法

| 项 | 值 |
|----|----|
| 面板 id | `panel.effect.list` |
| 集合 destination | 效果实例 / EntityCollection |
| 收集 op | `QueryCollectActiveEffects` |
| Showcase | `panel_effect_list` |

本场独立 showcase：`mods/showcases/panel_effect_list/PanelEffectListShowcaseMod`。

## 边界

- 只展示当前生效实例，不把未施加的模板混进同一袋。
- 名单跟查询图走；面板不另筛。

## 验收（玩家视角）

```gherkin
Feature: 效果条单场
  Scenario: 进场看见带倒计时的 buff
    Given 我启动 panel_effect_list
    When 地图加载完成
    Then 屏幕上只有一块效果条面板
    And 行上能读到效果显示名与剩余时间
```

## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $panel_effect_list --adapter raylib
```
