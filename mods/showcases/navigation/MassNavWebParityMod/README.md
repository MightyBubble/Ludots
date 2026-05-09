# MassNavWebParityMod Boundary

`MassNavWebParityMod` is the acceptance showcase shell for the large-scale navigation work. It exists to prove the Ludots formal chains end to end: config pipeline, template spawn, selection, order bridge, ECS writeback, performer presentation, minimap, diagnostics, and UAT evidence.

The reusable capability that should survive this showcase is named by responsibility, not by this mod:

- `MassFlow`: the high-performance SoA flow-field and crowd solver capability.
- `MassNavigation`: the reusable navigation-facing integration layer that binds commands, groups, authored agents, and ECS state to a mass solver.
- `MassNavWebParity`: the showcase acceptance shell, scenario authoring, visual proof, and parity/UAT harness.

Do not rename runtime IDs, config files, components, or systems in this step. Existing `MassNav*` names stay in place until the core extraction lands in a dedicated change. This README is the boundary marker for that extraction.

## Configuration Boundary

Current showcase-owned files stay under `assets/` and remain named for `MassNavWebParity` until the generic runtime exists.

| Current file | Current owner | Future owner | Extraction note |
| --- | --- | --- | --- |
| `assets/MassNavWebParityConfig.json` | Showcase shell | Split between `MassFlow` tuning and showcase scenario config | Solver window, cadence, flow, arrival, avoidance, and crowd semantics are reusable; scenario, teams, obstacles, hotspots, and presentation ids are showcase authoring. |
| `assets/GAS/order_types.json` | Showcase shell | `MassNavigation` contract plus showcase binding | `massNavMove` proves the formal order chain. A generic order contract should not depend on the WebParity scenario. |
| `assets/Entities/templates.json` | Showcase shell | Showcase shell | Templates are acceptance authoring. Core extraction may define required component contracts, but not own these concrete entities. |
| `assets/Presentation/performers.json` | Showcase shell | Showcase shell | Performer choices, styles, markers, and visual proof stay mod-local. Generic navigation must only require a presentation handoff contract. |
| `assets/Maps/mass_nav_web_parity.json` | Showcase shell | Showcase shell | Board/world bounds remain map-owned. Generic runtime reads bounds through existing core services. |

## Runtime Extraction Map

These files are the first-pass map for moving behavior out of the showcase later. This step does not move them.

| Current file | Future name or home | Boundary |
| --- | --- | --- |
| `Runtime/MassNavWebParitySimState.cs` | `MassFlow` solver state | SoA grid/cache, flow rebuild, obstacle cache, pair avoidance, and solver hot path. Must stay independent from showcase presentation and UI. |
| `Runtime/MassNavSimulationRuntime.cs` | `MassNavigation` runtime facade | Owns reusable runtime state around agents, groups, command application, sync cadence, and diagnostics. Must depend on core services through formal keys only. |
| `Runtime/MassNavFlowTuning.cs` | `MassFlow` config | Flow-field scheduler and rebuild tuning. |
| `Runtime/MassNavArrivalTuning.cs` | `MassFlow` config | Arrival behavior tuning. |
| `Runtime/MassNavAvoidanceTuning.cs` | `MassFlow` config | Pair/crowd avoidance tuning. |
| `Runtime/MassNavCrowdSemantics.cs` | `MassNavigation` or `MassFlow` config | Team relationship to navigation policy mapping. Keep gameplay relationship source in core team infrastructure. |
| `Runtime/MassNavGroupRuntime.cs` | `MassNavigation` groups | Group command state and formation target ownership. |
| `Runtime/MassNavCommandRuntime.cs` | `MassNavigation` commands | Reusable command intent, lifecycle, and dirty state. |
| `Runtime/MassNavAgentState.cs` | `MassNavigation` agent index | ECS entity to solver-agent binding and selection/order metadata. |
| `Runtime/MassNavAgentProfileConfig.cs` | Split config | Reusable movement/mass profile schema can move to `MassNavigation`; concrete profiles stay showcase config. |
| `Runtime/MassNavAuthoringContract.cs` | Split contract | Generic required-component validation belongs in `MassNavigation`; performer ids and showcase templates stay here. |
| `Systems/MassNavCommandApplySystem.cs` | `MassNavigation` system | Applies reusable commands to runtime groups and solver state. |
| `Systems/MassNavOrderBridgeSystem.cs` | `MassNavigation` order adapter | Bridges formal `OrderBuffer` to generic mass-navigation command intent. |
| `Systems/MassNavCommandBridgeSystem.cs` | Showcase or thin adapter | Input command collection is shell-owned unless a reusable player-command adapter emerges. |
| `Systems/MassNavSpawnReceiptBindingSystem.cs` | Split system | Generic spawn receipt to agent binding can move; showcase template/profile/style assumptions stay here. |
| `Systems/MassNavAgentMetadataSyncSystem.cs` | `MassNavigation` sync | Syncs ECS/team/profile metadata into reusable runtime state. |
| `Systems/MassNavFormationSystem.cs` | `MassNavigation` formation | Reusable formation target allocation and group arrangement, if still independent from UI/scenario. |
| `Systems/MassNavSelectionSyncSystem.cs` | `MassNavigation` selection adapter | Bridges `SelectionRuntime` to agent selected flags through formal selection keys. |
| `Systems/MassNavSelectionPerformerSyncSystem.cs` | Showcase presentation adapter | Selection overlay performer params stay shell-owned until there is a generic presentation contract. |
| `Systems/MassNavPanelPresentationSystem.cs` | Showcase shell | UI presentation and tuning panel are acceptance-only. |
| `Systems/MassNavHudPresentationSystem.cs` | Showcase shell | Diagnostics HUD is acceptance evidence, not core navigation. |
| `Systems/MassNavScenarioBootstrap.cs` | Showcase shell | Scenario spawn authoring stays in the showcase. |
| `UI/MassNavPlaygroundPanelController.cs` | Showcase shell | Tuning UI proves runtime controls but does not define core API. |

## Rules For Follow-Up Work

- Keep `MassNavWebParity` as the acceptance mod name until the extracted runtime compiles and the showcase consumes it.
- Do not move camera, minimap, or order marker behavior as part of naming cleanup.
- Do not add fallback paths for missing services, config, templates, performers, or map bounds.
- Do not create a parallel config loader, registry, spawn path, selection runtime, order runtime, performer runtime, or minimap runtime.
- When a file becomes reusable for two or more mods, move that capability to `MassFlow`, `MassNavigation`, core, or another formal reusable infrastructure in the same change that updates callers.
