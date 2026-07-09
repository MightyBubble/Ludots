# Showcase: Graph Op Extension

## 概述

这个案例展示一个 Mod 如何提供 GAS graph op, 另一个 Mod 如何复用它。玩家视角是: 威胁系统 Mod 装上后, 火法的自动施法会优先打高威胁目标, 但火法 Mod 不需要复制威胁计算代码。

## 结构

```text
ThreatProviderMod/
  ThreatProviderModEntry.cs

ArcMageMod/
  assets/
    Configs/
      GAS/
        graphs/
          arc_mage.score_threat.json
```

## 详情

提供方在 `OnLoad` 注册 graph op:

```csharp
public void OnLoad(IModContext context)
{
    context.Extensions.Gas.RegisterGraphOp(
        "ThreatProviderMod.QueryThreat",
        GraphValueType.Float,
        QueryThreat,
        GraphValueType.Entity);
}
```

handler 使用 VM op 签名:

```csharp
private static void QueryThreat(
    ref GraphExecutionState state,
    in GraphInstruction instruction,
    ref int pc)
{
    Entity target = state.E[instruction.A];
    state.F[instruction.Dst] = ReadThreatScore(target);
}
```

消费方只在 graph 配置里引用 provider key:

```json
[
  {
    "id": "ArcMage.ScoreThreat",
    "kind": "Score",
    "entry": "target",
    "nodes": [
      { "id": "target", "op": "LoadExplicitTarget", "next": "threat" },
      { "id": "threat", "op": "ThreatProviderMod.QueryThreat", "inputs": [ "target" ] }
    ],
    "outputs": [
      {
        "id": "score",
        "destination": "Summary",
        "type": "Float",
        "source": "threat",
        "key": "threat"
      }
    ]
  }
]
```

扩展 op 的输出只能是 `Void`, `Bool`, `Int`, `Float`, `Entity`; 输入最多 3 个, 类型只能是 `Bool`, `Int`, `Float`, `Entity`。`TargetList` 是 VM 内部 scratch 结构, 不能作为 Mod op 的注册签名。

## 场景

1. 玩家安装 `ThreatProviderMod` 和 `ArcMageMod`。
2. `ThreatProviderMod` 注册 `ThreatProviderMod.QueryThreat`。
3. `ArcMageMod` 的 graph shard 引用这个 op。
4. 自动施法评分时, 火法图能拿到威胁分。
5. 如果只安装 `ArcMageMod` 而缺 provider, graph 编译失败。

## 边界

- consumer Mod 可以引用 provider key, 不能注册 provider 命名空间。
- graph op 注册发生在配置编译前, 运行中不能补注册。
- handler table 必须显式传入 graph 执行, 不使用静态 singleton。
- op 签名在注册时校验, 不把错误推迟到第一场战斗。

## UAT

```gherkin
Feature: 一个 Mod 提供 graph op, 另一个 Mod 复用它

  Scenario: 火法 Mod 复用威胁评分 op
    Given `ThreatProviderMod` 注册 `ThreatProviderMod.QueryThreat`
    And `ArcMageMod` 的 graph 节点引用 `ThreatProviderMod.QueryThreat`
    When 游戏编译 GAS graph
    Then `ArcMage.ScoreThreat` 编译成功
    And 玩家释放自动施法时高威胁目标获得更高评分

  Scenario: 消费方抢注提供方命名空间
    Given 当前正在加载 `ArcMageMod`
    When 它尝试注册 `ThreatProviderMod.OtherOp`
    Then 启动失败并提示只能注册 `ArcMageMod.` 命名空间下的 key
```
