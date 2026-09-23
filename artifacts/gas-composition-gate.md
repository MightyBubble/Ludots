## GAS Composition Gate — Self Review

- **Task / Issue**: Case E 10k two-player showcase bootstrap
- **Date**: 2026-09-07
- **Agent / Author**: Codex

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A — existing spawn operation with data-driven requests and existing query graph wiring.

结论: PASS

一句话理由: The showcase composes the existing runtime template spawn queue and existing query graphs; it adds no profile field, enum, semantic component, or parallel materialization path.

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 10k entity materialization | 0/1 existing | `RuntimeEntitySpawnQueue` and `RuntimeEntitySpawnSystem` |
| candidate membership | 2 existing | `BindQueryCollection` graph assets |
| player switch | showcase orchestration | existing seat and logic-view registries |

### 3. Reuse list

- **Handlers:** existing template spawn handler and presentation bootstrap.
- **Queues / Systems:** `RuntimeEntitySpawnQueue`, `RuntimeEntitySpawnSystem`, `SeatPossessionSyncSystem`, existing presentation system registration.
- **Resolvers / Registries:** `PlayerEntityLookup`, `ClientLocalSeatRegistry`, `LogicViewRegistry`, `EntitySetQueryRuntime` / `DerivedEntityIndex`.
- **Existing presets / graphs:** Case E selection graphs and presenter assets.

### 4. New Layer 0 ops (if any)

N/A.

### 5. Transaction boundary

The only batch operation is accepted by the existing spawn queue; enqueue must write all requested entries or fail before the scenario is marked spawned. Player switching updates the existing seat and lookup-facing session state as one method call.

### 6. Config SSOT

Behavior configuration remains in the existing map, entity template, input, and graph assets. No new JSON schema.

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线 / effect 步骤（graph filter parameters and map data only).
