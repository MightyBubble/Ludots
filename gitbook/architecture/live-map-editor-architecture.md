# Live Map Editor Architecture

Epic: [#451](https://github.com/MightyBubble/Ludots/issues/451). Scope: EDR-0 through EDR-7. EDR-8 / #460 pixel streaming is future work and is not part of this implementation.

## Decisions

1. The editor is a launch-time capability mod: `LiveMapEditorMod` is added through launcher selectors. Ludots does not hot-inject mods into an already running session.
2. The panel is a CEF Web UI surface in `UiSurfaceSegment.Overlay`. It is non-exclusive and is toggled in-session with `F4`.
3. Web UI is only the control and data plane. It does not render terrain, navmesh, entities, or a parallel world model.
4. Core is the authoring SSOT. Commands mutate Core state first, then presentation/Raylib/nav/save observe the same state.
5. Raylib/Core is the authoritative viewport renderer. Debug overlays draw from Core `NavTileStore`, path query results, selection state, and brush state.
6. The existing React/Three.js editor and `Ludots.Editor.Bridge` stay as the offline editor path. This Epic does not delete or rewrite them.
7. Live runtime nav rebake is `runtime-incremental + cdt`. Offline full bake may still use Recast, but the in-session editor must not claim Recast for live rebake.
8. Missing runtime `.ntil` tiles are produced by the explicit runtime-incremental CDT source path. Offline/recast mode remains fail-fast on missing `.ntil`.
9. The UAT map is `live_editor_nav_grid`, a child of the existing core `nav_editor_grid` map. It overrides the boards with grid boards that intentionally omit `DataFile`, so Core creates flat grid `LogicTerrainField` authoring data instead of loading the parent `.bin` terrain.
10. `.ltrn` v1 stores grid logic terrain cells. Height is currently authored as `HeightLevel`; the temporary visual adapter projects one height level to 100 cm until the #399 logic/visual height scale line defines a richer authored height scale.

## Ownership

| Area | Owner | Runtime object | Editor behavior |
|---|---|---|---|
| Map session | Core | `MapSession`, `MapConfig` | Read current map/board state through DataPlane topics |
| Logic terrain | Core | `LogicTerrainField`, `.ltrn` for grid authoring | `paintTerrain` mutates grid cells and marks dirty AABB |
| Entities | Core/ECS | `RuntimeEntitySpawnQueue`, `MapEntity`, `MapLoadEntityIndex` | `placeEntity`, `selectEntity`, `removeEntity` command lane |
| Navigation | Core nav | `RuntimeIncrementalNavMeshRebuildQueue`, `NavTileStore`, `NavQueryService` | dirty terrain/obstacle areas rebake, path overlay draws authoritative results |
| Save | Core authoring | `MapAuthoringAssetWriter` | `saveMap` writes an explicit or unambiguous authoring map fragment, `.ltrn`, entities, and loaded nav tiles |
| Panel | WebUI DataPlane | `WebUiDataPlaneRuntime`, `WebUiCommandRouter` | Inspector and controls only |

## EDR-2 Spike Result

The spike result is go for EDR-3 through EDR-7 with these constraints:

| Topic | Result |
|---|---|
| Viewport picking | Uses Core/Raylib pointer state via `AuthoritativeGroundPointerHelper`; Web UI does not compute ray hits |
| Brush input | Raylib viewport drives `paintTerrain` while the panel is open; panel chrome suppresses viewport commands |
| Simulation picking | In `sim`/`nav` tool, left click sets start and right click sets goal; query runs through `NavQueryService` |
| Debug draw | Brush, selection, path, and nav tile triangles are drawn by Core/Raylib buffers |
| Known limitation | Panel hit testing is currently mirrored by fixed overlay chrome bounds in the presentation system; the browser surface still owns the CEF hit test for UI delivery |

## Flow

```mermaid
flowchart LR
    User["User input in Raylib viewport or CEF panel"]
    Panel["CEF Overlay panel<br/>DataPlane command"]
    Runtime["LiveMapEditorRuntime"]
    Core["Core authoring state<br/>MapSession / LogicTerrain / ECS"]
    Presentation["Core presentation buffers<br/>Raylib authoritative draw"]
    Nav["RuntimeIncrementalNavMeshRebuildQueue<br/>CDT bake"]
    Query["NavQueryService path query"]
    Save["MapAuthoringAssetWriter<br/>mod assets"]

    User --> Panel
    User --> Runtime
    Panel --> Runtime
    Runtime --> Core
    Core --> Presentation
    Core --> Nav
    Nav --> Query
    Query --> Presentation
    Core --> Save
```

## Command Lane

| Command | Writes | Notes |
|---|---|---|
| `setTool` | editor runtime only | Selects inspect/paint/entity/sim/nav behavior |
| `setBrush` | editor runtime only | Brush radius, height, area, cost, blocked/water/ramp flags |
| `paintTerrain` | `MutableGridLogicTerrainField` | Converts focused terrain to mutable grid when needed, bumps editor terrain revision, enqueues dirty AABB |
| `placeEntity` | `RuntimeEntitySpawnQueue` | Uses spawn receipts to bind the authored instance to the live entity |
| `selectEntity` | editor selection state | Selects nearest map entity near the picked world point |
| `removeEntity` | ECS + session entity index/authored list | Uses presentation destroy lifecycle when stable visual ids exist |
| `rebakeDirty` | nav tile stores | Processes runtime-incremental CDT dirty queue |
| `queryPath` | nav debug state | Uses `NavQueryService.TryFindPath`, reports elapsed microseconds |
| `saveMap` | mod assets | Writes an explicit `metadata.liveMapEditor.saveTarget=true` map fragment, or exactly one unambiguous fragment with boards |

## Save Scope

`saveMap` writes the focused merged authoring state back to the selected mod map fragment. For this Epic it writes:

| Asset | Status |
|---|---|
| Grid `LogicTerrainField` | Written as `.ltrn` through `LogicTerrainBinary` |
| `MapConfig.Entities` | Written into the selected map JSON |
| Loaded nav tiles | Written as `.ntil` through `NavTileBinary` after the runtime queue is clean |
| Visual heightmap `.vhtm` | Not independently authored by this editor path; when the map has no explicit `.vhtm`, Core derives the displayed visual heightfield from the grid LogicTerrain adapter |

## EDR Delivery Matrix

| Issue | Result |
|---|---|
| #452 EDR-0 | Boundary documented here; launcher preset keeps Raylib + original Web editor/Bridge paths separate |
| #453 EDR-1 | `LiveMapEditorMod` adds a launch-time CEF overlay panel and DataPlane state topic |
| #454 EDR-2 | Raylib pointer picking drives brush/entity/path interactions; CEF chrome suppresses viewport commands |
| #455 EDR-3 | WebUI command lane places, selects, and removes map entities through Core/ECS |
| #456 EDR-4 | Grid `LogicTerrainField` can be edited live and saved as `.ltrn` |
| #457 EDR-5 | Terrain dirty AABBs feed runtime-incremental CDT rebake; Raylib overlays draw authoritative nav tiles/path |
| #458 EDR-6 | `MapAuthoringAssetWriter` saves map config, grid terrain, entities, and loaded nav tiles in-process |
| #459 EDR-7 | UAT preset `live_map_editor_nav_grid_cef_raylib` stacks CEF runtime, an existing nav grid map, and the editor mod |

## Non-Goals

- No online pixel streaming in this Epic.
- No second WebGL/Three world renderer for in-session editing.
- No fallback to fake navmesh, fake green planes, or JS-computed paths.
- No runtime dependency on `Ludots.Editor.Bridge` for save.
