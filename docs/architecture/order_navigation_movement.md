# Order / Navigation / Movement Architecture

## Scope

This document defines the authoritative runtime contract for RTS-style movement in Ludots.

It covers:

- selector and local-order handoff
- order queue versus active order versus nav path
- layered movement responsibilities (`查 / 算 / 选 / 走 / Check抵达 / Timeout`)
- the correct `NavAgent2D` / `NavGoal2D` usage contract

Primary evidence:

- `mods/showcases/road_network/RoadNetworkShowcaseMod/Systems/RoadNetworkLocalOrderSourceSystem.cs`
- `mods/showcases/road_network/RoadNetworkShowcaseMod/Systems/RoadMoveOrderBindingSystem.cs`
- `mods/showcases/road_network/RoadNetworkShowcaseMod/Systems/RoadMovePlanSelectionSystem.cs`
- `mods/showcases/road_network/RoadNetworkShowcaseMod/Systems/RoadMoveExecutionSystem.cs`
- `mods/showcases/road_network/RoadNetworkShowcaseMod/Systems/RoadMoveLifecycleSystem.cs`
- `src/Core/Navigation2D/Components/NavGoal2D.cs`
- `src/Core/Navigation2D/Components/NavDesiredVelocity2D.cs`
- `src/Core/Ludots.Physics2D/Systems/Navigation2DSteeringSystem2D.cs`
- `src/Core/Ludots.Physics2D/Systems/Physics2DToWorldPositionSyncSystem.cs`
- `src/Tests/GasTests/RoadNetworkShowcaseTests.cs`

## Single Source Of Truth

These concepts are distinct and must never be collapsed into one field or one runtime struct:

1. `Selection`
- answers which entities are currently grouped under a selector/set
- source of truth: `SelectionRuntime` plus `SelectionBuffer`

2. `Order queue`
- answers which authored commands are pending or active
- source of truth: `OrderBuffer`

3. `Nav plan`
- answers which sampled movement points are currently valid for the active order
- source of truth: feature-owned plan runtime/store such as `RoadNavPlanStore`

4. `Nav execution`
- answers which immediate point goal the low-level agent is currently trying to reach
- source of truth: `NavGoal2D`

5. `Steering output`
- answers what desired velocity / force the nav layer produced this frame
- source of truth: `NavDesiredVelocity2D` and `ForceInput2D`

If one layer writes another layer's source of truth directly, the design is wrong.

## Terminology Contract

### Authored order waypoint

An authored waypoint is part of the player's command intent.

Examples:

- right-click move target
- shift-queued move target 1 / 2 / 3
- attack-move target

Authored waypoints belong to the order layer, not the nav runtime.

### Nav path sample

A nav path sample is an execution-time point produced by path planning or path slicing.

Examples:

- sampled curved road points
- corridor turning samples
- projected local start point on a curve

Nav path samples belong to the nav-plan layer, not the order queue.

### Immediate nav goal

An immediate nav goal is the current point-goal fed into `NavGoal2D`.

It is always derived from the current nav plan selection, never authored directly by gameplay movement systems once execution starts.

## Layered Runtime Flow

```text
SelectionRuntime / local controller
  -> local order source (查)
  -> planner / route compute (算)
  -> active-order binding + plan selection (选)
  -> execution intent -> NavGoal2D (走)
  -> arrival / timeout / refresh (Check抵达 / Timeout)
  -> Navigation2DSteeringSystem2D
  -> Physics2D simulation
  -> Physics2DToWorldPositionSyncSystem
  -> WorldPositionCm
```

### 1. 查: query and authored-order acquisition

Responsibilities:

- resolve the selector owner and selection set
- resolve the current actor set
- resolve local input intent
- submit authored orders only

Relevant code:

- `src/Core/Input/Selection/SelectionRuntime.cs`
- `mods/showcases/road_network/RoadNetworkShowcaseMod/Systems/RoadNetworkLocalOrderSourceSystem.cs`
- `src/Core/Input/Orders/InputOrderMappingSystem.cs`

Rules:

- this layer may choose actors and authored targets
- this layer must not fabricate nav samples
- this layer must not write `NavGoal2D`, `NavDesiredVelocity2D`, `ForceInput2D`, or `Position2D`

### 2. 算: route planning and order expansion

Responsibilities:

- turn authored targets into a feature-appropriate route
- encode the final target separately from sampled execution points when needed
- preserve order semantics such as `Immediate` versus `Queued`

Relevant code:

- `mods/showcases/road_network/RoadNetworkShowcaseMod/Gameplay/RoadMoveOrderExpander.cs`
- `mods/showcases/road_network/RoadNetworkShowcaseMod/Gameplay/RoadRoutePlanningService.cs`
- `mods/showcases/road_network/RoadNetworkShowcaseMod/Gameplay/RoadRouteComputeService.cs`

Rules:

