# MassNavigation Foundation

`MassNavigationMod` is the reusable large-scale navigation foundation asset pack. It is not the Formation Capability scenario itself. The runtime is owned by `src/Core/MassNavigation`; this mod contributes config, templates, performers, and maps that exercise the formal navigation chain.

Start here if you are new:

- Beginner formation capability RTS guide: `gitbook/reference/mass-navigation-user-book.md`
- Formal chain and boundary audit: `gitbook/reference/mass-navigation-formal-chain.md`
- Reference game mod: `mods/showcases/formation_capability/FormationCapabilityShowcaseMod/`

## Responsibility Split

| Layer | Responsibility |
| --- | --- |
| `MassNavigationFlow` | Hot-path SoA solver data, flow/crowd/avoidance calculations, cadence-sensitive simulation. |
| Core `MassNavigation` runtime | Gameplay-facing navigation integration: agents, profiles, groups, orders, ECS state, authoring contracts, and component-authored runtime binding. |
| `MassNavigationMod` | Foundation assets and UI for the default large-world navigation map. |
| Game/showcase mod | Product rules such as formations owning soldiers, army names, formation outlines, obstacle overlay style, initial scenario setup, authored through components and runtime spawn parameters. |

Do not add alternate names for old experiment labels. `MassNavigation*` is the formal runtime surface for this foundation.

## Foundation Config

Foundation-owned files stay under `assets/` and are loaded through the normal config pipeline.

| File | Responsibility |
| --- | --- |
| `assets/MassNavigationConfig.json` | Solver window, cadence, flow, arrival, avoidance, crowd semantics, scenario teams, and view residency. |
| `assets/Configs/Camera/virtual_cameras.json` | Foundation visual-heightmap-aware camera profile for the large-world map. |
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

## Runtime Ownership

Runtime files for MassNavigation live in `src/Core/MassNavigation`. Component authoring drives the core runtime:

- `MassNavigationAgent` participates in the MassNavigation runtime.
- `OrderBuffer` makes an authored agent controllable/orderable.
- `ManifestationObstacleIntent2D` / `CompoundObstacle2D` author obstacle geometry.
- `MassNavigationFlowObstacleProjection` is the bridge output consumed by MassNavigationFlow environment binding.
- `MassNavigationFormationAnchor`, `MassNavigationFormationFollower`, and optional `MassNavigationFollowerLocomotion` enable formation behavior only for templates that author those components.

`MassNavigationConfig.world.obstacles[]` is obsolete and must fail strict config loading. Obstacle authoring belongs to map/template ECS components and is documented in `gitbook/reference/obstacle-authoring.md`.

The mod-owned source surface is intentionally small:

| File | Boundary |
| --- | --- |
| `MassNavigationModEntry.cs` | Data-only mod entry for MassNavigation foundation assets. |
| `assets/**` | Default MassNavigation config, templates, performers, input, and map data. |

## Rules For Follow-Up Work

- No fallback paths for missing services, config, templates, performers, map bounds, or visual heightmap.
- No parallel config loader, registry, spawn path, selection runtime, order runtime, performer runtime, or minimap runtime.
- No Formation Capability formation ownership inside the MassNavigation foundation.
- No MassNavigation-owned post-spawn lifecycle or bootstrap path inside this mod; runtime entity discovery is core-owned and component-authored.
- No private MassNavigation marker lifecycle system when performer rules can express the lifecycle.
- Move reusable behavior into `MassNavigationFlow`, `MassNavigation`, Core, or another formal reusable mod when two or more mods need it.
