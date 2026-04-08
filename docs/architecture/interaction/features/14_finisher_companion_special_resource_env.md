# Feature: Finisher / Companion / Special Input / Resource / Environment

> 本文合并记录 Q / R / S / T / U 五组 checklist，但所有“已实现”判断只以当前分支 runtime 与 acceptance 证据为准。

---

## 1. 统一架构边界

这五组 feature 都必须遵守同一套边界：

- ability activation 的离散阻塞继续用 `RequiredAll` / `BlockedAny`
- numeric / context 检查复用最小 validation graph primitive：`src/Core/NodeLibraries/GASGraph/GraphExecutor.cs`
- 不引入并行 `AbilityConditionSystem`
- cooldown / charges 不写成“本分支强制 baseline 的 attribute tick 系统”
- 环境结构变更不在 Graph 里直接伪造 `CreateUnit` / `DestroyEntity`，而是复用 `RuntimeEntitySpawnQueue` / builtin handler：`src/Core/Gameplay/Spawning/RuntimeEntitySpawnQueue.cs`、`src/Core/Gameplay/GAS/BuiltinHandlers.cs`

---

## 2. Q: Finisher / Execution (Q1–Q8)

### 当前分支可复用的基础

- discrete gate：`src/Core/Gameplay/GAS/Components/AbilityActivationBlockTags.cs`
- ability activation：`src/Core/Gameplay/GAS/Systems/AbilitySystem.cs`、`src/Core/Gameplay/GAS/Systems/AbilityExecSystem.cs`
- context-scored finisher style candidate：`src/Core/Input/Orders/ContextScoredOrderResolver.cs`
- finisher-like candidate resolution 验证：`src/Tests/GasTests/ContextScoredResolverTests.cs`

### 当前结论

| Slice | 当前状态 | 说明 |
|------|----------|------|
| Q1/Q2 离散处决前置 | `runtime-ready` | 直接用 `RequiredAll` / `BlockedAny` 或由上游投影出的 ready tag |
| Q3/Q4 空间/角度/高度 | `runtime-ready, not showcase-closed` | 应走 validation graph 或 ContextScored precondition，不新增 condition system |
| Q5 反击后处决 | `runtime-ready` | 仍应表现为 tag gate 或 validation graph 结果 |
| Q6/Q7 资源 / combo 处决 | `runtime-ready, not universal baseline` | 数值条件可投影为 tag，或在相邻边界复用 validation graph |
| Q8 QTE | `design backlog on existing InputGate path` | InputGate 是现有机制，但本分支未把 finisher QTE 全链路收口进 showcase |

### 对本文的修正

不再写：

- “AbilityActivationRequireTags + Attribute precondition 是新的 P0”

改为：

- tag gate 已存在
- numeric/context 条件复用 validation graph primitive 或先投影为 ready tag

---

## 3. R: Companion / Multi-Unit (R1–R6)

### 当前分支已证明

- shared selection fan-out：`artifacts/acceptance/interaction-showcase/feature_coverage_matrix.md`
- 现有输入与 order pipeline：`src/Core/Input/Orders/InputOrderMappingSystem.cs`、`src/Core/Input/Selection/AmbientSelectionApplySystem.cs`

### 当前结论

| Slice | 当前状态 | 说明 |
|------|----------|------|
| R1/R3 多单位共享下指令 | `implemented + verified baseline` | 当前 showcase 已证明 shared selection fan-out，不等于完整 companion feature family |
| R2 companion 模式切换 | `design backlog` | 需要更具体的 actor routing / behavior authoring |
| R4 装载 / R6 集结点 | `design backlog on existing order pipeline` | 可复用现有 order / blackboard，但未 branch-closed |
| R5 合并 / sacrifice / spawn | `design backlog with structural-change guardrail` | 必须经 handler / spawn queue，不能在 Graph 中写结构变更伪代码 |

---

## 4. S: Special Input (S1–S7)

### 当前分支已实现并验证

- Quick Cast / SmartCast：`artifacts/acceptance/interaction-showcase/feature_coverage_matrix.md`
- vector cast + chord input：`artifacts/acceptance/interaction-showcase/feature_coverage_matrix.md`
- double-tap skill activation：`src/Core/Input/Orders/InputOrderMapping.cs`、`src/Core/Input/Orders/InputOrderMappingSystem.cs`、`src/Tests/GasTests/InputOrderContractTests.cs`、`src/Tests/GasTests/Production/InteractionShowcasePlayableAcceptanceTests.cs`
- queued orders：`src/Core/Input/Orders/InputOrderMappingSystem.cs`、`src/Core/Gameplay/GAS/Orders/OrderSubmitter.cs`

