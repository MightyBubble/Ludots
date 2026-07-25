# GAS Composition Gate - Issue #709

## Task Summary

Issue #709 adds a platform-independent, server-authoritative multiplayer Core,
an RTS command path that reuses the existing GAS order queue, and the
`rts_duel_v1` two-player showcase. It does not add a gameplay preset, a new
spawn/morph DSL, or a parallel entity materialization path.

## GAS Composition Gate - Self Review

- **Task / Issue**: #709 - RTS multiplayer Core and playable showcase
- **Date**: 2026-07-24
- **Agent / Author**: Codex

### 1. Core judgment

New variant primary deliverable (A/B/C/D): **A**

Conclusion: **PASS**

One-line reason: The showcase composes the existing order and lifecycle
pipelines; the new work is a reusable networking boundary, not a new GAS
profile, preset switch, or materialization pipeline.

### 2. Layer assignment

| Step / capability | Layer (0/1/2/3) | Implementation carrier |
|---|---:|---|
| Admit an RTS command batch | 1 | Fixed-capacity networking command admission plus existing `OrderQueue` |
| Materialize authoritative units | 0/1 | Existing `RuntimeEntitySpawnQueue` and lifecycle transaction path |
| Compose duel gameplay | 2 | Existing GAS graphs, effects, orders, and Mod configuration |
| Package the playable duel | 3 | `rts_duel_v1` showcase Mod and launch presets |

### 3. Reuse list

- Handlers: existing GAS order handlers and lifecycle built-in handlers.
- Queues / Systems: `OrderQueue`, `OrderBufferSystem`,
  `RuntimeEntitySpawnQueue`, `RuntimeEntityLifecycleQueue`, Pacemaker fixed
  simulation boundary.
- Resolvers / Registries: `ControlDomainQuery`, Entity Association Core,
  `KnowledgeProjectionResolver`, `KnowledgeProjectionStore`, existing config
  and Mod registries.
- Existing presets / graphs: current RTS move, attack, production, spawn, and
  lifecycle compositions. The showcase may configure them but must not fork
  their runtime implementation.

### 4. New Layer 0 ops (if any)

N/A. Network entity handle allocation is a networking identity service, not a
GAS lifecycle op. It attaches to the existing committed lifecycle boundary.

### 5. Transaction boundary

The following operations are all-or-nothing:

1. An inbound command batch is either fully admitted to the fixed-capacity
   command buffer or rejected with a typed reason.
2. Network handles for a committed spawn set are capacity-checked before any
   handle is published.
3. A client applies a structurally valid snapshot page as one committed unit;
   malformed or capacity-exceeding pages are rejected explicitly.

Existing lifecycle materialization and rollback remain owned by the current
lifecycle transaction executor.

### 6. Config SSOT

Behavior configuration lives in the existing effect templates, GAS graphs,
order definitions, Mod config pipeline, and a versioned networking session
profile under the normal config pipeline.

New JSON schema: **YES** - a networking session/replication schema is required
for capacities, protocol rates, and replicated fields. It describes transport
and replication policy only; it does not encode lifecycle inheritance,
placement, spawn behavior, or a gameplay preset DSL.

### 7. Red flag scan

- [x] No profile inheritance or placement enum is added.
- [x] No materialization pipeline parallel to spawn is created.
- [x] Placement validation is not moved into a lifecycle op.
- [x] No unnamed default fallback is added.

### 8. Next variant test

The next Mod variant changes **graph wiring / effect steps and networking
profile data**. It does not require a Core enum.

## Reuse / Add Matrix

| Type | Items |
|---|---|
| Reuse | Pacemaker fixed tick; `OrderQueue`; `OrderBufferSystem`; Entity Association Core; knowledge projection; runtime spawn/lifecycle queues; config and Mod loading |
| Add Layer 0 op | None |
| Add Layer 1 | Networking command-batch admission; snapshot apply transaction |
| Add Layer 2 | RTS duel composition using existing gameplay capabilities |
| Forbidden | Networking-specific order queue; spawn/morph profile DSL; browser/socket types in Core; silent capacity truncation |

## Follow-up Review - Reusable Resource And Attack Loops

- **Task / Issue**: #709 - remove Showcase-private harvest and combat runtimes
- **Date**: 2026-07-25
- **Agent / Author**: Codex

### 1. Core judgment

New variant primary deliverable (A/B/C/D): **A**

Conclusion: **PASS**

