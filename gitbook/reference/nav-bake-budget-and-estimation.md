# Nav Bake Budget And Estimation

Parent: [Epic #281](https://github.com/MightyBubble/Ludots/issues/281). Related: [NAV-5 #287](https://github.com/MightyBubble/Ludots/issues/287), [NAV-10 #304](https://github.com/MightyBubble/Ludots/issues/304), [NAV-11 #369](https://github.com/MightyBubble/Ludots/issues/369), [NAV-14 #372](https://github.com/MightyBubble/Ludots/issues/372), [NAV-15 #373](https://github.com/MightyBubble/Ludots/issues/373). Authoring toolchain: [Navmesh Authoring Bake Toolchain](navmesh-authoring-bake-toolchain.md).

This page is a design reference for planning navmesh bake cost before running the production bake tools. It is not a benchmark result. Any real performance claim must come from the current branch, current hardware, current `NavBakeContext`, and a recorded command.

## Pipeline

The target production pipeline is:

```text
Board -> VisualHeightmap -> LogicTerrain -> NavMesh
```

NodeGraph boards use the short graph path and do not bake navmesh. Grid and HexGrid boards bake only when navigation is enabled and a `LogicTerrainField` exists for the board.

Current status note: CLI `nav estimate-recast-react` / `nav bake-recast-react` and Editor Bridge resolve the primary navigation board and choose grid or hex `LogicTerrainField` by topology. NAV-15 #373 still owns the final single-source asset closure from official `VisualHeightmap`/classification assets into `LogicTerrain`; until then, React `map_data.bin` is the production editing upload format for the current toolchain.

The old branch `origin/codex/mass-navigation-bake-data-showcase` is useful as a reference for chunked logic-terrain materialization and tile-window reads. It must not be merged as-is: it carried a private `.lhtm` lane, huge baked fixtures, fallback-like heightmap sampling, hardcoded area classification, and baked/runtime scale mapping drift. Reuse the ideas, not the data source.

## Inputs

| Input | Unit | Owner | Cost effect |
|---|---:|---|---|
| `WidthInMacroTiles` / `HeightInMacroTiles` | macro tiles | board config | Multiplies world cells by `MacroTileCells` |
| `GridCellSizeCm` / `CellCm` | cm | board config / scale SSOT | Smaller cells increase world cell count for the same physical map |
| `TerrainChunkCells` | cells | terrain chunk owner | Defines current nav tile footprint; default is 64 cells |
| `Targets` | tiles | `NavBakeContext.Targets` | Full bake targets every terrain chunk; dirty bake targets only changed chunks |
| `layers[]` | count | `Navigation/navmesh.json` | Multiplies every target tile |
| `profiles[]` | count | `Navigation/navmesh.json` + `AgentProfileRegistry` | Multiplies every target tile |
| `algorithm` | `recast` / `cdt` | `NavBakeContext.Algorithm` | Recast is offline full bake; CDT is runtime-incremental only |
| `maxDegreeOfParallelism` | workers | `NavBakeExecutionOptions` | Divides wall-clock time only until CPU/IO contention dominates |
| `runtimeIncremental.tileBudgetPerFixedTick` | target tiles | runtime rebuild queue | Caps how many dirty target tiles publish per fixed tick |

## Theoretical Parameters

The estimator has three parameter groups. Each group must be reported separately so reviewers can see whether cost came from map size, target selection, or algorithm detail.

| Group | Parameter | Required in estimate | Notes |
|---|---|---|---|
| Map extent | `WidthInMacroTiles`, `HeightInMacroTiles`, `CellCm`, `MacroTileCells` | Yes | Defines world cells and world cm; `MacroTileCells` is fixed at `MapTile.Size` / 256. |
| Terrain footprint | `TerrainChunkCells`, terrain width/height chunks, source chain | Yes | Defines full nav tile count and whether the source is projected `LogicTerrain`. |
| Target mode | `full`, `dirty`, or `window` | Yes | Full = all terrain chunks; dirty = changed chunks plus optional neighbors; window = explicit chunk rectangle. |
| Multipliers | nav layer count, nav profile count | Yes | Every target tile bakes per layer and per profile. |
| Agent geometry | `radiusCm`, `heightCm`, `maxClimbCm`, `maxSlopeDeg` | Yes for Recast | Radius drives Recast voxel size; climb/slope/height affect walkable filtering. |
| Terrain complexity | height variance, area/tag regions, water/blocked cells | Recommended | Used for report classification until measured calibration exists. |
| Obstacle complexity | obstacle count, covered tile count, polygon vertex count | Recommended | Recast triangle filtering and CDT triangulation cost both grow with obstacle density. |
| Execution | `parallel`, `maxDegreeOfParallelism`, logical CPU count, utilization | Yes | Used only for wall-clock estimate; it does not change operation count. |
| Runtime budget | `tileBudgetPerFixedTick` | Runtime only | Converts dirty target count into publish latency in fixed ticks. |

Current Recast tuning derives voxel size from agent radius in `RecastNavTileBaker`:

```text
recastCellSizeCm = clamp(agentRadiusCm / 3, 5, 50)
recastCellHeightCm = recastCellSizeCm * 0.5
```

That means small agents are expensive. A 30 cm radius agent uses roughly 10 cm Recast cells; a 150 cm radius agent hits the 50 cm cap.

Current Recast fixed parameters in code are also part of the estimate contract:

| Parameter | Current value | Effect |
|---|---:|---|
| partition | watershed | Region partitioning mode; affects contour quality and build cost. |
| region min size | 8 | Small region removal threshold, converted by DotRecast to area. |
| region merge size | 20 | Small-region merge threshold, converted by DotRecast to area. |
| edge max length | 12 m | Contour simplification boundary length. |
| edge max error | 1.3 | Contour simplification tolerance. |
| verts per poly | 6 | Max polygon vertices before triangulation/detail output. |
| detail sample dist | 6 | DotRecast multiplies this by cell size. |
| detail sample max error | 1 | DotRecast multiplies this by cell height. |
| filters | low-hanging, ledge, low-height enabled | Walkable-span filters are enabled. |

These are not Mod-author tuning knobs yet. If they become data-driven later, they must move through `Navigation/navmesh.json` or an explicit bake profile and appear in the estimate hash.

## Target Modes

| Mode | Target count formula | Intended use |
|---|---|---|
| `full` | `fullTileCount` | Offline CI/server bake for small or intentionally scheduled maps. |
| `dirty` | `dirtyTileCount + includedNeighborTiles` | Editor incremental bake or runtime structural changes. |
| `window` | `ceil(windowWidthCells / TerrainChunkCells) * ceil(windowHeightCells / TerrainChunkCells)` | Local tactical region, streaming region, or validation slice. |

The estimator must never silently reinterpret one mode as another. If dirty data is missing, `dirty` fails. If a window exceeds the world extent, `window` fails. If the caller asks for `full`, the tool reports the full cost even if that looks expensive.

## Formulas

For grid maps:

```text
worldWidthCells  = WidthInMacroTiles  * MacroTileCells(256)
worldHeightCells = HeightInMacroTiles * MacroTileCells(256)

worldWidthCm  = worldWidthCells  * CellCm
worldHeightCm = worldHeightCells * CellCm

navTileWidthCells  = TerrainChunkCells(64)
navTileHeightCells = TerrainChunkCells(64)

fullTileCountX = ceil(worldWidthCells  / navTileWidthCells)
fullTileCountY = ceil(worldHeightCells / navTileHeightCells)
fullTileCount  = fullTileCountX * fullTileCountY

bakeOperations = targetTileCount * layerCount * profileCount
```

Per tile, the logical terrain sample count is:

```text
terrainCellsPerTile = TerrainChunkCells * TerrainChunkCells
                    = 64 * 64
                    = 4096 cells
```

The Recast voxel columns per tile are approximately:

```text
tileWidthCm = TerrainChunkCells * CellCm
recastColumnsPerAxis = ceil(tileWidthCm / recastCellSizeCm)
recastColumnBudget = recastColumnsPerAxis * recastColumnsPerAxis
```

Recast also has vertical budget pressure:

```text
recastSpanBudget ~= recastColumnBudget * averageSpansPerColumn
walkableHeightVoxels = ceil(agentHeightCm / recastCellHeightCm)
walkableClimbVoxels  = floor(maxClimbCm / recastCellHeightCm)
```

`averageSpansPerColumn` is content-dependent. Flat terrain is close to one span. Cliffs, caves, bridges, stacked geometry, or dense carved obstacles can push it higher and must move the report into a heavier planning band.

Example with `CellCm = 100`, `TerrainChunkCells = 64`, `agentRadiusCm = 48`:

```text
tileWidthCm = 6400 cm
recastCellSizeCm = 16 cm
recastColumnsPerAxis = 400
recastColumnBudget = 160000 columns per tile
```

## Time Estimate

The planning estimate is:

```text
estimatedSeconds =
  bakeOperations * calibratedMsPerOperation
  / 1000
  / effectiveWorkers

effectiveWorkers = max(1, min(maxDegreeOfParallelism, logicalCpuCount) * utilization)
utilization      = 0.60..0.85, depending on IO and obstacle complexity
```

`calibratedMsPerOperation` is measured per algorithm, profile class, tile terrain complexity, and machine. Until the tool has a local calibration record, use bands:

| Workload | Planning band |
|---|---:|
| CDT runtime incremental, simple tile | 2-20 ms / operation |
| Recast flat/simple terrain | 20-80 ms / operation |
| Recast ordinary height + obstacles | 80-250 ms / operation |
| Recast dense heightfield or obstacle-heavy tile | 250-1000+ ms / operation |

Do not commit to a schedule from these bands. The production tool should print an estimate, then print measured per-operation time after a real bake or calibration run.

Calibration design:

1. Build a normal `NavBakeContext` with the real terrain, obstacle set, layers, profiles, mode, algorithm, and targets.
2. Pick a deterministic sample set, for example first N targets plus a density-stratified set of obstacle-heavy targets.
3. Bake those samples through the real `NavBakeService`; record elapsed ms, success/failure, triangle counts, tile bytes, and obstacle counts.
4. Store the measurement with an input hash and machine summary. Do not reuse calibration when the hash-relevant inputs change.
5. Estimate the remaining run with the calibrated p50/p90 ms per operation, then print measured-vs-estimated after the full bake.

The first production implementation can omit persisted calibration and use manual bands, but the report shape should already leave fields for measured p50/p90 so the CLI contract does not churn later.

## Example Budgets

Assume one layer, one profile, `CellCm = 100`, `TerrainChunkCells = 64`, and 8 effective workers.

| Map / target | Target tiles | Operations | 50 ms/op | 200 ms/op |
|---|---:|---:|---:|---:|
| Dirty 3 x 3 neighborhood | 9 | 9 | < 1 s | < 1 s |
| Local 32 x 32 tactical window | 1024 | 1024 | ~6 s | ~26 s |
| 64 x 64 MacroTile map full bake | 65536 | 65536 | ~7 min | ~27 min |
| 250 x 250 MacroTile map full bake | 1000000 | 1000000 | ~1.7 h | ~6.9 h |
| 250 x 250 map, 2 layers x 4 profiles | 1000000 | 8000000 | ~13.9 h | ~55.6 h |

The 250 x 250 example is a warning, not a recommendation. It is a 64 km x 64 km board at 1 m cells. Full Recast bake at 64 m tiles can be valid for a controlled server-side bake farm, but it is not a casual editor button.

## Memory And Storage

Avoid materializing huge logic terrain as a single in-memory array.

```text
worldCells = worldWidthCells * worldHeightCells
conceptualLogicBytes = worldCells * bytesPerLogicCell
```

For a 250 x 250 MacroTile map at 1 m cells:

```text
worldCells = 64000 * 64000 = 4096000000 cells
```

Even a compact 8 bytes/cell layout is around 30 GiB before object overhead, bake scratch buffers, nav tiles, obstacles, and output bytes. Large worlds need chunk-window projection/readback. The old `.lhtm` prototype is useful here because it proved a tile-window reader shape; the current production design should express the same idea through the official `LogicTerrain` pipeline.

Storage is also multiplicative:

```text
navTileFiles = targetTiles * layerCount * profileCount
estimatedBytes = navTileFiles * averageTileBytes
```

Average tile bytes must be measured. Empty/open flat tiles may be small; detailed obstacle-rich Recast tiles can be much larger.

## Parameter Design

The production estimator should be a first-class step before bake:

```text
ludots nav estimate --mapId <mapId> --target full|dirty|window --profile <id> --layer <id>
```

It should build the same `NavBakeContext` inputs as real bake, but stop before invoking `INavBakeAlgorithm`. The report should include:

| Field | Meaning |
|---|---|
| `worldCells` / `worldCm` | Board extent derived from scale SSOT |
| `fullTileCount` | TerrainChunk/NavTile footprint count |
| `targetTileCount` | Actual full/dirty/window target count |
| `layerCount` / `profileCount` | Multipliers from strict config |
| `bakeOperations` | `targets * layers * profiles` |
| `budgetWorkUnitCount` | CPU/planning budget after algorithm detail is applied |
| `recastCellSizeCmByProfile` | Derived current Recast voxel size |
| `terrainCellsPerTile` | 4096 at current 64-cell footprint |
| `recastColumnBudgetByProfile` | Derived voxel columns per tile for each profile |
| `obstacleStats` | Total obstacles, affected target tiles, max polygon vertices per tile |
| `estimatedSecondsLow/High` | Band estimate or calibrated estimate |
| `calibratedMsP50/P90` | Optional measured local calibration values |
| `estimatedOutputBytesLow/High` | Based on last measured tile-size stats |
| `budgetStatus` | ok / large / reject |
| `estimateHash` | Hash of inputs and target terrain content so review can see what was accepted |

`nav bake` should print the same estimate before doing work. Above a configured work-unit or time threshold, it should fail fast unless the command explicitly asks for a large bake. That is not a fallback; it is a cost guard.

`bakeOperations = targetTileCount * layerCount * profileCount` remains in the report because it explains how many layer/profile bake passes will run. The budget gate uses `budgetWorkUnitCount`, which includes algorithm detail:

```text
CDT work units    = terrainCellSampleCount * layerCount * profileCount
Recast work units = sum(targetTiles * layers * recastColumnBudgetPerTile for each profile)
```

The estimate hash includes the selected target tile terrain content hash, so approving a large editor bake for one painted terrain state does not approve another same-sized map.

Suggested thresholds:

| Status | Condition | Behavior |
|---|---|---|
| `ok` | <= 2,000,000 work units | Bake can run directly |
| `large` | <= 200,000,000 work units | Require explicit large-bake confirmation flag in CLI/Bridge |
| `reject` | > 200,000,000 work units without bake-farm/profiled mode | Fail before writing partial output |

Exact thresholds should become config once production estimates are implemented.

Bridge/editor behavior should mirror CLI behavior:

- show the estimate before starting a bake;
- require an explicit large-bake action for `large`;
- refuse `reject` unless the editor is connected to a profiled/headless bake mode;
- keep the estimate visible next to the final measured bake report.

## Genre Guidance

RTS / battlefield:

- Use full Recast bake for small tactical boards or authored mesh corridors.
- For a huge world board, prefer NodeGraph/road for long-range route selection and MassNavigationFlow for local execution.
- Do not make a 64 km board `NavigationEnabled` and full-bake every 64 m tile casually.

Grand strategy / 4X:

- Prefer NodeGraph boards. Navmesh operation count is zero for pure region/token movement.
- If battles occur locally, use a separate tactical board or a windowed bake target.

Open world / streaming:

- Use chunk-window terrain projection and offline regional bake.
- Use runtime-incremental CDT only for persistent structural changes such as doors, bridges, walls, and buildings.
- Temporary crowd blockage remains MassNavigationFlow avoidance and should not trigger navmesh rebuild.

## Runtime Incremental Budget

Runtime incremental rebuild is `runtime-incremental` + `cdt` only. The frame budget is target-tile based:

```text
ticksToPublish = ceil(dirtyTargetTileCount / tileBudgetPerFixedTick)
```

Each target tile still bakes all configured layers and profiles. Keep the layer/profile multiplier small for runtime-incremental maps, or split runtime-rebuildable layers from offline-only layers.

Suggested starting points:

| Scenario | `tileBudgetPerFixedTick` | Notes |
|---|---:|---|
| Player-visible door/wall edits | 1 | Stable frame pacing; dirty area takes several ticks |
| Editor preview | 2-4 | Faster feedback; only if measured frame cost is acceptable |
| Headless/server rebuild | 4+ | Requires benchmark and queue backpressure |

## Contract Expectations

- No fallback: estimator failure must not silently shrink a map, widen a dirty list, drop profiles, or switch algorithms.
- No duplicate data source: estimation and bake must use the same map, terrain, obstacle, layer, and profile loaders as `NavBakeContext`.
- Strict casing: mode, algorithm, layer id, profile id, and map ids keep existing fail-fast rules.
- Baked-time reports must include target count, layer/profile multipliers, measured success/failure counts, and elapsed milliseconds.
- Large-world docs and tools must distinguish full-world bake, dirty bake, local window bake, and runtime incremental rebuild.
