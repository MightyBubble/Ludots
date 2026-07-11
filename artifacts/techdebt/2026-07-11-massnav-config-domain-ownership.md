# Tech Debt Report: MASSNAV-CONFIG-DOMAIN-OWNERSHIP

Date: 2026-07-11
Reporter: Codex issue #642 delivery
Owner: MassNavigation capability and map-authoring maintainers
Severity: P1
Scope: Cross-layer

## Trigger

- Scenario: Load MassNavigation and externally authored Formation/RoadNetwork profiles in one process.
- Entry point: `MassNavigationConfigLoader` -> `MassNavigationSimulationRuntime` -> metadata/control systems.
- Repro: Mutate or inherit the former flat config where map identity, solver execution, presentation, generated scenario, and global team relationships shared one object.

## Evidence

- `src/Core/MassNavigation/Runtime/MassNavigationCapabilityProfile.cs`
- `src/Core/MassNavigation/Runtime/MassNavigationRuntimePlan.cs`
- `src/Core/MassNavigation/Systems/MassNavigationAgentMetadataSyncSystem.cs`
- `mods/capabilities/navigation/MassNavigationMod/MassNavigationSceneOwner.cs`
- `artifacts/acceptance/mass-navigation-config-ssot/trace.jsonl`

## Impact

- User-visible impact: A showcase profile could inherit unrelated scene generation or presentation data, and scene bootstrap could replace process-global team relationships.
- Correctness/stability risk: Mutable authoring config remained observable after runtime construction; runtime team membership was constrained by showcase scenario teams instead of actual entities.
- Blast radius: Core solver, Mod lifecycle, map binding, presentation authoring, and relationship state.

## Fuse Decision

- Mode: hard-stop
- Reason: Missing/unknown fields, inherited scene data on disabled scene owners, and capacity violations now reject activation explicitly.
- Observability fields: profile id, map id event state, strict JSON property path, `runtime.capacity.*` validation path.

## Containment and Follow-up

- Immediate containment: Delivered. Flat profiles and mutable runtime config aliases were removed without compatibility readers.
- Permanent fix direction: Delivered through `runtime`/`sceneAuthoring` domains, immutable runtime plans, entity-derived runtime teams, and Mod-owned scene control.
- Target milestone: issue #642 completion.