One-line reason: the reusable loops consume authored ECS profile data and submit
existing orders/effects; no handler, preset switch, graph op, lifecycle op, or
parallel queue is introduced.

### 2. Layer assignment

| Step / capability | Layer (0/1/2/3) | Implementation carrier |
|---|---:|---|
| Route a carrier to a source and sink | 0 | Core resource-transport system over `OrderQueue` |
| Credit an authored resource attribute | 0 | Existing `AttributeBuffer` mutation |
| Pursue a target and request an effect | 0 | Core direct-attack system over `OrderQueue` and `EffectRequestQueue` |
| Define Frontline crystals and infantry damage | 2 | Showcase entity profiles, order ids, tags, and existing effect templates |

### 3. Reuse list

- Handlers: existing effect-template processing and attribute modifiers.
- Queues / Systems: `OrderQueue`, `OrderBuffer`, `OrderSubmitter`,
  `EffectRequestQueue`, and the existing movement/order systems.
- Resolvers / Registries: `AttributeRegistry`, `EffectTemplateIdRegistry`,
  `ComponentRegistry`, `OrderTypeRegistry`, and `TeamManager` relationship policy.
- Existing presets / graphs: Frontline's existing instant-damage effect and move
  orders; no new graph or preset is required.

### 4. New Layer 0 ops

| Op name | Single responsibility | Why existing ops cannot compose it |
|---|---|---|
| Resource transport tick | Advance source/load/sink state while delegating movement to the existing order path | Existing orders move actors but do not own cargo timing or sink credit |
| Direct attack tick | Advance pursue/cooldown state and publish an existing effect template | Existing attack intent and effect application do not connect target pursuit to repeated effect requests |

### 5. Transaction boundary

No new rollback transaction is introduced. Movement remains an order; damage
remains an effect request; spawn/death remain on their existing lifecycle paths.
Queue capacity failures are explicit and never fall back to direct mutation.

### 6. Config SSOT

Behavior data lives in Core-authored ECS profile components in
`Entities/templates.json`; damage remains in the existing effect template.

New JSON schema: **NO** - component authoring uses the existing entity-template
pipeline and strict `ComponentRegistry` setters.

### 7. Red flag scan

- [x] No profile inheritance or placement enum is added.
- [x] No materialization pipeline parallel to spawn is created.
- [x] Placement validation is not moved into a lifecycle op.
- [x] No unnamed default fallback is added.

### 8. Next variant test

The next Mod variant changes **effect steps and authored profile values**. It
does not require a Core enum or a private gameplay runtime.

## Follow-up Review - Atomic Match Resolution Evidence

- **Task / Issue**: #709 - preserve the authoritative result across core destruction
- **Date**: 2026-07-25
- **Agent / Author**: Codex

### 1. Core judgment

New variant primary deliverable (A/B/C/D): **A**

Conclusion: **PASS**

One-line reason: the change records an immutable result at the existing match
commit boundary before the existing lifecycle cleanup removes defeated
entities; it does not add a lifecycle op, profile field, preset, or queue.

### 2. Layer assignment

| Step / capability | Layer (0/1/2/3) | Implementation carrier |
|---|---:|---|
| Read both command-core health values | 0 | Existing chunk query in `FrontlineDeathAndMatchSystem` |
| Commit outcome and evidence atomically | 1 | Existing `FrontlineRuntime` match transaction boundary |
| Remove defeated replicated entities | 0/1 | Existing `PresentationDestroyPending` and network binding cleanup |
| Prove the player-visible result | 2/3 | Existing match-state replication and three-process acceptance Mod |

### 3. Reuse list

- Handlers: N/A; no GAS handler changes.
- Queues / Systems: `FrontlineDeathAndMatchSystem`, existing cleanup command
  buffer, and `FrontlineNetworkEntityBindingSystem`.
- Resolvers / Registries: existing health attribute registry and Frontline
  participant side mapping.
- Existing presets / graphs: N/A; death remains driven by the existing damage
  effect and attack loop.

### 4. New Layer 0 ops

N/A. The resolution snapshot is an immutable value captured by the existing
match transaction, not an entity structural operation.

### 5. Transaction boundary

Outcome, winning side, resolution reason, committed tick, and both final core
health values are validated and committed together before cleanup can remove a
defeated core.

### 6. Config SSOT

Behavior remains in the existing Frontline config, effects, and entity
templates. New JSON schema: **NO**.

