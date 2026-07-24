## GAS Composition Gate — Self Review

Current closeouts and prior issue reviews follow.

## PR #660 Order Admission Mergeability Repair - 2026-07-24

This is the live self-review section for the order admission repair on branch `codex/issues-649-651-ordering`. Older sections below are historical context only.

- **Task / Issue**: PR #660 strict audit repair for #649 / #650 / #651 / #669 order-result traceability.
- **Date**: 2026-07-24
- **Agent / Author**: Codex.

### 1. Core judgment

Primary delivery: A. Repair existing Order admission, input fan-out, ResponseChain, and continuation paths so every player-visible order attempt produces a typed, queryable result.

Result: PASS.

Reason: This work reuses the existing `OrderQueue`, `OrderAdmissionResultBuffer`, `OrderBufferSystem`, `InputOrderMappingSystem`, `ResponseChain*OrderSourceSystem`, `CompositeOrderPlanner`, and `OrderContinuationSystem`. It adds no graph op, effect preset enum, gameplay profile field, loader, registry, fallback path, or parallel order runtime.

### 2. Layer assignment

| Step / capability | Layer | Implementation carrier |
|---|---:|---|
| Batch queue-full rejection keeps nonzero order ids | N/A | existing `OrderQueue` + `OrderAdmissionResultBuffer` |
| Collection fan-out returns assigned batch id or typed rejection | N/A | existing `InputOrderMappingSystem` batch submit path |
| ResponseChain enqueue failure is observable and retryable | N/A | existing `ResponseChainHumanOrderSourceSystem` and `ResponseChainAiOrderSourceSystem` |
| Continuation follow-up admission result is published | N/A | existing `OrderContinuationBuffer`, `OrderContinuationSystem`, `OrderSubmitter` |
| Entity-intake close remains explicit in production | N/A | existing cleanup phase with `OrderAdmissionEntityIntakeEndSystem` |

### 3. Reuse list

- Handlers: no new BuiltinHandler.
- Queues / Systems: `OrderQueue`, `OrderAdmissionResultBuffer`, `OrderBufferSystem`, `OrderContinuationSystem`, `ResponseChainHumanOrderSourceSystem`, `ResponseChainAiOrderSourceSystem`.
- Resolvers / Registries: existing `OrderTypeRegistry`, input mapping profiles, response-chain order type ids.
- Existing presets / graphs: unchanged.

### 4. New Layer 0 ops

N/A.

### 5. Transaction boundary

Batch and continuation intake must reserve admission-result capacity before mutating queue or continuation state. Queue-full paths must publish `RejectedQueueFull` with a nonzero order id instead of silently dropping, throwing from player input, or marking an AI response as submitted when it was not accepted.

### 6. Config SSOT

Behavior remains in the existing order type catalog, input mapping data, response-chain queue contracts, and runtime capacity values.

New JSON schema: NO.

### 7. Red flag scan

- [x] No profile inherit/placement enum added
- [x] No parallel order, response-chain, continuation, or lifecycle runtime added
- [x] No fallback constructor or compatibility bypass added
- [x] No silent queue-full, zero-id rejection, or unqueryable order result remains in the repaired paths
- [x] No Core gameplay enum, preset, loader, registry, or schema added

### 8. Next variant test

A new player command variant changes input mapping data, effect chains, or graph wiring and continues through the same order admission/result contract. It must not add an alternate Core order runtime or silently bypass admission results.

## PR #660 Final Status - 2026-07-24

This is the current status for the PR #660 worktree. Older sections below are historical evidence and must not be treated as alternate live branches.

- Worktree: `C:\001_AI\_codex_audit\Ludots-pr660-086d3f4-exact-20260724-1415`.
- Branch: `codex/issues-649-651-ordering`.
- Starting PR head before this repair: `086d3f4f35079eaab417bfea166335e5ba9c9b9d`.
- Main merge base used here: `5712a4eef4cdb1011cc0694d52e77de95bfe4aaa`.
- Related issues #649 / #650 / #651 remain the original order/input work history for this PR. They are not separate active branches; current acceptance status is this section.

Current blockers repaired:

- Grid/NodeGraph map assets now declare positive `LoadedChunkCapacity`; legacy `WidthInTiles` / `HeightInTiles` map fields are rejected by regression coverage.
- Ability input and target-collection gates fail the active order when the input request queue is missing/full instead of entering a permanent fake wait.
- Response-chain window depth overflow, response queue overflow, prompt input queue failure, and prompt order request queue failure are explicit failures instead of silent drops.
- Graph blackboard writes fail on dead entities or missing pre-added blackboard buffers instead of silently returning.

Verification on this worktree:

- `dotnet test src\Tests\GasTests\GasTests.csproj --filter "FullyQualifiedName~ResponseWindowRobustnessTests|FullyQualifiedName~GraphFailFastAndCapacityTests|FullyQualifiedName~EffectPhaseArchitectureTests|FullyQualifiedName~InteractionSelectionConvergenceTests" -c Debug --no-restore --logger "console;verbosity=minimal" -clp:ErrorsOnly -m:1`: PASS, 80/80.
- `dotnet test src\Tests\GasTests\GasTests.csproj --filter FullyQualifiedName~MapAssets_GridAndNodeGraphBoards_DeclarePositiveLoadedChunkCapacity -c Debug --no-restore --logger "console;verbosity=minimal" -clp:ErrorsOnly -m:1`: PASS, 1/1.
- `dotnet test src\Tests\PresentationTests\PresentationTests.csproj --filter FullyQualifiedName~Showcase_UnchangedRelationshipRevisionDoesNotRepeat10kDomainResolution -c Debug --no-restore --logger "console;verbosity=minimal" -clp:ErrorsOnly -m:1`: PASS, 1/1.
- `dotnet build` for every `src\Tests\**\*.csproj`, then `dotnet build src\Tools\ModdingSmoke\ModdingTest.csproj -c Debug`: PASS.
- `solution-verify.yml` architecture guard slice: PASS, 80/80.
- `solution-verify.yml` MassNavigation PR acceptance slice: PASS, 11/11.
- `dotnet test src\Tests\GasTests\GasTests.csproj -c Debug --no-build --filter "TestCategory=ci-gate" --logger "console;verbosity=minimal"`: PASS, 166/166 after an initial local Fog benchmark throughput-noise rerun.
- `dotnet test src\Tests\RaylibAdapterTests\RaylibAdapterTests.csproj -c Debug --no-build --filter "TestCategory=raylib-field" --logger "console;verbosity=minimal"`: PASS, 4/4.
- `git diff --check`: PASS.
- `rg -n "WidthInTiles|HeightInTiles" mods assets src -g "*.json"`: no matches.
- CI audit gate artifacts: `artifacts/ci-audit/pr660/result.md` and `artifacts/ci-audit/pr660/result.json`.

## GAS Composition Gate - PR #660 Final Mergeability Repair

