# Dynamic NavMesh Bake Showcase (Shared)

Player-facing construction lab for both Dynamic 3D NavMesh scenes. Launch
`nav_bake_showcase_raylib` to enter the RTS map and switch maps from the same panel.

## What players do

- Switch between the RTS map and the 64x64-chunk open world.
- Switch bake algorithm (Recast / CDT / Layered Span).
- Choose Building or Terrain, stage the edit, then press Bake.
- Restore the last committed building or terrain edit and Bake again.
- Show or hide baked NavMesh faces, edges, tile bounds, and pending dirty tiles.
- Deploy a selected squad and move it to the goal flag to see the route react.

## Ownership

- Core owns `NavMeshPresentationState`, `NavMeshPresentationSystem`, and
  `NavMeshPresentationBuffer`. Raylib consumes only that Core buffer.
- Shared Mod owns config schema/loader, wall pool, path/corridor orchestration, UI, and public action API (`DynamicNavBakeShowcaseActions`).
- Thin scene Mods own map/data/config only.
- Recast remains host-injected. This Mod never constructs `RecastNavBakeAlgorithm`.

## Public test API

`DynamicNavBakeShowcaseActions.Require(engine)` exposes:

- `TrySwitchMap` / `TrySwitchAlgorithm` / `TrySetEditTool` / `TryConfirmPlacement`
- `TryStageBuilding` / `TryStageTerrainRaise` / `TryBake` / `TryRestore`
- `TrySetNavMeshVisible` / `TryDeploySquad` / `TryDeploySquadNonBlocking` / `TryCommandMoveToGoal`
- Open-world: `TryNextHotspot` / `TryReturn`
- `DrainUntilIdle` / `CaptureEvidence`

## Raylib auto timeline

Set `LUDOTS_DYNAMIC_NAV_BAKE_AUTO_TIMELINE` to an exact algorithm name (`recast`, `cdt`, or `layered-span`) to drive the authored `raylibAutoTimeline` frame contract during Raylib host frames. Empty/unset disables the player. Invalid nonempty values fail fast.