### 7. Red flag scan

- [x] No profile inheritance or placement enum is added.
- [x] No materialization pipeline parallel to spawn is created.
- [x] Placement validation is not moved into a lifecycle op.
- [x] No unnamed default fallback is added.

### 8. Next variant test

The next Mod variant changes **effect steps** and match configuration. It does
not require a Core enum or a parallel lifecycle path.

### Reuse / Add Matrix

| Type | Items |
|---|---|
| Reuse | Frontline match commit; health attributes; cleanup command buffer; replicated match state; acceptance evidence |
| Add Layer 0 op | None |
| Add Layer 1 | Immutable resolution value committed with the existing outcome transaction |
| Add Layer 2 | None |
| Forbidden | Delayed destruction; retained dead core; post-destruction ECS lookup; silent terminal-state fallback |

## Follow-up Review - Queued Training Player Acceptance

- **Task / Issue**: #709 - prove immediate and queued training in the real three-process player flow
- **Date**: 2026-07-25
- **Agent / Author**: Codex

### 1. Core judgment

New variant primary deliverable (A/B/C/D): **A**

Conclusion: **PASS**

One-line reason: the acceptance flow submits the existing training ability twice
and observes the existing order queue transition from queued to active; it adds
no handler, preset, profile field, schema, registry, or gameplay pipeline.

### 2. Layer assignment

| Step / capability | Layer (0/1/2/3) | Implementation carrier |
|---|---:|---|
| Submit normal and queued training | 2 | Existing `TrainInfantry` command intent and replicated command port |
| Admit, queue, and activate orders | 1 | Existing order admission transaction and `OrderQueue` |
| Materialize trained infantry | 0/2 | Existing configured GAS ability/effect chain |
| Record player-visible proof | 3 | Existing three-process acceptance Mod and evidence writer |

### 3. Reuse list

- Handlers: existing training ability handlers; no changes.
- Queues / Systems: existing `OrderQueue`, order admission pipeline, and training continuation.
- Resolvers / Registries: existing command intent, ability, entity template, and network schema registries.
- Existing presets / graphs: existing Frontline training ability and effects.

### 4. New Layer 0 ops

N/A.

### 5. Transaction boundary

Each training submission keeps the existing atomic admission and resource charge
boundary. The acceptance code only observes the second order being queued and
later activated after the first completes.

### 6. Config SSOT

Behavior remains in the existing Frontline ability, entity template, command
intent, and networking configuration assets. New JSON schema: **NO**.

### 7. Red flag scan

- [x] No profile inheritance or placement enum is added.
- [x] No materialization pipeline parallel to spawn is created.
- [x] Placement validation is not moved into a lifecycle op.
- [x] No unnamed default fallback is added.

### 8. Next variant test

The next Mod variant changes **effect steps or graph wiring**. It does not add a
Core enum or a private training runtime.

### Reuse / Add Matrix

| Type | Items |
|---|---|
| Reuse | Training ability/effects; order admission; `OrderQueue`; replicated command results; acceptance evidence |
| Add Layer 0 op | None |
| Add Layer 1 | None |
| Add Layer 2 | None |
| Forbidden | New training preset mode; private Showcase queue; duplicate resource ledger; silent command fallback |

## Follow-up Review - Repeated CreateUnit Scatter Identity

- **Task / Issue**: #709 - keep separately trained units independently selectable
- **Date**: 2026-07-25
- **Agent / Author**: Codex

### 1. Core judgment

New variant primary deliverable (A/B/C/D): **A**

Conclusion: **PASS**

One-line reason: the existing `CreateUnit` atomic handler keeps the same
placement mode and authored radius, while the shared phase executor now
preserves the request root and effect entity identities already carried by the
formal effect pipeline.

### 2. Layer assignment

| Step / capability | Layer (0/1/2/3) | Implementation carrier |
|---|---:|---|
| Resolve the authored spawn origin | 0 | Existing `EffectTargetPointResolver` |
| Preserve effect identity through phase execution | 0 | Existing `EffectPhaseExecutor` and `EffectContext` |
| Derive a deterministic per-effect scatter offset | 0 | Existing `CreateUnit` built-in handler |
| Queue and materialize the unit | 0/1 | Existing `RuntimeEntitySpawnQueue` and lifecycle path |
| Prove units remain independently selectable | 3 | Existing three-process player-input acceptance Mod |

### 3. Reuse list