- **Task / Issue**: PR #660 final mergeability repair after strict audit found CI map-load failure and remaining silent-drop paths
- **Date**: 2026-07-24
- **Agent / Author**: Codex

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: 本轮修复沿现有 BoardConfig/Map merge、GAS queue、ResponseChain、Effect side-effect transaction、Graph blackboard 写入管线补齐失败语义；不新增 profile enum、preset 开关、loader、registry、parallel runtime 或 fallback。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 地图 loaded chunk 容量契约 | N/A | existing `BoardConfig`, map JSON assets, `BoardFactory`/board constructors |
| Ability input gate enqueue failure | N/A | existing `AbilityExecSystem`, `InputRequestQueue` |
| ResponseChain create/depth/queue overflow | Layer 2 execution repair | existing `EffectProposalProcessingSystem`, `EffectProposalWindow`, `ProposalResponseQueue` |
| Response prompt order request enqueue failure | N/A | existing `OrderRequestQueue` |
| Blackboard write dead/missing component failure | Layer 2/graph runtime guard | existing `GasGraphRuntimeApi`, `EffectPhaseSideEffectTransaction` |

### 3. Reuse list

- Handlers: no new BuiltinHandler.
- Queues / Systems: `InputRequestQueue`, `OrderRequestQueue`, `EffectProposalProcessingSystem`, `AbilityExecSystem`.
- Resolvers / Registries: `BoardConfig`, `MapManager`, existing graph blackboard buffers and `EffectPhaseSideEffectTransaction`.
- Existing presets / graphs: unchanged.

### 4. New Layer 0 ops (if any)

N/A.

### 5. Transaction boundary

ResponseChain overflow and prompt enqueue failures must stop the active response window instead of continuing with missing responses/prompts. Blackboard side effects staged through `EffectPhaseSideEffectTransaction` must reject dead or missing target storage before commit-visible state can pretend success.

### 6. Config SSOT

行为配置落在: existing map board JSON (`LoadedChunkCapacity`) and existing GAS graph/effect runtime contracts.

是否新增 JSON schema: NO.

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线 / effect 步骤 / map runtime capacity data.

---

## GAS Composition Gate - PR #660 Capacity Fail-Fast Repair

- **Task / Issue**: PR #660 merge readiness repair for GAS capacity overflow audit findings
- **Date**: 2026-07-24
- **Agent / Author**: Codex

### 1. Core judgment

Primary delivery: A. Repair existing GAS queue, fan-out, root-budget, active-effect, and phase-listener capacity handling.

Result: PASS

Reason: This work changes existing budget enforcement from silent drop/truncation to explicit capacity failure. It adds no graph op, effect preset enum, gameplay profile field, loader, registry, fallback path, or parallel runtime.

### 2. Layer assignment

| Step / capability | Layer | Implementation carrier |
|---|---:|---|
| Root budget table full-table guard | N/A | existing `RootBudgetTable` |
| Fan-out root budget exhaustion | N/A | existing `TargetResolverFanOutHelper` |
| Effect request queue overflow | N/A | existing `EffectRequestQueue` |
| Gameplay event bus overflow | N/A | existing `GameplayEventBus` |
| Phase listener dispatch capacity | N/A | existing `EffectPhaseExecutor` scratch buffer |
| Active effect/listener registration capacity | N/A | existing `EffectApplicationSystem` |

### 3. Reuse list

- Handlers: no new BuiltinHandler.
- Queues / Systems: `EffectRequestQueue`, `GameplayEventBus`, `EffectProcessingLoopSystem`, `EffectProposalProcessingSystem`, `EffectApplicationSystem`, `EffectLifetimeSystem`.
- Resolvers / Registries: `TargetResolverFanOutHelper`, `RootBudgetTable`, `GlobalPhaseListenerRegistry`.
- Existing presets / graphs: unchanged.

### 4. New Layer 0 ops

N/A.

### 5. Transaction boundary

Capacity overflow now fails before publishing partial requests, dropping fan-out targets, truncating listener dispatch, destroying an unattached active effect, or partially registering listener entries. Existing side-effect transactions continue to own rollback for graph-triggered side effects.

### 6. Config SSOT

Root fan-out budget capacity is derived from the existing `gasRuntimeCapacity.effectFanOutCommandCapacity` path through the existing constructors. New JSON schema: NO.

### 7. Red flag scan

- [x] No profile inherit/placement enum added
- [x] No parallel queue, event bus, effect pipeline, or listener runtime added
- [x] No fallback constructor or silent overflow branch added
- [x] No capacity truncation that lets gameplay continue with missing effects/events
- [x] No Core gameplay enum, preset type, loader, or registry added

### 8. Next variant test

The next Mod variant changes effect chains, graph wiring, or configured runtime capacity. It must not change Core enums to bypass a capacity failure.

---

## GAS Composition Gate - PR #660 Final Main-Lag Repair

- **Task / Issue**: PR #660 final merge repair against current `main`
- **Date**: 2026-07-24
- **Agent / Author**: Codex

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A（沿现有 Order/GAS、Knowledge、UI Surface、Input 与 Physics2D 管线修复合并落后问题）

结论: PASS

一句话理由: 本轮只修正现有 showcase、验收宿主和热路径使用方式；没有新增 graph op、effect preset enum、profile 字段、loader、registry、fallback 或平行运行时。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| Interaction stress order pressure | N/A | existing `gasRuntimeCapacity` budget and existing stress fireball effect templates |
| Relationship frontend mount | N/A | existing `AcceptanceUiHostInstaller` + `UiSurfaceHost` |
| Physics2D steady-state allocation repair | N/A | existing `InputActionAttributeBindingSystem` and `Physics2DSimulationSystem` inline queries |
| Performance Visualization Health HUD audience | N/A | existing `LocalPlayerEntity`, `KnowledgeProjectionStore`, `KnowledgeDisclosureRecord` |
| UX Prototype selectable visibility | N/A | existing local player services, `PlayerEntityLookup`, `KnowledgeProjectionStore` |

### 3. Reuse list

- Handlers: existing GAS effect handlers; no new builtin handler.
- Queues / Systems: `OrderAdmissionResultBuffer`, `RuntimeEntitySpawnQueue`, `InputActionAttributeBindingSystem`, `Physics2DSimulationSystem`, existing presentation/UI runtime.
- Resolvers / Registries: `KnowledgeProjectionStore`, `PlayerEntityLookup`, `EntityTemplateKeyRegistry`, existing UI surface services.
- Existing presets / graphs: unchanged; stress fireball keeps existing `LaunchProjectile` / `InstantDamage` presets and only opts out of response-chain participation for the stress-only path.

### 4. New Layer 0 ops (if any)

N/A. No atomic op, graph operation, effect handler, preset, registry, schema, or materialization path was added.

### 5. Transaction boundary

