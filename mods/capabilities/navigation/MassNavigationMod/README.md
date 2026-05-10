# MassNavigation Foundation

`MassNavigationMod` is the official large-scale navigation foundation mod. It owns the Ludots formal chains end to end: config pipeline, template spawn, selection, order bridge, ECS writeback, performer presentation, minimap, diagnostics, and UAT evidence.

For a zero-context user guide, read `gitbook/reference/mass-navigation-user-book.md`. For the formal acceptance chain and UAT contract, read `gitbook/reference/mass-navigation-formal-chain.md`.

The reusable capability is named by responsibility:

- `MassFlow`: the high-performance SoA flow-field and crowd solver capability.
- `MassNavigation`: the reusable navigation-facing integration layer that binds commands, groups, authored agents, and ECS state to a mass solver.

`MassNavigation*` names are the formal runtime surface for this foundation. Do not add alternate names for old experiment labels.

## Configuration Boundary

Foundation-owned files stay under `assets/` and use single-source identifiers. Runtime loads them through the normal config pipeline; missing or inconsistent data must fail fast.

| File | Owner | Responsibility |
| --- | --- | --- |
| `assets/MassNavigationConfig.json` | MassNavigation | Solver window, cadence, flow, arrival, avoidance, crowd semantics, scenario teams, obstacles, hotspots, and presentation references. |
| `assets/GAS/order_types.json` | MassNavigation | `massNavigationMove` order registration and rule authoring. |
| `assets/Entities/templates.json` | MassNavigation | Agent, blocker, hotspot, and local-player templates that demonstrate the required component contract. |
| `assets/Presentation/performers.json` | MassNavigation presentation authoring | Agent visuals, health HUD, selection markers, minimap markers, and world props. |
| `assets/Maps/mass_navigation.json` | MassNavigation | Board/world bounds, authored entities, and visual terrain binding. |

## Runtime Map

These files define the current foundation split. Solver hot-path data stays in `MassFlow`; ECS, orders, selection, authoring, and presentation handoff stay in `MassNavigation`.

| File | Boundary |
| --- | --- |
| `Runtime/MassFlowSimulationState.cs` | SoA grid/cache, flow rebuild, obstacle cache, pair avoidance, and solver hot path. No presentation or UI dependencies. |
| `Runtime/MassNavigationSimulationRuntime.cs` | Runtime facade for agents, groups, command application, sync cadence, diagnostics, and ECS writeback. |
| `Runtime/MassFlowTuning.cs` | Flow-field scheduler and rebuild tuning. |
| `Runtime/MassFlowArrivalTuning.cs` | Arrival behavior tuning. |
| `Runtime/MassFlowAvoidanceTuning.cs` | Pair/crowd avoidance tuning. |
| `Runtime/MassNavigationCrowdSemantics.cs` | Team relationship to navigation policy mapping; gameplay relationship source remains core team infrastructure. |
| `Runtime/MassNavigationGroupRuntime.cs` | Group command state and formation target ownership. |
| `Runtime/MassNavigationCommandRuntime.cs` | Command intent, lifecycle, and dirty state. |
| `Runtime/MassNavigationAgentState.cs` | ECS entity to solver-agent binding and selection/order metadata. |
| `Runtime/MassNavigationAgentProfileConfig.cs` | Movement/mass profile schema and concrete profile authoring. |
| `Runtime/MassNavigationAuthoringContract.cs` | Required-component validation for authored navigation entities. |
| `Systems/MassNavigationCommandApplySystem.cs` | Applies commands to runtime groups and solver state. |
| `Systems/MassNavigationOrderBridgeSystem.cs` | Bridges formal `OrderBuffer` to MassNavigation command intent. |
| `Systems/MassNavigationCommandBridgeSystem.cs` | Bridges local player input into the formal command path. |
| `Systems/MassNavigationSpawnReceiptBindingSystem.cs` | Binds template spawn receipts to MassNavigation agents. |
| `Systems/MassNavigationAgentMetadataSyncSystem.cs` | Syncs ECS/team/profile metadata into runtime state. |
| `Systems/MassNavigationFormationSystem.cs` | Formation target allocation and group arrangement. |
| `Systems/MassNavigationSelectionSyncSystem.cs` | Bridges `SelectionRuntime` to agent selected flags through formal selection keys. |
| `assets/Presentation/performers.json` selection rules | Selection marker lifecycle is driven by generic selection presentation events and performer rules. |
| `Systems/MassNavigationPanelPresentationSystem.cs` | Tuning panel and runtime controls for the mod. |
| `Systems/MassNavigationHudPresentationSystem.cs` | Diagnostics HUD and UAT evidence output. |
| `Systems/MassNavigationScenarioBootstrap.cs` | Scenario spawn authoring. |
| `UI/MassNavigationPanelController.cs` | Runtime tuning panel UI. |

## Rules For Follow-Up Work

- Do not move camera, minimap, or order marker behavior as part of naming cleanup.
- Do not add fallback paths for missing services, config, templates, performers, or map bounds.
- Do not create a parallel config loader, registry, spawn path, selection runtime, order runtime, performer runtime, or minimap runtime.
- When a file becomes reusable for two or more mods, move that capability to `MassFlow`, `MassNavigation`, core, or another formal reusable infrastructure in the same change that updates callers.