- Handlers: existing `BuiltinHandlerId.CreateUnit` handler.
- Queues / Systems: existing `EffectRequestQueue`, `EffectPhaseExecutor`,
  `RuntimeEntitySpawnQueue`, and spawn materialization systems.
- Resolvers / Registries: existing `EffectTargetPointResolver`; the request
  `RootId` and live effect entity identity already owned by the effect pipeline.
- Existing presets / graphs: existing Frontline training ability and
  `Effect.Rts.Frontline.CreateInfantry` template.

### 4. New Layer 0 ops

N/A. The change repairs context propagation through the existing phase executor
and corrects the deterministic input set of the existing atomic placement
operation.

### 5. Transaction boundary

No new rollback boundary is introduced. All spawn requests from one
`CreateUnit` invocation retain the existing queue-capacity and materialization
semantics.

### 6. Config SSOT

The placement mode and radius remain authored in the existing effect template.
New JSON schema: **NO**.

### 7. Red flag scan

- [x] No profile inheritance or placement enum is added.
- [x] No materialization pipeline parallel to spawn is created.
- [x] Placement validation is not moved into a lifecycle op.
- [x] No unnamed default fallback is added.

### 8. Next variant test

The next Mod variant changes **effect steps or authored placement values**. It
does not require a Core enum.

### Reuse / Add Matrix

| Type | Items |
|---|---|
| Reuse | `CreateUnit`; `EffectContext.RootId`; effect entity identity; `EffectPhaseExecutor`; `EffectTargetPointResolver`; runtime spawn queue and materializer |
| Add Layer 0 op | None |
| Add Layer 1 | None |
| Add Layer 2 | None |
| Forbidden | Per-Mod spawn counter; new placement mode; wall-clock/random seed; silent overlap fallback |

## Follow-up Review - Async Effect Root Propagation

- **Task / Issue**: #709 - preserve effect roots across asynchronous spawn and projectile carriers
- **Date**: 2026-07-26
- **Agent / Author**: Codex

### 1. Core judgment

New variant primary deliverable (A/B/C/D): **A**

Conclusion: **PASS**

One-line reason: The change extends existing fixed-size carrier structs with the
effect root already owned by `EffectContext`; it adds no handler, graph op,
preset switch, schema, registry, or parallel runtime pipeline.

### 2. Layer assignment

| Step / capability | Layer (0/1/2/3) | Implementation carrier |
|---|---:|---|
| Carry the parent root through deferred materialization | 0 | Existing `RuntimeEntitySpawnRequest` and `RuntimeEntitySpawnSystem` |
| Carry the parent root through projectile travel | 0 | Existing `ProjectileState` and `ProjectileRuntimeSystem` |
| Publish derived effects under the same root | 0 | Existing `EffectRequestQueue` |

### 3. Reuse list

- Handlers: existing `CreateUnit` and `CreateProjectile` built-in handlers.
- Queues / Systems: `RuntimeEntitySpawnQueue`, `RuntimeEntitySpawnSystem`,
  `ProjectileRuntimeSystem`, and `EffectRequestQueue`.
- Resolvers / Registries: existing effect template and entity template registries.
- Existing presets / graphs: unchanged; all existing compositions inherit the fix.

### 4. New Layer 0 ops

N/A. No new operation is introduced; this repairs context propagation through
the existing asynchronous carriers.

### 5. Transaction boundary

No new rollback boundary is introduced. Spawn queue admission, materialization,
projectile travel, and effect request publication retain their current ownership.
Configured derived effects fail explicitly when their required queue is absent.

### 6. Config SSOT

Behavior remains in existing effect and entity templates. New JSON schema:
**NO**.

### 7. Red flag scan

- [x] No profile inheritance or placement enum is added.
- [x] No materialization pipeline parallel to spawn is created.
- [x] Placement validation is not moved into a lifecycle op.
- [x] No unnamed default fallback is added.

### 8. Next variant test

The next Mod variant changes **effect steps or graph wiring**. It does not add a
Core enum or a parallel asynchronous effect carrier.

### Reuse / Add Matrix

| Type | Items |
|---|---|
| Reuse | `EffectContext.RootId`; existing spawn and projectile carriers; `EffectRequestQueue` |
| Add Layer 0 op | None |
| Add Layer 1 | None |
| Add Layer 2 | None |
| Forbidden | Fresh child roots for derived effects; silent missing effect queue; Mod-private carrier |
