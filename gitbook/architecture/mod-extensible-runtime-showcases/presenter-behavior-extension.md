# Showcase: Presenter Behavior Extension

## 概述

这个 showcase 展示作者如何扩展 presenter behavior。玩家进入地图后会看到 `Presenter Behavior Extension` 面板；CloudDrift presenter 被创建后持续 tick，面板上的 tick 数会增长。点击 `Focus Cloud Drift` 时，面板继续显示行为仍在正式 Presenter behavior 系统里运行。

它证明表现行为可以由 Mod 注册并被 presenter 数据复用，而不是新增 Core behavior enum。

## 结构

```text
CapabilityStandardPresenterBehaviorExtensionShowcaseMod/
  CapabilityStandardPresenterBehaviorExtensionShowcaseModEntry.cs
  assets/
    game.json
    Maps/
      capability_standard_presenter_behavior_extension_showcase.json
    Configs/
      Presentation/
        presenters/
          capability_standard.presenter_behavior_extension.cloud_banner.json
```

## 详情

Mod 启动时注册 behavior key，并声明执行 lane：

```csharp
context.Extensions.Presentation.RegisterPresenterBehavior(
    "CapabilityStandardPresenterBehaviorExtensionShowcaseMod.CloudDrift",
    new PresenterBehaviorExtensionDescriptor(
        PresenterBehaviorExecutionLane.ContinuousTick,
        RunCloudDrift));
```

presenter shard 通过 key 引用 behavior。loader 会把它编译为 `BehaviorKind.Extension` 和动态 `KindId`，并校验配置 lane 与注册 descriptor 一致。

运行时流程：

1. 地图加载后 root mod 创建 owner entity。
2. root mod 通过 `PresenterCommandBuffer` 发出 `CreatePresenter`。
3. `PresenterRuntimeSystem` 创建 presenter 实例。
4. `PresenterBehaviorSystem` 在 ContinuousTick lane 调用 CloudDrift handler。
5. handler 写入 presenter param，UI 面板显示 tick 数。

## 场景

玩家看到的是一块会持续变化的展示目标。作者看到的是 behavior slot 如何从 JSON 进入正式 presenter 定义，再通过 Presenter behavior 系统执行。

## 边界

- extension behavior 必须注册 execution lane。
- presenter 定义必须引用已注册的 Mod key。
- behavior 不能绕过 presenter runtime 自己开平行表现管线。
- builtin behavior 不能携带 extension lane 或 extension kind id。
- 未注册 key 或 lane 不匹配必须在加载期失败。

## UAT

```gherkin
Feature: 玩家看到 CloudDrift 持续运行

  Scenario: CloudDrift 在地图加载后自动运行
    Given 我启动 `capability_standard_presenter_behavior_extension_showcase_raylib`
    When 地图加载完成
    Then 我能看到 Presenter Behavior Extension 面板
    And 面板的 Ticks 数量大于 0
    And 面板事件说明 CloudDrift 正在运行

  Scenario: 点击聚焦按钮后行为仍继续运行
    Given CloudDrift 已经开始运行
    When 我点击 `Focus Cloud Drift`
    Then 面板显示 CloudDrift 事件
    And 面板的 Actions 计数变为 1

  Scenario: 玩家停留在地图上时行为持续增长
    Given 我正在观看 Presenter Behavior Extension 面板
    When 我等待几帧
    Then 面板的 Ticks 数量继续增长
    And CloudDrift 仍显示为正在运行
```
