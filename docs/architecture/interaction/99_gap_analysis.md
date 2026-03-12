# Interaction Gap Analysis — Branch-Calibrated

> 基线：`feat/mod-interaction-showcase` / `94f5277`
> 本文只记录在当前分支事实之上仍然成立的 gap，不再把已落地 runtime 写成待实现提案。

---

## 1. 已关闭的误判

以下项目在当前分支已经不应再视为 gap：

| 项目 | 当前结论 | 证据 |
|------|----------|------|
| `ContextScored Acquisition 不存在` | 已关闭。`ContextScored` 已进入 `InteractionModeType` 与 `InputOrderMappingSystem` | `src/Core/Input/Orders/InputOrderMapping.cs`、`src/Core/Input/Orders/InputOrderMappingSystem.cs` |
| `ContextGroup -> ability dispatch` | 已关闭。真实运行时为 registry + loader + resolver 链路 | `src/Core/Gameplay/GAS/ContextGroupRegistry.cs`、`src/Core/Gameplay/GAS/Config/ContextGroupConfigLoader.cs`、`src/Core/Input/Orders/ContextScoredOrderResolver.cs` |
| `AbilityActivationRequireTags 不存在` | 已关闭。当前仓库使用 `AbilityActivationBlockTags.RequiredAll` / `BlockedAny` | `src/Core/Gameplay/GAS/Components/AbilityActivationBlockTags.cs`、`src/Core/Gameplay/GAS/Systems/AbilitySystem.cs`、`src/Core/Gameplay/GAS/Systems/AbilityExecSystem.cs` |
| `Form-based ability routing 不存在` | 已关闭。当前分支已有 form-set registry + loader + routing system，并通过分层 effective slot 解析接入输入/执行/indicator/context 路径 | `src/Core/Gameplay/GAS/AbilityFormSetRegistry.cs`、`src/Core/Gameplay/GAS/Config/AbilityFormSetConfigLoader.cs`、`src/Core/Gameplay/GAS/Systems/AbilityFormRoutingSystem.cs`、`src/Core/Gameplay/GAS/Components/AbilityStateBuffer.cs` |
| `CooldownTickSystem 是前置` | 已关闭。当前分支不需要通用 cooldown tick baseline | `src/Tests/GasTests/InputOrderAbilityAuditTests.cs`、`artifacts/acceptance/interaction-showcase/feature_coverage_matrix.md` |

---

## 2. 已固定的架构边界

### 2.1 ContextGroup 不进入 AbilityDefinition

当前分支的 `ContextGroup` 不走 “往 `AbilityDefinition` 增加 `ContextGroupId` / `ScoreGraphId` 字段” 方案。

真实实现是：

- `ContextGroupRegistry` 保存 `groupId -> ContextGroupDefinition` 与 `rootAbilityId -> groupId`
- `ContextGroupConfigLoader` 从 `GAS/context_groups.json` 编译候选
- `ContextScoredOrderResolver` 解析 concrete slot + concrete target
- `InputOrderMappingSystem` 在 `InteractionModeType.ContextScored` 分支提交最终 order

这让 ContextScored 保持在输入解析层闭环，而不把评分路由硬编码进 `AbilityDefinition`。

### 2.2 ability activation 继续以 tag gate 为主

当前分支的 ability 激活仍然只有离散 tag gate：

- `RequiredAll`
- `BlockedAny`

执行点：

- `src/Core/Gameplay/GAS/Systems/AbilitySystem.cs`
- `src/Core/Gameplay/GAS/Systems/AbilityExecSystem.cs`
- `src/Core/Gameplay/GAS/Config/AbilityExecLoader.cs`

结论：

- 离散阻塞继续用 tag gate
- 数值/上下文条件不新增平行 `AbilityConditionSystem`
- 如果需要更复杂的 numeric/context 判定，复用现有 minimal validation graph primitive

### 2.3 minimal validation graph primitive 已存在

当前仓库已经有统一的 validation primitive：`src/Core/NodeLibraries/GASGraph/GraphExecutor.cs`

现有复用点：

- `src/Core/Input/Orders/ContextScoredOrderResolver.cs`
- `src/Core/Gameplay/GAS/Systems/OrderBufferSystem.cs`
- `src/Tests/GasTests/InputOrderAbilityAuditTests.cs`

因此未来如果某个 family 真的需要 ability-side numeric gate，应沿用这条 primitive，而不是新起一套 condition runtime。

