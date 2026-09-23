# Tech Debt Report: massnav-runtime-boundary

Date: 2026-09-23
Reporter: Codex
Owner: Core / MassNavigation maintainers
Severity: P1
Scope: Cross-layer

## Trigger

- Scenario: MassNavigation 10k/showcase correctness and performance audit found Core/MassNavigation owning scenario spawn, presentation, route execution, group order state, entity lifecycle and solver state in one runtime stack.
- Entry point: `src/Core/MassNavigation/Runtime/MassNavigationRuntime.cs` installs runtime, binding, MovePlan, presentation and scenario paths from one Core module.
- Repro steps:
  1. Inspect `src/Core/MassNavigation/Runtime/MassNavigationRuntime.cs` system registration.
  2. Inspect `src/Core/MassNavigation/Systems/MassNavigationScenarioBootstrap.cs` scenario auto-spawn path.
  3. Inspect `src/Core/MassNavigation/Runtime/MassNavigationAgentState.cs` Id-only runtime entity indexes and lifecycle cleanup.
  4. Inspect `src/Core/MassNavigation/Runtime/MassNavigationRouteExecutionSink.cs` direct pathing ownership.

## Evidence

- `src/Core/MassNavigation/Runtime/MassNavigationRuntime.cs`
- `src/Core/MassNavigation/Runtime/MassNavigationConfig.cs`
- `src/Core/MassNavigation/Runtime/MassNavigationAuthoringContract.cs`
- `src/Core/MassNavigation/Systems/MassNavigationScenarioBootstrap.cs`
- `src/Core/MassNavigation/Runtime/MassNavigationAgentState.cs`
- `src/Core/MassNavigation/Runtime/MassNavigationRouteExecutionSink.cs`
- `src/Core/MassNavigation/Runtime/MassNavigationGroupRuntime.cs`
- `src/Core/MassNavigation/Runtime/MassNavigationSimulationRuntime.cs`
- Epic: #1644
- Split issues: #1645, #1646, #1647, #1648, #1649, #1650, #1651, #1652

## Impact

- User-visible impact: MassNavigation showcase and gameplay paths can drift when spawn, presentation, route cache, group order completion or ECS binding get out of sync.
- Correctness/stability risk: Arch ECS is no longer the only entity lifecycle and identity truth for MassNavigation participants; route and order state can survive outside the canonical MovePlan/GAS/navigation seams.
- Blast radius: Core, Navigation, MovePlanning, ScenarioPlan, Presentation, Physics2D bridge and showcase mods.

## Fuse Decision

- Mode: hard-stop for new boundary debt through an architecture ratchet.
- Reason: this PR does not move runtime ownership yet, but it makes any new Core/MassNavigation dependency on presentation, scenario bootstrap, pathing ownership or Id-only entity indexing visible in CI.
- Observability fields:
  - debt id: `massnav-runtime-boundary`
  - impacted feature: `MassNavigation`
  - branch reason code: `massnav.boundary.ratchet`
  - test: `MassNavigationRuntimeBoundaryDebtRatchetTests.MassNavigation_CoreBoundaryDebt_DoesNotGrowBeyondTrackedInventory`

## Containment and Follow-up

- Immediate containment: keep current debt inventory fixed; new entries fail the architecture ratchet.
- Permanent fix direction: follow #1644 and remove tracked entries as each seam is split.
- Target milestone: close P0 split issues #1645, #1646 and #1647 before starting broad SimulationRuntime surgery.
