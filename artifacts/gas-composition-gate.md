## GAS Composition Gate — Self Review

- **Task / Issue**: #1087 Entity history, knowledge-safe effect targets, and Effect History Showcase
- **Date**: 2026-08-24
- **Agent / Author**: Codex

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A — 新的 effect target resolution steps and existing-op composition.

结论: PASS

一句话理由: Live、known、last-known、point、cell and stale behavior are target-resolution data and effect steps; no new preset enum or parallel lifecycle DSL is required.

### 2. Layer assignment

| 步骤/能力 | Layer | 实现载体 |
|---|---:|---|
| Entity identity capture | 0 | value contract + lifecycle capture |
| Snapshot insertion/expiry | 1 | bounded store transaction |
| Effect target resolution | 0/1 | resolver + explicit result |
| Effect execution history | 1 | RootId-bound record buffer |
| Live/LastKnown/Point/Cell compositions | 2 | existing effect graph/template composition |
| Interactive showcase | 3 | Showcase Mod and configured effect templates |

### 3. Reuse list

- Handlers: existing Effect handlers and `EffectTargetPointResolver`.
- Queues / Systems: existing EffectRequestQueue, GameplayEventBus, Attribute/Tag changed triggers, lifecycle queues.
- Resolvers / Registries: existing KnowledgeProjectionResolver, ConfigPipeline, EffectTemplateRegistry, RootId allocation.
- Existing presets / graphs: existing effect graph and projectile delivery only as a consumer, never as a new target SSOT.

### 4. New Layer 0 ops

| Op | Single responsibility | Why existing ops cannot compose |
|---|---|---|
| CaptureEntitySnapshot | Read declared facts before removal | No existing unified lifecycle capture entry exists |
| ResolveEffectTargetRef | Resolve explicit target variant and stale result | Existing live-only blackboard target read has no history variant |

### 5. Transaction boundary

Target acquisition record, effect target resolution, snapshot capture, and Attribute/Tag writes must be committed or reported as an explicit rejected result. Store capacity failures must not partially publish a success record.

### 6. Config SSOT

Behavior configuration lives in existing effect templates/graphs and Knowledge configuration. No new JSON schema or preset DSL is introduced.

### 7. Red flag scan

- [x] No profile inherit/placement enum added
- [x] No parallel spawn/materialization pipeline
- [x] Placement validation remains outside lifecycle capture
- [x] No implicit default fallback for stale or missing knowledge values

### 8. Next variant test

The next Mod variant changes graph connections or effect steps, not a Core enum.
