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

Transport network editor run:

```powershell
dotnet run --project src/Tools/Ludots.Launcher.Cli/Ludots.Launcher.Cli.csproj -- launch preset:live_map_editor_transport_network_cef_raylib --adapter raylib --build auto
```

## Manual UAT

| Step | Operation | Expected feedback |
|---|---|---|
| 1 | Launch `live_map_editor_nav_grid_cef_raylib` | Raylib opens `live_editor_nav_grid`, a child of the existing core `nav_editor_grid`; no separate Three.js editor is involved |
| 2 | Press `F4` | CEF overlay panel appears in `UiSurfaceSegment.Overlay` |
| 3 | Inspect the panel | Map id, board list, terrain dimensions, entity count, nav runtime, loaded/pending tiles are visible |
| 4 | Choose Paint, adjust brush, left-drag the Raylib viewport | Core grid `LogicTerrainField` cells change; brush ring and dirty AABB state update |
| 5 | Press Rebake Dirty | Runtime-incremental CDT queue processes dirty tiles; panel shows rebuilt/failed/pending counts |
| 6 | Choose Sim, left-click start and right-click goal in the viewport | `NavQueryService.TryFindPath` runs; Raylib draws start/goal and the query path; panel shows query time in microseconds |
| 7 | Choose Entity, place/select/remove at the picked point | ECS map entity state changes and the DataPlane inspector refreshes |
| 8 | Press Save | `MapAuthoringAssetWriter` writes the authoring map fragment, `.ltrn`, entities, and loaded nav tiles without Bridge HTTP |
| 9 | Relaunch the same preset | Saved terrain/entity/nav authoring changes remain present |

## Entity Placement Rules

`placeEntity` is intentionally narrow: it uses an existing template id and the focused map session. The editor does not create private entity templates, private geometry, or placeholder render data.

| Rule | Expected behavior |
|---|---|
| Template source | The template must already be provided by the loaded mod stack; unknown templates fail through the normal spawn path |
| Map ownership | The spawned entity receives the focused map id and is tracked through `MapEntity` / `MapLoadEntityIndex` |
| Selection/remove safety | Removal uses the selected `PresentationStableId` plus ECS generation validation before mutating state |
| Selection geometry | Selection remains a consumer of `SpatialBounds` / `SpatialFootprint2D` / `SpatialBox3D`; the editor does not author `SelectionFootprint2D` or any selection-only footprint |
| Obstacle geometry | Obstacle truth remains `ManifestationObstacleIntent2D` / `CompoundObstacle2D`; derived `Collider2D`, `NavObstacle2D`, and `CompoundObstacle2DState` are not hand-written by the editor |
| Rendering truth | Raylib shows the real spawned presentation asset; the Web UI only reports inspector state |

## Configuration

The preset stacks three selectors:

| Selector | Purpose |
|---|---|
| `$browser_cef_runtime` | Provides the CEF browser runtime service used by the overlay panel |
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

## Transport Network Follow-Up Boundary

This #451 UAT proves the shared in-session editor shell: CEF DataPlane controls, Raylib authoritative viewport picking/drawing, Core command handling, runtime CDT nav rebake, path overlay, and in-process save for grid terrain/entities/nav tiles.

Transport network editing is a separate follow-up Epic: [#462](https://github.com/MightyBubble/Ludots/issues/462). It should reuse the same shell, but its authoring source is `TransportNetworkAsset`, not `LogicTerrainField` or a private road model. A transport UAT must prove:

| Step | Required production path |
|---|---|
| Launch | Use `preset:live_map_editor_transport_network_cef_raylib` so `BrowserCefRuntimeMod`, `CapabilityStandardTransportNetworkMod`, and `LiveMapEditorMod` are stacked together |
| Node edits | Add/select/update/move/delete nodes against `TransportNetworkAsset`; node rename rewrites segment node references before Core validation |
| Segment edits | Draft/commit segment geometry, update area/tag/direction/flow/capacity fields, and insert/move/delete points against `TransportNetworkAsset` |
| Graph/ribbon refresh | Run `TransportNetworkBaker.Bake(asset, chunkSizeCm)` and publish graph chunks plus `TransportNetworkRibbonSource` ribbon chunks |
| Rendering | Draw authoritative ribbon through `TransportNetworkRibbonSource` -> `SurfaceSourcePayloadRegistry` -> Core/Raylib presentation; `RoadSplineBuffer` remains renderer-facing where configured |
| Route simulation | Select an agent profile in the panel, then use Transport Route mode: left click start, right click goal; Core uses `GraphEdgeProjectionQuery` / `PolylineGoalSnapQuery` and `AutoPathService` / `PathServiceRouter` |
| Save | Write `TransportNetwork/transport_network.json` and catalog registration, then reload through `TransportNetworkAssetLoader` |

The `CapabilityStandardTransportNetworkMod` stack contributes three transport route agent types for this UAT:

| Agent type | Profile | Expected route behavior |
|---|---|---|
| `Transport.FootScout` | `draftCm=0`, `beamCm=0`, forbids `Transport.Area.Water` | Uses the `Transport.Area.Crossing` leg and rejects river/deep-water edges |
| `Transport.ShallowBoat` | `draftCm=100`, `beamCm=300`, requires `Transport.Area.Water` | Can use the shallow river and deep channel; upstream flow is more expensive |
| `Transport.DeepDraftShip` | `draftCm=500`, `beamCm=1200`, requires `Transport.Area.Water` | Cannot use the shallow river because capacity is below draft/beam, so it routes through deep water |

The live map editor must not count a JS spline preview, a hand-authored `.graph`, a fake road mesh, or a custom route solver as transport-network evidence.