必须原子 rollback 的步骤: N/A；本轮不改 mutation transaction。Order 压力修复只对齐现有入队/接单结果容量；UI 修复只补齐验收 composition root；Physics2D 修复只缓存稳定相机输入承载实体并使用 inline-query 读取统计。

### 6. Config SSOT

行为配置落在:

- `mods/showcases/interaction/InteractionShowcaseMod/assets/game.json`：showcase 专属 Order admission / rejection capacity。
- `mods/showcases/interaction/InteractionShowcaseMod/assets/GAS/effects.json`：stress-only fireball response-chain participation。
- 其他修复复用现有 runtime services，不新增配置文件。

是否新增 JSON schema: NO

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn/effect/order/UI/physics 平行的运行时管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加默认 fallback、兼容旁路或静默容量放过
- [x] 未新增 registry、preset、loader 或 schema
- [x] 热路径修复不新增 ECS 结构变更或托管集合增长

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线 / effect 步骤 / showcase 数据预算。输入、知识可见性、UI 挂载和 Physics2D 统计继续走现有服务与系统，不修改 Core gameplay enum。

---

## GAS Composition Gate - PR #660 Spawn Relationship Test Repair

- **Task / Issue**: PR #660 merge repair for runtime spawn Team → MemberOf relationship contract
- **Date**: 2026-07-23
- **Agent / Author**: Codex

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A（测试装配复用现有 spawn 与 relationship 基建）

结论: PASS

一句话理由: 本轮只让 runtime spawn 测试显式注入正式 `RelationshipRuntime`、`RelationshipTypeRegistry` 与 `TeamEntityLookup`，不新增 graph op、preset、profile 字段、loader、registry 或平行物化管线。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| Team 代表实体装配 | N/A | `TeamIdentity` + `TeamEntityLookup` |
| MemberOf 类型注册 | N/A | `RelationshipTypeRegistry` |
| Spawn 后队伍归属关系 | N/A | existing `RuntimeEntitySpawnSystem` + `RelationshipRuntime` |

### 3. Reuse list

- Handlers: existing `CreateUnit` / runtime spawn path unchanged
- Queues / Systems: existing `RuntimeEntitySpawnQueue` and `RuntimeEntitySpawnSystem`
- Resolvers / Registries: `RelationshipTypeRegistry`, `TeamEntityLookup`, `RelationshipReverseIndex`
- Existing presets / graphs: unchanged

### 4. New Layer 0 ops (if any)

N/A

### 5. Transaction boundary

必须原子 rollback 的步骤: N/A；本轮不改生产事务，只补测试 composition root。spawn 系统继续在入队消费前显式校验 Team 代表和 MemberOf 类型，缺失即 hard failure。

### 6. Config SSOT

行为配置落在: existing spawn request and relationship runtime registration.

是否新增 JSON schema: NO

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加默认 fallback 或兼容旁路
- [x] 未新增 registry、preset 或 schema

### 8. Next variant test

「下一个 Mod 变体」将修改: effect 步骤 / graph 连线。需要队伍归属时继续通过正式 relationship runtime 和 Team 代表实体表达。

---

## GAS Composition Gate - PR #660 main merge repair

- **Task / Issue**: PR #660 merge with current `main`
- **Date**: 2026-07-23
- **Agent / Author**: Codex

### 1. Core judgment

Primary delivery: A. Merge the existing typed Order admission/result contract with the existing mainline batch, shared-id, and clustered command admission path.

Result: PASS

Reason: The repair reuses `OrderQueue`, `OrderAdmissionResultBuffer`, `OrderBufferSystem`, `OrderSubmitter`, `InputOrderMappingSystem`, and existing MovePlan/MassNavigation ports. It adds no gameplay profile enum, effect preset switch, graph op, lifecycle DSL, loader, or parallel order runtime.

### 2. Layer assignment

| Step / capability | Layer | Implementation carrier |
|---|---:|---|
| Typed global/entity order admission | N/A | `OrderAdmissionResultBuffer` |
| Atomic batch/shared/clustered intake | N/A | `OrderQueue` |
| Batch preflight before activation | N/A | `OrderBufferSystem` |
| Actor authorization before fan-out submit | N/A | `InputOrderMappingSystem` |
| MovePlan-backed road execution binding | N/A | existing `MassNavigationRuntimeBinding` and `MovePlanStore` |

### 3. Reuse list

- Handlers: no new BuiltinHandler.
- Queues / Systems: `OrderQueue`, `OrderBufferSystem`, `OrderSubmitter`, existing `SystemGroup.RuntimeEntityBinding`.
- Resolvers / Registries: `OrderTypeRegistry`, `CommandIntentProfileRegistry`, `CastDispatchProfileRegistry`, `MassNavigationRuntimeBinding`.
- Existing presets / graphs: unchanged.

### 4. New Layer 0 ops

N/A.

### 5. Transaction boundary

Batch intake validates order type, capacity, actor ownership, command-source grouping, and entity intake preflight before any row activates. If one batch member fails entity intake, every row is rejected and spatial payload ownership is released.

### 6. Config SSOT

Behavior remains in the existing order type catalog, input mapping data, and GAS runtime capacity config. New JSON schema: NO.

### 7. Red flag scan

- [x] No profile inherit/placement enum added
- [x] No parallel spawn, order, MovePlan, or lifecycle runtime added
- [x] No fallback constructor restored for `OrderQueue`
- [x] No silent partial batch admission

### 8. Next variant test

The next Mod variant changes command routing data or effect/graph composition, not Core enums.

---

## GAS Composition Gate - #649 Production Closeout

- **Task / Issue**: #649
- **Date**: 2026-07-12
- **Agent / Author**: Codex

### 1. Core judgment

Primary delivery: A. Extend the existing Order pipeline with one typed, bounded terminal-result contract.

Result: PASS

Reason: The change reuses OrderSubmitter, OrderTypeRegistry, OrderContinuationBuffer and the SchemaUpdate frame boundary. It adds no gameplay profile, preset, graph op, loader or parallel order runtime.

### 2. Layer assignment

| Step / capability | Layer | Implementation carrier |
|---|---:|---|
| Unique order finalization | N/A | `OrderSubmitter.FinalizeActive` |
| Current-frame terminal snapshot | N/A | `OrderTerminalResultBuffer` owned by `OrderTypeRegistry` |
| Continuation dispatch | N/A | `OrderContinuationSystem` consumes every typed outcome once |
| Frame reset | N/A | Existing `GasBudgetResetSystem` in SchemaUpdate |

### 3. Reuse list

- Handlers: existing OrderSubmitter activation and blackboard cleanup helpers
- Queues / Systems: OrderBuffer, OrderContinuationBuffer, OrderContinuationSystem, GasBudgetResetSystem
- Resolvers / Registries: OrderTypeRegistry
- Existing presets / graphs: unchanged

### 4. New Layer 0 ops

N/A.

### 5. Transaction boundary

Terminal-result capacity and outcome validity are checked before the active order is mutated. The single internal finalizer then clears blackboard and active state, removes non-completed continuations, publishes exactly one typed outcome, and optionally promotes the next queued order.

