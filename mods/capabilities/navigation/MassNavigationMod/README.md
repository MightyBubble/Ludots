# MassNavigation Foundation

`MassNavigationMod` is the reusable large-scale navigation foundation asset pack. The runtime is owned by `src/Core/MassNavigation`; this mod contributes config, templates, presenters, and maps that exercise the formal navigation chain.

Start here if you are new:

- Beginner large-scale navigation guide: `gitbook/reference/mass-navigation-user-book.md`
- Formal chain and boundary audit: `gitbook/reference/mass-navigation-formal-chain.md`

## Responsibility Split

| Layer | Responsibility |
| --- | --- |
| `MassNavigationFlow` | Hot-path SoA solver data, flow/crowd/avoidance calculations, cadence-sensitive simulation. |
| Core `MassNavigation` runtime | Agents, profiles, typed MovePlan execution, command groups, ECS state, and component-authored runtime binding. It does not inspect Order state. |
| `MassNavigationMod` | Foundation assets plus the composition adapter that projects `massNavigationMove` between GAS and typed MovePlan. |
| Game/showcase mod | Product rules such as Formation anchor/member expansion, physical input mappings, visual outlines, obstacle overlay style, and initial scenario setup. |

Do not add alternate names for old experiment labels. `MassNavigation*` is the formal runtime surface for this foundation.

## Foundation Config

Foundation-owned files stay under `mods/capabilities/navigation/MassNavigationMod/assets/` and are loaded through the normal config pipeline.

| File | Responsibility |
| --- | --- |
| `mods/capabilities/navigation/MassNavigationMod/assets/MassNavigationConfig.json` | Solver window, cadence, flow, arrival, avoidance, crowd semantics, scenario teams, and view residency. |
| `mods/capabilities/navigation/MassNavigationMod/assets/Configs/Camera/virtual_cameras.json` | Foundation visual-heightmap-aware camera profile for the large-world map. |
| `mods/capabilities/navigation/MassNavigationMod/assets/GAS/order_types.json` | `massNavigationMove` order registration and rule authoring. |
| `mods/capabilities/navigation/MassNavigationMod/assets/Entities/templates.json` | Foundation example templates and required component contract examples. |
| `mods/capabilities/navigation/MassNavigationMod/assets/Presentation/presenters.json` | Foundation example presenter authoring. |
| `mods/capabilities/navigation/MassNavigationMod/assets/Maps/mass_navigation.json` | Foundation example map, board bounds, and visual terrain binding. |

Game-specific mods can provide their own files with the same kinds of data. They should not create a private loader.

## Order Authoring

Order config is authored with semantic keys:

```json
{
  "orderBlackboardKeys": {},
  "orderTypes": {
    "massNavigationMove": {
      "intArg0BlackboardKey": "none"
    }
  }
}
```

Do not write numeric order blackboard ids in JSON. The loader resolves strings once at startup; hot-path buffers use compiled runtime ids.

## Runtime Ownership

Runtime files for MassNavigation live in `src/Core/MassNavigation`. Component authoring drives the core runtime:

- `MassNavigationAgent` participates in the MassNavigation runtime.
- `MovePlanExecutionIntent` is the typed execution input; `MovePlanExecutionResult` is the typed output.
- `ManifestationObstacleIntent2D` / `CompoundObstacle2D` author obstacle geometry.
- `MassNavigationFlowObstacleProjection` is the bridge output consumed by MassNavigationFlow environment binding.
- Formation identity, anchor/member state and slot expansion belong to the consuming game/showcase Mod. Anchor is not a navigation actor; members are normal MassNavigation agents.

`massNavigationMove` contains one spatial destination only. Formation mode, slot layout, facing and
rotation are not valid generic MassNavigation order payloads. A Formation Mod expands its anchor through the
Command Router, then GAS projects the resulting member orders into `MovePlanExecutionIntent(CommandGroup)`.

`MassNavigationConfig.world.obstacles[]` is obsolete and must fail strict config loading. Obstacle authoring belongs to map/template ECS components and is documented in `gitbook/reference/obstacle-authoring.md`.

The mod-owned source surface is intentionally small:

| File | Boundary |
| --- | --- |
| `MassNavigationModEntry.cs` | Installs the GAS Order-to-MovePlan adapter around the typed MassNavigation consumer. |
| `mods/capabilities/navigation/MassNavigationMod/assets/**` | Default MassNavigation config, templates, presenters, input, and map data. |

## Rules For Follow-Up Work

- No fallback paths for missing services, config, templates, presenters, map bounds, or visual heightmap.
- No parallel config loader, registry, spawn path, selection runtime, order runtime, presenter runtime, or minimap runtime.
- No game-specific group-layout ownership inside the MassNavigation foundation.
- No physical input action id, Formation business state, rotation shortcut, camera, HUD, outline, colour or showcase scenario policy inside Core MassNavigation.
- No MassNavigation-owned post-spawn lifecycle or bootstrap path inside this mod; runtime entity discovery is core-owned and component-authored.
- No private MassNavigation marker lifecycle system when presenter rules can express the lifecycle.
- Move reusable behavior into `MassNavigationFlow`, `MassNavigation`, Core, or another formal reusable mod when two or more mods need it.
