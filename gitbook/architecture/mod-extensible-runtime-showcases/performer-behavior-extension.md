# Showcase: Performer Behavior Extension

## 概述

这个案例展示用户如何扩展 performer behavior。玩家视角是: 地图上的云旗、火焰标记或能量护盾会持续变化, 这些表现不是 Core 内置行为, 而是 Mod 自己注册的 behavior。

## 结构

```text
WeatherMod/
  WeatherModEntry.cs
  assets/
    Configs/
      Presentation/
        performers/
          weather.cloud_banner.json
```

## 详情

Mod 启动时注册 behavior kind:

```csharp
public void OnLoad(IModContext context)
{
    context.Extensions.Presentation.RegisterPerformerBehavior(
        "WeatherMod.CloudDrift",
        new PerformerBehaviorExtensionDescriptor(
            PerformerBehaviorExecutionLane.ContinuousTick,
            TickCloudDrift));
}
```

handler 进入现有 Performer behavior 系统:

```csharp
private static void TickCloudDrift(in PerformerBehaviorExecutionContext context)
{
    // 只更新该 behavior 负责的表现状态。
}
```

performer 定义通过 shard 引用注册 key:

```json
[
  {
    "id": "Weather.CloudBanner",
    "behaviors": [
      {
        "slot": "body",
        "kind": "WeatherMod.CloudDrift",
        "activeByDefault": true,
        "execution": { "lane": "ContinuousTick" }
      }
    ]
  }
]
```

`kind` 不是 `BehaviorKind.Extension` 字符串, 而是 Mod 注册的 key。loader 会把它解析成 `BehaviorKind.Extension`, 写入 resolved `KindId`, 并校验 `execution.lane` 与注册 descriptor 一致。

## 场景

1. 玩家进入天气 showcase 地图。
2. 地图生成 `Weather.CloudBanner` performer。
3. `body` behavior slot 每帧执行 `WeatherMod.CloudDrift`。
4. 云旗随风漂移, 但它仍然走 Performer behavior 系统。
5. 其他 Mod 可以复用 `WeatherMod.CloudDrift` 制作自己的云旗 performer。

## 边界

- 扩展 behavior 必须声明执行 lane。
- dirty lane 必须声明触发源, 例如属性变化或 tag 变化。
- behavior 不能绕过 performer runtime 自己开并行表现管线。
- builtin behavior 不能携带 extension lane 或 extension trigger。
- 未注册 key 必须在 performer 定义加载时失败。

## UAT

```gherkin
Feature: Mod 作者添加可复用 performer behavior

  Scenario: 云旗 performer 使用 Mod behavior 持续漂移
    Given `WeatherMod` 注册 `WeatherMod.CloudDrift`
    And `Weather.CloudBanner` 的 behavior 使用 `WeatherMod.CloudDrift`
    When 玩家进入天气 showcase 地图
    Then 云旗 performer 出现在场景中
    And 云旗持续漂移
    And 该更新由 Performer behavior 系统执行

  Scenario: performer definition 漏写 execution lane
    Given `Weather.CloudBanner` 使用 `WeatherMod.CloudDrift`
    But behavior 没有写 `execution.lane`
    When 游戏加载 performer 定义
    Then 启动失败并提示 extension performer behavior 需要 execution
```
