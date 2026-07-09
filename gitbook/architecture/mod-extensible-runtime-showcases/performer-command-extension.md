# Showcase: Performer Command Extension

## 概述

这个案例展示用户如何扩展 performer command。玩家视角是: 施法瞬间地面出现提示环, 或命中时弹出一次性表现, 这些命令由 Mod 注册, 再被 performer rule 触发。

## 结构

```text
SignalMod/
  SignalModEntry.cs
  assets/
    Configs/
      Presentation/
        performers/
          signal.cast_ping_rule.json
```

## 详情

Mod 启动时注册 command kind, 并明确 route:

```csharp
public void OnLoad(IModContext context)
{
    context.Extensions.Presentation.RegisterPerformerCommand(
        "SignalMod.SpawnCastPing",
        new PerformerCommandExtensionDescriptor(
            PerformerCommandRouteStrategy.SingleRuntime,
            SpawnCastPing));
}
```

handler 由现有 Performer runtime 执行:

```csharp
private static void SpawnCastPing(in PerformerCommandExecutionContext context)
{
    // 根据 command payload 和事件上下文生成提示表现。
}
```

performer rule 通过 `kind` 引用注册 key, 并写出同一个 route:

```json
[
  {
    "id": "Signal.CastPingRules",
    "rules": [
      {
        "event": { "kind": "CastCommitted", "key": "Ability.ArcMage.EmberBolt" },
        "condition": { "inline": "SourceHasVisualTransform" },
        "command": {
          "kind": "SignalMod.SpawnCastPing",
          "route": "SingleRuntime",
          "scopeTag": "signal.cast_ping"
        }
      }
    ]
  }
]
```

loader 会把注册 key 解析成 `PerformerCommandKind.Extension` 和 resolved `CommandKindId`。`route` 必须和注册 descriptor 完全一致, 因为 rule system 需要在发命令前知道它是单运行时命令、已有 performer 命令、作用域 performer 命令, 还是创建/销毁类命令。

## 场景

1. 玩家释放 `Ember Bolt`。
2. `CastCommitted` 事件触发 `Signal.CastPingRules`。
3. rule 发出 `SignalMod.SpawnCastPing` 命令。
4. Performer runtime 调用 `SignalMod` 的 handler。
5. 玩家看到地面提示环, 不需要 Core 新增 command enum。

## 边界

- 扩展 command 必须声明 route。
- JSON 里的 `route` 必须匹配注册 descriptor。
- command handler 不绕过 Performer command buffer 和 runtime。
- builtin command 不写 extension route override。
- 未注册 key 或 route 不匹配必须在加载期失败。

## UAT

```gherkin
Feature: Mod 作者添加可复用 performer command

  Scenario: 施法事件触发 Mod 自定义提示环命令
    Given `SignalMod` 注册 `SignalMod.SpawnCastPing` 并声明 route 为 `SingleRuntime`
    And performer rule 在 `CastCommitted` 时发出 `SignalMod.SpawnCastPing`
    When 玩家释放 `Ability.ArcMage.EmberBolt`
    Then 地面出现一次性施法提示环
    And Core 不需要新增 performer command enum

  Scenario: rule 写错 route
    Given `SignalMod.SpawnCastPing` 注册 route 为 `SingleRuntime`
    But performer rule 写成 `ExistingInstances`
    When 游戏加载 performer 定义
    Then 启动失败并指出 rule route 与注册 route 不匹配
```
