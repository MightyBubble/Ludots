# Scenario: rfc0065-showcase-workflow

## Header
- build: GasTests / Show5Show6Workflow_PointerCommandRoutesThroughIntentDispatchAndOrderBuffer
- seed: interaction_showcase_hub deterministic headless run
- clock: engine fixed step sampled through 1/60s test ticks
- execution timestamp UTC: 2026-08-16T17:27:15.7211690+00:00

## Scenario Card
- Player goal: issue a ground pointer command with three command-source actors active.
- Gameplay domain: RFC-0065 SHOW-5 / SHOW-6 production pointer command workflow.
- Runtime path: `PlayerInputHandler` -> `InputRuntimeSystem` -> `AuthoritativeInputSnapshotSystem` -> `InteractionShowcaseLocalOrderSourceSystem` -> `InputOrderMappingSystem` -> `CommandIntentArbiter` -> `CommandIntentProfileRegistry.RouteGroup` -> `CastDispatchProfileRegistry.SelectDispatchTargets` -> `OrderQueue` -> `OrderBufferSystem`.
- Launcher binding: `interaction_showcase` (`.\scripts\run-mod-launcher.cmd cli launch interaction_showcase --adapter raylib`).
- Primary success condition: Arcweaver, Vanguard, and Commander all receive the same shared moveTo order id at the target point, even when the hover collection contains an entity.
- Failure branch condition: no active scheme intent, no command-source collection, hidden legacy fallback, non-shared order ids, or missing OrderBuffer promotion.

## Timeline
- T+000: verify launcher binding `interaction_showcase` -> `mods/showcases/interaction/InteractionShowcaseMod` and load `interaction_showcase_hub` with CoreInputMod and InteractionShowcaseMod.
- T+004: production startup has active `scheme.default` and resolves `intent.command.default`.
- T+008: publish local `(owner, collection.command.source)` with Arcweaver, Vanguard, and Commander.
- T+012: submit ground pointer command target (2080, 1080) through production input.
- T+016: `dispatch.all_together` fans out 3 moveTo orders with shared order id 1.

## Outcome
- result: success
- headless evidence: production pointer command intake used scheme default intent, command-source collection, cast dispatch fan-out, shared order id assignment, and OrderBuffer promotion.
- visible evidence boundary: this run is headless GasTests evidence; it does not claim a captured raylib/CEF video.

## Runtime Values
| Field | Value |
|---|---|
| local player | Entity = { Id = 8, WorldId = 12, Version = 1 } |
| scheme.default registry id | 1 |
| intent.command.default registry id | 1 |
| dispatch.all_together registry id | 1 |
| command source rows | Entity = { Id = 6, WorldId = 12, Version = 1 }, Entity = { Id = 7, WorldId = 12, Version = 1 }, Entity = { Id = 8, WorldId = 12, Version = 1 } |
| hover entity ignored by ground command | Entity = { Id = 7, WorldId = 12, Version = 1 } |
| shared order id | 1 |
| target world cm | (2080, 1080) |

## Dispatch Variants
| Profile | Registry id | Selected count | Shared order id | Sequential |
|---|---:|---:|---|---|
| dispatch.all_together | 1 | 3 | True | False |
| dispatch.one_by_one | 2 | 1 | False | True |
| dispatch.nearest_top_n | 3 | 3 | True | False |

## Orders
| Actor | Order id | Type id | Player | Target X | Target Z |
|---|---:|---:|---:|---:|---:|
| Entity = { Id = 6, WorldId = 12, Version = 1 } | 1 | 101 | 1 | 2080 | 1080 |
| Entity = { Id = 7, WorldId = 12, Version = 1 } | 1 | 101 | 1 | 2080 | 1080 |
| Entity = { Id = 8, WorldId = 12, Version = 1 } | 1 | 101 | 1 | 2080 | 1080 |
