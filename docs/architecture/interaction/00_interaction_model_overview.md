# Ability Interaction Model — Architecture Overview

> **SSOT**: 交互层架构总览。
> 本文只定义当前分支已经确认的交互原语、运行时边界与 backlog 边界，不把 design catalog 自动等同于 shipped runtime。

---

## 1. 当前分支事实边界

截至 `feat/mod-interaction-showcase` / `94f5277`，以下能力已经是分支内的真实 runtime，而不是待实现提案：

- `ContextGroup` 运行时基础设施：`src/Core/Gameplay/GAS/ContextGroupRegistry.cs`、`src/Core/Gameplay/GAS/Config/ContextGroupConfigLoader.cs`
- `ContextScored` 输入路由：`src/Core/Input/Orders/ContextScoredOrderResolver.cs`、`src/Core/Input/Orders/InputOrderMappingSystem.cs`
- form-based ability routing：`src/Core/Gameplay/GAS/AbilityFormSetRegistry.cs`、`src/Core/Gameplay/GAS/Config/AbilityFormSetConfigLoader.cs`、`src/Core/Gameplay/GAS/Systems/AbilityFormRoutingSystem.cs`
- ability 激活 tag gate：`src/Core/Gameplay/GAS/Components/AbilityActivationBlockTags.cs`、`src/Core/Gameplay/GAS/Systems/AbilitySystem.cs`、`src/Core/Gameplay/GAS/Systems/AbilityExecSystem.cs`
- 最小 validation graph primitive：`src/Core/NodeLibraries/GASGraph/GraphExecutor.cs`
- 分支验证边界：`src/Tests/GasTests/ContextScoredResolverTests.cs`、`src/Tests/GasTests/InputOrderAbilityAuditTests.cs`、`src/Tests/GasTests/Production/InteractionShowcasePlayableAcceptanceTests.cs`、`artifacts/acceptance/interaction-showcase/feature_coverage_matrix.md`

本文不把下列内容描述成已闭环：

- 并行的 `AbilityConditionSystem`
- 通用的 cooldown / charges attribute-tick baseline
- 覆盖全部 `docs/architecture/interaction/features/` 目录的 runtime 实现

---

## 2. 核心原则

**交互层的职责只有一件事**：把玩家输入转换成 GAS 可执行的具体 `(ability, target)`。

交互层不负责：

- 伤害计算
- 状态生命周期
- 资源扣减
- 冷却递减
- effect 过期条件

这些都属于 Tag / Effect / Attribute / Graph / Handler 组合层。

---

## 3. 三轴模型

### Axis 1: InputConfig

```yaml
InputConfig:
  ReactsTo: Down | Up | DownAndUp
```

- `Down`: 瞬发、toggle、channel start、recast
- `DownAndUp`: hold-release、charge、indicator release
- `Up`: 少数特殊释放

`HoldRelease` 不是单独原语。按住期间的积累仍然由 Effect tick 写 Attribute。

### Axis 2: TargetMode

```csharp
enum TargetMode
{
    None,
    Unit,
    Point,
    Direction,
    Vector
}
```

### Axis 3: Acquisition

```csharp
enum Acquisition
{
    Explicit,
    ContextScored
}
```

- `Explicit`: 玩家直接给目标或位置
- `ContextScored`: 系统根据候选、空间关系、graph 与权重自动解析出具体 `(ability, target)`

---

## 4. Ludots 映射

### InteractionModeType → Acquisition

| Ludots | Acquisition | 当前状态 |
|--------|-------------|----------|
| `TargetFirst` | `Explicit` | 已有 |
| `SmartCast` | `Explicit` | 已有 |
| `AimCast` | `Explicit` | 已有 |
| `SmartCastWithIndicator` | `Explicit` | 已有 |
| `ContextScored` | `ContextScored` | 已落地，见 `src/Core/Input/Orders/InputOrderMapping.cs`、`src/Core/Input/Orders/InputOrderMappingSystem.cs` |

### OrderSelectionType → TargetMode

| Ludots | TargetMode |
|--------|------------|
| `None` | `None` |
| `Entity` | `Unit` |
| `Position` | `Point` |
| `Direction` | `Direction` |
| `Vector` | `Vector` |
| `Entities` | `Unit` multi-select |

---

## 5. Activation Boundary

当前分支的 activation boundary 已经固定为两层，不再扩成第三套平行系统。

### 5.1 离散阻塞：tag gate

ability 激活阶段只读两类 gate：

- `RequiredAll`
- `BlockedAny`

实际执行点：

- `src/Core/Gameplay/GAS/Systems/AbilitySystem.cs`
- `src/Core/Gameplay/GAS/Systems/AbilityExecSystem.cs`
- `src/Core/Gameplay/GAS/Config/AbilityExecLoader.cs`

这层适合表达：

- silenced / stunned / rooted 之类的离散阻塞
- combo stage、form、finisher-ready 之类的离散前置
- cooldown ready / charge ready / mana ready 这类已经被投影成 tag 的结果

### 5.2 数值 / 上下文检查：最小 validation graph primitive

当前分支已经有最小 validation primitive：`src/Core/NodeLibraries/GASGraph/GraphExecutor.cs`

它当前已被复用在相邻边界，而不是再造 `AbilityConditionSystem`：

- `src/Core/Input/Orders/ContextScoredOrderResolver.cs`：候选 precondition graph
- `src/Core/Gameplay/GAS/Systems/OrderBufferSystem.cs`：order validation graph