### 2.4 cooldown / charges 不是本分支的强制 baseline

当前分支没有“所有技能都必须有 cooldown attribute 并每 tick 递减”的架构前提。

本分支已经证明的 only baseline 是：

- cooldown 可以由 tag-duration / block-tag 组合表达
- toggle deactivate 可以在 reactivation cooldown tag 存在时正常关闭

对应证据：

- `src/Tests/GasTests/InputOrderAbilityAuditTests.cs`
- `artifacts/acceptance/interaction-showcase/feature_coverage_matrix.md`
- `artifacts/acceptance/interaction-showcase/path.mmd`

charges / refill timer 也不应被写成“本分支必须具备的统一底座”；只有具体玩法切片真的需要时才立项。

### 2.5 GasConditionRegistry 不承担 ability activation

`GasConditionRegistry` 的职责仍然是 effect 生命周期条件，而不是 ability activation runtime：

- `src/Core/Gameplay/GAS/GasConditionRegistry.cs`
- `src/Core/Gameplay/GAS/Systems/EffectLifetimeSystem.cs`

### 2.6 form-based ability routing 已落地，但不复用为平行 condition runtime

当前分支的 form routing 走的是一条单独且松耦合的 slot 覆写链路：

- `AbilityFormSetRegistry` / `AbilityFormSetConfigLoader` 从 `GAS/ability_form_sets.json` 编译 form route
- `AbilityFormRoutingSystem` 在 `SystemGroup.InputCollection` 早期根据 actor effective tags 解析当前 form
- `AbilityFormSlotBuffer` 与 `GrantedSlotBuffer` 分层存在，避免 stance/form 路由和未来 item/buff grant 互相踩写
- `AbilitySlotResolver` 统一按 `Granted > Form > Base` 解析 effective slot

结论：

- form routing 继续只用 tag 条件（`requiredAll` / `blockedAny` + priority）
- 不为姿态切换引入新的 `AbilityConditionSystem`
- 下游 `AbilitySystem`、`AbilityExecSystem`、`ContextScoredOrderResolver`、`AbilityIndicatorOverlayBridge` 统一消费同一套 effective slot

---

## 3. 当前仍然成立的 backlog

以下项目在当前分支上仍然是 backlog，但都应建立在已存在的运行时之上扩展：

| 项目 | 优先级 | 原因 | 应复用的现有基础 |
|------|--------|------|------------------|
| Response target mutability / richer response-window UI | P1-P2 | 当前 showcase 只证明基础路径 | `ResponseChain`、presentation pipeline |
| Companion focus / actor routing 细化 | P2 | shared selection fan-out 已有，但 companion 专用工作流未闭环 | `InputOrderMappingSystem`、`EntityClickSelectSystem`、现有 order pipeline |
| Generic environment scan / displacement collision / nav blocker | P2 | showcase 未证明整族场景 | `ContextGroup`、`RuntimeEntitySpawnQueue`、effect / handler pipeline |
| Minimap click adapter | P3 | 需要 adapter 侧世界坐标转换 | 现有 `OrderArgs.Spatial` |

---

## 4. 推荐 follow-up 顺序

以 `artifacts/acceptance/interaction-showcase/feature_coverage_matrix.md` 为 stage boundary，建议按以下顺序继续：

1. 在现有 `ContextGroupRegistry` / `ContextScoredOrderResolver` / `AbilityFormRoutingSystem` 之上补更多 acceptance，不把路由回写进 `AbilityDefinition`
2. 在现有 aim / indicator / queued-order 路径上继续补更多交互切片，而不是新建第二套 preview 或 input stack
3. 仅当某个 feature 确实需要 numeric/context activation gate 时，再在现有 `GraphExecutor.ExecuteValidation(...)` primitive 上增量扩展
4. 环境结构变更类玩法统一复用 `RuntimeEntitySpawnQueue` / builtin handler，不在 Graph 中写结构变更伪代码

---

## 5. 使用方式

读本文时请遵循以下边界：

- “已实现 / 已验证” 以 `artifacts/acceptance/interaction-showcase/feature_coverage_matrix.md` 为准
- “可表达 / 可扩展” 不等于 “本分支已闭环”
- 对 feature 文档做设计时，优先引用当前分支已有的 runtime 路径，而不是重新提出 `ContextGroup`、`AbilityConditionSystem`、`CooldownTickSystem` 一类平行底座