- planning may replace the order payload with a feature-specific follow order
- planning must not advance runtime waypoint cursors
- planning must preserve authored final destination semantics

### 3. 选: active order binding and nav sample selection

Responsibilities:

- bind the current active order to a plan store/runtime
- repair runtime when the plan is missing or stale
- choose the current execution sample from the plan

Relevant code:

- `mods/showcases/road_network/RoadNetworkShowcaseMod/Systems/RoadMoveOrderBindingSystem.cs`
- `mods/showcases/road_network/RoadNetworkShowcaseMod/Systems/RoadMovePlanSelectionSystem.cs`
- `mods/showcases/road_network/RoadNetworkShowcaseMod/Gameplay/RoadMoveRuntimeService.cs`
- `mods/showcases/road_network/RoadNetworkShowcaseMod/Gameplay/RoadRouteSelectionStrategy.cs`

Rules:

- execution cursor belongs in runtime state such as `RoadNavPlanRuntime.CurrentWaypointIndex`
- authored order payload must not be reused as an execution cursor
- if plan storage is missing or stale, binding must repair consistency before selection treats it as a terminal failure
- timeout refresh must update active-order payload and bound runtime together

### 4. 走: execution sink into nav

Responsibilities:

- translate feature-owned execution intent into core nav contracts
- set `NavGoal2D`
- allow core nav/physics to produce desired velocity and force

Relevant code:

- `mods/showcases/road_network/RoadNetworkShowcaseMod/Systems/RoadMoveExecutionSystem.cs`
- `mods/showcases/road_network/RoadNetworkShowcaseMod/Gameplay/RoadRouteWalkStrategy.cs`
- `src/Core/Navigation2D/Components/NavGoal2D.cs`

Rules:

- feature systems may write `NavGoal2D`
- feature systems must not write `NavDesiredVelocity2D` directly
- feature systems must not write `ForceInput2D` directly for nav-follow movement
- feature systems must not integrate `Position2D` or `WorldPositionCm` directly

### 5. Check抵达 and Timeout

Responsibilities:

- evaluate final arrival against authored final target
- detect stall / no-progress
- decide refresh, abandon, or completion

Relevant code:

- `mods/showcases/road_network/RoadNetworkShowcaseMod/Systems/RoadMoveLifecycleSystem.cs`
- `mods/showcases/road_network/RoadNetworkShowcaseMod/Gameplay/RoadRouteArrivalPolicy.cs`
- `mods/showcases/road_network/RoadNetworkShowcaseMod/Gameplay/RoadRouteTimeoutPolicy.cs`
- `mods/showcases/road_network/RoadNetworkShowcaseMod/Gameplay/RoadRouteRefreshService.cs`

Rules:

- arrival must be checked against the authored final target, not only the current sample
- timeout belongs to lifecycle policy, not to steering output
- successful refresh keeps the current active order slot but replaces its payload and bound plan consistently
- failed refresh completes or abandons through the order layer, not by leaving orphaned runtime state behind

## Correct `NavAgent2D` Usage

### What gameplay systems may assume

If an entity is a nav-driven mover, gameplay systems may assume:

- `NavAgent2D` marks participation in Navigation2D
- `NavGoal2D` is the only low-level movement input they need to set for point-goal movement
- `NavDesiredVelocity2D` is an output produced by the nav layer

### What gameplay systems must not do

Gameplay and mod systems must not:

- write `NavDesiredVelocity2D` as if it were an input
- write `ForceInput2D` for normal nav-follow movement
- mutate `Position2D` / `WorldPositionCm` every frame to "help" nav catch up
- keep feature-local sleeping or wakeup hacks in showcase code

### Sleep / wake contract

If a nav-driven entity has a point goal, the core nav/physics infrastructure must wake it before physics integration.

Evidence:

- `src/Core/Ludots.Physics2D/Systems/Navigation2DSteeringSystem2D.cs`
- `src/Tests/GasTests/RoadNetworkShowcaseTests.cs`

This means:

- wakeup is a core nav/physics concern
- showcase mods may rely on the contract
- showcase mods must not own a parallel wakeup path

## Correct Order Semantics

### Immediate order

An immediate order replaces the current active order according to order rules.

The movement stack must react by:

- rebinding active order runtime
- discarding stale execution intent
- selecting from the new plan immediately

### Queued order

A queued order is a future authored command, not a continuation sample of the current nav path.

This means:

- `Shift + right-click 1 / 2 / 3` produces three authored orders
- each order may later generate its own nav plan and its own runtime samples
- failure of segment 2 is handled at the order layer, not by mutating segment 1 nav samples

## Reference Acceptance Evidence

- `src/Tests/GasTests/RoadNetworkShowcaseTests.cs`
- `artifacts/acceptance/road_network_showcase_timeout/battle-report.md`
- `artifacts/acceptance/road_network_showcase_timeout/trace.jsonl`
- `artifacts/acceptance/road_network_showcase_timeout/path.mmd`
