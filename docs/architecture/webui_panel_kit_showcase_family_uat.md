# WPK-10 UAT Evidence — Panel Kit 面板级小演示

## 1. 概述

面向新玩家的 Panel Kit 演示证据包。验收看的是「一种面板一个小 showcase，能不能玩懂」，不是 topic 名是否打印出来，也不是一个浏览器大场景塞满全部 HUD。

本证据包对应 authoring / 小演示骨架：七个 `panel_kit_*_showcase` folder 加一个 WPK-8 minimap Web shell showcase，用配置切换 RTS 与 4X 语义。它不是完整 CEF 大 HUD 运行时验收，也不要求 launcher 大场景入口。

## 2. 结构

| Folder | Panel | RTS 第一眼 | 4X 第一眼 |
|--------|-------|------------|-----------|
| `panel_kit_resource_showcase` | resource-bar | Ore / Power / Supply | Influence / Research / Authority |
| `panel_kit_command_deck_showcase` | command-deck | Train Scout | Advance Colony Charter |
| `panel_kit_production_worker_showcase` | production-overview | Scout 队列 + 工人 | Research 承诺 |
| `panel_kit_quest_objective_showcase` | objective | Field your first Scout | Unlock Colony Charter |
| `panel_kit_notification_showcase` | notification | Scout ready… | Colony Charter unlocked… |
| `panel_kit_tooltip_showcase` | tooltip | Scout / Depot 说明 | Colony Charter 说明 |
| `browser_minimap_composited_overlay` | minimap.web-shell | 拖动浮动小地图 | 聚焦地图区域 |
| `panel_kit_techtree_progression_showcase` | techtree | Scout Doctrine 树 | Colony Charter 树 |

Authoring SSOT：每个 mod 自己的 `Assets/PanelKit/*.json`；通用 Panel Kit 不写专有名词。

## 3. 详情

### 启动（authoring / 资产验收）

每个小 showcase 以 `panel_manifest.json` + `profile.*.json` 为 SSOT。本切片不注册 RTS/4X 大场景 launcher preset。

### 基建状态

| 能力 | 状态 |
|------|------|
| WPK-1..7 | 本分支基线已有 |
| WPK-9 TechTree | 本 worktree 已整合样本合同与测试 |
| WPK-8 Minimap Web | 本整合分支已吸收 #607 / PR613；Web 只做外壳，marker 热路径仍归 Core/Skia |

## 4. 场景（玩家视角）

见各 `mods/showcases/panel_kit_*_showcase/*/README.md`。跨面板拼路径时：资源 → 命令 → 生产 → 通知 → tooltip；4X 再加 objective + techtree。

## 5. 边界

- 不存在 `browser_panel_kit` 总家族 + RTS/4X profile root。
- 不往 launcher 加大场景入口。
- 缺 token / topic / profile 必须 fail-fast。
- Minimap Web 外壳必须复用 WPK-8 的 `browser_minimap_composited_overlay`，不在 Web 层私造 marker 热路径。

## 6. UAT（Cucumber）

```gherkin
Feature: Panel Kit showcase 新玩家体验（面板级）
  Scenario: RTS 玩家完成一次生产闭环（跨小 showcase）
    Given 玩家依次打开资源、命令、生产、通知小演示的 RTS profile
    When 玩家查看资源并点击全局训练按钮
    Then 生产概览显示队列进度
    And 完成后通知面板出现本地化消息
    And tooltip 能解释该单位或技能的作用

  Scenario: 4X 玩家理解下一步发展目标
    Given 玩家打开 objective 与 techtree 小演示的 4X profile
    When 玩家阅读目标与进度树
    Then 面板显示当前目标、可推进节点和阻塞原因
    And 玩家不需要阅读技术说明也能知道下一步操作

  Scenario: 同一套 C# 通过配置切换语义
    Given 七个小 showcase 共用 Panel Kit 面板类型
    When 切换 profile.rts.json 与 profile.fourx.json
    Then 资源名、单位名、科技名只来自配置
    And 通用 panel kit 代码中不出现这些专有名词

  Scenario: Minimap 作为独立 Web 外壳 showcase
    Given 玩家打开 browser_minimap_composited_overlay showcase
    When 玩家拖动小地图面板或触发聚焦
    Then Web 层只负责外壳与交互反馈
    And 地图 marker 仍由 Core/Skia 原生路径渲染
```