### 当前结论

| Slice | 当前状态 | 说明 |
|------|----------|------|
| S1 组合键 | `runtime-ready` | 依赖现有 input binding / chord authoring |
| S2 双击 | `implemented + verified` | 不再是“需新增 trigger type”；`InputTriggerType.DoubleTap` 已存在 |
| S3 方向 + 按键 | `runtime-ready` | `Direction` / `Vector` 已有，细分玩法仍看具体 authoring |
| S4 modifier 自我施放 | `runtime-ready` | 仍在现有 input mapping / modifier 路径上扩展 |
| S5 Quick Cast | `implemented + verified` | showcase 已覆盖 |
| S6 小地图施放 | `backlog` | 仍缺 adapter 侧 world-position 转换 |
| S7 Shift queue | `implemented + verified baseline` | 现有 queued-order runtime 已闭环 |

---

## 5. T: Resource / Gating (T1–T8)

### 当前分支基线

资源家族当前的基线不是“统一 attribute precondition system”，而是：

- 离散 ability gate：`RequiredAll` / `BlockedAny`
- 最小 validation primitive：`GraphExecutor.ExecuteValidation(...)`
- 具体资源玩法按 feature authoring 选择 tag projection、validation graph、或两者组合

### 当前结论

| Slice | 当前状态 | 说明 |
|------|----------|------|
| T1/T4/T6/T7/T8 资源门控 | `runtime-ready` | 应优先走 ready tag 或 validation graph，不新增平行 runtime |
| T2 冷却 | `implemented + verified branch baseline` | 当前分支已证明 tag-duration / blocked-tag 足够；不把 cooldown tick system 当 baseline |
| T3 充能 | `design backlog on existing primitives` | 如果玩法需要 refill timer，再单独立项；不是本分支强制底座 |
| generalized ability-side numeric precondition framework | `not shipped on this branch` | 如果后续需要，也应复用现有 validation graph primitive |

### 关键证据

- `src/Core/Gameplay/GAS/Systems/AbilitySystem.cs`
- `src/Core/Gameplay/GAS/Systems/AbilityExecSystem.cs`
- `src/Tests/GasTests/InputOrderAbilityAuditTests.cs`
- `artifacts/acceptance/interaction-showcase/feature_coverage_matrix.md`
- `artifacts/acceptance/interaction-showcase/path.mmd`

特别是 `src/Tests/GasTests/InputOrderAbilityAuditTests.cs` 已验证：toggle deactivate 可以在 reactivation cooldown tag 存在时正常关闭。

---

## 6. U: Environmental Interaction (U1–U7)

### 当前分支可复用的基础

- 上下文自动选取：`src/Core/Input/Orders/ContextScoredOrderResolver.cs`
- runtime structural change pipeline：`src/Core/Gameplay/Spawning/RuntimeEntitySpawnQueue.cs`、`src/Core/Gameplay/GAS/BuiltinHandlers.cs`
- effect processing pipeline：`src/Core/Gameplay/GAS/Systems/EffectProcessingLoopSystem.cs`

### 当前结论

| Slice | 当前状态 | 说明 |
|------|----------|------|
| U1/U5 环境驱动候选选择 | `design backlog on ContextGroup runtime` | 可复用 ContextScored，但本分支未做完整 acceptance 闭环 |
| U2 撞墙 / 悬崖碰撞反馈 | `backlog` | 需要 displacement collision 回调 |
| U3 可破坏物 / U7 地形创造 | `backlog with spawn-queue guardrail` | 必须走 handler / spawn queue，不能在 Graph 文档里直接写结构变更 |
| generic environment scan | `backlog` | 仍缺统一环境 authoring 与验证 |

---

## 7. 结论

这五组 feature 的文档边界应这样理解：

- **已实现 showcase/runtime**：special input 的若干核心路径、shared selection fan-out、ContextScored baseline、tag-gated ability activation、tag-duration cooldown baseline
- **已有 primitive 但未闭环**：finisher 的空间/数值判定、companion 专用 actor routing、environment family、resource refill timer
- **明确不该再提的平行底座**：`AbilityConditionSystem`、通用 `CooldownTickSystem`

后续若继续扩展，请以 `artifacts/acceptance/interaction-showcase/feature_coverage_matrix.md` 作为 stage boundary，而不是把 design catalog 直接当作 shipped feature list。
