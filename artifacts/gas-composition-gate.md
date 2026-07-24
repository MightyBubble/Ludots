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
