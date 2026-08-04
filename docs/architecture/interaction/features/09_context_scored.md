# Feature: Context-Scored Abilities (N1–N8)

> 本文只描述当前分支的真实 runtime 形态，以及在该形态上仍未闭环的 checklist 项。
> 分支验证边界以 `artifacts/acceptance/interaction-showcase/feature_coverage_matrix.md` 为准。

---

## 1. 当前状态

`ContextGroup` 与 `ContextScored` 已经是 `feat/mod-interaction-showcase` 的 runtime infrastructure，不再是设计提案。

已实现并有证据的路径：

- 注册与配置：`src/Core/Gameplay/GAS/ContextGroupRegistry.cs`、`src/Core/Gameplay/GAS/Config/ContextGroupConfigLoader.cs`
- 解析与路由：`src/Core/Input/Orders/ContextScoredOrderResolver.cs`、`src/Core/Input/Orders/InputOrderMappingSystem.cs`
- 接入本地输入：`mods/CoreInputMod/Systems/LocalOrderSourceHelper.cs`
- 测试与 acceptance：`src/Tests/GasTests/ContextScoredResolverTests.cs`、`src/Tests/GasTests/Production/InteractionShowcasePlayableAcceptanceTests.cs`、`artifacts/acceptance/interaction-showcase/path.mmd`

---

## 2. Checklist 覆盖状态

| Slice | 当前状态 | 说明 |
|------|----------|------|
| N1 自动选目标 | `implemented + verified` | 已由 `ContextScoredOrderResolver` 返回 concrete slot + target，并在 `ContextScoredResolverTests.cs` 验证 |
| N2 距离决定 | `implemented + verified` | `searchRadiusCm`、`maxDistanceCm`、`distanceWeight` 已进入 runtime candidate 定义 |
| N3 自身状态决定 | `runtime-ready, not showcase-closed` | 可由 precondition graph / tag 查询表达，但当前 showcase 不是完整闭环 |
| N4 目标状态决定 | `implemented + verified` | `ContextScoredResolverTests.cs` 已验证 downed target 导向 finisher slot |
| N5 环境决定 | `design backlog on existing runtime` | 现有 runtime 可扩展，但本分支未给出完整 acceptance |
| N6 连击仪表决定 | `design backlog on existing runtime` | 应复用 tag gate 或 validation graph primitive，不新增平行系统 |
| N7 摇杆偏移 | `partial runtime only` | 当前 runtime 内建的是 `HoveredBiasScore`；通用 stick bias 仍属后续 authoring/backlog |
| N8 锁定辅助 | `design backlog` | 现有分支未把 lock-on override 闭环到 acceptance |

---

## 3. 当前 runtime 形态

### 3.1 输入侧不是 `contextGroupId`，而是 root slot

当前输入映射并没有单独的 `contextGroupId` 字段。

实际做法是：

- `InputOrderMapping.ArgsTemplate.I0` 指向 root ability slot
- `ContextScoredOrderResolver` 从 slot 解析出 root ability
- `ContextGroupRegistry.TryGetByRootAbility(...)` 找到候选组
- 解析出 concrete slot index 与 concrete target

对应实现：

- `src/Core/Input/Orders/InputOrderMappingSystem.cs`
- `src/Core/Input/Orders/ContextScoredOrderResolver.cs`
- `src/Core/Gameplay/GAS/ContextGroupRegistry.cs`

### 3.2 ContextGroup 的真实数据形态

当前候选定义已经落地为 runtime struct，而不是文档中的占位设计：

```csharp
ContextGroupCandidate:
  AbilityId
  PreconditionGraphId
  ScoreGraphId
  BasePriority
  MaxDistanceCm
  DistanceWeight
  MaxAngleDeg
  AngleWeight
  HoveredBiasScore
  RequiresTarget

ContextGroupDefinition:
  SearchRadiusCm
  Candidates[]
```

真实代码见：

