# MassNavigation Foundation

`MassNavigationMod` is the reusable large-scale navigation foundation. It is not the Total War scenario itself. It provides the formal navigation chain that a game mod can build on: config loading, MassFlow simulation, agent profiles, formal move order ingestion, ECS writeback, selection metadata sync, performer handoff, minimap handoff, diagnostics, and tests.

Start here if you are new:

- Beginner Total War-like RTS guide: `gitbook/reference/mass-navigation-user-book.md`
- Formal chain and boundary audit: `gitbook/reference/mass-navigation-formal-chain.md`
- Reference game mod: `mods/showcases/mass_navigation_total_war_entry/MassNavigationTotalWarEntryMod/`

## Responsibility Split

| Layer | Responsibility |
| --- | --- |
| `MassFlow` | Hot-path SoA solver data, flow/crowd/avoidance calculations, cadence-sensitive simulation. |
| `MassNavigation` | Gameplay-facing navigation integration: agents, profiles, groups, orders, ECS state, authoring contracts. |
| Game/showcase mod | Product rules such as formations owning soldiers, army names, formation outlines, obstacle overlay style, initial scenario setup. |

Do not add alternate names for old experiment labels. `MassNavigation*` is the formal runtime surface for this foundation.

## Foundation Config

Foundation-owned files stay under `assets/` and are loaded through the normal config pipeline.

| File | Responsibility |
| --- | --- |
| `assets/MassNavigationConfig.json` | Solver window, cadence, flow, arrival, avoidance, crowd semantics, scenario teams, obstacles, camera profiles, and view residency. |
| `assets/GAS/order_types.json` | `massNavigationMove` order registration and rule authoring. |
| `assets/Entities/templates.json` | Foundation example templates and required component contract examples. |
| `assets/Presentation/performers.json` | Foundation example performer authoring. |
| `assets/Maps/mass_navigation.json` | Foundation example map, board bounds, and visual terrain binding. |

Game-specific mods can provide their own files with the same kinds of data. They should not create a private loader.

## Order Authoring

Order config is authored with semantic keys:

```json
{
  "orderBlackboardKeys": {
    "MassNavigation.FormationMode": true
  },
  "orderTypes": {
    "massNavigationMove": {
      "intArg0BlackboardKey": "MassNavigation.FormationMode"
    }
  }
}
```

Do not write numeric order blackboard ids in JSON. The loader resolves strings once at startup; hot-path buffers use compiled runtime ids.

## Runtime Map

| File | Boundary |
| --- | --- |
| `Runtime/MassFlowSimulationState.cs` | SoA grid/cache, flow rebuild, obstacle cache, pair avoidance, and solver hot path. |
| `Runtime/MassNavigationSimulationRuntime.cs` | Runtime facade for agents, groups, order ingestion, sync cadence, diagnostics, and ECS writeback. |
| `Runtime/MassFlowTuning.cs` | Flow-field scheduler and rebuild tuning. |
| `Runtime/MassFlowArrivalTuning.cs` | Arrival behavior tuning. |
| `Runtime/MassFlowAvoidanceTuning.cs` | Pair/crowd avoidance tuning. |
| `Runtime/MassNavigationCrowdSemantics.cs` | Team relationship to navigation policy mapping. |
| `Runtime/MassNavigationGroupRuntime.cs` | Formal order group state and target ownership. |
| `Runtime/MassNavigationAgentState.cs` | ECS entity to solver-agent binding and selection/order metadata. |
| `Runtime/MassNavigationAgentProfileSetConfig.cs` | Movement/mass profile schema and concrete profile authoring. |
| `Runtime/MassNavigationAuthoringContract.cs` | Required-component validation for authored navigation entities. |
| `Systems/MassNavigationOrderIngestionSystem.cs` | Consumes formal `OrderBuffer` move orders into MassNavigation groups. |
| `Systems/MassNavigationLocalCommandInputSystem.cs` | Reads local player command input and submits formal move orders. |
| `Systems/MassNavigationSpawnReceiptBindingSystem.cs` | Binds template spawn receipts to MassNavigation agents. |
| `Systems/MassNavigationAgentMetadataSyncSystem.cs` | Syncs ECS/team/profile metadata into runtime state. |
| `Systems/MassNavigationFormationSystem.cs` | Updates foundation formation/group target arrangement. |
| `Systems/MassNavigationSelectionSyncSystem.cs` | Projects `SelectionRuntime` membership into agent selected flags. |
| `Systems/MassNavigationPanelPresentationSystem.cs` | Foundation tuning panel presentation. |
| `Systems/MassNavigationHudPresentationSystem.cs` | Diagnostics HUD and UAT evidence output. |
| `Systems/MassNavigationScenarioBootstrap.cs` | Foundation example scenario spawn. |

## Rules For Follow-Up Work

- No fallback paths for missing services, config, templates, performers, map bounds, or visual heightmap.
- No parallel config loader, registry, spawn path, selection runtime, order runtime, performer runtime, or minimap runtime.
- No Total War formation ownership inside the MassNavigation foundation.
- No private MassNavigation marker lifecycle system when performer rules can express the lifecycle.
- Move reusable behavior into `MassFlow`, `MassNavigation`, Core, or another formal reusable mod when two or more mods need it.
