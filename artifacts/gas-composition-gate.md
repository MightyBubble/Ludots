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

---

# GAS Composition Gate - Physics3D 30Hz Scale Extension

## Task Summary

The Physics3D 30Hz scale extension completes network-entity removal semantics,
session-epoch teardown, fixed-input admission, and Physics3D replication on the
existing issue #709 networking runtime. It does not add a gameplay lifecycle
preset, spawn/morph schema, or a second materialization pipeline.

## GAS Composition Gate - Self Review

- **Task / Issue**: Physics3D 30Hz fixed-input and replication completion
- **Date**: 2026-07-24
- **Agent / Author**: Codex with delegated agents

### 1. Core judgment

New variant primary deliverable (A/B/C/D): **A**

Conclusion: **PASS**

One-line reason: The work extends the existing committed lifecycle and
networking boundaries; gameplay variants remain effect/graph compositions and
no profile switch or parallel spawn path is introduced.

### 2. Layer assignment

| Step / capability | Layer (0/1/2/3) | Implementation carrier |
|---|---:|---|
| Allocate or release a network handle at a committed Tick | 1 | Existing lifecycle commit boundary plus `NetworkEntityTable` |
| Apply conceal or permanent removal on a client | 1 | Existing replication bridge transaction with explicit apply context |
| Admit a player's fixed-frame input | 1 | Existing authenticated session seat plus authoritative Tick state |
| Compose character, vehicle, platform, and ragdoll behavior | 2 | Existing Physics3D gameplay systems and Mod configuration |
| Present the ten-station Playground | 3 | Existing capability showcase Mod and launch presets |

### 3. Reuse list

- Handlers: existing lifecycle built-in handlers; no new GAS handler.
- Queues / Systems: existing runtime lifecycle queues, Pacemaker fixed Tick,
  authoritative/client network runtimes, and replication bridges.
- Resolvers / Registries: `NetworkEntityTable`, `SessionSeatBinding`, existing
  Core service keys, Mod/config registries, and entity association services.
- Existing presets / graphs: current spawn, consume, character, traversal,
  vehicle, ragdoll, and Physics3D showcase compositions.

### 4. New Layer 0 ops (if any)

N/A. Conceal, removal, input admission, and session teardown are networking
state transitions around existing lifecycle operations, not new gameplay
lifecycle operations.

### 5. Transaction boundary

Network handles are allocated or released only at a committed fixed-frame
boundary. A snapshot page is validated before mutation. Session-epoch changes
tear down the complete old client mirror before creating the new bridge. A
fixed-input batch is fully validated before any frame in the batch is admitted.

### 6. Config SSOT

Gameplay behavior remains in existing effect templates, graphs, and Mod
configuration. Networking capacities and fixed-input schema live in the
existing versioned networking configuration path.

New JSON schema: **NO**. The extension adds fields to the existing networking
contract and does not create a parallel loader or gameplay DSL.

### 7. Red flag scan

- [x] No profile inheritance or placement enum is added.
- [x] No materialization pipeline parallel to spawn is created.
- [x] Placement validation is not moved into a lifecycle operation.
- [x] No unnamed default fallback is added.

### 8. Next variant test

The next Mod variant changes **graph wiring / effect steps** and existing
networking profile data. It does not require a Core gameplay enum.

## Reuse / Add Matrix

| Type | Items |
|---|---|
| Reuse | Pacemaker Tick; authenticated session seats; `NetworkEntityTable`; authoritative/client runtimes; replication bridge; lifecycle queues; Physics3D modules |
| Add Layer 0 op | None |
| Add Layer 1 | Explicit replication apply context; conceal/removal release contract; epoch teardown; fixed-input admission and acknowledgement |
| Add Layer 2 | Physics3D network projector/applier and existing Mod configuration |
| Forbidden | Tombstone registry; second Tick/AOI/baseline/input-ACK truth; silent missing input; implicit keep-last-input; parallel materialization path |