- `src/Core/Gameplay/GAS/ContextGroupRegistry.cs`
- `src/Core/Gameplay/GAS/Config/ContextGroupConfigLoader.cs`

### 3.3 解析流程

`ContextScoredOrderResolver` 的当前分支流程：

1. 从 `ArgsTemplate.I0` 取 root slot
2. `TryGetByRootAbility(...)` 找到 `ContextGroupDefinition`
3. 在 `searchRadiusCm` 内做空间查询
4. 逐 candidate / target 计算内建距离、角度、hovered bias
5. 如配置了 `PreconditionGraphId`，调用 `GraphExecutor.ExecuteValidation(...)`
6. 如配置了 `ScoreGraphId`，叠加 score graph 输出
7. 返回最终 concrete slot 与 concrete target

对应实现：

- `src/Core/Input/Orders/ContextScoredOrderResolver.cs`
- `src/Core/NodeLibraries/GASGraph/GraphExecutor.cs`

### 3.4 与 ability activation 的边界

`ContextScored` 只负责把上下文解析成具体 `(ability, target)`，并不负责替代 ability activation。

实际边界：

- candidate precondition 使用最小 validation graph primitive
- 具体 ability 真正激活时仍然走 `RequiredAll` / `BlockedAny`

对应实现：

- `src/Core/Gameplay/GAS/Systems/AbilityExecSystem.cs`
- `src/Core/Gameplay/GAS/Systems/AbilityExecSystem.cs`
- `src/Core/Gameplay/GAS/Components/AbilityActivationBlockTags.cs`

这意味着 ContextScored family 不应引出新的 `AbilityConditionSystem`。

---

## 4. Authoring 方式

### 4.1 Context group config

当前 loader 读取的是 `GAS/context_groups.json`，而不是 input mapping 内联整组候选。

示意形态：

```json
{
  "id": "commander_attack",
  "rootAbilityId": "commander_attack_root",
  "searchRadiusCm": 600,
  "candidates": [
    {
      "abilityId": "commander_light_attack",
      "basePriority": 10,
      "maxDistanceCm": 300,
      "maxAngleDeg": 120,
      "requiresTarget": true
    },
    {
      "abilityId": "commander_finisher",
      "preconditionGraph": "target_downed_only",
      "scoreGraph": "finisher_bonus",
      "basePriority": 0,
      "maxDistanceCm": 300,
      "requiresTarget": true
    }
  ]
}
```

字段来源见 `src/Core/Gameplay/GAS/Config/ContextGroupConfigLoader.cs`。

### 4.2 Input mapping

input mapping 仍然只声明 root cast：

```json
{
  "actionId": "Attack",
  "trigger": "PressedThisFrame",
  "orderTypeKey": "castAbility",
  "argsTemplate": { "i0": 0 },
  "targetType": "Entity",
  "isSkillMapping": true
}
```

真正的 ContextScored 解析由 provider 注入，而不是 input config 自带候选数组。

---

## 5. 当前 showcase / runtime 证明了什么

本分支已经证明：

- ContextScored 会把 root slot 解析为具体 candidate slot，而不是停留在设计概念
- precondition graph 与 score graph 已进入真实 runtime
- ContextGroup 可以作为后续扩展的共享底座继续长大

集中证据：

- `src/Tests/GasTests/ContextScoredResolverTests.cs`
- `src/Tests/GasTests/Production/InteractionShowcasePlayableAcceptanceTests.cs`
- `artifacts/acceptance/interaction-showcase/feature_coverage_matrix.md`
- `artifacts/acceptance/interaction-showcase/path.mmd`

---

## 6. 仍然未闭环的 backlog

以下需求不应被写成“当前分支已实现”：

- 通用 lock-on override
- 通用 input-direction score factor
- 大规模环境候选 authoring 套件
- AI 全面复用 `ContextGroup` 决策
- 将全部 N1–N8 场景都接入 acceptance

这些后续工作都应复用现有 `ContextGroupRegistry` 与 `ContextScoredOrderResolver`，而不是把路由搬进 `AbilityDefinition`。
