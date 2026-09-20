# 身上的印记

守望者身上现有的印记被点名列出。

> 状态：🟢（G12）——查询图写出类型化集合袋；面板只消费。Showcase：`panel_present_tags`。

> **合同**：[标签袋](../query-graph-collection-outputs.md) · 收集节点见 Graph 画廊 [QueryCollectPresentTags](../../reference/graph-node-op-wiki/QueryCollectPresentTags.md)

![验收截图](artifacts/acceptance/panel_present_tags/screens/001_present_tags.png)

## 玩家 30 秒

进场只见到一块「身上的印记」。能认出「勇气印记」「洞察印记」「守望印记」——这是当前挂在身上的标签，不是图鉴里的空壳定义。

## 作者写法

| 项 | 值 |
|----|----|
| 面板 id | `panel.collection.tags` |
| 集合 destination | `TagCollection` |
| 收集 op | `QueryCollectPresentTags` |
| Showcase | `panel_present_tags` |

## 边界

- 只列当前在场的印记；未挂上的标签不进袋。
- 一场只开这一块面板。

## 验收（玩家视角）

```gherkin
Feature: 身上的印记单场
  Scenario: 进场点名现有印记
    Given 我启动 panel_present_tags
    When 地图加载完成
    Then 屏幕上只有一块「身上的印记」
    And 名单含「勇气印记」「洞察印记」「守望印记」
```

## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $panel_present_tags --adapter raylib
```