---

# GAS Composition Gate - Physics3D Ordinary Replicated Body Lifecycle

## Task Summary

This change registers already-materialized ordinary Physics3D bodies with the
existing authoritative network entity table, applies registration and release
only at the fixed-frame structural binding phase, and makes the existing
Physics3D AOI port own its per-seat live knowledge projection. It does not
materialize entities, add a gameplay preset, or create another AOI or snapshot
pipeline.

## GAS Composition Gate - Self Review

- **Task / Issue**: Physics3D ordinary replicated-body lifecycle and AOI knowledge lanes
- **Date**: 2026-07-25
- **Agent / Author**: Codex delegated agent

### 1. Core judgment

New variant primary deliverable (A/B/C/D): **A**

Conclusion: **PASS**

One-line reason: The work extends the existing network identity and committed
lifecycle boundary; it adds no lifecycle profile switch, preset enum, or
parallel entity materialization path.

### 2. Layer assignment

| Step / capability | Layer (0/1/2/3) | Implementation carrier |
|---|---:|---|
| Admit an existing valid Physics3D body for replication | 1 | Fixed-capacity registry command buffer plus `NetworkEntityTable` |
| Release an ordinary replicated body | 1 | Generation-checked registry transaction at `RuntimeEntityBinding` |
| Project one seat's current AOI into live knowledge | 1 | Existing `Physics3DNetworkAoiInterestPort` plus `KnowledgeProjectionStore` |
| Select which bodies are network-authored | 2 | Existing `ReplicationSchemaRef` on ECS entities |

### 3. Reuse list

- Handlers: no GAS handler is added.
- Queues / Systems: existing fixed-frame phase order and
  `SystemGroup.RuntimeEntityBinding` structural boundary.
- Resolvers / Registries: `NetworkEntityTable`, `KnowledgeProjectionStore`,
  `Physics3DNetworkPlayerLifecycle`, existing replication schema registry.
- Existing presets / graphs: N/A; gameplay graphs continue to materialize and
  configure bodies before the network binding phase sees them.

### 4. New Layer 0 ops (if any)

N/A. Network registration and AOI disclosure are infrastructure state
transitions, not gameplay lifecycle operations.

### 5. Transaction boundary

An ordinary body is published only after its ECS body, pose, Physics3D world
body, and replication schema all validate. Release validates the registry
slot, generation, entity table mapping, and ECS component before removing the
component and releasing the handle. Commands apply only while an authoritative
simulation tick is executing in `RuntimeEntityBinding`.

### 6. Config SSOT

Networking capacities remain in the existing `Physics3D/network.v1.json` and
merged networking profile. Gameplay behavior remains in existing effects,
graphs, and Mod configuration.

New JSON schema: **NO**. Capacity values extend the existing Physics3D network
configuration object and use the existing config loader.

### 7. Red flag scan

- [x] No profile inheritance or placement enum is added.
- [x] No materialization pipeline parallel to spawn is created.
- [x] Placement validation is not moved into a lifecycle operation.
- [x] No unnamed default fallback is added.

### 8. Next variant test

The next Mod variant changes **graph wiring / effect steps** and attaches the
existing `ReplicationSchemaRef`; it does not require a Core gameplay enum.

## Reuse / Add Matrix

| Type | Items |
|---|---|
| Reuse | `NetworkEntityTable`; `KnowledgeProjectionStore`; Physics3D body/pose/schema components; fixed-frame phase order; current AOI and replication bridge |
| Add Layer 0 op | None |
| Add Layer 1 | Fixed-capacity ordinary-body registration/release transaction; per-seat AOI knowledge lane |
| Add Layer 2 | Existing `ReplicationSchemaRef` data selects replicated bodies |
| Forbidden | Second AOI/snapshot builder; hot-path structural change; unbounded body/seat Cartesian knowledge; silent stale/capacity cleanup |
