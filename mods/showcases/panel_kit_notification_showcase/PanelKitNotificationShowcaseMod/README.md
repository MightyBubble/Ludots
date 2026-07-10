# 通知小演示（WPK-10）

## 1. 概述

这是给新玩家上手的**单面板**演示：只讲「通知」这一块 HUD，不把整套 RTS/4X 大场景塞进来。

玩家小目标：完成后出现本地化 toast。

同一套 Panel Kit 面板类型，用 `Assets/PanelKit/profile.rts.json` 与 `profile.fourx.json` 切换语义；通用 Panel Kit 代码不写资源名/单位名/科技名。

## 2. 结构

```text
panel_kit_notification_showcase/PanelKitNotificationShowcaseMod/
  Assets/PanelKit/
    panel_manifest.json          # 只声明一个 notification
    profile.rts.json             # RTS 文案与字段
    profile.fourx.json           # 4X/大战略文案与字段
  README.md                      # 本页：玩家路径 + UAT
```

Minimap Web 外壳由 WPK-8 的 browser_minimap_composited_overlay 独立 showcase 覆盖，本 showcase 不私造。

## 3. 详情

- 第一屏只突出 通知，没有诊断条、topic 名、连接相位。
- 缺 profile / token / topic 时由正式 Panel Kit 加载链路 fail-fast。
- RTS 与 4X 共用 `panelType=notification`，只换配置。

## 4. 场景

### RTS

1. 打开本 showcase 的 RTS profile。
2. 玩家第一眼看到与「通知」相关的可玩信息（来自 `profile.rts.json`）。
3. 按 README 小目标操作一次，得到可见反馈。

### 4X

1. 打开本 showcase 的 4X profile。
2. 同一面板类型，文案变成国家/进度语义（来自 `profile.fourx.json`）。
3. 玩家不需要读技术说明也能理解下一步。

## 5. 边界

- 不承载其它面板类型的完整 HUD。
- 不往 `launcher.config.json` 加 RTS/4X 大场景入口。
- 不把游戏专有名词写进 `Ludots.WebUI.PanelKit` 通用代码。
- 不为 showcase 私造平行玩法真相。

## 6. UAT

```gherkin
Feature: 通知面板小演示
  Scenario: RTS 玩家看懂并完成小目标
    Given 玩家进入 notification RTS profile
    When 玩家按第一屏提示完成一次操作
    Then 面板给出与配置文案一致的反馈
    And 玩家不需要阅读技术说明

  Scenario: 4X 玩家看懂并完成小目标
    Given 玩家进入 notification 4X profile
    When 玩家查看面板上的目标或进度语义
    Then 面板显示配置里的下一步与阻塞原因（如有）
    And 通用 Panel Kit 代码中不出现这些专有名词
```
