# Showcase: Performer Command Extension

## 概述

这个 showcase 展示作者如何扩展 performer command。玩家进入地图后看到 `Performer Command Extension` 面板；点击 `Send Signal Ping` 后，按钮发布一个 presentation event，performer rule 把它路由到 Mod command handler，面板的 handled 数量变为 1。

它证明一次性表现命令可以由 Mod 注册、由 performer rule 触发、由正式 Performer runtime 执行。

## 结构

```text
CapabilityStandardPerformerCommandExtensionShowcaseMod/
  CapabilityStandardPerformerCommandExtensionShowcaseModEntry.cs
  assets/
    game.json
    Maps/
      capability_standard_performer_command_extension_showcase.json
    Configs/
      Presentation/
        performers/
          capability_standard.performer_command_extension.signal_rules.json
```

## 详情

Mod 启动时注册 command key，并声明 route：

```csharp
context.Extensions.Presentation.RegisterPerformerCommand(
    "CapabilityStandardPerformerCommandExtensionShowcaseMod.EmitSignalPing",
    new PerformerCommandExtensionDescriptor(
        PerformerCommandRouteStrategy.ExistingInstances,
        EmitSignalPing));
```

performer rule 通过 key 发出 extension command。loader 会把它编译为 `PerformerCommandKind.Extension` 和动态 `CommandKindId`，并校验 JSON route 与注册 descriptor 一致。

运行时流程：

1. root mod 创建 owner entity。
2. root mod 通过 `PerformerCommandBuffer` 创建 performer 实例。
3. 玩家点击按钮，root mod 向 `PresentationEventStream` 发布 gameplay event。
4. `PerformerRuleSystem` 命中 rule，并向 command buffer 写入 extension command。
5. `PerformerRuntimeSystem` 使用 ExistingInstances route 找到 performer，并调用 Mod command handler。
6. 面板显示 handled 计数。

## 场景

玩家看到的是一次“发送信号后被处理”的即时反馈。作者看到的是事件、rule、command buffer、runtime handler 的完整正式链路。

## 边界

- extension command 必须声明 route。
- JSON rule 的 route 必须与注册 descriptor 完全一致。
- command handler 不能绕过 `PerformerCommandBuffer` 和 `PerformerRuntimeSystem`。
- builtin command 不能写 extension route override。
- 未注册 key、route 不匹配、没有 routed performer 时必须失败。

## UAT

```gherkin
Feature: 玩家点击信号按钮后看到信号被处理

  Scenario: Signal Ping 点击后处理计数增加
    Given 我启动 `capability_standard_performer_command_extension_showcase_raylib`
    And 地图显示 Performer Command Extension 面板
    When 我点击 `Send Signal Ping`
    Then 面板显示信号已被处理
    And 面板的 Handled 数量变为 1

  Scenario: 玩家连续发送信号时处理次数继续增加
    Given 面板的 Handled 数量已经变为 1
    When 我再次点击 `Send Signal Ping`
    Then 面板的 Actions 计数增加
    And 面板的 Handled 数量继续增加
    And 面板仍提示信号被处理
```
