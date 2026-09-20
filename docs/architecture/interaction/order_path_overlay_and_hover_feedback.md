# Order Path Overlay and Hover Feedback

> SSOT scope: command actor move path preview, shared order world-space resolution, and sandbox hover feedback reuse.
> This document describes the shipped runtime implemented by the current slice. It does not redefine generic camera follow, ability routing, or navigation execution beyond the parts directly used by move path and hover feedback.

## 1. Runtime Intent

The current interaction stack needs two kinds of immediate visual feedback without inventing parallel pipelines:

- command actor right-click move orders should expose a visible path preview before and during movement
- hovered entities in `ChampionSkillSandboxMod` should expose a stable marker even outside active aiming mode

The shipped implementation keeps both behaviors on top of existing infrastructure:

- order/runtime reuse stays inside `src/Core/Gameplay/GAS/Orders/`
- presentation reuse stays inside `PresentationEventStream -> PresenterRuleSystem -> PresenterCommand`
- sandbox-specific indicator policy stays inside `mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/Runtime/ChampionSkillSandboxRuntime.cs`

## 2. Reuse-First Design

The slice explicitly reuses these existing systems and boundaries:

- `src/Core/Gameplay/GAS/Systems/MoveToWorldCmOrderSystem.cs`
  - remains the authoritative `moveTo` order consumer
- `src/Core/Gameplay/GAS/Orders/CompositeOrderPlanner.cs`
  - continues to plan cast-followed-by-move sequences, now via shared spatial resolution
- `src/Core/Input/Orders/AbilityAimPresentationRuntime.cs`
  - remains the reference path for ability aim events feeding presenter rules instead of direct visual ownership in ability config
- `mods/CoreInputMod/Triggers/InstallCoreInputOnGameStartTrigger.cs`
  - remains the single install point for generic input presentation systems
- `src/Core/Navigation/Pathing/IPathService.cs` runtime service contract when a map exposes pathing
  - command actor move preview prefers the existing path solve result instead of hand-building a second route planner

No parallel renderer, no mod-local move runtime, and no duplicate spatial parsing path were introduced.

## 3. Shared Spatial Resolution

`src/Core/Gameplay/GAS/Orders/OrderWorldSpatialResolver.cs` centralizes the common world-space conversions that were previously duplicated across planner/runtime call sites:

- `TryResolveSpatialTarget`
- `TryResolveMoveDestination`
- `TryGetEntityWorldCm`
- `TryResolveProjectedQueuedOrigin`

Current consumers:

- `src/Core/Gameplay/GAS/Orders/CompositeOrderPlanner.cs`
- `src/Core/Gameplay/GAS/Systems/MoveToWorldCmOrderSystem.cs`

This keeps queued-order projection, cast anchor planning, and move destination extraction aligned on one interpretation of `OrderSpatial`.

## 4. Command Actor Move Path Preview

`mods/CoreInputMod/Systems/CommandActorMovePathPresentationSystem.cs` is the generic event projection responsible for previewing move plans for the current entity collection provided by the caller.

Its runtime behavior is:

1. Read the current entity collection from `EntityCollectionContextRuntime`.
2. Resolve the currently active or queued move destination through `OrderWorldSpatialResolver`.
3. Publish `MovePathBegun` / `MovePathUpdated` / `MovePathEnded` events into `PresentationEventStream`.
4. Let presenter rules in `mods/CoreInputMod/assets/Presentation/presenters.json` create, update, and destroy the ground overlay presenters.

The system is registered in `mods/CoreInputMod/Triggers/InstallCoreInputOnGameStartTrigger.cs` immediately before `PresenterRuleSystem`.

### 4.1 Fallback Boundary

Some maps, including the current champion sandbox, boot without a board/path graph. In that case command actor move path projection uses the authored order-space waypoints or final destination instead of inventing a private renderer.

This keeps the UX visible on intentionally lightweight maps while preserving a single presentation route: projected events first, presenter rules second.

Relevant code and evidence:

- `mods/CoreInputMod/Systems/CommandActorMovePathPresentationSystem.cs`
- `mods/CoreInputMod/assets/Presentation/presenters.json`
- `src/Tests/GasTests/CommandActorMovePathPresentationSystemTests.cs`
- `src/Tests/GasTests/OrderNavigationMoveRuntimeTests.cs`

## 5. Hover Marker Policy

`mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/Runtime/ChampionSkillSandboxRuntime.cs` now resolves hover indicator targets with two rules:

- any live hovered entity may receive the hover indicator, even when the input mapping is not currently aiming
- the current command-source primary is suppressed, so command-source feedback and hover ring do not stack on the same actor

This keeps the sandbox readable in normal selection/movement flow and avoids marker duplication noise.

The hover marker still reuses the existing presenter/overlay stack:

- presenter definitions live in `mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/assets/Presentation/presenters.json`
- runtime creation/destruction stays inside the existing presenter command flow

## 6. Acceptance Evidence

Code evidence:

- `src/Core/Gameplay/GAS/Orders/OrderWorldSpatialResolver.cs`
- `mods/CoreInputMod/Systems/CommandActorMovePathPresentationSystem.cs`
- `mods/CoreInputMod/assets/Presentation/presenters.json`
- `mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/Runtime/ChampionSkillSandboxRuntime.cs`

Test evidence:

- `src/Tests/GasTests/CommandActorMovePathPresentationSystemTests.cs`
- `src/Tests/GasTests/Production/InputOrderConvergenceValidationTests.cs`
- `src/Tests/GasTests/Production/OrderCompositePlannerTests.cs`
- `src/Tests/GasTests/OrderNavigationMoveRuntimeTests.cs`
- `src/Tests/GasTests/Production/ChampionSkillSandboxConfigTests.cs`
- `src/Tests/GasTests/Production/ChampionSkillSandboxPlayableAcceptanceTests.cs`

Acceptance artifacts:

- `artifacts/acceptance/champion-skill-sandbox/battle-report.md`
- `artifacts/acceptance/champion-skill-sandbox/trace.jsonl`
- `artifacts/acceptance/champion-skill-sandbox/path.mmd`
