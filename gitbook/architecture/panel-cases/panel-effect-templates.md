# 效果图鉴

翻开墙上的效果说明书：只有模板名，没有剩余时间。

> 状态：🟢（G12）——查询图写出类型化集合袋；面板只消费。Showcase：`panel_effect_templates`。

> **合同**：[效果模板袋](../query-graph-collection-outputs.md) · 收集节点见 Graph 画廊 [QueryCollectEffectTemplates](../../reference/graph-node-op-wiki/QueryCollectEffectTemplates.md)

![验收截图](artifacts/acceptance/panel_effect_templates/screens/001_effect_templates.png)

## 玩家 30 秒

进场只见到一块「效果图鉴」。名单上能认出「祝福」「迅捷」「护盾」——这是说明书，不是身上正在生效的 buff，所以看不到倒计时。

## 作者写法

| 项 | 值 |
|----|----|
| 面板 id | `panel.collection.effects` |
| 集合 destination | `EffectTemplateCollection` |
| 收集 op | `QueryCollectEffectTemplates` |
| Showcase | `panel_effect_templates` |

共享宿主 `mods/showcases/panel_collection_bags/PanelCollectionBagsShowcaseMod` 放种子与面板模板；本场薄入口只改 `startupMapId`。

## 边界

- 一场只开这一块面板，不和效果条、背包等并排。
- 名单跟查询图走；面板不另筛、不造假模板行。
- 同种集合袋在一张 Query 图里只能写一个输出口。

## 验收（玩家视角）

```gherkin
Feature: 效果图鉴单场
  Scenario: 进场只看说明书
    Given 我启动 panel_effect_templates
    When 地图加载完成
    Then 屏幕上只有一块标题为「效果图鉴」的面板
    And 名单里能看到「祝福」「迅捷」「护盾」
    And 看不到剩余时间进度条
```

## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $panel_effect_templates --adapter raylib
```
