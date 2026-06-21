# MassNavigation Execution Avoidance and Targets

Parent: [Epic #281](https://github.com/MightyBubble/Ludots/issues/281). This page lands [NAV-6 #288](https://github.com/MightyBubble/Ludots/issues/288), and links back to [NAV-2 #284](https://github.com/MightyBubble/Ludots/issues/284) because execution consumes the unified navigation domain vocabulary.

## Background

Before NAV-6, `MassFlowSimulationState` was already the Core execution engine for mass movement, but it primarily served team-slot movement and direct flow targets. The deprecated `Navigation2D` stack still owned several execution features.

| Capability | Old owner | Problem |
|---|---|---|
| Per-agent arbitrary world target | `Navigation2D` components | MassFlow callers could not address one unit as the execution sink |
| Runtime obstacle stamp | MassFlow internals plus older bridges | No public Core runtime contract for dynamic obstacle snapshots |
| Arrival event | MassFlow settled counters only | Callers could not drain per-agent arrival facts |
| High quality local avoidance | `Navigation2D/Avoidance` | Pure math kernels were trapped under deprecated namespace |

NAV-6 moves the pure avoidance kernels into `Ludots.Core.Navigation.Avoidance` and makes MassFlow the execution owner for per-agent target, runtime obstacle stamp, arrival event, and optional ORCA/Sonar avoidance.

## Goal

`MassNavigationSimulationRuntime` is the public execution facade.

| API | Owner | Contract |
|---|---|---|
| `SetAgentNavigationTargetWorldCm(int, float, float, bool)` | `MassNavigationSimulationRuntime` | Set one agent's world-space target; converts to solver-local cm |
| `SetAgentNavigationTargetWorldCm(Entity, Vector2, bool)` | `MassNavigationSimulationRuntime` | Set a controllable ECS agent's world-space target |
| `TryGetAgentNavigationTargetWorldCm` | `MassNavigationSimulationRuntime` | Read the active target in world-space cm |
| `RebuildRuntimeObstacles(ReadOnlySpan<MassNavigationObstacleSnapshot>)` | `MassNavigationSimulationRuntime` | Replace the runtime obstacle stamp set for the current solver window |
| `DrainArrivalEvents(Span<MassNavigationArrivalEvent>)` | `MassNavigationSimulationRuntime` | Drain per-agent arrivals emitted once per target |

The avoidance kernels live at:

| Kernel | Namespace | Notes |
|---|---|---|
| `OrcaSolver2D` | `Ludots.Core.Navigation.Avoidance` | Stateless math kernel reused by MassFlow |
| `SonarSolver2D` | `Ludots.Core.Navigation.Avoidance` | `UsePreferredVelocityWhenBlocked` replaces the old fallback wording |

## User Story

US-6.1: As an RTS player, I can issue separate target points to a few selected agents, so each unit can move to its own destination and stop.

US-6.2: As a player, I can switch the MassFlow avoidance mode from `Separation` to `Orca` or `Sonar`, so narrow passages use a higher quality local avoidance kernel.

US-6.3: As a gameplay system, I can stamp runtime obstacles into the execution engine, so agents can project blocked targets and steer around dynamic blockers without reviving `Navigation2D`.

## UAT Showcase

Shared showcase target from #288:

`.\\scripts\\run-mod-launcher.cmd cli launch nav_squad --adapter raylib`

| Operation | Visible feedback |
|---|---|
| Select 3 units and issue 3 different right-click targets | Units move toward separate destinations; HUD arrival count increases to 3 |
| Drive a squad into a narrow passage with HUD mode `Separation` | Units visibly compress and jitter more |
| Press `[O]` to switch to `Orca`, then repeat | Units queue and steer more smoothly; HUD shows `Orca` |
| Stamp a runtime obstacle in front of a moving unit | Unit reprojects movement and avoids the obstacle |

The contract tests exercise the production runtime API directly; showcase wiring must use the same facade, not a script-only metric.

## Configuration

`MassNavigationConfig.json` owns the execution avoidance config.

| Field | Values | Constraint |
|---|---|---|
| `avoidance.mode` | `Separation`, `Orca`, `Sonar` | Required, strict case |
| `avoidance.orca.timeHorizonSeconds` | float seconds | Required, > 0 |
| `avoidance.orca.maxNeighbors` | integer | Required, 1..64 |
| `avoidance.sonar.maxSteerAngleDeg` | integer degrees | Required, 1..360 |
| `avoidance.sonar.backwardPenaltyAngleDeg` | integer degrees | Required, 1..360 |
| `avoidance.sonar.predictionTimeScale` | float | Required, >= 0 |
| `avoidance.sonar.ignoreBehindMovingAgents` | bool | Required |
| `avoidance.sonar.blockedStop` | bool | Required |
| `avoidance.sonar.usePreferredVelocityWhenBlocked` | bool | Required; replaces fallback wording |
| `avoidance.sonar.timeHorizonSeconds` | float seconds | Required, > 0 |
| `avoidance.sonar.maxNeighbors` | integer | Required, 1..64 |

Missing `avoidance.mode`, missing `orca`/`sonar` blocks, or wrong case such as `orca` fails during config load. There is no alias and no default injection.

## Configuration To Behavior

| Config change | Runtime behavior | Contract test |
|---|---|---|
| `avoidance.mode: Separation` | Uses existing separation and hard resolve path | `MassNavigationExecutionAvoidanceContractTests` |
| `avoidance.mode: Orca` | Reuses separation hash neighbors and calls `OrcaSolver2D` | `Runtime_ConfiguredHighQualityAvoidanceModesStepWithoutNavigation2DDependency` |
| `avoidance.mode: Sonar` | Reuses separation hash neighbors and calls `SonarSolver2D` | `Runtime_ConfiguredHighQualityAvoidanceModesStepWithoutNavigation2DDependency` |
| Missing/wrong-case mode | Loader fails fast | `MassNavigationConfig_RequiresExplicitStrictCaseAvoidanceMode` |
| Agent reaches target threshold | One `MassNavigationArrivalEvent` is emitted for that target | `Runtime_PerAgentWorldTargetProducesArrivalEvent` |
| Runtime obstacle snapshots change | MassFlow obstacle stamp is rebuilt | `Runtime_RuntimeObstacleStampRebuildsAndBlocksTargetProjection` |

## Merge And Reuse

NAV-6 builds on PR #235's Core-owned `MassCrowd` runtime and the NAV-3 obstacle SSOT. It reuses:

| Reused item | Purpose |
|---|---|
| `MassNavigationSimulationRuntime` | Public execution facade |
| `MassFlowSimulationState` | Single execution engine |
| Separation hash | Neighbor query source for ORCA/Sonar |
| `ManifestationObstacleIntent2D` + `ShapeDataStorage2D` + `CompoundObstacle2DState` | Authored obstacle source upstream |
| `MassNavigationObstacleSnapshot` | Runtime dynamic obstacle stamp input |

`Navigation2DSteeringSystem2D` only keeps a temporary compatibility call to the moved kernels until [NAV-8 #289](https://github.com/MightyBubble/Ludots/issues/289) deletes the old stack.

## DoD

- Execution engine exposes per-agent target, runtime obstacle stamp, arrival event, and optional high quality avoidance.
- ORCA/Sonar live outside `Navigation2D` and do not depend on `Navigation2D.Config`.
- New Sonar config uses `UsePreferredVelocityWhenBlocked`; no new fallback naming is introduced.
- Config is data-driven and strict-case fail-fast.
- Contract tests cover moved kernel namespace, config strictness, per-agent target arrival, runtime obstacle stamp, and ORCA/Sonar step behavior.
- GitBook reference is updated and links back to #281 and #288.