### 6. Config SSOT

Production capacity is explicitly configured at `game.json -> gasRuntimeCapacity.orderTerminalResultCapacity` and validated as positive during engine composition.

New JSON schema: YES. This is a runtime capacity budget, not gameplay behavior and cannot be represented by effect/graph composition.

### 7. Red flag scan

- [x] No profile inherit/placement enum added
- [x] No parallel spawn, order or terminal runtime added
- [x] No placement validation moved into lifecycle operations
- [x] No default gameplay fallback or silent capacity drop added
- [x] No per-finalization ECS component add/remove; continuation consumes the bounded result snapshot directly

### 8. Next variant test

Future order variants publish through the same finalizer and terminal-result buffer. They do not add Core enums, consumer-specific signals or alternate completion paths.

---

## GAS Composition Gate - #647 Consumer Closeout

- **Task / Issue**: #647
- **Date**: 2026-07-12
- **Result**: PASS
- CoreInput aiming, context-scored order routing, and the entity-query showcase now consume the engine-owned `GasGraphRuntimeApi` service.
- Consumer-side `CreateProduction` calls were removed; missing composition-root service is an explicit hard failure.
- No graph op, profile schema, preset enum, loader, or parallel runtime was added.

---

## GAS Composition Gate - Combined Order/Input Review

- **Task / Issue**: #650, #649, #651
- **Date**: 2026-07-12
- **Agent / Author**: Codex

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A（沿现有 Order/Input 管线扩展类型化结果合同；不新增 gameplay 变体）

结论: PASS

一句话理由: 修改限于现有订单接入、订单终态和输入激活入口，不新增 profile、preset、graph、lifecycle op 或平行管线。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 订单接入结果 | N/A | 现有 OrderQueue、OrderSubmitter、OrderBufferSystem |
| 订单唯一终态 | N/A | 现有 OrderSubmitter、AbilityExecSystem、OrderContinuationSystem |
| 角色隔离激活 | N/A | 现有 InputOrderMappingSystem、EntityCommandPanelMod |

### 3. Reuse list

- Handlers: 现有 InputOrderMappingSystem.OrderSubmitHandler
- Queues / Systems: OrderQueue、OrderBufferSystem、AbilityExecSystem、OrderContinuationSystem
- Resolvers / Registries: OrderTypeRegistry、AbilityDefinitionRegistry、现有 actor/mapping 解析
- Existing presets / graphs: N/A

### 4. New Layer 0 ops (if any)

N/A

### 5. Transaction boundary

必须原子 rollback 的步骤: N/A；订单 finalize 通过单一入口保证每个 active order 只结束一次。

### 6. Config SSOT

行为配置落在: 现有 order type catalog 与 OrderBuffer 正式容量。

是否新增 JSON schema: NO

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: effect 步骤（本任务本身不引入 Mod gameplay 变体）

---

## GAS Composition Gate — #646 Self Review

- **Task / Issue**: #646
- **Date**: 2026-07-12
- **Agent / Author**: Codex

### 1. Core judgment

主要交付物为 A：沿现有 Effect Phase、BuiltinHandler、EffectRequestQueue 与时间切片合同重组执行路径，不新增 profile 字段、preset 枚举、Graph VM 或平行管线。

结论：PASS。

### 2. Layer assignment

| 步骤/能力 | Layer | 实现载体 |
|---|---:|---|
| 瞬时效果阶段组合 | 2 | 现有 `EffectPhaseExecutor` + effect template phase bindings |
| 预设主行为 | 0 | 现有 `BuiltinHandlerRegistry` / `BuiltinHandlers` |
| 生命周期切片 | N/A | 现有 `EffectLifetimeSystem` + `ITimeSlicedSystem` |
| 后续效果发布 | N/A | 现有 `EffectRequestQueue` |

### 3. Reuse list

- Handlers: `BuiltinHandlerRegistry`, `BuiltinHandlers`, `BuiltinHandlerExecutionContext`
- Queues / Systems: `EffectRequestQueue`, `EffectProcessingLoopSystem`, `EffectLifetimeSystem`, `AbilityExecSystem`
- Resolvers / Registries: `EffectPhaseExecutor`, `EffectTemplateRegistry`, existing target resolvers and graph program registry
- Existing presets / graphs: existing `EffectPresetType` definitions and phase graph bindings; no new variant

### 4. New Layer 0 ops

N/A。

### 5. Transaction boundary

瞬时效果必须在同一次正式阶段执行中完成 OnResolve、OnHit、OnApply，并在结束时清理配置上下文和扇出暂存；需要跨帧监听器所有权的效果不得进入瞬时路径。

### 6. Config SSOT

行为仍由现有 effect template、preset catalog 和 graph 配置表达。新增的仅是 `game.json` 中启动期 GAS 快照容量；没有新增玩法 DSL。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建平行瞬时运行时、Graph VM 或 loader
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 容量不足明确失败，预算不足明确延后，无 fallback/静默截断

### 8. Next variant test

下一个 Mod 变体只调整 graph 连线或 effect 步骤，不修改 Core enum。

---

## GAS Composition Gate — #647 Self Review

- **Task / Issue**: #647
- **Date**: 2026-07-12
- **Agent / Author**: Codex

### 1. Core judgment

主要交付物为 A：统一现有生产 Graph API 的服务装配和生命周期/诊断接线；没有新增 graph op、preset 字段、玩法枚举或平行运行时。

结论：PASS。

### 2. Layer assignment

| 步骤/能力 | Layer | 实现载体 |
|---|---:|---|
| 生产图服务装配 | N/A | `GasGraphRuntimeApi.CreateProduction` + `GasGraphRuntimeProductionServices` |
| 派生属性图执行 | 2 | 现有 `GraphProgramRegistry` + `AttributeAggregatorSystem` |
| 输出生命周期 | N/A | 现有 `GraphOutputValueStore` + Cleanup system |
| GAS 告警出口 | N/A | 现有 `GasBudget` / `OrderAdmissionResultBuffer` + 固定容量结构化事件缓冲 |

### 3. Reuse list

- Handlers: existing `GasGraphOpHandlerTable`
- Queues / Systems: `AttributeAggregatorSystem`, `GasBudgetReportSystem`, `OrderAdmissionResultBuffer`
- Resolvers / Registries: `GraphProgramRegistry`, topology services, `GraphOutputValueStore`
- Existing presets / graphs: unchanged

### 4. New Layer 0 ops

N/A。

### 5. Transaction boundary

生产 Graph API 构造要求完整服务集合，缺失任一正式依赖立即失败；owner 版本退役时，输出槽位、哈希索引和旧句柄在同一 Cleanup 更新中一起失效。

### 6. Config SSOT

没有新增玩法配置 schema。生产服务来自引擎唯一强类型服务集合；诊断指标来自 `GasBudget` 与订单接入结果。

