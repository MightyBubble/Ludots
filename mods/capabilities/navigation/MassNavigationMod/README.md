# MassNavigation Foundation

`MassNavigationMod` is the reusable large-scale navigation foundation capability. It is not the Formation Capability scenario itself. Runtime implementation lives in `src/Core/MassNavigation`; this mod owns map-event activation and contributes config, templates, performers, and maps that exercise the formal navigation chain.

Start here if you are new:

- Beginner formation capability RTS guide: `gitbook/reference/mass-navigation-user-book.md`
- Formal chain and boundary audit: `gitbook/reference/mass-navigation-formal-chain.md`
- Reference game mod: `mods/showcases/formation_capability/FormationCapabilityShowcaseMod/`

## Responsibility Split

| Layer | Responsibility |
| --- | --- |
| `MassNavigationFlow` | Hot-path SoA solver data, flow/crowd/avoidance calculations, cadence-sensitive simulation. |
| Core `MassNavigation` runtime | Gameplay-facing navigation integration: agents, profiles, groups, orders, ECS state, authoring contracts, and component-authored runtime binding. |
| `MassNavigationMod` | Activates the Core runtime from map lifecycle events and owns foundation assets/UI for the default large-world navigation map. |
| Game/showcase mod | Product rules such as formations owning soldiers, army names, formation outlines, obstacle overlay style, initial scenario setup, authored through components and runtime spawn parameters. |

Do not add alternate names for old experiment labels. `MassNavigation*` is the formal runtime surface for this foundation.

## Foundation Config

Foundation-owned files stay under `mods/capabilities/navigation/MassNavigationMod/assets/` and are loaded through the normal config pipeline.

| File | Responsibility |
| --- | --- |
| `mods/capabilities/navigation/MassNavigationMod/assets/MassNavigationConfig.json` | ArrayById profile catalog. `runtime` owns solver/cadence/crowd/arrival/avoidance/streaming execution data and explicit capacities, including bounded `(team, layer)` flow-state storage via `capacity.flowStateCapacity`; required `sceneAuthoring` is explicitly disabled with `autoSpawnConfiguredScenario=false` when no example spawn/presentation/relationship data is needed. Maps bind a profile only through `Metadata.massNavigation.profileId`. |
| `mods/capabilities/navigation/MassNavigationMod/assets/Configs/Camera/virtual_cameras.json` | Foundation visual-heightmap-aware camera profile for the large-world map. |
| `mods/capabilities/navigation/MassNavigationMod/assets/GAS/order_types.json` | `massNavigationMove` order registration and rule authoring. |
| `mods/capabilities/navigation/MassNavigationMod/assets/Entities/templates.json` | Foundation example templates and required component contract examples. |
| `mods/capabilities/navigation/MassNavigationMod/assets/Presentation/performers.json` | Foundation example performer authoring. |
| `mods/capabilities/navigation/MassNavigationMod/assets/Maps/mass_navigation.json` | Foundation example map, board bounds, and visual terrain binding. |

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

## Runtime Ownership

Runtime files for MassNavigation live in `src/Core/MassNavigation`. Component authoring drives the core runtime:

- `MassNavigationAgent` participates in the MassNavigation runtime.
- `OrderBuffer` makes an authored agent controllable/orderable.
- `ManifestationObstacleIntent2D` / `CompoundObstacle2D` author obstacle geometry.
- `MassNavigationFlowObstacleProjection` is the bridge output consumed by MassNavigationFlow environment binding.
- `MassNavigationFormationAnchor`, `MassNavigationFormationFollower`, and optional `MassNavigationFollowerLocomotion` enable formation behavior only for templates that author those components.

Initial spawn and unbound discovery establish the binding automatically. After an agent is bound, any writer that changes its `Team`, `MassNavigationAgent.ProfileId`, `EntityLayer`, or `OrderBuffer` presence must call `MassNavigationAgentBinding.MarkDirty(world, entity)`. This explicit dirty contract keeps steady-state binding O(1) without hiding authoring changes behind periodic 10K-agent scans.

`MassNavigationConfig.world.obstacles[]` is obsolete and must fail strict config loading. Obstacle authoring belongs to map/template ECS components and is documented in `gitbook/reference/obstacle-authoring.md`.

The mod-owned source surface is intentionally small:

| File | Boundary |
| --- | --- |
| `MassNavigationModEntry.cs` | Registers map loaded/resumed/suspended/unloaded handlers for the Core runtime. |
| `MassNavigationSceneOwner.cs` | Owns only the optional foundation example scene authoring and reset lifecycle. |
| `mods/capabilities/navigation/MassNavigationMod/assets/**` | Default MassNavigation config, templates, performers, input, and map data. |

## Rules For Follow-Up Work

- No fallback paths for missing services, config, templates, performers, map bounds, or visual heightmap.
- No parallel config loader, registry, spawn path, selection runtime, order runtime, performer runtime, or minimap runtime.
- No Formation Capability formation ownership inside the MassNavigation foundation.
- No game/showcase-specific post-spawn rules inside this mod; generic runtime discovery is Core-owned, while optional foundation example spawning is isolated in `MassNavigationSceneOwner`.
- No private MassNavigation marker lifecycle system when performer rules can express the lifecycle.
- Move reusable behavior into `MassNavigationFlow`, `MassNavigation`, Core, or another formal reusable mod when two or more mods need it.
