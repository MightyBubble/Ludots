## GAS Composition Gate — Self Review

Current closeouts and prior issue reviews follow.

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