### 7. Red flag scan

- [x] 未新增 profile enum 或 graph op
- [x] 未建立第二套 Graph API 运行时
- [x] 未用全 ECS 表扫描或定期全清实现输出回收
- [x] 缺服务、诊断缓冲溢出和计数器回退均 hard-stop

### 8. Next variant test

下一个 Mod 变体继续通过现有 graph 连线和服务集合接入，不修改 Core enum。

---

## GAS Composition Gate — #653 Self Review

- **Task / Issue**: #653
- **Date**: 2026-07-12
- **Agent / Author**: Codex

### 1. Core judgment

主要交付物为 A：收口现有有效技能槽位解析与输入/展示接线；没有新增 ability profile、preset、graph op 或第二套优先级规则。

结论：PASS。

### 2. Layer assignment

| 步骤/能力 | Layer | 实现载体 |
|---|---:|---|
| 有效技能槽位解析 | N/A | 唯一 `AbilitySlotResolver.Resolve` |
| 输入覆盖 | N/A | `SkillMappingOverrideResolver` |
| 面板与实体信息展示 | N/A | 现有 EntityCommandPanel / EntityInfo 消费者 |

### 3. Reuse list

- Handlers: N/A
- Queues / Systems: existing input mapping, routing, aiming and execution systems
- Resolvers / Registries: `AbilitySlotResolver`, `AbilityDefinitionRegistry`
- Existing presets / graphs: unchanged

### 4. New Layer 0 ops

N/A。

### 5. Transaction boundary

同一 actor/slot 的 base、form、item、granted 来源必须一起参与一次确定性解析；不完整重载被删除，生产调用无法再省略 item 层。

### 6. Config SSOT

没有新增配置 schema。优先级 SSOT 固定为 `granted > item > form > base`，输入覆盖继续来自有效 `AbilityDefinition.InputBindingOverride`。

### 7. Red flag scan

- [x] 未新增 profile enum 或输入 fallback
- [x] 未在消费者复制第二套优先级梯子
- [x] 不完整 resolver 重载已删除
- [x] 预热后的输入覆盖解析 0 分配

### 8. Next variant test

新增技能来源必须扩展唯一 resolver 合同和全链路一致性测试，不得只改单个消费者。

---

## GAS Composition Gate — #651 Production Closeout

- **Task / Issue**: #651
- **Date**: 2026-07-12
- **Agent / Author**: Codex

### 1. Core judgment

主要交付物为 A：沿现有 InputOrderMapping / OrderQueue 管线贯穿 actor context 与类型化接单结果；没有新增面板专用 order 管线。

结论：PASS。

### 2. Layer assignment

| 步骤/能力 | Layer | 实现载体 |
|---|---:|---|
| 程序化激活 | N/A | `InputOrderMappingSystem.ActivateMappedAction` |
| 接单结果 | N/A | `OrderSubmitResult` + `OrderQueue.Submit` |
| 瞄准主体固定 | N/A | 现有 aiming state + `InputOrderActivationContext` |

### 3. Reuse list

- Handlers: typed `OrderSubmitHandler`
- Queues / Systems: `OrderQueue`, `CompositeOrderPlanner`, `InputOrderMappingSystem`
- Resolvers / Registries: existing mapping and command-intent routing
- Existing presets / graphs: unchanged

### 4. New Layer 0 ops

N/A。

### 5. Transaction boundary

一次激活固定 actor/player；进入瞄准后确认、取消和拒绝继续使用该上下文。提交结果携带 actor、orderId 和共享 `OrderSubmitResult`。

### 6. Config SSOT

没有新增配置 schema；mapping、order type 和 actor source 继续来自现有正式配置与服务。

### 7. Red flag scan

- [x] 无 actor 的程序化入口已删除
- [x] `void OrderSubmitHandler` 已删除
- [x] 显式 actor 不回退 provider 或 collection fan-out
- [x] aiming actor 失效返回 `RejectedInvalidActor`

### 8. Next variant test

新入口必须返回共享接单结果，不得恢复 bool/void 或临时替换 actor provider。

---

## GAS Composition Gate - #667 Tag Transaction Closeout

- **Task / Issue**: #667
- **Date**: 2026-07-12
- **Result**: PASS

### 1. Core judgment

The change extends the existing `TagOps`, `GameplayTagContainer`, `TagCountContainer`, and `DirtyFlags` contract with fixed-size value snapshots. It adds no tag DSL, effect preset, graph op, loader, or parallel tag runtime.

### 2. Reuse list

- Existing rule executor: `TagOps` and `TagRuleTransaction`
- Existing effect path: `EffectTagContributionHelper`
- Existing state: `GameplayTagContainer`, `TagCountContainer`, and `DirtyFlags`
- Existing diagnostics: `GasBudget.TagCountOverflowDropped`

### 3. Transaction boundary

One effect grant, revoke, or stack update snapshots all three fixed-size components before the first write. Any capacity or rule failure restores all snapshots and publishes one stable error. Rule-driven attach/remove work runs inside the same boundary.

### 4. Red flag scan

- [x] No dynamic collection or per-operation allocation added
- [x] No effect hot-path component add/remove remains in the contribution helper
- [x] No empty `TagOps` fallback remains in the three effect systems
- [x] Capacity failure is explicit and counted once per failed transaction

---

## GAS Composition Gate - #669 Audit Closeout

- **Task / Issue**: #669
- **Date**: 2026-07-13
- **Result**: PASS

### 1. Core judgment

The change closes six production-contract gaps inside the existing GAS, Order, template spawning, graph-output lifecycle, and Arch World destruction paths. It adds no gameplay DSL, effect preset, graph operation, loader, or parallel runtime pipeline.

### 2. Layer assignment

| Capability | Layer | Existing carrier |
|---|---:|---|
| Complete tag state before gameplay | N/A | `TagStateInstaller`, `EntityBuilder`, batch/runtime spawning |
| Sparse deferred-trigger collection | N/A | `DirtyFlags`, `TagOps`, `DeferredTriggerCollectionSystem` |
| Shared effect work budget | N/A | `EffectProcessingLoopSystem` and existing stage cursors |
| Cross-presentation admission generation | N/A | `OrderAdmissionResultBuffer` and SchemaUpdate owner |
| Owner-scoped graph output cleanup | N/A | Existing owner retirement notifications and output store |
| Unobserved destruction fast path | N/A | Existing Arch World entity-destroyed notification |

### 3. Reuse list

- Handlers: existing builtin effect handlers and lifecycle atomic handlers
- Queues / systems: existing effect proposal/application/lifetime loop, order intake, deferred triggers, entity spawning, and World destruction
- Resolvers / registries: existing `TagOps`, `TagRuleRegistry`, `EntityBuilder`, graph runtime API, and order registries
- Existing presets / graphs: unchanged; `WriteSelfAttribute` uses the existing graph instruction through the production graph API

### 4. New Layer 0 ops

