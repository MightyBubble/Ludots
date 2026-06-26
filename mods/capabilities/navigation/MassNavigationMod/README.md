# MassNavigation Foundation

`MassNavigationMod` is the reusable large-scale navigation foundation asset pack. It is not the Total War scenario itself. The runtime is owned by `src/Core/MassCrowd`; this mod contributes config, templates, performers, maps, and the tuning panel that exercise the formal navigation chain.

Start here if you are new:

- Beginner Total War-like RTS guide: `gitbook/reference/mass-navigation-user-book.md`
- Formal chain and boundary audit: `gitbook/reference/mass-navigation-formal-chain.md`
- Reference game mod: `mods/showcases/mass_navigation_total_war_entry/MassNavigationTotalWarEntryMod/`

## Responsibility Split

| Layer | Responsibility |
| --- | --- |
| `MassFlow` | Hot-path SoA solver data, flow/crowd/avoidance calculations, cadence-sensitive simulation. |
| Core `MassCrowd` runtime | Gameplay-facing navigation integration: agents, profiles, groups, orders, ECS state, authoring contracts, and component-authored runtime binding. |
| `MassNavigationMod` | Foundation assets and UI for the default large-world navigation map. |
| Game/showcase mod | Product rules such as formations owning soldiers, army names, formation outlines, obstacle overlay style, initial scenario setup, authored through components and runtime spawn parameters. |

Do not add alternate names for old experiment labels. `MassNavigation*` is the formal runtime surface for this foundation.

## Foundation Config

Foundation-owned files stay under `assets/` and are loaded through the normal config pipeline.

| File | Responsibility |
| --- | --- |
| `assets/MassNavigationConfig.json` | Solver window, cadence, flow, arrival, avoidance, crowd semantics, scenario teams, camera profiles, and view residency. |
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

Runtime files for MassNavigation live in `src/Core/MassCrowd`. Component authoring drives the core runtime:

- `MassCrowdAgent` participates in the MassCrowd runtime.
- `OrderBuffer` makes an authored agent controllable/orderable.
- `ManifestationObstacleIntent2D` / `CompoundObstacle2D` author obstacle geometry.
- `MassFlowObstacleProjection` is the bridge output consumed by MassFlow environment binding.
- `MassCrowdFormationAnchor`, `MassCrowdFormationFollower`, and optional `MassCrowdFollowerLocomotion` enable formation behavior only for templates that author those components.

`MassNavigationConfig.world.obstacles[]` is obsolete and must fail strict config loading. Obstacle authoring belongs to map/template ECS components and is documented in `gitbook/reference/obstacle-authoring.md`.

The mod-owned source surface is intentionally small:

| File | Boundary |
| --- | --- |
| `MassNavigationModEntry.cs` | Installs the core MassCrowd runtime and map event hooks. |
| `UI/MassNavigationPanelController.cs` | Foundation tuning panel. |
| `assets/**` | Default MassNavigation config, templates, performers, input, and map data. |

## Rules For Follow-Up Work

- No fallback paths for missing services, config, templates, performers, map bounds, or visual heightmap.
- No parallel config loader, registry, spawn path, selection runtime, order runtime, performer runtime, or minimap runtime.
- No Total War formation ownership inside the MassNavigation foundation.
- No MassNavigation-owned post-spawn lifecycle or bootstrap path inside this mod; runtime entity discovery is core-owned and component-authored.
- No private MassNavigation marker lifecycle system when performer rules can express the lifecycle.
- Move reusable behavior into `MassFlow`, `MassNavigation`, Core, or another formal reusable mod when two or more mods need it.
