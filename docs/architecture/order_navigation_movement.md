# Order / Navigation / Movement Architecture

This page records the current movement split after the navigation-domain unification in [Epic #281](https://github.com/MightyBubble/Ludots/issues/281).

## Current Ownership

| Layer | Owner | Responsibility |
|---|---|---|
| Input | `CoreInputMod` and input order systems | Convert player intent into typed orders |
| Order runtime | GAS order buffer and submitter | Authoritative active/queued order state |
| Route planning | `PathServiceRouter`, road/nodegraph/navmesh services | Produce route points or movement plans |
| Move planning | `Ludots.Core.MovePlanning` | Store waypoints, track current index, emit execution intents |
| Execution | MassNavigationFlow / `MassNavigationSimulationRuntime` | Per-agent targets, arrival events, runtime obstacles, avoidance |
| Presentation | performer/overlay systems | Show paths and movement feedback without owning movement truth |

## Data Flow

```text
player command
  -> typed order
  -> optional route or move plan
  -> MovePlanExecutionIntent
  -> MassNavigationFlow per-agent target
  -> WorldPositionCm
  -> VisualTransform
```

## Rules

- `WorldPositionCm` is the gameplay position truth.
- MassNavigationFlow is the only navigation-domain movement execution sink.
- Move-plan cursors live in runtime state such as `MovePlanRuntime.CurrentWaypointIndex`; authored order payloads must not be used as execution cursors.
- Road-specific corridor, capture, preview, and AI policy stays inside the road showcase mod.
- Core move-planning code must not contain road-specific names.

## Current References

- `gitbook/reference/routing-to-mass-execution.md`
- `gitbook/reference/move-planning-mass-navigation-flow-road-execution.md`
- `gitbook/reference/mass-navigation-execution-avoidance-and-targets.md`
- `gitbook/reference/mass-navigation-flow-routing-legacy-execution-removal.md`
