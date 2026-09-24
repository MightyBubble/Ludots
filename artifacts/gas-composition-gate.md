# GAS Composition Gate - Raylib Performer Micro Showcases

- Date: 2026-07-24
- Agent: Codex
- Result: PASS

## Core Judgment

Primary delivery is A: concrete performer composition and adapter rendering over existing Presentation infrastructure. This work does not add a GAS preset, gameplay graph op, lifecycle profile enum, or parallel loader. The authoring idea stays as offline expansion into existing performer definitions and concrete `AssetKind` values.

## Layer Assignment

| Step / Capability | Layer | Carrier |
| --- | --- | --- |
| Player semantic authoring notes | Layer 3 authoring preset concept only | PRD / report documentation |
| Semantic plus theme expansion | Layer 2 composition concept | Generated performer child graph data |
| Runtime creation and cleanup | Existing runtime | `CreatePerformer` / `DestroyPerformerScope` commands |
| Concrete drawing | Adapter primitive | `AssetKind.Ring`, `Line`, `ParticleEmitter`, `RibbonTrail`, `Mesh` context |

## Reuse List

- Handlers: existing presentation command handlers; no new GAS `BuiltinHandler`.
- Queues / Systems: existing Performer emit systems, Presentation request flush, Raylib primitive draw path.
- Resolvers / Registries: `PerformerDefinitionConfigLoader`, `PerformerDefinitionRegistry`, `AssetKind` parser and retired-kind validation.
- Existing data path: mod assets, launcher binding/preset registry, Raylib screenshot capture environment variables.

## New Layer 0 Ops

N/A. No entity lifecycle atomic op is added.

## Transaction Boundary

Runtime uses the existing performer scope lifecycle: a spawned showcase entity creates one root performer, and entity destruction destroys that scope. There is no new all-or-nothing gameplay transaction.

## Config SSOT

- Performer data: `mods/showcases/performer_raylib_micro_showcases/PerformerRaylibMicroShowcasesMod/assets/Presentation/performers.json`
- Showcase manifest: `mods/showcases/performer_raylib_micro_showcases/PerformerRaylibMicroShowcasesMod/assets/Presentation/showcases.manifest.json`
- Launcher entries: `launcher.config.json`, `launcher.presets.json`, `showcase.registry.json`

New JSON schema: NO. The generated files use existing performer, map, entity template, launcher and registry shapes.

## Red Flag Scan

- [x] No new profile inherit/placement enum
- [x] No new spawn/morph/lifecycle materialization pipeline
- [x] No new generic `VFX` / `Effect` asset kind or compatibility alias
- [x] No silent fallback for retired `AssetKind.VFX`
- [x] Theme remains offline authoring parameters, not a performer behavior

## Next Variant Test

The next visual semantic variant should change performer child composition or concrete primitive parameters. It must not add a semantic `AssetKind`, a generic VFX registry, or a runtime theme decision path.

---

# GAS Composition Gate - PR #658 / Issue #690

- Date: 2026-07-19
- Agent: Codex
- Result: PASS

## Core judgment

主要交付物：A，复用现有 Command Router、OrderQueue、OrderBuffer、GAS system phase 和 typed MovePlan contract，删除 MassNavigation/Formation 的平行 Order consumer。

本次没有新增 effect preset、profile enum、graph op、lifecycle DSL 或平行 loader。`MovePlanExecutionMode` 是中立执行端口的显式类型边界，不是 GAS 玩法变体开关。

## Layer assignment

| 能力 | Layer | 实现载体 |
| --- | --- | --- |
| cluster actor expansion | Input/Command Router extension | `ICommandActorExpander` / `FormationCommandActorExpander` |
| atomic admission and activation | existing GAS order infrastructure | `OrderQueue` / `OrderBufferSystem` |
| Order to typed movement | GAS adapter | `MovePlanOrderProjectionSystem` |
| typed movement execution | MovePlanning port + Mass adapter | `MovePlanExecutionIntent` / `MassNavigationMovePlanExecutionSystem` |
| typed result to lifecycle | GAS adapter | `MovePlanOrderLifecycleSystem` |

## Reuse list

- Handlers: existing order type registry and order rules; no new BuiltinHandler.
- Queues / Systems: `OrderQueue`, `OrderBufferSystem`, `OrderSubmitter`, existing `SystemGroup.AbilityActivation`.
- Resolvers / Registries: `CommandIntentProfileRegistry`, `CastDispatchProfileRegistry`, `OrderTypeRegistry`, `ControlDomainQuery`.
- Existing contracts: `MovePlanExecutionIntent`, `IMovePlanExecutionSink`, `MassNavigationRuntimeBinding`.

## New Layer 0 ops

N/A. No entity lifecycle atomic op was added.

## Transaction boundary

- Command Router fan-out validates expansion capacity and submits one clustered batch.
- `OrderBufferSystem` previews every row before activating any row.
- Mass command-group execution prepares final resolved destination and member targets once, validates binding/focus/route capacity against that exact data, then commits the same prepared targets without recomputation.
- Route rejection emits typed failure; GAS cancels the matching order and removes its continuation.

## Config SSOT

- Order catalog: `mods/capabilities/navigation/MassNavigationMod/assets/GAS/order_types.json`
- Formation business data: `mods/showcases/formation_capability/FormationCapabilityShowcaseMod/assets/FormationCapabilityShowcaseConfig.json`
- Input routing: `mods/showcases/formation_capability/FormationCapabilityShowcaseMod/assets/Input/`
- Mass capacities: each Mod's `MassNavigationConfig.json`

新增 JSON schema: NO. Renamed dead ingestion capacity fields to typed MovePlan execution capacity fields and removed the unused `orderIdleScanIntervalFrames` property.

## Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建 spawn/order/MovePlan 平行管线
- [x] 未添加 fallback 或兼容旁路
- [x] MassNavigation 不读取或反写 Order
- [x] Formation 不拥有专用 Order consumer
- [x] 热路径容量显式，容量不足失败

## Next variant test

下一个 Formation Mod 变体应修改 Mod-owned anchor/member 数据和 `ICommandActorExpander` 实现，继续复用同一 Command Router、GAS Order 与 typed MovePlan 链；不得新增 Core Formation enum 或专用 order pipeline。
