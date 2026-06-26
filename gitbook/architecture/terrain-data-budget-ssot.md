# Terrain Data Budget SSOT

Related: [Epic #399](https://github.com/MightyBubble/Ludots/issues/399), [NAV-14 #372](https://github.com/MightyBubble/Ludots/issues/372), [NAV-15 #373](https://github.com/MightyBubble/Ludots/issues/373), [Spatial Scale SSOT](spatial-scale-and-resolution-ssot.md), [Nav Bake Budget And Estimation](../reference/nav-bake-budget-and-estimation.md).

This page is the single vocabulary source for terrain data budgets. The production order is:

```text
Board -> VisualHeightmap -> LogicTerrain -> NavMesh
```

Terrain classification follows NAV-14/NAV-15: cells and polygons store classification keys such as `areaId` and flags; traversal cost is resolved later through the `area x agent` table. Do not add per-agent cost to terrain cell storage.

## Constants

Current scale owners:

| Name | Owner | Value |
|---|---|---:|
| `CellCm` | `SpatialScaleDefaults.CellCm` | 100 cm |
| `TerrainChunkCells` | `SpatialScaleDefaults.TerrainChunkCells` | 64 cells |
| `LogicTerrainMaxHeightLevel` | `SpatialScaleDefaults.LogicTerrainMaxHeightLevel` | 15 |
| `MacroTileCells` | `SpatialScaleDefaults.MacroTileCells` | 256 cells |

## Five Budget Domains

| Domain | Unit | Owner | Formula | Example | Storage class | Tooltip |
|---|---:|---|---|---:|---|---|
| VisualHeightmap estimate | bytes on disk | `IVisualHeightmap` asset authoring / `VisualHeightmapBinary` | `sampleColumns * sampleRows * bytesPerSample * layerCount`, plus small metadata and optional chunk headers | `8192 * 8192 * 2 = 128 MiB` for 8K R16 single-layer | saved-file estimate | Continuous visual height storage. It is a saved asset budget, not the logic terrain dense size. |
| LogicTerrain dense-equivalent | theoretical bytes | Map domain `LogicTerrainField` semantics | `widthCells * heightCells * denseBytesPerCell` where current planning cell is `height/area/flags = 4 bytes` | `64000 * 64000 * 4 ~= 15.3 GiB` | dense-equivalent only | Theoretical full-grid equivalent for comparing scale. This is not a file size and not resident memory for sparse worlds. |
| LogicTerrain actual sparse resident | resident bytes / file bytes after compression | Map field store / `.ltrn` board-field chunks | `residentChunkCount * compressedChunkBytes + metadata`; unknown before measurement | Lower bound is metadata plus written chunks only | sparse resident / sparse saved file | Actual loaded or persisted chunks. Empty default chunks are not instantiated and not written. |
| Recast bake estimate | voxel columns / work units | `NavBakeContext` / `NavBakeEstimator` | `targetTileCount * layerCount * sum(profile.recastColumnBudgetPerTile)` | A 64-cell tile at 1 m and 10 cm Recast cells is `640 * 640 = 409600` columns | bake-cost, not storage | CPU and scratch-budget estimate for the bake step. It is not saved output size. |
| NavMesh output estimate | tile bytes | `NavTileBinary` / Detour tile bytes emitted by bake | `targetTiles * layers * profiles * measuredAverageTileBytes` | Must be measured from current bake artifacts | saved output, measured | Baked `.ntil`/Detour tile payload bytes. Use measured low/high bands, not dense terrain math. |

## Required Counterexample

These two numbers are both useful, but they are different species:

```text
8K R16 visual heightmap = 8192 * 8192 * 2 = 134217728 bytes = 128 MiB saved asset

64 km @ 1 m logic terrain dense-equivalent
  = 64000 * 64000 * 4
  = 16384000000 bytes
  ~= 15.3 GiB theoretical dense-equivalent, not saved file size
```

Never label the second number as a saved file size. In production it is represented by sparse `LogicTerrain` chunks, and only resident or dirty chunks are materialized.

## Formula Details

VisualHeightmap:

```text
visualHeightmapBytes =
  sampleColumns * sampleRows * layerCount * bytesPerSample
```

Use `2` bytes for R16/R16-scaled authoring. Quantized or compressed chunk profiles report their own encoded bytes once the format records them.

LogicTerrain dense-equivalent:

```text
widthCells  = worldWidthCm  / CellCm
heightCells = worldHeightCm / CellCm
logicDenseEquivalentBytes = widthCells * heightCells * 4
```

The `4` byte planning value is a dense-equivalent display unit for `height`, `areaId`, and flags. It is deliberately separate from sparse resident memory and `.ltrn` bytes.

LogicTerrain actual sparse resident:

```text
logicSparseResidentBytes =
  metadataBytes + sum(residentOrDirtyChunk.encodedBytes)
```

Before `.ltrn` bytes have been measured, tools must label this as `not-measured` or `estimated lower bound`; they must not invent a dense fallback estimate.

Recast bake:

```text
recastCellSizeCm = clamp(agentRadiusCm / 3, 5, 50)
recastColumnsPerAxis = ceil(tileWorldWidthCm / recastCellSizeCm)
recastColumnBudgetPerTile = recastColumnsPerAxis * recastColumnsPerAxis
recastColumnBudgetTotal = targetTileCount * layerCount * sum(profile.recastColumnBudgetPerTile)
```

This is a bake-time cost signal. It does not describe saved terrain or navmesh bytes.

NavMesh output:

```text
navMeshOutputBytesLowHigh =
  bakeOperationCount * measuredTileBytesLowHigh
```

The estimator may carry conservative low/high bands. Real output size must be recorded from the emitted tile payloads.

## UI Labels

Use these labels consistently:

| Field | Required label |
|---|---|
| VisualHeightmap | `VisualHeightmap saved estimate` |
| LogicTerrain dense-equivalent | `LogicTerrain dense-equivalent, not saved` |
| LogicTerrain actual sparse resident | `LogicTerrain sparse resident, not measured` or `LogicTerrain sparse resident, measured` |
| Recast bake | `Recast bake-cost estimate` |
| NavMesh output | `NavMesh output bytes, measured/estimated` |

Review rule: if a tooltip contains `GiB` for LogicTerrain, it must also say `dense-equivalent` or `sparse resident`; if it says `saved file`, it must refer to VisualHeightmap, `.ltrn`, or NavMesh output bytes only.
