# Showcase: Graph Op Extension

## 概述

这个 showcase 给第一次写 Mod 的人看一件事：别人登记的评分公式，你这边拿来就能用。

玩家进地图后能看到左右两个目标。点 `Re-score Threat`，左右分数会换成新的一组，高分那边被点亮。分数不是面板自己写的，而是 consumer 的打分图调用了 provider 登记的 `CapabilityStandardGraphOpProviderMod.QueryThreat`。

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
    GAS/
      graphs/
        capability_standard.graph_op_extension.score_threat.json
```

`CapabilityStandardGraphOpProviderMod` 没有独立启动器房间。玩家启动的是 consumer 这一间，启动器会把 provider 一起加载。

## 详情

provider 在 `IMod.OnLoad` 登记图算子：

```csharp
context.Extensions.Gas.RegisterGraphOp(
    "CapabilityStandardGraphOpProviderMod.QueryThreat",
    GraphValueType.Float,
    fixedRegister: 0,
    QueryThreat,
    GraphValueType.Entity);
```

consumer 的打分图只引用这个键，走现行控制流边：

```json
{
  "id": "Graph.CapabilityStandard.GraphOpExtension.ScoreThreat",
  "kind": "Score",
  "entry": "target",
  "nodes": [
    { "id": "target", "op": "LoadExplicitTarget" },
    { "id": "threat", "op": "CapabilityStandardGraphOpProviderMod.QueryThreat" }
  ],
  "controlEdges": [
    { "from": "target", "fromPort": "next", "to": "threat" }
  ],
  "valueEdges": [
    { "from": "target", "fromPort": "value", "to": "threat", "toPort": "a" }
  ]
}
```

编译期 `GasGraphOpRegistry` 按名解析；执行期走引擎注入的 `GasGraphOpHandlerTable`。handler 从目标实体读 `CapabilityStandardGraphOpThreatScore`。consumer 不能再登记 `CapabilityStandardGraphOpProviderMod.*`。

## 场景

玩家点重算时，consumer 给左右目标写入不同威胁分，再对两个目标各跑一遍打分图。面板显示 `Left` / `Right`，并记下这次分数来自 provider 公式。测试会确认图已经进 `GraphProgramRegistry`。

## 边界

- consumer 可以引用 provider 键，但不能登记 `CapabilityStandardGraphOpProviderMod.*`。
- 图算子必须在图编译前登记。
- 执行必须带上引擎里那张 handler 表，不能走空表。
- 目标必须是活实体，并且带 `CapabilityStandardGraphOpThreatScore`；缺了就失败。
- 扩展算子最多三个输入，类型只允许 `Bool`、`Int`、`Float`、`Entity`。
- 查询图不许挂扩展算子。
- 作者图必须写 `controlEdges` / `valueEdges`，不能再写 `nodes[].next`。

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