N/A. No graph op, preset enum, gameplay profile, or configuration DSL was added.

### 5. Transaction boundary

Tag and attribute writes validate complete state before mutation. The fixed-capacity dirty-entity queue accepts each entity once per logic step. Capacity failure is explicit and restores tag, count, attribute, dirty, and presentation-visible state before returning control. Effect stages debit one shared remaining-work counter and retain their existing stage/pass cursors when exhausted. Admission generations have one SchemaUpdate owner.

### 6. Config SSOT

`game.json -> gasRuntimeCapacity.deferredTriggerActiveEntityCapacity` is the single production capacity source and is validated as positive during engine composition. This is a runtime budget, not gameplay behavior. The queue is registered once as `CoreServiceKeys.DirtyEntityQueue` and shared by the production `TagOps` and deferred-trigger collector.

### 7. Red flag scan

- [x] No gameplay DSL, preset, graph op, or parallel GAS/Order pipeline added
- [x] No effect hot-path component Add/Remove fallback remains
- [x] No permanent `DirtyFlags` full-world scan remains after one-time migration bootstrap
- [x] No validated batch component is silently omitted
- [x] No stage can independently consume the full outer effect budget
- [x] No admission producer or consumer owns an independent Clear boundary
- [x] No unobserved World destruction enters the subscriber lock
- [x] Fixed-capacity overflow is explicit and mutation paths roll back
- [x] Sparse dirty processing is zero-allocation after the complete active-path warmup

### 8. Next variant test

A new Mod or gameplay variant must install tag state through `TagStateInstaller`, mutate attributes through the shared production `TagOps` path, and rely on the same effect budget and order admission generation. It must not add a Mod-local dirty queue, fallback `TagOps`, alternate effect loop, or consumer-owned admission lifetime.

---

## GAS Composition Gate - #672-#680 Audit Closeout

- **Task / Issues**: #672, #673, #674, #675, #676, #677, #678, #679, #680
- **Date**: 2026-07-14
- **Agent / Author**: Codex
- **Result**: PASS

### 1. Core judgment

Primary delivery: A. Tighten existing Order, GAS, Graph, and Navigation contracts without adding a gameplay profile, preset switch, graph operation, loader, or parallel runtime.

The audit removes presentation-state fallback, repairs listener cache invalidation, makes graph paths blittable and deterministic, validates sink channels and GAS config at load time, removes legacy projectile and duplicate movement paths, deletes retired runtime types, freezes static registries, and moves long order paths into an explicit fixed-capacity component.

### 2. Issue and commit map

| Issue | Result | Commit |
|---|---|---|
| #672 | Order planning consumes only authoritative gameplay positions | `0e19772ed` |
| #673 | Response listener registration and removal invalidate the cache | `31b92da18` |
| #674 | Graph path ECS state is fixed-capacity and blittable | `ccbed8f4f` |
| #675 | Loaded graph construction and chunk events are deterministic | `fef4f394b` |
| #676 | Attribute sink channels fail during config load; hot paths do not validate strings | `5fef49d51` |
| #677 | Legacy projectile modes and implicit direction fallback are removed | `9a1b8ac62` |
| #678 | GAS config uses canonical required fields and load-time order ids | `4b2628769` |
| #679 | Duplicate ability movement runtime and Core business tags are removed | `9d3ed6ead` |
| #680 | Retired state is deleted; registries freeze; order path layout is compact | current issue commit |

### 3. Layer assignment

| Capability | Layer | Existing carrier |
|---|---:|---|
| Order position and path resolution | N/A | `OrderWorldSpatialResolver`, `OrderSubmitter`, `OrderBufferSystem` |
| Long-path storage and ownership | N/A | `OrderSpatialPayloadBuffer` authored on capable actors |
| Response listener lifecycle | N/A | `ResponseChainListenerOps` and existing effect request queue revision |
| Graph path execution | N/A | Existing `GraphPathfindingSystem` and graph stores |
| GAS config validation | N/A | Existing ability, effect, context, and attribute binding loaders |
| Ability and context id lifecycle | N/A | Existing static id registries with `Clear` / `Freeze` |

### 4. Reuse and new operations

- Handlers: existing projectile, order, effect, and ability handlers
- Queues / systems: existing Order queue/buffer/finalizer, effect request queue, response processing, and graph pathfinding system
- Resolvers / registries: existing order spatial resolver, order type registry, ability form set registry, and context group registry
- Existing presets / graphs: unchanged
- New Layer 0 graph/effect operations: N/A
- New gameplay schema: NO

`OrderSpatialPayloadOps` is an ownership helper for an existing Order value. It does not interpret gameplay or create a second order pipeline.

### 5. Transaction and ownership boundary

Long order paths use one fixed-capacity `OrderSpatialPayloadBuffer` per capable actor. An order owns exactly one generation-checked slot while it is in global intake, active, queued, pending, or being rebuilt.

The owner releases the slot on intake rejection, queue expiration/removal, pending replacement/expiration, active finalization, cancel-all, road replan replacement, or failed road enqueue. Public buffer and queue clearing APIs reject payload-bearing orders when no `World` owner is available. Missing buffers, exhausted slots, stale handles, point-count mismatches, and blackboard capacity failures are explicit errors; none silently truncate or leak.

### 6. Config SSOT

- Business blackboard keys are declared by `GAS/order_types.json`, not hard-coded in Core.
- Ability form set and context group ids are declared during config load, then frozen.
- Projectile modes, ability execution fields, order references, and attribute sink channels have one canonical name and load-time validation.
- Runtime capacity is defined by fixed component constants derived from the formal Order slots; no dynamic allocation or hidden global pool is used.

### 7. Red flag scan

- [x] No profile inherit/placement enum added
- [x] No parallel spawn, order, graph, or movement runtime added
- [x] No presentation component is used as gameplay position truth
- [x] No legacy projectile compatibility mapping remains
- [x] No silent config default, unknown order key, path truncation, or capacity drop remains
- [x] No managed ECS path component or hot-path growth remains
- [x] No long-path ECS component is added or removed in the hot path
- [x] Every path ownership exit has an explicit release or hard failure

### 8. Historical verification snapshot

The figures below belong to the earlier #644-#680 delivery snapshot. They are retained as historical evidence and must not be used as the acceptance result for the current PR HEAD.

- #680 focused acceptance, including direct payload lifecycle tests: 41 / 41
- GAS architecture workflow guard: 45 / 45
- Association workflow slice: 127 / 127
- Raylib field workflow slice: 4 / 4
- Web UI Panel Kit tests: PASS
- Maintained `src/Tests` project graphs: all build successfully
- Production Mod smoke: 34 / 35; the only failure is the existing CameraAcceptance unbound local-player fixture
- Full GasTests at that snapshot: 1729 passed, 82 failures, 1 skipped
- Full ArchitectureTests after the two Order fixture corrections: 136 passed, 2 unrelated repository-wide legacy-token scan failures
- `git diff --check`: PASS

