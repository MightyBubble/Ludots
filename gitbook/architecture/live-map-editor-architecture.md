# Live Map Editor Architecture

Epic: [#451](https://github.com/MightyBubble/Ludots/issues/451). Scope: EDR-0 through EDR-7 plus Phase 2 EDR-9 through EDR-21 parity controls. EDR-8 / #460 pixel streaming is future work and is not part of this implementation.

## Decisions

1. The editor is a launch-time capability mod: `LiveMapEditorMod` is added through launcher selectors. Ludots does not hot-inject mods into an already running session.
2. Phase 2 uses a CEF Web UI surface in `UiSurfaceSegment.Main` with an exclusive lease so the denser parity panel owns the screen while open. It is toggled in-session with `F4`.
3. Web UI is only the control and data plane. It does not render terrain, navmesh, entities, or a parallel world model.
4. Core is the authoring SSOT. Commands mutate Core state first, then presentation/Raylib/nav/save observe the same state.
5. Raylib/Core is the authoritative viewport renderer. Debug overlays draw from Core `NavTileStore`, path query results, selection state, and brush state.
6. The existing React/Three.js editor and `Ludots.Editor.Bridge` stay as the offline editor path. This Epic does not delete or rewrite them.
7. Live runtime nav rebake is `runtime-incremental + cdt`. Offline full bake may still use Recast, but the in-session editor must not claim Recast for live rebake.
8. Missing runtime `.ntil` tiles are produced by the explicit runtime-incremental CDT source path. Offline/recast mode remains fail-fast on missing `.ntil`.
9. The UAT map is `live_editor_nav_grid`, a child of the existing core `nav_editor_grid` map. It overrides the boards with grid boards that intentionally omit `DataFile`, so Core creates flat grid `LogicTerrainField` authoring data instead of loading the parent `.bin` terrain.
10. `.ltrn` v1 stores grid logic terrain cells. Height is currently authored as `HeightLevel`; the temporary visual adapter projects one height level to 100 cm until the #399 logic/visual height scale line defines a richer authored height scale.
11. Phase 2 parity follows [ADR-0005](../../docs/adr/ADR-0005-live-map-editor-phase2-parity-boundary.md): Grid parity is limited to live `LogicTerrainCell` fields, transport is gated by exactly one `NodeGraph` board, runtime bake controls remain runtime-incremental CDT, and biome/vegetation/layers are deferred until Core owns those fields.
12. Raylib WYSIWYG terrain rendering reads optional `IVisualTerrainRenderFeatureSource` metadata from the Core visual terrain source. `IVisualHeightmap` remains only the height sampling contract; water, area tint, blocked tint, and ramp/cliff edge styling are render metadata derived from `LogicTerrainCell`.
13. Map/Board lifecycle authoring writes `MapConfig.Boards[]` and reload-required state through Core. `BoardConfig.NavigationEnabled` is persisted as a board setting, but EDR-14 does not automatically add or remove the global `Feature.NavMesh:On` tag because that tag makes map load require existing `.ntil` assets.

## Phase 2 Parity Boundary

Phase 2 does not move the editor back to the old browser/Bridge stack. The in-session panel is still a DataPlane control surface over Core authoring state.

| React editor concept | In-session owner | Result |
|---|---|---|
| Cell set/raise/lower | `MutableGridLogicTerrainField.SetCell` over `LogicTerrainCell` | Implemented for height, water, area, cost, blocked, and ramp |
| Water bucket | `LogicTerrainCell.WaterHeightLevel` + `SurfaceFlags.Water` | Implemented as connected same-height fill |
| Terrain territory | `LogicTerrainCell.AreaId` | Implemented as area id paint |
| Biome / vegetation / snow / mud / ice / layers | none in live Grid `LogicTerrainCell` | Deferred by ADR-0005 |
| Bake controls | `RuntimeIncrementalNavMeshRebuildQueue` + `NavTileStore` | Dirty, Dirty+N, Full, Estimate, Bake, Clear over CDT |
| Path simulation | `NavQueryServiceRegistry` + `NavMeshProfileRegistry` | Layer/profile selection implemented; `MaxPortals` is passed to `NavQueryService.TryFindPath` |
| Transport | `TransportNetworkAsset` + exactly one `NodeGraph` board | Enabled only when the focused map has one NodeGraph board |
| Minimap | DataPlane 2D inspector over terrain chunks | Implemented as non-authoritative Web UI canvas with chunk-level dirty highlights |
| Entity palette + overrides | `EntityTemplateKeyRegistry.SnapshotMappings()` + `MapConfig.Entities[].Overrides` | Implemented as template selection source and selected-entity JSON override editor |
| Obstacle brush | `ManifestationObstacleIntent2D` / `ManifestationObstaclePolygon2D` + `RuntimeNavMeshStructuralObstacle` | Implemented for Circle/Box/Polygon via runtime spawn and authored map overrides |
| Map/Board lifecycle | `MapConfig.Boards[]` + `MapAuthoringAssetWriter` | Create map asset, add/delete board, update board scale/nav flag, save, and reload-required state; no runtime hot-build/hot-switch |

### Raylib Terrain WYSIWYG

Grid live editing uses `LogicTerrainVisualHeightmapAdapter` as both the regular `IVisualHeightmapRenderSource` and an optional `IVisualTerrainRenderFeatureSource`.

| Render concern | Source | Result |
|---|---|---|
| Height surface | `IVisualHeightmap` samples projected from `LogicTerrainCell.HeightLevel` | Raylib terrain mesh rebuilds when the editor terrain revision changes |
| Water | `LogicTerrainCell.WaterHeightLevel` + `SurfaceFlags.Water` | Raylib visual-heightmap renderer emits a transparent water mesh only when water covers the surface |
| Area / territory tint | `LogicTerrainCell.AreaId` | Terrain vertex colors receive the same stable hashed tint rule as the old editor's territory overlay |
| Blocked | `LogicTerrainCell.SurfaceFlags.Blocked` | Terrain vertex colors receive a red blocked tint |
| Ramp / cliff edge line | neighboring `HeightLevel` delta + `SurfaceFlags.Ramp` | Raylib draws green ramp edges and red cliff edges from Core cell data |
| Biome / vegetation / snow / mud / ice / layers | no live Grid Core field | Not rendered in this path until a Core SSOT exists |

## Ownership

| Area | Owner | Runtime object | Editor behavior |
|---|---|---|---|
| Map session | Core | `MapSession`, `MapConfig` | Read current map/board state through DataPlane topics |
| Board lifecycle | Core authoring | `BoardConfig`, `BoardAllocationPreviewCalculator` | New map/add board/update/delete board write mod map assets and require reload |
| Logic terrain | Core | `LogicTerrainField`, `.ltrn` for grid authoring | `paintTerrain` mutates grid cells and marks dirty AABB |
| Entities | Core/ECS | `RuntimeEntitySpawnQueue`, `MapEntity`, `MapLoadEntityIndex` | `placeEntity`, `selectEntity`, `removeEntity` command lane |
| Navigation | Core nav | `RuntimeIncrementalNavMeshRebuildQueue`, `NavTileStore`, `NavQueryService` | dirty terrain/obstacle areas rebake, path overlay draws authoritative results |
| Save | Core authoring | `MapAuthoringAssetWriter` | `saveMap` writes an explicit or unambiguous authoring map fragment, `.ltrn`, entities, and loaded nav tiles |
| Panel | WebUI DataPlane | `WebUiDataPlaneRuntime`, `WebUiCommandRouter` | Inspector and controls only |

## Transport Network Editing Boundary

Road, waterway, railway, and other NodeGraph authoring is tracked by [#462](https://github.com/MightyBubble/Ludots/issues/462). That Epic reuses this editor host, DataPlane command lane, Raylib picking, and save surface, but it is not part of the grid terrain/entity/nav UAT delivered by #451.

The transport leg has its own SSOT from [#415](https://github.com/MightyBubble/Ludots/issues/415):

| Concern | Required owner | Editor boundary |
|---|---|---|
| Authoring source | `TransportNetworkAsset` loaded from `TransportNetwork/transport_network.json` | Transport tools mutate the live asset reference only; no private road graph, waterway graph, or spline JSON is introduced |
| Bake | `TransportNetworkBaker.Bake(asset, chunkSizeCm)` | `.graph` chunks and ribbon chunks are derived together through the existing Core baker; the editor never authors `.graph` or ribbon output directly |
| Graph runtime | `ChunkedNodeGraphStore` / `LoadedGraphRuntime` | Rebuilt graph chunks replace store data through the existing graph runtime path so `PathServiceRouter` / `AutoPathService` read the same data |
| Ribbon rendering | `TransportNetworkRibbonSource` -> `SurfaceSourcePayloadRegistry` -> Core/Raylib presentation, with `RoadSplineBuffer` as a renderer-facing buffer where configured | Raylib draws the authoritative ribbon; the Web UI panel must not reconstruct ribbon geometry or use a JS world renderer |
| Route validation | `GraphEdgeProjectionQuery`, `PolylineGoalSnapQuery`, `GraphHybridRouteBuilder`, `AutoPathService` | Start/goal picking and multimodal route checks reuse Core query services; no new routing algorithm is added by the editor |
| Cost ownership | `pathing.json` tag rules, `AgentProfile` capacity fields, optional `GraphEdgeCostOverlay` | Transport authoring edits area/tag/capacity/flow only; edge cost is not baked into the asset |
| Save | #451 save surface plus transport serializer/catalog registration from #462 TNE-5 | Saving writes back `transport_network.json` and required catalog registration, then round-trips through `TransportNetworkAssetLoader` |

#451 therefore provides the shared shell. #462 adds transport-specific tools: node CRUD, segment point CRUD, area/tag/direction/flow/capacity editing, baker-triggered graph+ribbon refresh, route overlay, and transport asset persistence. Those tools must keep neutral `transport` terminology; road, water, and rail are configurations of the same transport network, not separate editor-owned systems.

## Entity Placement And Spatial Geometry

Entity placement follows the spatial geometry SSOT called out by #455 / #457. The editor may place and remove map-scoped entities, but it must not invent a parallel geometry model.

| Concern | SSOT | Editor behavior |
|---|---|---|
| Spawn request | `RuntimeEntitySpawnQueue` / `RuntimeEntitySpawnSystem` | `placeEntity` enqueues an existing template at the picked world point and waits for the spawn receipt |
| Map ownership | `MapEntity { MapId }` and `MapLoadEntityIndex` | Spawned entities are registered against the focused `MapSession`; save writes authored `MapConfig.Entities` |
| Entity generation | `PresentationStableId` plus ECS generation resolver | `removeEntity` requires the selected stable id/generation to still be current |
| Selection geometry | `SpatialBounds` / `SpatialFootprint2D` / `SpatialBox3D` | Selection remains a consumer of generic spatial geometry; no `SelectionFootprint2D`, `SelectionRange`, or editor-owned selection shape is introduced |
| Obstacle intent | `ManifestationObstacleIntent2D` / `ManifestationObstaclePolygon2D` / `CompoundObstacle2D` | The obstacle brush writes authored intent and structural-obstacle markers onto map entities; the editor does not synthesize private obstacle data |
| Derived physics/nav | `Collider2D`, `NavObstacle2D`, `CompoundObstacle2DState` | Existing bridge systems derive physics/nav state from authored geometry; editor commands do not write those derived sinks directly |
| Rendering | Core presentation / Raylib performer state | Placed entities render through their real presentation assets; no placeholder mesh, fake icon, or JS-side representation is authoritative |

Obstacle editing remains bounded by the same rule: authored truth lives on entity geometry components, then existing bridge systems derive physics/nav. `placeObstacle` writes `ManifestationObstacleIntent2D` for Circle/Box/Polygon, optional `ManifestationObstaclePolygon2D`, and `RuntimeNavMeshStructuralObstacle`; `eraseObstacle` removes the map entity and dirties the previous AABB. `ManifestationObstacleBridge2DSystem` rematerializes sinks by `ShapeSignature` / `PoseSignature` / `SinkSignature`.

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
    Panel["CEF Main panel<br/>DataPlane command"]
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
| `setBrush` | editor runtime only | Brush mode, target, radius, height, water height, area, cost, blocked/water/ramp flags |
| `paintTerrain` | `MutableGridLogicTerrainField` | Converts focused terrain to mutable grid when needed, applies set/raise/lower, bumps editor terrain revision, enqueues dirty AABB |
| `bucketFillWater` | `MutableGridLogicTerrainField` | Flood-fills connected same-height grid cells with `WaterHeightLevel` and `SurfaceFlags.Water` |
| `placeEntity` | `RuntimeEntitySpawnQueue` | Uses spawn receipts to bind the authored instance to the live entity |
| `selectEntity` | editor selection state | Selects nearest map entity near the picked world point |
| `removeEntity` | ECS + session entity index/authored list | Uses presentation destroy lifecycle when stable visual ids exist |
| `setObstacle` / `placeObstacle` / `eraseObstacle` | `RuntimeEntitySpawnQueue` + obstacle authored components | Places Circle/Box/Polygon obstacle entities with authored intent and structural nav dirtying |
| `setEntityOverride` / `deleteEntityOverride` | `MapConfig.Entities[].Overrides` | Edits selected entity component override JSON; save persists through `MapAuthoringAssetWriter` |
| `setBakeOptions` / `estimateNavBake` / `rebakeNav` / `clearNavTiles` | nav tile stores | Uses runtime-incremental CDT queue for Dirty, Dirty+N, and Full; clear calls `NavTileStore.Clear` |
| `setPathOptions` / `queryPath` | nav debug state | Uses `NavQueryService.TryFindPath` for selected layer/profile and reports elapsed microseconds |
| `setViewToggle` | editor runtime only | Gates Core/Raylib overlays for grid, chunks, navmesh, path, transport, entities, and minimap snapshot |
| `previewBoardAllocation` | editor runtime only | Calculates sparse allocation preview from Core `SpatialScaleDefaults`; Web UI does not recompute budget truth |
| `createMap` | mod map asset | Writes a new `assets/Maps/<map>.json` in the current writable authoring mod; created maps are not hot-loaded |
| `addBoard` / `deleteBoard` / `updateBoard` | `MapConfig.Boards[]` | Persists board lifecycle changes, clears loaded nav stores when relevant, and marks reload required |
| `selectBoard` / `reloadMap` | editor runtime / `GameEngine.LoadMap` | Selects the authored board for editing controls; reload re-enters the normal map load lifecycle for the current map |
| `transportSetMode` | editor runtime only | Selects transport node/segment/route viewport behavior |
| `transportAddNode` / `transportMoveNode` / `transportDeleteNode` | `TransportNetworkAsset.nodes` | Mutates the live asset and validates through Core |
| `transportBeginSegment` / `transportAppendSegmentPoint` / `transportCommitSegment` | `TransportNetworkAsset.segments` | Drafts and commits segment points and area/tag/flow/capacity fields |
| `transportRebake` | Core graph/ribbon derived outputs | Runs `TransportNetworkBaker.Bake(asset, chunkSizeCm)` and refreshes `ChunkedNodeGraphStore` plus `TransportNetworkRibbonSource` |
| `transportQueryRoute` | transport route debug state | Uses Core pathing (`GraphEdgeProjectionQuery`, `LoadedChunkSolvePrimer`, `PathServiceRouter` / `AutoPathService`) |
| `transportSave` | mod transport asset | Writes `TransportNetwork/transport_network.json`, ensures catalog registration, then round-trips through `TransportNetworkAssetLoader` |
| `saveMap` | mod assets | Writes an explicit `metadata.liveMapEditor.saveTarget=true` map fragment, or exactly one unambiguous fragment with boards |

## Save Scope

`saveMap` writes the focused merged authoring state back to the selected mod map fragment. For this Epic it writes:

| Asset | Status |
|---|---|
| Grid `LogicTerrainField` | Written as `.ltrn` through `LogicTerrainBinary` |
| `MapConfig.Boards[]` | Written by map/board lifecycle commands and by `saveMap` with the same authoring target rules |
| `MapConfig.Entities` | Written into the selected map JSON |
| Loaded nav tiles | Written as `.ntil` through `NavTileBinary` after the runtime queue is clean |
| Transport network asset | When loaded, written as `TransportNetwork/transport_network.json` through the #462 transport save path |
| Visual heightmap `.vhtm` | Not independently authored by this editor path; when the map has no explicit `.vhtm`, Core derives the displayed visual heightfield from the grid LogicTerrain adapter |

## EDR Delivery Matrix

| Issue | Result |
|---|---|
| #452 EDR-0 | Boundary documented here; launcher preset keeps Raylib + original Web editor/Bridge paths separate |
| #453 EDR-1 | `LiveMapEditorMod` adds a launch-time CEF panel and DataPlane state topic |
| #454 EDR-2 | Raylib pointer picking drives brush/entity/path interactions; CEF chrome suppresses viewport commands |
| #455 EDR-3 | WebUI command lane places, selects, and removes map entities through Core/ECS |
| #456 EDR-4 | Grid `LogicTerrainField` can be edited live and saved as `.ltrn` |
| #457 EDR-5 | Terrain dirty AABBs feed runtime-incremental CDT rebake; Raylib overlays draw authoritative nav tiles/path |
| #458 EDR-6 | `MapAuthoringAssetWriter` saves map config, grid terrain, entities, and loaded nav tiles in-process |
| #459 EDR-7 | UAT preset `live_map_editor_nav_grid_cef_raylib` stacks CEF runtime, an existing nav grid map, and the editor mod |
| #473 EDR-9 | Phase 2 contract ADR, no-map binding, and no-NodeGraph transport gate |
| #474 EDR-10 | `LogicTerrainCell` SSOT ruling for height/water/area/cost/blocked/ramp; biome/vegetation/layers deferred |
| #475 EDR-11 | Set/Raise/Lower cell brush plus connected same-height Water Bucket |
| #476 EDR-12 | Circle/Box/Polygon obstacle brush writes `ManifestationObstacleIntent2D` / `ManifestationObstaclePolygon2D` and `RuntimeNavMeshStructuralObstacle`; derived physics/nav state remains bridge-owned |
| #477 EDR-13 | Entity palette reads `EntityTemplateKeyRegistry`; selected entity component override JSON edits `MapConfig.Entities[].Overrides` and saves through the map writer |
| #478 EDR-14 | Map/Board lifecycle is in-session authoring plus save/reload; no runtime hot-build or hot-switch |
| #479 EDR-15 | Navigation config is visualized from loaded Core config; runtime Recast editing remains outside this path |
| #480 EDR-16 | Bake controls cover Dirty, Dirty+N, Full, Estimate, Bake, and Clear over runtime-incremental CDT |
| #481 EDR-17 | Path simulation selects nav layer/profile and passes MaxPortals to Core path query |
| #482 EDR-18 | Raylib visual-heightmap rendering consumes Core terrain feature metadata for height shading, water, area tint, blocked tint, and ramp/cliff edges; Web UI does not draw a 3D terrain copy |
| #483 EDR-19 | View toggles gate Core/Raylib grid/chunk/nav/path/transport/entity overlays |
| #484 EDR-20 | Minimap is a Web UI 2D inspector over DataPlane chunk summaries, camera state, and dirty chunk flags |
| #485 EDR-21 | Parity UAT uses the integrated terrain/nav/transport preset |

## Non-Goals

- No online pixel streaming in this Epic.
- No second WebGL/Three world renderer for in-session editing.
- No fallback to fake navmesh, fake green planes, or JS-computed paths.
- No runtime dependency on `Ludots.Editor.Bridge` for save.