因此本仓库的架构结论是：

- discrete blocking 继续走 `RequiredAll` / `BlockedAny`
- numeric / spatial / context 检查复用现有 validation graph primitive
- 如未来确实需要 ability-side numeric gate，也应扩展这一 primitive，而不是创建平行 `AbilityConditionSystem`

### 5.3 明确不属于 activation boundary 的机制

- `GasConditionRegistry` 只服务 effect 生命周期与 expire condition：`src/Core/Gameplay/GAS/GasConditionRegistry.cs`、`src/Core/Gameplay/GAS/Systems/EffectLifetimeSystem.cs`
- cooldown / charges 不要求统一落成 attribute-tick baseline
- `ContextGroup` 不写回 `AbilityDefinition`，而是独立注册与解析

---

## 6. Tag / Effect / Attribute / ContextGroup 职责分界

| 概念 | 所属层 | 当前分支表达 |
|------|--------|--------------|
| 蓄力积累 | Effect + Attribute | tick 写 `charge_amount` 等属性 |
| 连击段数 | Tag | `combo_stage` 等 tag |
| 变身/姿态 | Tag + Form Routing | form tag 经 `AbilityFormRoutingSystem` 解析为 form slot override，再由 `AbilitySlotResolver` 统一转成实际 ability |
| 资源数值 | Attribute | mana / rage / charges / hp 等原始数值 |
| ability 离散门控 | Tag gate | `RequiredAll` / `BlockedAny` |
| 数值准入 | validation graph 或 tag bridge | graph 直接判定，或先投影为 ready tag |
| 冷却 | tag duration 优先 | 见 `src/Tests/GasTests/InputOrderAbilityAuditTests.cs` 与 `artifacts/acceptance/interaction-showcase/feature_coverage_matrix.md` |
| ContextScored 路由 | ContextGroup | 候选集合、距离/角度/hovered bias、precondition graph、score graph |
| effect 生命周期条件 | GasCondition | 非 ability activation |

---

## 7. ContextGroup Runtime Flow

当前分支没有把 `ContextGroupId` 塞进 `AbilityDefinition`。真实路径是：

```text
Input mapping root slot (ArgsTemplate.I0)
  -> resolve root ability
  -> ContextGroupRegistry.TryGetByRootAbility(...)
  -> spatial query in search radius
  -> per-candidate distance / angle / hovered bias
  -> optional precondition graph
  -> optional score graph
  -> concrete slot index + concrete target
  -> normal ability execution
```

关键实现路径：

- `src/Core/Gameplay/GAS/ContextGroupRegistry.cs`
- `src/Core/Gameplay/GAS/Config/ContextGroupConfigLoader.cs`
- `src/Core/Input/Orders/ContextScoredOrderResolver.cs`
- `src/Core/Input/Orders/InputOrderMappingSystem.cs`
- `mods/CoreInputMod/Systems/LocalOrderSourceHelper.cs`

分支验证路径：

- `src/Tests/GasTests/ContextScoredResolverTests.cs`
- `src/Tests/GasTests/Production/InteractionShowcasePlayableAcceptanceTests.cs`
- `artifacts/acceptance/interaction-showcase/path.mmd`
- `artifacts/acceptance/interaction-showcase/feature_coverage_matrix.md`

---

## 8. Form Routing Runtime Flow

当前分支的 form/stance 路由不占用新的 activation runtime，而是挂在已有 slot 解析链路上：

```text
entity AbilityFormSetRef
  -> AbilityFormRoutingSystem (InputCollection)
  -> match route by effective tags + priority
  -> write AbilityFormSlotBuffer
  -> AbilitySlotResolver (Granted > Form > Base)
  -> AbilitySystem / AbilityExecSystem / ContextScored / Indicator
```

Template boundary:
- `AbilityStateBuffer` and `AbilityFormSetRef` belong to the unit template baseline.
- `PlayerOwner`, `WorldPositionCm`, and other scene ownership facts belong to map entity overrides or runtime spawn requests.

关键实现路径：

- `src/Core/Gameplay/GAS/AbilityFormSetRegistry.cs`
- `src/Core/Gameplay/GAS/Config/AbilityFormSetConfigLoader.cs`
- `src/Core/Gameplay/GAS/Systems/AbilityFormRoutingSystem.cs`
- `src/Core/Gameplay/GAS/Components/AbilityStateBuffer.cs`

---

## 9. Showcase Runtime 与 Design Backlog 的边界

已由当前分支证明的切片，以 `artifacts/acceptance/interaction-showcase/feature_coverage_matrix.md` 为准。

这意味着：

- 可以把 `ContextGroup` 当成真实基础设施继续扩展
- 可以把 tag gate 当成真实 activation baseline
- 可以把 `GraphExecutor.ExecuteValidation(...)` 当成现有最小 validation primitive

但不应自动推导：

- 所有 finisher / companion / environment / response-window 家族都已实现
- cooldown / charges 必须走统一 attribute tick 系统
- 应该把数值门控从现有 graph primitive 拆成新的 condition runtime

---

## 10. 相关文档

- 交互 backlog 与分支校准差异：`docs/architecture/interaction/99_gap_analysis.md`
- ContextScored 专项：`docs/architecture/interaction/features/09_context_scored.md`
- Finisher / Companion / Special Input / Resource / Environment：`docs/architecture/interaction/features/14_finisher_companion_special_resource_env.md`
- 分支验证边界：`artifacts/acceptance/interaction-showcase/feature_coverage_matrix.md`
