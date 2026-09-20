# WebUI Panel Kit Showcase Family（WPK-10）

面向新玩家的跨类型 Panel Kit 演示：同一套 C# 面板合同，用配置切换 RTS 与 4X/大战略语义。**一种面板一个小 showcase mod**，不做浏览器大而全总场景。

本切片是 authoring / 小演示骨架：每个目录只讲一种面板怎么被配置出来。它不是完整 CEF 大 HUD 运行时，也不往 launcher 挂 RTS/4X 大场景入口。

## 1. 概述

本 showcase 不是技术 topic 流水账，而是让新玩家在每个面板小演示里完成一个小目标：

| Showcase folder | 面板 | 玩家小目标 |
|-----------------|------|------------|
| `panel_kit_resource_showcase` | 资源栏 | 看库存数字，理解当前能花什么 |
| `panel_kit_command_deck_showcase` | 全局命令栏 | 点全局训练/推进，无需先选单位 |
| `panel_kit_production_worker_showcase` | 生产/工人 | 看队列进度与工人分配 |
| `panel_kit_quest_objective_showcase` | 任务目标 | 读目标与阻塞原因 |
| `panel_kit_notification_showcase` | 通知 | 完成后看到本地化 toast |
| `panel_kit_tooltip_showcase` | 提示 | 悬停看到作用说明 |
| `browser_minimap_composited_overlay` | 小地图 Web 外壳 | 拖动/聚焦小地图控件，marker 仍由原生路径承载 |
| `panel_kit_techtree_progression_showcase` | 科技/进度树 | 看可推进节点与阻塞原因 |
| `activity_dispatch` | 活动事件面板 | 三条派发路径（forced/pooled/automatic）各触发一次：forced 弹层拍板、pooled 看确定性抽签、automatic 只留通报；选项的锁定原因与 Gate 隐藏当场可验 |

语义全部来自各 mod 的 `Assets/PanelKit/profile.rts.json` / `profile.fourx.json`；通用 Panel Kit 代码不硬编码资源名、单位名、科技名。

## 2. 结构

```text
mods/showcases/
  panel_kit_resource_showcase/...
  panel_kit_command_deck_showcase/...
  panel_kit_production_worker_showcase/...
  panel_kit_quest_objective_showcase/...
  panel_kit_notification_showcase/...
  panel_kit_tooltip_showcase/...
  browser_minimap_composited_overlay/...
  panel_kit_techtree_progression_showcase/...
```

每个 folder 内：

```text
Assets/PanelKit/
  panel_manifest.json   # 只声明一个 panelType
  profile.rts.json
  profile.fourx.json
README.md               # 玩家路径 + UAT
```

不往 `launcher.config.json` / `launcher.presets.json` 加 RTS/4X 大场景入口。

## 3. 详情

### 3.1 复用清单

- WPK-1：`WebUiPanelKitManifestLoader` / `WebUiPanelKitSurfaceBinder`
- WPK-2..7：Resource / CommandDeck / Production / Tooltip / Quest Objective / Notification
- WPK-8：`browser_minimap_composited_overlay` 复用 Core/Skia minimap marker 热路径，Web 只负责外壳与交互
- WPK-9：TechTree / Progression panel（本 worktree 已整合样本合同）
- 玩法种子：Progression / Quest / Attribute 等正式链路；不新建平行真相

### 3.2 玩家体验约束

- 每个小 showcase 第一屏只突出一种面板，禁止诊断条、topic 名、ack 计数。
- 缺 profile / token / topic 时启动 fail-fast，错误含具体 id。
- 两组 profile 共用同一套 panel kit 类型与 C#，只换配置。

## 4. 场景

### RTS（跨小 showcase 拼出的上手路径）

1. 资源栏：看到 Ore / Power / Supply。
2. 命令栏：点 Train Scout。
3. 生产概览：队列进度与工人分配。
4. 通知：Scout ready…。
5. Tooltip：悬停解释作用。
6. 小地图：拖动浮动小地图面板，看到原生 marker 持续更新。

### 4X

1. 资源栏：Influence / Research / Authority。
2. 目标：Unlock Colony Charter + 阻塞原因。
3. 科技树：可推进节点与 Requires Colony Charter。
4. 通知 / tooltip：确认解锁与下一步。

## 5. 边界

- 禁止 `browser_panel_kit` 式总家族 + RTS/4X profile root 大场景。
- 禁止一种 mod 同时承载全部面板类型。
- 不恢复 `SelectionRuntime`；命令来源只说 collection / control view / global profile。
- 不把游戏专有名词写进 `Ludots.WebUI.PanelKit` 通用代码。
- 不在 Web 层循环绘制 minimap marker；小地图 Web showcase 只做外壳、拖拽和聚焦命令。
- 本切片是小演示骨架，不是完整 CEF 大 HUD 运行时；不改 launcher。

## 6. UAT

```gherkin
Feature: Panel Kit 面板级小演示
  Scenario: 每种面板都有独立 showcase folder
    Given 仓库存在七个 panel_kit_*_showcase 目录和一个 browser_minimap_composited_overlay 目录
    When 打开任一目录的 panel_manifest.json
    Then 该 manifest 只声明一种 panelType
    And 不存在 browser_panel_kit 大场景入口

  Scenario: RTS 与 4X 由配置切换语义
    Given 同一 panelType 的 profile.rts.json 与 profile.fourx.json
    When 切换 profile
    Then 资源名、单位名、科技名只来自配置
    And 通用 panel kit 代码中不出现这些专有名词

  Scenario: Minimap Web 外壳保持原生热路径
    Given 玩家打开 browser_minimap_composited_overlay showcase
    When 玩家拖动小地图面板或触发聚焦
    Then Web 层只负责外壳和交互
    And marker 渲染仍来自 Core/Skia 原生路径
```
