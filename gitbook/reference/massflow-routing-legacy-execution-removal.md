# MassFlow Routing Legacy Execution Removal

Parent: [Epic #281](https://github.com/MightyBubble/Ludots/issues/281). This page lands [NAV-8 #289](https://github.com/MightyBubble/Ludots/issues/289) after [NAV-9 #303](https://github.com/MightyBubble/Ludots/issues/303) moved road move plans onto the MassFlow sink.

## Background

Before NAV-8, the repository still carried an older point-target execution domain beside MassFlow. It had its own runtime folders, playground mod, config files, scripts, startup wiring, tests, and a physics-side steering pair. Several consumers had already been migrated by NAV-6, NAV-7, and NAV-9, so the remaining work was deletion and contract hardening.

Two policy gaps were also still visible:

| Area | Problem |
|---|---|
| Bake/editor config | Some paths could synthesize default layer data or widen a partial bake request silently |
| Component registration | Re-registering the same component shape from multiple mods threw even when the definition was identical |

## Goal

NAV-8 leaves MassFlow as the only movement execution sink for navigation-domain orders.

In scope:

- Remove the retired runtime folders, playground mod, config assets, scripts, app entries, and tests.
- Remove physics startup and config keys for the retired domain.
- Keep ORCA and Sonar kernels under `Ludots.Core.Navigation.Avoidance`.
- Make `ComponentRegistry` idempotent for identical component definitions and identical setters across mods.
- Keep conflicts fail-fast when a later registration has the same id but a different shape.
- Add an architecture contract that scans the whole repository for removed-domain tokens and banned bake/editor policy markers.

Out of scope:

- No new runtime ability.
- No NAV-10 runtime tile rebuild work.
- No compatibility aliases for removed config keys, launcher selectors, or old playground assets.

## User Story

As a developer, I can delete the retired execution domain after all consumers use MassFlow, so the codebase has one movement sink and no shadow runtime.

Given NAV-6, NAV-7, and NAV-9 are complete; when the retired files are removed; then road, champion, and MassNavigation tests still exercise production movement through MassFlow.

As a reviewer, I can run a single architecture test, so removed-domain references and banned bake/editor policy paths are caught with file and line output.

Given a forbidden token is reintroduced; when `ArchitectureTests` runs; then the scan fails and prints the offending path.

## UAT Showcase

NAV-8 is regression-oriented and reuses the existing production tests and showcase entry points.

| Command / operation | Feedback |
|---|---|
| `dotnet test src/Tests/GasTests/GasTests.csproj --filter "RuntimeManifestationBridgeTests|Physics2DIntegrationTests|DisplacementPresetTests|StaticObstaclePhysicsShowcaseAcceptanceTests|CapabilityStandardPhysics2DShowcaseAcceptanceTests|RoadNetworkShowcaseTests|ChampionSkillSandbox" /m:1 /nr:false --no-restore` | Gameplay and showcase regressions stay green |
| `dotnet test src/Tests/PresentationTests/PresentationTests.csproj --filter "MassNavigationExecutionAvoidanceContractTests|MassNavigationPerformerContractTests" /m:1 /nr:false --no-restore` | MassFlow execution and performer contracts stay green |
| `dotnet test src/Tests/ArchitectureTests/ArchitectureTests.csproj /m:1 /nr:false --no-restore` | Repository scan reports no removed-domain or banned bake/editor policy hits |
| Reintroduce one removed-domain token in any tracked file and rerun architecture tests | The scan fails with file and line |

## Configuration

There is no replacement config file for the removed runtime. Current navigation-domain movement is configured through:

| Area | Current owner |
|---|---|
| Agent geometry/profile | `gitbook/reference/agent-profile.md` |
| Spatial scale | `gitbook/architecture/spatial-scale-and-resolution-ssot.md` and `gitbook/reference/spatial-scale-configuration.md` |
| Obstacles | `gitbook/reference/obstacle-authoring.md` |
| Bake context | `gitbook/reference/nav-bake-context.md` |
| Execution targets and avoidance | `gitbook/reference/mass-navigation-execution-avoidance-and-targets.md` |
| Route-to-execution | `gitbook/reference/routing-to-mass-execution.md` |
| Move-plan road execution | `gitbook/reference/move-planning-massflow-road-execution.md` |

Removed config keys and selectors are not aliases. Loading them must fail through the normal strict config and launcher paths.

## Configuration To Behavior

| Change | Behavior | Contract |
|---|---|---|
| Missing bake layer/profile data | Bake/editor path rejects the request instead of synthesizing defaults | Architecture scan plus bake service tests |
| Partial bake target list is empty | Editor path rejects the request instead of widening silently | Architecture scan plus bridge tests |
| Same component definition registered by multiple mods | Registration is a no-op and returns the existing definition | `RuntimeManifestationBridgeTests` |
| Same component id registered with a different shape | Registration throws with the conflicting mod ids | `RuntimeManifestationBridgeTests` |
| Removed-domain token appears in any repository file | Architecture test fails with path and line | `NavigationLegacyDomainRemovalContractTests` |

## Merge And Reuse

NAV-8 does not merge external branches. It reuses:

- PR #235 Core-owned MassFlow runtime.
- NAV-6 per-agent targets, runtime obstacle stamps, arrival events, and moved ORCA/Sonar kernels.
- NAV-7 routing-to-MassFlow sink.
- NAV-9 Core `MovePlanning` seam and `MassMovePlanExecutionSink`.
- Mainline obstacle authoring types: `ManifestationObstacleIntent2D`, `ShapeDataStorage2D`, and `CompoundObstacle2DState`.

PR #123 was used only as historical guidance for deletion and contract shape; it was not merged.

## DoD

- Retired execution runtime, playground, configs, scripts, app entries, and tests are removed.
- ORCA/Sonar kernels remain available from `Ludots.Core.Navigation.Avoidance`.
- Bake/editor paths fail fast for missing config instead of synthesizing defaults or widening silently.
- `ComponentRegistry` is idempotent for identical definitions and strict for conflicts.
- Architecture scan covers the whole repository and reports file/line hits.
- GitBook links back to #281 and #289.