### 9. Next variant test

A new Order variant must reuse `OrderSpatialPayloadOps` and the existing Order finalizer. A new GAS variant must change graph wiring or effect steps. Neither may add a Core gameplay enum, fallback field name, Mod-local path pool, or alternate movement/order runtime.

---

## GAS Composition Gate - PR #660 Fan-Out Budget Diagnostics Repair

- **Task / Issue**: PR #660 audit repair for persistent fan-out budget diagnostics and retired callback state
- **Date**: 2026-07-18
- **Agent / Author**: Codex gas-diagnostics agent

### 1. Core judgment

Primary delivery: A. Repair the existing builtin fan-out accounting path and make one root budget span the complete effect-processing transaction.

Result: PASS

Reason: The change reuses `BuiltinHandlerExecutionContext.DroppedCount`, `RootBudgetTable`, `GasBudget`, `GasBudgetReportSystem`, and `EffectProcessingLoopSystem`. It adds no gameplay variant, graph op, preset switch, profile field, registry, loader, or parallel runtime.

### 2. Layer assignment

| Step / capability | Layer | Implementation carrier |
|---|---:|---|
| Root fan-out admission | N/A | Existing `RootBudgetTable` shared by the effect loop |
| Per-phase dropped accounting | N/A | Existing `BuiltinHandlerExecutionContext.DroppedCount` and `GasBudget` |
| Structured reporting | N/A | Existing `GasBudgetReportSystem` and `GasDiagnosticEventBuffer` |
| Retired callback cleanup | N/A | Existing application/lifetime systems |

### 3. Reuse list

- Handlers: existing `SpatialQuery`, `DispatchPayload`, and `ReResolveAndDispatch` builtin handlers
- Queues / Systems: `EffectProcessingLoopSystem`, proposal/application/lifetime systems, `EffectRequestQueue`, `GasBudgetReportSystem`
- Resolvers / Registries: `TargetResolverFanOutHelper`, existing `RootBudgetTable`, existing handler/template registries
- Existing presets / graphs: unchanged

### 4. New Layer 0 ops

N/A. No atomic operation, graph operation, preset, registry, or schema was added.

### 5. Transaction boundary

One `EffectProcessingLoopSystem` transaction advances one shared `RootBudgetTable` generation before its first proposal pass. Proposal, application, lifetime, and all follow-up passes consume that same generation. Every proposal-phase builtin flush, including the instant phase chain and `OnCalculate`, drains `BuiltinHandlerExecutionContext.DroppedCount` into `GasBudget` before the runtime is reset. Independently constructed systems retain a private table and advance it only when beginning their own slice.

### 6. Config SSOT

Behavior remains in the existing effect template and graph configuration. No JSON schema or config field was added.

### 7. Red flag scan

- [x] No profile inherit/placement enum added
- [x] No parallel spawn, effect, or diagnostics pipeline added
- [x] No placement validation moved into lifecycle operations
- [x] No fallback or compatibility alias added
- [x] No new handler, graph op, preset, registry, or schema added
- [x] Retired callback command/list/stage/budget state was deleted rather than preserved

### 8. Next variant test

A new Mod fan-out variant changes an effect step or graph connection and continues through the same builtin handler, root budget, and structured diagnostics path. It does not add a Core enum or alternate budget pipeline.

---

## GAS Composition Gate - Issues #686, #687, #688

- **Task / Issues**: #686 invalid transactional attribute target, #687 continuation-state preinstallation, #688 continuation payload ownership
- **Date**: 2026-07-18
- **Agent / Author**: Codex

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A（修复现有 effect transaction 与 Order continuation 管线的校验、预装和 rollback/ownership 合同；不新增玩法变体）

结论: PASS

一句话理由: 修改仅收紧现有 `EffectPhaseSideEffectTransaction`、`EntityRuntimeStatePlan`、`CompositeOrderPlanner`、`OrderSubmitter` 和 `OrderContinuationSystem`，不新增 graph op、preset、profile、registry、loader 或平行运行时。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| persistent effect side-effect rollback | 1 | 现有 `EffectPhaseSideEffectTransaction` + `EffectApplicationSystem` |
| order runtime-state assembly | N/A | 现有 `EntityRuntimeStatePlan`、scalar/batch/runtime spawn |
| continuation payload ownership | N/A | 现有 `OrderContinuationBuffer`、`OrderSubmitter`、`OrderContinuationSystem`、`CompositeOrderPlanner` |

### 3. Reuse list

- Handlers: 现有 Graph `ModifyAttributeAdd` / `ModifyAttributeSet` 入口
- Queues / Systems: `EffectApplicationSystem`、`OrderQueue`、`OrderContinuationSystem`
- Resolvers / Registries: `EntityRuntimeStatePlan`、`OrderTypeRegistry`
- Existing presets / graphs: 不变

### 4. New Layer 0 ops (if any)

N/A。没有新增 handler、graph op 或生命周期原子操作。

### 5. Transaction boundary

必须原子 rollback 的步骤: persistent effect 的全部 staged 属性、标签、fan-out、listener、presentation/event side effects；任一无效属性目标必须在 commit 前失败并由现有 application rollback 撤销。Order continuation 在注册后持有 follow-up payload；Failed/Cancelled、registration failure 或 primary admission failure必须释放一次，Completed 仅把所有权转移到正式 Order submission 链。

### 6. Config SSOT

行为配置仍位于现有 effect template/graph 与 order type catalog。

是否新增 JSON schema: NO

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn/effect/order 平行的运行时管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加默认 fallback 或兼容旁路
- [x] 未新增 registry、preset 或 schema

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线 / effect 步骤；Order 变体继续使用同一 runtime-state installer、continuation buffer 和 terminal-result 管线，不修改 Core gameplay enum。

---

## GAS Composition Gate - PR #660 Lifetime Phase Atomicity

- **Task / Issue**: PR #660 audit repair for `OnPeriod`, `OnExpire`, and `OnRemove` transaction coverage and pacemaker reset safety
- **Date**: 2026-07-19
- **Agent / Author**: Codex

### 1. Core judgment

Primary delivery: A. Extend the existing effect side-effect transaction to the existing lifetime phase execution path.

Result: PASS

Reason: The repair reuses `EffectPhaseSideEffectTransaction`, `GasGraphRuntimeApi`, `EffectLifetimeSystem`, and the existing effect/event/spawn/presentation queues. It adds no gameplay variant, graph op, preset switch, profile field, registry, loader, fallback, or parallel runtime.

### 2. Layer assignment

| Step / capability | Layer | Implementation carrier |
|---|---:|---|
| Lifetime graph side-effect staging | 1 | Existing `EffectPhaseSideEffectTransaction` bound by `EffectLifetimeSystem` |
| Lifetime scan rollback | 1 | Existing effect, target, presentation, and dirty-queue checkpoints |
| Listener owner removal | 1 | Fixed-capacity listener staging in `EffectPhaseSideEffectTransaction` |
| Pacemaker reset after commit | 1 | Existing `EffectProcessingLoopSystem.ResetSlice` and lifetime committed-cleanup tail |
| Mod behavior | 2 | Existing effect templates and phase graph bindings |

