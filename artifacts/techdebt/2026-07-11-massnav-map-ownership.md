# Tech Debt Report: massnav-map-ownership

Date: 2026-07-11
Reporter: Codex
Owner: Core Map / MassNavigation maintainers
Severity: P1
Scope: Cross-layer

## Trigger

- Scenario: load MassNavigation on a square-grid map, then switch to another MassNavigation profile in the same process.
- Entry point: `MassNavigationRuntime.HandleMapFocused`.
- Repro steps: observe `GridBoard.LoadedChunks == null`; MassNavigation creates its own set and replaces `CoreServiceKeys.LoadedChunks`; load a second profile and observe systems retain the first simulation reference. Install the Road showcase once, unload/reload its map, and observe its long-lived move-plan systems retain the destroyed first simulation.

## Evidence

- `src/Core/Map/Board/GridBoard.cs`
- `src/Core/Engine/GameEngine.cs`
- `src/Core/MassNavigation/Runtime/MassNavigationRuntime.cs`
- `src/Core/MassNavigation/Runtime/MassNavigationMovePlanExecutionSink.cs`
- `mods/showcases/road_network/RoadNetworkShowcaseMod/Systems/RoadMoveExecutionSystem.cs`
- `mods/showcases/road_network/RoadNetworkShowcaseMod/Systems/RoadMoveLifecycleSystem.cs`
- `mods/showcases/road_network/RoadNetworkShowcaseMod/Systems/RoadMoveOrderBindingSystem.cs`
- `mods/showcases/road_network/RoadNetworkShowcaseMod/Systems/RoadMovePlanSelectionSystem.cs`
- `src/Tests/PresentationTests/MassNavigationStreamingOwnershipTests.cs`
- `src/Tests/PresentationTests/FormationCapabilityShowcaseContractTests.cs`
- `src/Tests/GasTests/RoadNetworkShowcaseTests.cs`

## Impact

- User-visible impact: AOI/spatial consumers can observe a feature-owned loaded-chunk set instead of the active map's set; sequential MassNavigation maps can run systems against stale configuration.
- Correctness/stability risk: global service ownership and system/service identity diverge across map lifecycle transitions; suspended Road entities can keep executing and a reloaded Road map can write through a stale simulation.
- Blast radius: map, spatial query, navigation, route execution, presentation, and Mod lifecycle boundaries.

## Fuse Decision

- Mode: hard-stop
- Reason: a board with an incompatible loaded-chunk implementation or mismatched chunk size must not silently start MassNavigation.
- Observability fields: map id, profile id, configured chunk size, board chunk size, binding/service identity.

## Containment and Follow-up

- Immediate containment: GridBoard owns its loaded chunks; MassNavigation requires and uses that instance; map metadata selects one ArrayById profile; unload clears the stable runtime binding. All long-lived Formation and Road consumers resolve the active simulation through that binding, gate themselves to their owning map, and exclude `SuspendedTag` entities.
- Permanent fix direction: completed in issue #642 branch; retain map-owned service and stable binding as architecture invariants.
- Target milestone: issue #642 merge.
