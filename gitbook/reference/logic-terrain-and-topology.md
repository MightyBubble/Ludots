# Logic Terrain and Topology

Parent: [Epic #281](https://github.com/MightyBubble/Ludots/issues/281). Subissue: [NAV-4 #286](https://github.com/MightyBubble/Ludots/issues/286). Scale vocabulary: [NAV-0 #282](https://github.com/MightyBubble/Ludots/issues/282).

## Background

Before NAV-4, navmesh bake read logical terrain through `VertexMap` only. `WalkMaskBuilder`, `NavTileBuilder`, `BakePipeline`, and Recast all assumed hex vertex coordinates and `VertexChunk` storage. Grid maps such as `mass_navigation` could own a grid board and visual `.vhtm`, but had no logical terrain field for bake.

Logical terrain and visual terrain remain separate:

- Logical terrain is gameplay truth: height level, water, ramp, blocked, area id, and cost.
- Visual terrain is continuous centimeter geometry, used by presentation, grounding, and NavMesh vertex sampling when the board policy selects it.
- Visual height does not change walkability classification: blocked/water/ramp/area/topology still come from `LogicTerrainField`.

## Target

`LogicTerrainField` is the topology-neutral Core input for nav bake. It has one owner per map board and two production backends:

- `VertexMapLogicTerrainField`: adapts existing hex `VertexMap` / `VertexChunk` without changing 4-bit height storage.
- `FlatGridLogicTerrainField` / `MutableGridLogicTerrainField`: supplies square-grid logical terrain for grid maps.

In scope:

- Grid and hex both build `TriWalkMask` and `NavTile`.
- Recast and CDT entry points can consume `LogicTerrainField`.
- Runtime map load creates flat grid logic terrain when a grid board has no `.vtxm`.
- Nav bake can combine direct visual-height geometry with logic classification without projecting VHTM into a second logic-height dataset.

Out of scope:

- Height precision expansion. Logic terrain height remains 4-bit.
- Making visual terrain the source of walkability.
- Runtime incremental navmesh rebuild details; those are covered by [NAV-10 #304](runtime-incremental-navmesh-rebuild.md).

## User Story

As a level designer, I want grid and hex maps to bake navmesh from the same logical terrain contract, so I can choose topology per gameplay need.

Given a grid board with navigation enabled, when bake code receives its `LogicTerrainField`, then it can produce a non-empty `NavTile` without a `VertexMap`.

As an artist, I want visual terrain to be independent from walkability truth, so sculpting render height does not silently change gameplay.

Given a visual heightmap change, when the board selects direct VHTM geometry, then NavTile vertex heights change while logical walkability remains unchanged.

## UAT Showcase

Preset: `mass_navigation`

Command:

```powershell
.\scripts\run-mod-launcher.cmd cli launch mass_navigation --adapter raylib
```

| Operation | Visible feedback |
|---|---|
| Start grid topology map with water, ramps, and blockers | HUD shows `Topology: Grid`; navmesh overlay covers walkable cells only |
| Command one unit across the map | Unit routes around blocked/water cells and reaches the target |
| Switch to hex topology and rebake | HUD shows `Topology: Hex`; navmesh overlay remains equivalent for the same logical terrain |
| Raise visual height only | Rendered ground and NavTile geometry change height; blocked/area decisions and route topology do not change |
| Run explicit visual-to-logic projection and rebake | Logic height levels change and bake output changes accordingly |

## Configuration

`BoardConfig.SpatialType` chooses board topology:

- `Grid`: uses square-grid logic terrain.
- `HexGrid` / `Hex`: uses `VertexMapLogicTerrainField` when `DataFile` points to `.vtxm`.
- `NodeGraph`: graph routing board, not a logic terrain owner.

`BoardConfig.DataFile` is optional for grid logic terrain. If absent, grid boards create a flat logic terrain sized by:

- `WidthInMacroTiles * SpatialScaleDefaults.MacroTileCells`
- `HeightInMacroTiles * SpatialScaleDefaults.MacroTileCells`
- `GridCellSizeCm`
- `ChunkSizeCells`

Surface flags:

| Field | Meaning |
|---|---|
| `HeightLevel` | 4-bit logic height, `0..15` |
| `WaterHeightLevel` | 4-bit water level; triangle is blocked when water level is above height |
| `Ramp` | Allows height difference within a triangle |
| `Blocked` | Forces triangle unwalkable |
| `AreaId` | Nav area id for later cost routing |
| `Cost` | Positive traversal cost |

## Config To Behavior Tests

Contract tests:

- `LogicTerrainFieldContractTests.VertexMapAdapter_PreservesWalkMaskSemantics`
- `LogicTerrainFieldContractTests.FlatGridLogicTerrainField_BuildsNavTile`
- `LogicTerrainFieldContractTests.VisualHeightmap_DoesNotChangeLogicWalkabilityUnlessExplicitlyProjected`

Changing logical terrain flags changes the walk mask and resulting tile. Changing only visual height does not.

## Merge And Reuse

Reused:

- Existing `VertexMap` / `VertexChunk` storage.
- Existing `WalkMaskBuilder`, `NavTileBuilder`, `BakePipeline`, and `RecastNavTileBaker`.
- Existing visual heightmap runtime.

Added:

- `LogicTerrainField` abstraction and grid/hex backends.
- Direct `IVisualHeightmap` geometry adapter used by `NavBakeContext`.

No external branch was merged for NAV-4.

## DoD

NAV-4 is complete when grid and hex both feed nav bake through `LogicTerrainField`, visual terrain is explicit-only for projection, contract tests cover topology parity and visual separation, GitBook indexes include this page, and all changes link back to #281.