### 3. Reuse list

- Handlers: existing GAS graph and builtin handlers, including attribute, event, effect-request, and fan-out operations
- Queues / Systems: existing effect request, spawn, gameplay event, presentation, dirty-entity, lifetime, effect-loop, and pacemaker paths
- Resolvers / Registries: existing template, graph, preset, builtin-handler, condition, tag-rule, and root-budget registries
- Existing presets / graphs: unchanged

### 4. New Layer 0 ops

N/A. No graph operation, effect operation, handler, preset, registry, schema, or materialization path was added.

### 5. Transaction boundary

One lifetime slice opens one existing `EffectPhaseSideEffectTransaction` after the bounded ECS snapshot scan. `OnPeriod`, `OnExpire`, and `OnRemove` graph/builtin side effects, fan-out effect requests, and listener owner removals remain staged until all lifetime phase entries succeed. Any failure rolls back staged attributes, gameplay events, effect requests, listener changes, presentation writes, dirty-queue writes, effect timers, granted tags, and active-effect container mutations.

Reset before commit rolls back the slice. Aggregate-dirty changes commit inside the transaction; reset after commit completes only the bounded effect-destruction tail, so the pacemaker budget fuse cannot convert a diagnostic halt into an exception or abandon partial cleanup.

All staging is fixed-capacity. Listener removals are deduplicated by `(entity, ownerEffectId)` so `OnExpire` plus `OnRemove` does not consume the same ownership slot twice. Capacity exhaustion remains an explicit error.

### 6. Config SSOT

Behavior remains in the existing effect template and phase graph assets. No JSON schema or configuration field was added.

### 7. Red flag scan

- [x] No profile inherit, placement, or preset enum added
- [x] No parallel effect, lifecycle, transaction, or reset pipeline added
- [x] No lifecycle behavior was moved into a new graph op
- [x] No fallback, compatibility bypass, silent drop, or dynamic hot-path growth added
- [x] No new registry, loader, or configuration SSOT added
- [x] Failure coverage distinguishes the pre-fix direct writes from transactional rollback for all three lifetime phases
- [x] Production pacemaker reset coverage exercises the real effect-processing loop

### 8. Next variant test

A new Mod lifetime variant changes an effect template or phase graph connection and automatically uses the same lifetime transaction. It does not add a Core enum, preset switch, alternate transaction, or compatibility path.

---

## GAS Composition Gate - PR #660 Collection Performer Lifecycle

- **Task / Issue**: PR #660 main merge repair for collection-member highlight lifetime and performer implicit parent resolution
- **Date**: 2026-07-24
- **Agent / Author**: Codex

### 1. Core judgment

Primary delivery: A. Repair existing performer rule command composition and runtime lifecycle semantics for entity collection presentation events.

Result: PASS

Reason: The change reuses `EntityCollectionPresentationEventSystem`, `PerformerRuleSystem`, `PerformerRuntimeSystem`, `PerformerEntityRuntime`, and `PresentationOwnerHasPerformerPayload`. It adds no graph op, effect preset, profile field, loader, registry, fallback path, or parallel presentation pipeline.

### 2. Layer assignment

| Step / capability | Layer | Implementation carrier |
|---|---:|---|
| Collection member event command emission | N/A | Existing `PerformerRuleSystem` |
| Persistent scoped performer idempotent update | N/A | Existing `PerformerRuntimeSystem` duplicate scoped create path |
| Owner root payload parent lookup | N/A | Existing `PresentationOwnerHasPerformerPayload` marker and `PerformerState` identity |

### 3. Reuse list

- Handlers: N/A
- Queues / Systems: existing collection event, performer rule, performer runtime, and performer behavior systems
- Resolvers / Registries: existing performer definition registry, entity collection key registry, and owner payload marker
- Existing presets / graphs: unchanged

### 4. New Layer 0 ops

N/A. No lifecycle atomic op, graph operation, effect handler, preset, registry, schema, or materialization path was added.

### 5. Transaction boundary

N/A. This repair does not introduce a mutation transaction. The lifecycle boundary remains the existing performer command tick: collection row removal emits a scoped destroy for the removed row, while a metadata re-add for a surviving row must resolve to the same persistent scoped performer and update in place.

### 6. Config SSOT

Behavior remains in existing performer definition rules and collection key registration.

New JSON schema: NO.

### 7. Red flag scan

- [x] No profile inherit, placement, or preset enum added
- [x] No parallel collection, performer, or lifecycle pipeline added
- [x] No owner/parent fallback or silent create bypass added
- [x] No hard-coded definition id or implicit numeric registry slot added
- [x] No hot-path structural mutation pattern added

### 8. Next variant test

A new Mod collection highlight variant changes performer definition rules or collection event bindings. It continues through the same collection event stream and performer command runtime; it does not add a Core enum, alternate parent resolver, or compatibility path.

---

## GAS Composition Gate - PR #660 Self Regression Test Isolation

- **Task / Issue**: PR #660 self-regression cleanup after full GasTests / merge-base TRX comparison
- **Date**: 2026-07-23
- **Agent / Author**: Codex supervising Cursor

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A（收紧现有 GAS 测试的 registry / attribute SSOT 使用；不新增 gameplay 变体）

结论: PASS

一句话理由: 修改只让 PR 新增/改过的 GAS 测试显式隔离全局 id registry，并通过 `AttributeRegistry` 使用测试专属属性 id；没有新增 graph op、effect preset、profile 字段、loader、registry 或平行运行时。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| Ability form set 测试隔离 | N/A | `TagStateInstallationContractTests` fixture setup/teardown |
| Mud demo 测试属性 SSOT | N/A | `AttributeRegistry` + `AttributeBuffer.SetBase` |

### 3. Reuse list

- Handlers: N/A
- Queues / Systems: existing Ability / Effect phase test systems unchanged
- Resolvers / Registries: existing `AbilityFormSetIdRegistry`, `AttributeRegistry`
- Existing presets / graphs: existing test graph instructions unchanged except attribute id source

### 4. New Layer 0 ops (if any)

N/A

### 5. Transaction boundary

必须原子 rollback 的步骤: N/A；本次不改生产 mutation transaction。测试只消除跨用例全局状态污染，避免 full suite 中 registry freeze 和 attribute constraints 影响后续用例。

### 6. Config SSOT

行为配置落在: existing test graph instructions and registry ids.

是否新增 JSON schema: NO

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn/effect/order 平行的运行时管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加默认 fallback 或兼容旁路
- [x] 未新增 registry、preset 或 schema

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线 / effect 步骤。测试若需要属性或 form set id，继续从正式 registry 取 id，不硬编码共享全局槽位。
