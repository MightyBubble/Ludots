# Navmesh Authoring Bake Toolchain

Parent: [Epic #281](https://github.com/MightyBubble/Ludots/issues/281). Related follow-ups: [NAV-11 #369](https://github.com/MightyBubble/Ludots/issues/369), [NAV-14 #372](https://github.com/MightyBubble/Ludots/issues/372), [NAV-15 #373](https://github.com/MightyBubble/Ludots/issues/373). Scale SSOT: [Spatial Scale and Resolution SSOT](../architecture/spatial-scale-and-resolution-ssot.md). Budget model: [Nav Bake Budget and Estimation](nav-bake-budget-and-estimation.md).

This page is the product and engineering contract for a real Ludots nav authoring toolchain. It is not a temporary editor branch, a private loader, or a script stub.

## Current Boundary

The production target is:

```text
Board -> VisualHeightmap -> LogicTerrain -> NavMesh
```

Current code now has the shared bake route for CLI and Editor Bridge. Remaining work is concentrated on the terrain-classification SSOT, strict visual/logic single-source enforcement, and a dedicated Raylib navmesh debug view:

| Area | Current status | Contract for the next implementation |
|---|---|---|
| Grid / HexGrid production bake | CLI `nav estimate-recast-react` / `nav bake-recast-react` and Editor Bridge resolve the primary navigation board and choose `MutableGridLogicTerrainField` for `Grid`, `VertexMapLogicTerrainField` for `HexGrid`; `NodeGraph` fails fast because it does not bake navmesh | Keep all production bake adapters on `NavBakeContext` + `NavBakeService`; do not reintroduce a topology-private bake entry |
| Terrain classification | React terrain stride-4 preserves `areaId` and blocked flags into logic terrain and nav bake; `LogicTerrainCell.Cost` still exists as a design smell | NAV-14 must move terrain classification to map/terrain SSOT and keep cost as per-agent consumer data |
| Pipeline order | Visual and logic terrain can still drift in authoring shape | NAV-15 must enforce `Board -> VisualHeightmap -> LogicTerrain -> NavMesh` and fail on conflicting independent sources |
| Editor surface | React editor can paint height, blocked cells, `areaId`, obstacles, agent/profile/layer config, estimate bake cost, and call Bridge Recast bake through the shared context | The final product should persist the official VisualHeightmap/terrain-classification assets once NAV-15 closes the single-source contract |
| Raylib debug | Debug draw and primitive buffers exist, but navmesh inspection needs a dedicated accurate view | Raylib must render cached tile geometry from `NavTile` data, not frame-by-frame disconnected line commands |

## Reuse List

Any implementation of this page must reuse:

| Capability | Owner |
|---|---|
| World units and chunk names | `SpatialScaleDefaults`, `WorldExtentSpec`, `WorldSizeSpec`, `MapTile.Size` |
| Bake request | `NavBakeContext` and `NavBakeService` |
| Agent geometry | `Navigation/agent_profiles.json` through `AgentProfileRegistry` |
| Bake profile | `Navigation/navmesh.json` through `NavMeshBakeConfig` |
| Pathing cost | `Navigation/pathing.json` through `PathingConfig` |
| Obstacles | `ManifestationObstacleIntent2D` + `ShapeDataStorage2D` + `CompoundObstacle2DState` |
| Terrain input | official `VisualHeightmap` projection to official `LogicTerrainField` |
| Runtime query | `NavTileStore`, `NavQueryServiceRegistry`, `NavQueryService` |
| Presentation/debug | `PresentationPrimitiveDrawBuffer`, `GroundOverlayBuffer`, `DebugDrawCommandBuffer` where appropriate |

Do not add a second config loader, second obstacle file, direct navmesh authoring source, private `.lhtm` lane, casing alias, or fallback mesh generator.

## Authoring Flow

The target editor flow is:

1. Create a board: choose `Grid`, `HexGrid`, or `NodeGraph`; set `WidthInMacroTiles`, `HeightInMacroTiles`, `GridCellSizeCm`, and `ChunkSizeCells`.
2. Paint terrain height on `VisualHeightmap` in centimeters.
3. Paint terrain classification as `areaId` and optional tags on the visual/terrain authoring layer.
4. Draw static structural obstacles as official ECS authored shapes: circle, box, polygon, or compound.
5. Configure agent geometry in `Navigation/agent_profiles.json`.
6. Configure bake profiles and layers in `Navigation/navmesh.json`.
7. Configure per-agent traversal cost and route preference in `Navigation/pathing.json`.
8. Run estimate using the same inputs that real bake will use.
9. Bake through CLI or Editor Bridge into `.ntil` nav tiles.
10. Inspect the produced mesh in Web editor and Raylib debug view.
11. Start runtime, select agents, issue movement, and verify route + MassNavigationFlow execution.

`NodeGraph` boards use the short graph path and do not bake navmesh.

## Parameter Taxonomy

### Units

Ludots world-space authoring uses centimeters. This matches the Unreal-style convention where `100cm = 1m`. Recast still consumes meters internally, so adapters convert centimeters with `/ 100f`. Do not expose Recast meters as a separate authoring unit.

| Parameter | Unit | Source | Meaning | Required |
|---|---:|---|---|---|
| `CellCm` / `GridCellSizeCm` | cm | board config | Physical size of one logical board cell | Yes |
| `VisualHeightmap.heightCm` | cm | terrain authoring | Continuous rendered/authoring height | Yes for visual terrain |
| `LogicTerrain.heightLevel` | discrete level | projected logic terrain | Compact logic height after projection | Yes for nav bake |
| `heightScaleMeters` | m per logic height unit | `NavBuildConfig` / runtime incremental config | Legacy conversion from height level to meters | Required today; target should be derived from projection profile |
| `areaId` | integer key | terrain classification SSOT | Terrain class id; no cost embedded | Yes when terrain classification is enabled |
| tags | tag bits / strings | terrain classification SSOT | Orthogonal terrain attributes such as forest, road, swamp | Optional, explicit when used |

### Agent Geometry

`Navigation/agent_profiles.json` owns shared geometry and avoidance identity.

| Field | Unit | Bake meaning | Runtime meaning |
|---|---:|---|---|
| `radiusCm` | cm | clearance radius for Recast erosion and route passability | MassNavigationFlow body radius / spacing |
| `heightCm` | cm | minimum vertical clearance for Recast walkable spans | visual/body metadata, not speed |
| `clearanceCm` | cm | extra authored clearance budget; must be explicit when applied to bake/profile policy | route/passability metadata |
| `mass` | scalar | not a bake knob | MassNavigationFlow resolve share / dominance |
| `layer` | integer | selects nav query layer for this profile | runtime pathing layer identity |

Speed stays out of `AgentProfileRegistry`; it belongs to MassNavigationFlow movement strategy.

### Bake Profile

`Navigation/navmesh.json` owns bake-only profile constraints.

| Field | Unit | Current consumer | Design rule |
|---|---:|---|---|
| `profiles[].id` | profile id | `NavMeshProfileRegistry` | Must reference an `AgentProfile` id exactly |
| `profiles[].maxClimbCm` | cm | Recast `walkableClimb`; CDT/logic filtering target | Maximum step/climb height for this agent profile |
| `profiles[].maxSlopeDeg` | degrees | Recast `walkableSlopeAngle`; target source for CDT slope filtering | Authoring-facing slope knob |
| `layers[].id` | string | obstacle carve + artifact path | Semantic layer id, strict casing |
| `layers[].layer` | int | `NavTileId.Layer` | Stored layer index |
| `runtimeIncremental.tileBudgetPerFixedTick` | tiles | runtime rebuild queue | Publish pacing, not bake quality |

Current CDT/runtime legacy knobs are still present:

| Legacy field | Meaning | Target direction |
|---|---|---|
| `minWalkableUpDot` | normal-up dot threshold | Derive from `maxSlopeDeg` as `cos(maxSlopeDeg)` when CDT slope filtering is normalized |
| `cliffHeightThreshold` | discrete height delta threshold | Replace or derive from `maxClimbCm` through projection metadata |
| `heightScaleMeters` | level-to-meter conversion | Keep explicit until `VisualHeightmap -> LogicTerrain` projection profile owns quantization |

Do not expose both `maxSlopeDeg` and `minWalkableUpDot` as independent user-facing knobs in the final editor. Show the derived value for debugging, but make `maxSlopeDeg` the authoring source.

### Recast Derived Parameters

Current `RecastNavTileBaker` derives:

```text
recastCellSizeCm   = clamp(agentRadiusCm / 3, 5, 50)
recastCellHeightCm = recastCellSizeCm * 0.5
walkableHeightVoxels = ceil(agentHeightCm / recastCellHeightCm)
walkableClimbVoxels  = floor(maxClimbCm / recastCellHeightCm)
```

The fixed Recast detail parameters are documented in [Nav Bake Budget and Estimation](nav-bake-budget-and-estimation.md). If any become configurable, they must move through an explicit bake profile and enter the estimate hash.

### Area Cost And Blocking

Terrain classification and traversal cost are separate:

| Concept | Source | Rule |
|---|---|---|
| `areaId` / tags | terrain SSOT | Stored on terrain/polygons/cells and propagated into `NavTile.TriAreaIds` |
| per-agent cost | `Navigation/pathing.json` | `agentTypes[].navMesh.areaCosts[]` maps `areaId -> cost` for that agent type |
| impassable terrain | explicit block policy | Prefer explicit blocked/forbidden rule over fake infinite cost |
| obstacle layer | authored obstacle sink + nav layer | Obstacles block only matching configured layer id |

Example pathing snippet:

```json
{
  "agentTypes": [
    {
      "id": "infantry",
      "profileId": "light",
      "selection": {
        "mode": "PreferMesh",
        "graphBias": 0,
        "meshBias": 0,
        "graphCostWeight": 1,
        "meshCostWeight": 1
      },
      "navMesh": {
        "areaCosts": [
          { "areaId": 1, "cost": 1.0 },
          { "areaId": 2, "cost": 2.4 },
          { "areaId": 3, "cost": 8.0 }
        ]
      },
      "nodeGraph": {
        "projectionMaxRadiusCm": 200000,
        "requiredTagsAll": [],
        "forbiddenTagsAny": [],
        "tagCostRules": []
      }
    }
  ]
}
```

## CLI Cookbook

Current production commands:

| Command | Current use | Notes |
|---|---|---|
| `nav estimate-recast-react` | estimate Recast bake cost from React editor `map_data.bin` | Resolves `mapId`/`modId`, chooses grid or hex logic terrain by board topology, prints budget and `estimateHash` |
| `nav bake-recast-react` | bake Recast nav tiles from React editor `map_data.bin` | Uses the same `NavBakeContext`; large bakes require explicit approval and matching `estimateHash` |
| `nav bake` | legacy `.vtxm` bake path | Kept for existing VertexMap fixtures; unified config is still required |
| `nav bake-react` | old CDT preview endpoint | Refuses generated defaults; use Recast path for production artifacts |

The final NAV-15 asset pipeline should rename the React upload path once official VisualHeightmap/classification persistence lands, but the bake semantics already go through the shared service.

### 1. Estimate Before Bake

```powershell
dotnet run --project src/Tools/Ludots.Tool/Ludots.Tool.csproj -- `
  nav estimate-recast-react `
  --mapId nav_domain_showcase `
  --modId NavDomainShowcaseMod `
  --in artifacts/editor/map_data.bin `
  --dirty artifacts/editor/dirty_chunks.json `
  --includeNeighbors true `
  --parallel true `
  --maxDegree 8
```

Expected report fields:

| Field | Meaning |
|---|---|
| `TerrainWidthCells` / `TerrainHeightCells` | derived from board scale and editor terrain input |
| `targetTileCount` | full/dirty/window target count |
| `LayerCount` / `ProfileCount` | multiplier lists after strict lookup |
| `ObstacleCount` | obstacle authoring consumed from the shared manifest/shape data |
| `Profiles[].RecastCellSizeCm` | derived voxel size |
| `Profiles[].WalkableHeightVoxels` | derived height clearance |
| `Profiles[].WalkableClimbVoxels` | derived climb budget |
| `EstimatedSecondsLow/High` | planning band estimate |
| `BudgetStatusText` | `ok`, `large`, or `reject` |
| `EstimateHash` | input hash used by later bake confirmation |

### 2. Bake A Local Editing Window

```powershell
dotnet run --project src/Tools/Ludots.Tool/Ludots.Tool.csproj -- `
  nav bake-recast-react `
  --mapId nav_domain_showcase `
  --modId NavDomainShowcaseMod `
  --in artifacts/editor/map_data.bin `
  --dirty artifacts/editor/dirty_chunks.json `
  --includeNeighbors true `
  --parallel true `
  --maxDegree 8 `
  --estimateHash <hash-from-estimate>
```

Expected feedback:

| Output | Requirement |
|---|---|
| `ok=<N> fail=0` | No fallback tile count |
| artifact paths | `assets/Data/Nav/<mapId>/layer<layer>/profile_<profile>/...` |
| measured timing | p50/p90 ms per operation |
| input hash | must match the estimate unless explicitly re-estimated |

### 3. Bake Dirty Tiles After Editor Changes

```powershell
dotnet run --project src/Tools/Ludots.Tool/Ludots.Tool.csproj -- `
  nav bake-recast-react `
  --mapId nav_domain_showcase `
  --modId NavDomainShowcaseMod `
  --in artifacts/editor/map_data.bin `
  --dirty artifacts/editor/dirty_chunks.json `
  --includeNeighbors true `
  --parallel true
```

Missing dirty file, unknown profile, unknown layer, or casing mismatch must fail before writing partial output.

### 4. Full Bake With Explicit Large Gate

```powershell
dotnet run --project src/Tools/Ludots.Tool/Ludots.Tool.csproj -- `
  nav bake `
  --mapId world_64km `
  --board default `
  --target full `
  --profiles light `
  --layers ground `
  --large-bake `
  --maxDegree 8
```

`--large-bake` is a cost guard, not a fallback. Without it, a large estimate must stop before output writes.

### 5. Inspect Artifacts

```powershell
dotnet run --project src/Tools/Ludots.Tool/Ludots.Tool.csproj -- `
  nav inspect `
  --mapId nav_domain_showcase `
  --profile light `
  --layer ground `
  --chunkX 12 `
  --chunkY 8
```

Expected report:

| Field | Meaning |
|---|---|
| `triangleCount` / `vertexCount` | mesh size |
| `areaIds` | propagated `TriAreaIds` distribution |
| `portalCount` | tile connectivity |
| `boundsCm` | exact world-space tile bounds |
| `buildHash` | bake input hash |

## Web Editor Design

The web editor must be a production authoring surface with Bridge-backed persistence. It should not be a pile of one-off JSON textareas.

### Main Views

| View | Purpose |
|---|---|
| Board setup | create/select board, show world size, chunk grid, FlowWindow fit |
| Terrain height | paint `VisualHeightmap.heightCm` with raise/lower/smooth/flatten/ramp brushes |
| Terrain area | paint `areaId` and tags with a palette and legend |
| Obstacles | draw circle/box/polygon/compound shapes, assign nav/physics sinks and layer id |
| Agents | edit geometry profiles, bake constraints, and pathing cost matrix side by side |
| Bake | estimate, select full/dirty/window target, run bake through Bridge, show artifacts |
| Inspect | click terrain cell / obstacle / nav polygon and show source chain |

### Tools

| Tool | Required controls |
|---|---|
| Height brush | mode, radius, strength, target height cm, ramp endpoints, smoothing iterations |
| Area brush | area palette, tag toggles, fill polygon, replace area, show selected cells |
| Obstacle brush | circle, rotated box, polygon, compound piece editor, nav layer selector |
| Agent matrix | rows = agent types, columns = area ids/tags, cells = cost/block |
| Bake target | full, dirty, window; tile rectangle picker; layer/profile filters |
| Inspector | source cell, visual height, logic height, area id, tags, slope, obstacle hits, nav triangle id |

### Data Contract

The editor writes only official data:

```text
MapConfig.Boards[]
VisualHeightmap asset + terrain area annotations
ECS obstacle authoring components
Navigation/agent_profiles.json
Navigation/navmesh.json
Navigation/pathing.json
```

The Bridge builds the same `NavBakeContext` as CLI. It must not:

- load a private editor-only heightmap format as the bake source;
- write navmesh directly as authored state;
- silently invent layers/profiles/areas;
- accept casing aliases;
- run a second bake algorithm path outside `NavBakeService`.

### Interaction Contract

| Action | Expected feedback |
|---|---|
| Paint height | visible terrain changes; dirty terrain chunks and dirty nav tiles are marked |
| Paint area id | area overlay changes; area legend count updates; affected nav tiles become dirty |
| Draw obstacle | exact shape appears; affected nav tiles become dirty; obstacle source inspector points to ECS component data |
| Change `radiusCm` | estimate updates Recast voxel size; passability preview invalidates relevant profiles |
| Change `maxSlopeDeg` | slope overlay threshold updates; derived `minWalkableUpDot` shown read-only |
| Mark area blocked for heavy agent | heavy route preview avoids it; light route can still cross if cost allows |
| Run bake | Bridge returns artifact list, timings, failures, and tile bytes/paths |
| Click nav triangle | inspector shows tile id, triangle id, area id, layer, profile, vertices, neighbors, portals |

### Web Editor Performance

| Concern | Required design |
|---|---|
| Large terrain | chunked storage, visible-window loading, typed arrays for height/area layers |
| Brush feedback | worker-side brush stamping with main-thread preview overlay |
| Dirty tracking | tile invalidation by `TerrainChunkCells` footprint plus optional neighbor expansion |
| Bake results | stream result summaries first, tile geometry on demand |
| Mesh preview | cache tile meshes by `(mapId, board, layer, profile, chunkX, chunkY, tileVersion)` |
| Area palette | store area dictionary once, reference ids in cells |
| Huge full bake | estimate gate before submission; no browser tab should own full-bake memory |

## Raylib Runtime Debug View

Raylib needs a precise navmesh debug view for production runtime investigation. It should be implemented as an adapter-side presentation mode that reads Core services, not as gameplay logic.

### Inputs

| Source | Use |
|---|---|
| `NavTileStore` | tile geometry and portals |
| `NavQueryServiceRegistry` | active layer/profile query services |
| `NavTile.TriAreaIds` | area coloring |
| `Navigation/pathing.json` | per-agent cost legend |
| `PathStore` / route output | selected route overlay |
| `MassNavigationFlowSolverState` | runtime agent movement overlay |

### Drawing Model

Render filled triangles first, then stable edges, then portals/routes/selection:

1. Build a per-tile mesh from `NavTile.VertexXcm/Ycm/Zcm` and `TriA/B/C`.
2. Color triangles by `TriAreaIds` and optionally multiply by agent cost.
3. Build an edge table from quantized world-space vertex pairs.
4. Draw boundary edges once; draw internal edges faintly or hide them.
5. Draw portals as directed edge markers.
6. Draw selected route as a continuous polyline on top of mesh.

Do not draw every triangle edge as independent debug lines every frame. That is the usual reason navmesh overlays look broken or noisy.

Edge key:

```text
edgeKey = sort(
  quantize(worldXcmA, worldYcmA, worldZcmA),
  quantize(worldXcmB, worldYcmB, worldZcmB))
```

Use centimeter integer coordinates from `NavTile` when possible. If a renderer path must use floats, quantize before edge dedupe so adjacent triangles share an edge.

### Filters And UI

| Control | Behavior |
|---|---|
| profile selector | switches agent profile and area cost coloring |
| layer selector | switches nav layer |
| tile bounds | shows/hides `TerrainChunk` / `NavTile` footprint rectangles |
| triangle fill | area id, cost heatmap, or disabled |
| edge mode | boundary only, boundary + portals, all edges |
| path overlay | selected unit route, last query route, or off |
| source overlay | terrain height, slope fail, obstacle carve, area id |
| performance panel | visible tiles, triangles, edge count, mesh cache hit rate, draw ms |

### Raylib Performance

| Requirement | Design |
|---|---|
| No per-frame rebuild | build tile debug mesh once per tile revision |
| Cache key | `(mapId, boardId, layer, profileId, chunkX, chunkY, tileVersion, displayMode)` |
| Culling | camera frustum / screen rect cull before draw |
| Batching | batch filled triangles by area/material; batch edges by color/style |
| Edge rendering | indexed line list or thin quads from deduped edges |
| Memory cap | LRU debug mesh cache by visible radius and max tile count |
| Backpressure | cap debug primitives and print dropped counts in debug panel |

## UAT Showcase

Target preset: `nav_authoring_toolchain`.

```powershell
.\scripts\run-mod-launcher.cmd cli launch nav_authoring_toolchain --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch nav_authoring_toolchain --adapter web
```

| Operation | Visible feedback |
|---|---|
| Paint a hill and a ramp in the web editor | height overlay changes; slope overlay marks the ramp walkable and the cliff blocked for the selected profile |
| Paint `areaId=forest` and `areaId=road` | area legend count changes; inspector shows area id on clicked cells |
| Draw a polygon obstacle on `ground` | obstacle appears in editor; dirty nav tiles include the polygon footprint |
| Configure `light` and `heavy` agents | light and heavy radius/height/climb/slope values appear in estimate report |
| Mark forest high-cost for heavy | heavy route preview avoids forest; light route may cross it |
| Bake window target | `.ntil` artifacts are produced through Bridge/CLI shared context; no fallback artifacts |
| Launch Raylib debug view | filled triangles align to terrain; boundary edges are continuous; selected route is one coherent polyline |
| Change `maxSlopeDeg` from 45 to 25 | rebake changes walkable ramp area and path route; test fails if artifact hash is unchanged |

## DoD

- Web editor writes official map, terrain, obstacle, agent, navmesh, and pathing configs.
- CLI and Bridge use one `NavBakeContext` and one `NavBakeService`.
- `maxSlopeDeg`, `maxClimbCm`, `radiusCm`, `heightCm`, and `clearanceCm` have one authoring owner each.
- `areaId` / tags are terrain classification SSOT; cost is per-agent/pathing data.
- Runtime incremental rebuild remains `runtime-incremental` + `cdt` only.
- Raylib debug view renders from `NavTile` geometry with cached meshes and deduped edges.
- No private loader, no fallback mesh, no duplicate obstacle source, no casing aliases.
- Contract tests cover strict config, source DAG direction, grid production bake, area propagation, per-agent cost behavior, and debug mesh edge dedupe.
