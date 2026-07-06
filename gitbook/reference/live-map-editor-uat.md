# Live Map Editor UAT

Epic: [#451](https://github.com/MightyBubble/Ludots/issues/451). This runbook validates the in-session editor path: launch-time capability mod, CEF DataPlane control panel, Raylib authoritative viewport, runtime-incremental CDT rebake, path query, and in-process save.

## Commands

```powershell
dotnet build mods/capabilities/live_map_editor/LiveMapEditorMod/LiveMapEditorMod.csproj
```

```powershell
dotnet run --project src/Tools/Ludots.Launcher.Cli/Ludots.Launcher.Cli.csproj -- resolve preset:live_map_editor_nav_grid_cef_raylib --adapter raylib --build never --json
```

```powershell
dotnet run --project src/Tools/Ludots.Launcher.Cli/Ludots.Launcher.Cli.csproj -- build preset:live_map_editor_nav_grid_cef_raylib --adapter raylib --build auto
```

Headed run:

```powershell
dotnet run --project src/Tools/Ludots.Launcher.Cli/Ludots.Launcher.Cli.csproj -- launch preset:live_map_editor_nav_grid_cef_raylib --adapter raylib --build auto
```

Integrated terrain + navmesh + transport run:

```powershell
dotnet run --project src/Tools/Ludots.Launcher.Cli/Ludots.Launcher.Cli.csproj -- launch preset:live_map_editor_integrated_nav_transport_cef_raylib --adapter raylib --build auto
```

Transport-only NodeGraph debugging run:

```powershell
dotnet run --project src/Tools/Ludots.Launcher.Cli/Ludots.Launcher.Cli.csproj -- launch preset:live_map_editor_transport_network_cef_raylib --adapter raylib --build auto
```

## Manual UAT

| Step | Operation | Expected feedback |
|---|---|---|
| 1 | Launch `live_map_editor_nav_grid_cef_raylib` | Raylib opens `live_editor_nav_grid`, a child of the existing core `nav_editor_grid`; no separate Three.js editor is involved |
| 2 | Press `F4` | CEF panel appears in `UiSurfaceSegment.Main` with an exclusive Phase 2 editor lease |
| 3 | Inspect the panel | Map id, board list, terrain dimensions, entity count, nav runtime, loaded/pending tiles are visible |
| 4 | Choose Paint, adjust brush, left-drag the Raylib viewport | Core grid `LogicTerrainField` cells change; brush ring and dirty AABB state update |
| 5 | Press Rebake Dirty | Runtime-incremental CDT queue processes dirty tiles; panel shows rebuilt/failed/pending counts |
| 6 | Choose Sim, left-click start and right-click goal in the viewport | `NavQueryService.TryFindPath` runs; Raylib draws start/goal and the query path; panel shows query time in microseconds |
| 7 | Choose Entity, place/select/remove at the picked point | ECS map entity state changes and the DataPlane inspector refreshes |
| 8 | Press Save | `MapAuthoringAssetWriter` writes the authoring map fragment, `.ltrn`, entities, and loaded nav tiles without Bridge HTTP |
| 9 | Relaunch the same preset | Saved terrain/entity/nav authoring changes remain present |

## Phase 2 Parity Checks

Phase 2 is governed by [ADR-0005](../../docs/adr/ADR-0005-live-map-editor-phase2-parity-boundary.md). It expands the panel without changing the authoring SSOT.

| Step | Operation | Expected feedback |
|---|---|---|
| 1 | Launch `live_map_editor_nav_grid_cef_raylib`, press `F4` | The top bar reads the focused `state.map?.id`; a no-map state would show `No map` instead of a stale id |
| 2 | Open Paint controls, switch Set/Raise/Lower and targets | `setBrush` updates mode/target/radius/height/water height/area/cost/blocked/ramp in the DataPlane state |
| 3 | Paint height/area/blocked/ramp and use Water Bucket | Grid `LogicTerrainCell.HeightLevel` / `WaterHeightLevel` / `AreaId` / `SurfaceFlags` update; Raylib shows height shading, water, area tint, blocked tint, and ramp/cliff edge lines; nav dirty AABB is queued |
| 4 | Use Navigation Scope Dirty, Dirty+N, and Full | `estimateNavBake` reports tile counts; `rebakeNav` processes only `RuntimeIncrementalNavMeshRebuildQueue`; `clearNavTiles` clears `NavTileStore` |
| 5 | Change Path profile/layer and query | `NavQueryServiceRegistry.TryCreateQuery(layer, profile, ...)` is used; Max Portals is passed to `NavQueryService.TryFindPath` |
| 6 | Toggle grid/chunks/navmesh/path/transport/entities/minimap | Core/Raylib overlay producers stop writing the disabled layers; minimap hides when disabled and highlights dirty chunks when terrain/nav edits mark a dirty AABB |
| 7 | Inspect entity template entry | Template suggestions come from `EntityTemplateKeyRegistry.SnapshotMappings()` |
| 8 | Select an entity, edit an override JSON component, then Save | `MapConfig.Entities[].Overrides` contains the edited component payload after `MapAuthoringAssetWriter` writes the map |
| 9 | Switch to Obstacle, place Circle/Box/Polygon and erase one | Authored `ManifestationObstacleIntent2D` / `ManifestationObstaclePolygon2D` map entities appear/disappear; derived physics/nav sinks remain bridge-owned |
| 10 | Use the top-bar `New Map` command, preview allocation, then `Create & Load` | A new `assets/Maps/<map>.json` is written to the current writable authoring mod and `GameEngine.LoadMap` focuses the created map |
| 11 | Add a board, update its cell/hex/nav flag, then reload | Preview numbers come from Core `SpatialScaleDefaults`; `MapConfig.Boards[]` is saved, loaded nav stores are invalidated, and `GameEngine.LoadMap` reloads the authored board stack |
| 12 | Launch a map without a `NodeGraph` board | Transport tab and controls are disabled and the panel shows the no-NodeGraph status |

Phase 2 does not revive Bridge HTTP. The React/Three.js editor stays as the offline path and is not a runtime dependency for these checks.

### Terrain Field SSOT

| Concept | Core field | Result |
|---|---|---|
| Height | `LogicTerrainCell.HeightLevel` | editable |
| Water | `LogicTerrainCell.WaterHeightLevel` + `SurfaceFlags.Water` | editable |
| Territory / area | `LogicTerrainCell.AreaId` | editable |
| Cost | `LogicTerrainCell.Cost` | editable |
| Blocked / ramp | `LogicTerrainCell.SurfaceFlags` | editable |
| Biome / vegetation / snow / mud / ice / layers | no live Grid `LogicTerrainCell` field | deferred; no private Web UI field |

Raylib WYSIWYG terrain rendering is intentionally sourced from Core. `LogicTerrainVisualHeightmapAdapter` exposes height samples through `IVisualHeightmapRenderSource` and exposes water/area/blocked/ramp styling through `IVisualTerrainRenderFeatureSource`; the Web UI does not draw a 3D terrain copy and biome/vegetation remain out of scope until Core owns those fields.

## Entity Placement Rules

`placeEntity` is intentionally narrow: it uses an existing template id and the focused map session. The editor does not create private entity templates, private geometry, or placeholder render data.

| Rule | Expected behavior |
|---|---|
| Template source | The template must already be provided by the loaded mod stack; unknown templates fail through the normal spawn path |
| Map ownership | The spawned entity receives the focused map id and is tracked through `MapEntity` / `MapLoadEntityIndex` |
| Selection/remove safety | Removal uses the selected `PresentationStableId` plus ECS generation validation before mutating state |
| Selection geometry | Selection remains a consumer of `SpatialBounds` / `SpatialFootprint2D` / `SpatialBox3D`; the editor does not author `SelectionFootprint2D` or any selection-only footprint |
| Obstacle geometry | Obstacle truth remains `ManifestationObstacleIntent2D` / `CompoundObstacle2D`; derived `Collider2D`, `NavObstacle2D`, and `CompoundObstacle2DState` are not hand-written by the editor |
| Component overrides | Selected entity overrides are edited as strict JSON and saved into `MapConfig.Entities[].Overrides`; invalid JSON fails before command dispatch |
| Rendering truth | Raylib shows the real spawned presentation asset; the Web UI only reports inspector state |

## Configuration

The preset stacks three selectors:

| Selector | Purpose |
|---|---|
| `$browser_cef_runtime` | Provides the CEF browser runtime service used by the editor panel |
| `$live_map_editor_nav_grid_uat` | Resource-only UAT selector that opens `live_editor_nav_grid`, inheriting the existing core `nav_editor_grid` map and adding `Feature.NavMesh:On` plus an explicit live-editor save target |
| `$live_map_editor` | Capability mod containing the panel, command lane, viewport overlays, and save command |

The editor mod contributes `assets/Configs/Navigation/navmesh.json`:

| Field | Required value for this Epic |
|---|---|
| `mode` | `runtime-incremental` |
| `algorithm` | `cdt` |
| `layers` | Single ground layer for this UAT |
| `runtimeIncremental.tileBudgetPerFixedTick` | Budget for live dirty-tile processing |

Live rebake is not Recast. Recast remains an offline full-bake toolchain option outside the live editor loop.

The UAT grid boards intentionally omit `DataFile`. Core therefore creates flat grid `LogicTerrainField` instances, and the editor saves the first edited grid terrain as `.ltrn`.

Map/Board lifecycle commands also write through the authoring map target. `Create Map` only writes the asset; `Create & Load` writes the same asset and immediately focuses it through `GameEngine.LoadMap`. `BoardConfig.NavigationEnabled` is persisted as board authoring data, but these controls do not automatically add `Feature.NavMesh:On`; that global tag is still owned by maps/nav assets that already have the required `.ntil` load contract.

The Raylib board guide overlay must align to the same authoring surface as the terrain. For grid `LogicTerrainField` maps this is the terrain's 0-based cell extent, not the centered `WorldSizeSpec.Bounds` used by spatial services.

## Integrated Terrain/Nav/Transport UAT

`preset:live_map_editor_integrated_nav_transport_cef_raylib` is the main #451 + #462 acceptance entry. It opens `live_editor_integrated_nav_transport`, which has:

| Board | Spatial type | Owner | Purpose |
|---|---|---|---|
| `default` | `Grid` | terrain/navmesh | Primary board, live `LogicTerrainField`, VisualHeightmap adapter, runtime-incremental CDT navmesh |
| `transport` | `NodeGraph` | transport network | `TransportNetworkAsset` bake target, `ChunkedNodeGraphStore`, `LoadedGraphRuntime` |

The integrated preset stacks:

| Selector | Purpose |
|---|---|
| `$browser_cef_runtime` | CEF browser runtime |
| `$live_map_editor_integrated_nav_transport_uat` | Resource-only map, camera, pathing profiles, and `TransportNetwork/transport_network.json` |
| `$live_map_editor` | Shared editor shell, command lane, terrain/nav/entity tools, and transport authoring |

Manual integrated acceptance:

| Step | Operation | Expected feedback |
|---|---|---|
| 1 | Launch `live_map_editor_integrated_nav_transport_cef_raylib` | Raylib opens one map with a Grid primary board and a `transport` NodeGraph board |
| 2 | Press `F4`, inspect boards | Panel lists both boards; nav status is available because `Feature.NavMesh:On` is present |
| 3 | Paint terrain, then Rebake Dirty | Grid terrain changes and runtime CDT nav tiles rebuild for the Grid board |
| 4 | Switch to Sim, left-click/right-click | `Humanoid` path uses the navmesh path service |
| 5 | Switch to Transport, inspect asset | `live_editor.integrated_nav_transport` asset shows nodes/segments and baked graph/ribbon counts after load/rebake |
| 6 | Transport Route mode, select `Transport.ShallowBoat`, left-click/right-click near the water network | Core projects endpoints onto the baked NodeGraph and solves through `AutoPathService`/`PathServiceRouter` |
| 7 | Edit a node or segment, then Bake/Save | `TransportNetworkBaker` refreshes graph/ribbon chunks, then `transport_network.json` is saved and reloaded strictly |

The transport-only preset is still useful for a narrow NodeGraph editor debug loop, but it is not evidence that terrain, navmesh, and transport coexist.

## Automated Checks

```powershell
dotnet test src/Tests/ArchitectureTests/ArchitectureTests.csproj --filter "LogicTerrainBinaryContractTests|LiveMapEditorLauncherContractTests"
```

```powershell
dotnet test src/Tests/GasTests/GasTests.csproj --filter MapAuthoringAssetWriterTests
```

## Save Rules

`MapAuthoringAssetWriter` refuses implicit save targets. It writes to a loaded map fragment with `metadata.liveMapEditor.saveTarget=true`; without that explicit flag it writes only when exactly one loaded mod map fragment for the focused map declares authoring boards. Tag-only overlays are ignored as save targets. Multiple board-declaring fragments without a single explicit target fail fast so the user does not accidentally split the map SSOT.

This editor path does not write an independent `.vhtm` asset. If a map has no explicit visual heightmap, Core derives the Raylib terrain surface from the grid LogicTerrain adapter; that derived visual surface is rebuilt live and is not a second authoring source.

## Transport Network Boundary

Transport network editing is the #462 leg inside the same in-session editor shell. Its authoring source is `TransportNetworkAsset`, not `LogicTerrainField` or a private road model. The integrated UAT must prove:

| Step | Required production path |
|---|---|
| Launch | Use `preset:live_map_editor_integrated_nav_transport_cef_raylib` so Grid terrain/navmesh and NodeGraph transport are loaded in one focused map |
| Node edits | Add/select/update/move/delete nodes against `TransportNetworkAsset`; node rename rewrites segment node references before Core validation |
| Segment edits | Draft/commit segment geometry, update area/tag/direction/flow/capacity fields, and insert/move/delete points against `TransportNetworkAsset` |
| Graph/ribbon refresh | Run `TransportNetworkBaker.Bake(asset, chunkSizeCm)` and publish graph chunks plus `TransportNetworkRibbonSource` ribbon chunks |
| Rendering | Draw authoritative ribbon through `TransportNetworkRibbonSource` -> `SurfaceSourcePayloadRegistry` -> Core/Raylib presentation; `RoadSplineBuffer` remains renderer-facing where configured |
| Route simulation | Select an agent profile in the panel, then use Transport Route mode: left click start, right click goal; Core uses `GraphEdgeProjectionQuery` / `PolylineGoalSnapQuery` and `AutoPathService` / `PathServiceRouter` |
| Save | Write `TransportNetwork/transport_network.json` and catalog registration, then reload through `TransportNetworkAssetLoader` |

The integrated UAT contributes three transport route agent types:

| Agent type | Profile | Expected route behavior |
|---|---|---|
| `Transport.FootScout` | `draftCm=0`, `beamCm=0`, forbids `Transport.Area.Water` | Uses the `Transport.Area.Crossing` leg and rejects river/deep-water edges |
| `Transport.ShallowBoat` | `draftCm=100`, `beamCm=300`, requires `Transport.Area.Water` | Can use the shallow river and deep channel; upstream flow is more expensive |
| `Transport.DeepDraftShip` | `draftCm=500`, `beamCm=1200`, requires `Transport.Area.Water` | Cannot use the shallow river because capacity is below draft/beam, so it routes through deep water |

The live map editor must not count a JS spline preview, a hand-authored `.graph`, a fake road mesh, or a custom route solver as transport-network evidence.
