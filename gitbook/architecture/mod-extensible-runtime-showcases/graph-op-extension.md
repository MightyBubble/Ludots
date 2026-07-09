# Showcase: Graph Op Extension

## 概述

这个 showcase 展示一个 Mod 如何提供 GAS graph op，另一个 Mod 如何复用它。玩家进入地图后看到左右两个目标评分；点击 `Re-score Threat` 后，分数由 provider mod 的 op 重新计算。

它证明 provider 和 consumer 的职责可以拆开：provider 拥有 `CapabilityStandardGraphOpProviderMod.QueryThreat`，consumer 只在 graph 配置里引用这个 key，并把威胁分数放在目标实体的数据上。

## 结构

```text
CapabilityStandardGraphOpProviderMod/
  CapabilityStandardGraphOpProviderModEntry.cs
  Runtime/
    CapabilityStandardGraphOpThreatScore.cs

CapabilityStandardGraphOpExtensionShowcaseMod/
  assets/
    game.json
    Maps/
      capability_standard_graph_op_extension_showcase.json
    Configs/
      GAS/
        graphs/
          capability_standard.graph_op_extension.score_threat.json
```

`CapabilityStandardGraphOpProviderMod` 没有 Raylib preset。玩家启动的是 consumer root mod，launcher 会同时加载 provider。

## 详情

provider 在 `IMod.OnLoad` 注册 graph op：

```csharp
context.Extensions.Gas.RegisterGraphOp(
    "CapabilityStandardGraphOpProviderMod.QueryThreat",
    GraphValueType.Float,
    QueryThreat,
    GraphValueType.Entity);
```

consumer 的 graph shard 引用 provider key：

```json
{
  "id": "Graph.CapabilityStandard.GraphOpExtension.ScoreThreat",
  "nodes": [
    {
      "id": "target",
      "op": "LoadExplicitTarget",
      "next": "threat"
    },
    {
      "id": "threat",
      "op": "CapabilityStandardGraphOpProviderMod.QueryThreat",
      "inputs": ["target"]
    }
  ]
}
```

编译期通过 `GasGraphOpRegistry` 解析 op；执行期通过显式 `GasGraphOpHandlerTable` 调用 handler。provider handler 从目标实体读取 `CapabilityStandardGraphOpThreatScore`。这里没有静态 singleton，也不让 consumer 重新注册 provider 命名空间。

## 场景

玩家点击重算按钮时，consumer root mod 给左右两个目标写入各自的威胁分数，再对两个目标分别运行 graph。面板显示 `Left` 和 `Right` 分数，并记录事件来自 provider op。测试还会确认 graph program 已进入 `GraphProgramRegistry`。

## 边界

- consumer 可以引用 provider key，但不能注册 `CapabilityStandardGraphOpProviderMod.*`。
- graph op 注册必须发生在 graph 编译前。
- handler table 必须显式传入 graph 执行。
- provider op 需要 live target entity，且目标必须带 `CapabilityStandardGraphOpThreatScore`；缺失时必须失败。
- extension op 的输入最多三个，类型只允许 `Bool`、`Int`、`Float`、`Entity`。
- `TargetList` 是 VM 内部 scratch，不作为 Mod op 签名类型。

## UAT

```gherkin
Feature: 玩家看到左右目标重新评分

  Scenario: 点击后左右目标出现新评分
    Given 我启动 `capability_standard_graph_op_extension_showcase_raylib`
    And 地图显示左右两个目标
    When 我点击 `Re-score Threat`
    Then 面板显示评分已重新计算
    And 左右目标都显示新的评分
    And 高分目标被高亮

  Scenario: 玩家再次重算时看到评分变化
    Given 我已经点击过一次 `Re-score Threat`
    When 我再次点击 `Re-score Threat`
    Then 面板的 Actions 计数增加
    And 左右目标的分数发生变化
    And 高亮目标跟随更高分数切换
```
