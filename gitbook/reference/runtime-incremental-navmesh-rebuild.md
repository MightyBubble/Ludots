# Runtime Incremental Navmesh Rebuild

Parent: [Epic #281](https://github.com/MightyBubble/Ludots/issues/281). Subissue: [NAV-10 #304](https://github.com/MightyBubble/Ludots/issues/304). Depends on [NAV-3 #285](https://github.com/MightyBubble/Ludots/issues/285) obstacle authoring and [NAV-5 #287](https://github.com/MightyBubble/Ludots/issues/287) unified bake service.

## Background

Before NAV-10, navmesh was an offline artifact. If a persistent structural obstacle appeared at runtime, such as a wall, door, bridge, or building footprint, the existing navmesh still described the old walkable topology. Precise mesh paths could therefore cross the new wall until a full offline bake regenerated the tiles.

Temporary dynamic avoidance is not part of this page. Unit-to-unit avoidance, short lived blockers, and crowd pressure remain MassNavigationFlow runtime behavior.

## Target

Runtime incremental rebuild adds a production path for sparse structural changes:

- `RuntimeNavMeshObstacleDirtySystem` reads the same SSOT materialized by Physics2D: `ManifestationObstacleIntent2D`, `ManifestationObstacleBridge2DState`, `CompoundObstacle2DState`, and `ShapeDataStorage2D`.
- Runtime obstacles are projected into a fixed-capacity `RuntimeNavObstacleSnapshot` (`INavObstacleSource`) at `CoreServiceKeys.RuntimeNavMeshObstacles`.
- Each primitive carries an explicit absolute world-cm half-open vertical interval `[minYcm,maxYcm)` authored on `ManifestationObstacleIntent2D` / `CompoundObstacle2D` and copied into the snapshot SoA; vertical-only changes dirty the same XZ bounds.
- Only entities marked with `RuntimeNavMeshStructuralObstacle` enter the runtime dirty queue. `SinkNavigationObstacle` still materializes navigation geometry, but the marker distinguishes persistent topology changes from temporary dynamic avoidance.
- Dirty AABBs are aggregated into chunk-aligned `NavTile` coordinates by `RuntimeIncrementalNavMeshRebuildQueue`.
- The queue processes a fixed number of tiles per fixed tick using FIFO order.
- Runtime rebuild preserves the configured algorithm and requires its adapter to declare runtime capability for the active terrain or triangle input; no adapter fallback is permitted.
- The current default `GameEngine` composition registers CDT for runtime logic terrain. Other algorithms fail fast until the host registers their adapters.
- A dirty batch owns one generation. Its tiles may be rebuilt across multiple fixed ticks, but `NavTileStore` publishes every layer/profile store atomically only after the whole batch succeeds.
- A fully blocked result publishes a checksum-bearing valid empty tile, so the previous walkable topology is removed instead of being retained as if the bake had failed.
- A failed tile fails the pending generation and publishes none of its tiles. Changes collected while a generation is being built are ordered into the next generation.
- `NavQueryService.TryFindPath` runs under a stable store read and returns `NotReady` instead of observing a commit during the query.

The implementation intentionally does not create `ObstacleGeometryProfile2D`, a MassNavigationFlow obstacle sidecar, a private map loader, or a fallback full-bake path.

## User Story

As a player, I want units to stop planning through a wall that was built during gameplay, so structural map changes affect precise pathing without restarting the map.

Given a unit uses `PreferMesh`, when a structural wall is created on its previous path, then after the dirty tile budget runs the next precise path avoids the wall.

As an engine maintainer, I want runtime rebuilds to publish only complete tiles, so path queries never observe a half-rebuilt navmesh.

Given a path query overlaps a tile replacement, when the store revision changes during the query, then the query retries and returns `NotReady` if it cannot complete on a stable revision.

## UAT Showcase

Current verified command:

```powershell
dotnet test src\Tests\GasTests\GasTests.csproj --filter "RuntimeNavMeshObstacleDirtySystem_UsesBridgeStateAsStructuralDirtySource|Physics2DIntegrationTests" /m:1 /nr:false --no-restore --logger "console;verbosity=minimal"
```

| Operation | Feedback |
|---|---|
| Create a structural obstacle entity with `RuntimeNavMeshStructuralObstacle` + `ManifestationObstacleIntent2D` | `ManifestationObstacleBridge2DSystem` materializes the shape through `ShapeDataStorage2D` |
| Run `RuntimeNavMeshObstacleDirtySystem` in `runtime-incremental` mode | One runtime obstacle is captured, the dirty tile is rebuilt, queue pending count returns to zero |
| Move an unmarked navigation obstacle | Runtime obstacle count and tile store revision do not change |
| Move the marked structural obstacle | Previous and current AABBs dirty the tile, `NavTileStore.Revision` increments |
| Run architecture queue contracts | Dirty AABB mapping, fixed work budget, generation-wide commit, failed-generation zero publication, valid empty tile publication, layer strictness, and stable read all pass |

## Configuration

`assets/Configs/Navigation/navmesh.json` must include an explicit `runtimeIncremental` object. Runtime dirty services are registered only when the top-level config selects runtime mode:

```json
{
  "mode": "runtime-incremental",
  "algorithm": "cdt",
  "runtimeIncremental": {
    "tileBudgetPerFixedTick": 1,
    "includeNeighborTiles": true,
    "heightScaleMeters": 1.0,
    "minWalkableUpDot": 0.6,
    "cliffHeightThreshold": 1,
    "trackedStructuralEntityCapacity": 256,
    "obstaclePrimitiveCapacity": 512,
    "polygonVertexCapacity": 4096
  }
}
```

Runtime map load creates a separate `NavBakeContext` with:

| Context field | Runtime value |
|---|---|
| `Mode` | `NavBakeMode.RuntimeIncremental` |
| `Algorithm` | Explicit configured algorithm; current default host registers `Cdt` |
| `Terrain` / `TriangleSurface` | Exactly one active input; current default host supplies the map `LogicTerrainField` |
| `Obstacles` | `CoreServiceKeys.RuntimeNavMeshObstacles` (`RuntimeNavObstacleSnapshot`) |
| `Targets` | Replaced by the queue with each dirty tile |
| `BuildConfig` | Derived from `runtimeIncremental` |
| `Execution` | Single-threaded, one dirty tile context at a time |

`runtimeIncremental` capacity fields are required, strictly positive, and fixed:

| Field | Owner | Constraint |
|---|---|---|
| `trackedStructuralEntityCapacity` | Runtime dirty tracking table | Required, `> 0`, no auto growth |
| `obstaclePrimitiveCapacity` | `RuntimeNavObstacleSnapshot` primitive SoA | Required, `> 0`, no auto growth |
| `polygonVertexCapacity` | `RuntimeNavObstacleSnapshot` polygon vertex SoA | Required, `> 0`, no auto growth |

Layer ids are strict and case-sensitive. Runtime obstacle capture currently requires exactly one authored nav layer so that no fallback layer attribution is invented.

Structural rebuild eligibility is authored as an ECS marker on the same entity that owns `ManifestationObstacleIntent2D` or `CompoundObstacle2D`:

```json
{
  "RuntimeNavMeshStructuralObstacle": {},
  "ManifestationObstacleIntent2D": {
    "shape": "Box",
    "sinkPhysicsCollider": true,
    "sinkNavigationObstacle": true,
    "halfWidthCm": 80,
    "halfHeightCm": 20,
    "navRadiusCm": 80
  }
}
```

Entities without `RuntimeNavMeshStructuralObstacle` can still contribute to Physics2D and MassNavigationFlow obstacle projections, but they do not dirty navmesh tiles.

## Config To Behavior Tests

| Change | Behavior | Coverage |
|---|---|---|
| `runtimeIncremental.tileBudgetPerFixedTick = 1` | Queue publishes one dirty tile per tick | `RuntimeIncrementalNavMeshRebuildQueue_ProcessesDirtyTilesByBudgetAndPublishesRevision` |
| Queue uses a registered runtime-capable non-CDT test adapter | Selected algorithm reaches the adapter unchanged | `RuntimeIncrementalNavMeshRebuildQueue_PreservesSelectedAlgorithm` |
| Triangle input grid has a non-zero origin | Dirty AABB mapping uses its origin, tile size, and counts | `RuntimeIncrementalNavMeshRebuildQueue_TriangleGridDirtyAabbHonorsOriginAndTileSize` |
| Dirty AABB touches a tile border with `includeNeighborTiles = true` | Neighbor tiles are enqueued and processed FIFO | `RuntimeIncrementalNavMeshRebuildQueue_DirtyAabbMapsToNeighborTilesAndIgnoresOutOfWorld` |
| Runtime rebuild bake fails | The pending generation publishes no tiles; every store keeps its previous committed generation | `RuntimeIncrementalNavMeshRebuildQueue_FailedGenerationPublishesNothingAcrossStores` |
| Runtime rebuild produces no walkable polygons | A valid empty tile is committed with the new generation and replaces the previous walkable tile | `RuntimeIncrementalNavMeshRebuildQueue_ValidEmptyTileRemovesPreviousTopology` |
| A dirty batch exceeds one fixed-tick budget | Pending results remain invisible until the final tile succeeds, then all stores advance together | `RuntimeIncrementalNavMeshRebuildQueue_GenerationSpansTicksAndCommitsAtomicallyAcrossStores` |
| Obstacle layer id has wrong casing | Bake fails fast as unknown nav layer | `CdtBake_ConsumesObstacleSetWithStrictLayerId` |
| Obstacle bridge changes or moves a structural obstacle marker entity | Runtime dirty system rebuilds from bridge state and `ShapeDataStorage2D` | `RuntimeNavMeshObstacleDirtySystem_UsesBridgeStateAsStructuralDirtySource` |
| Obstacle bridge changes an unmarked navigation obstacle | Runtime dirty system ignores it; MassNavigationFlow avoidance remains responsible | `RuntimeNavMeshObstacleDirtySystem_UsesBridgeStateAsStructuralDirtySource` |
| Runtime mode is disabled between map loads | Dirty system clears local tracked obstacle state and does not dirty stale tiles | `RuntimeNavMeshObstacleDirtySystem_ClearsTrackedStateWhenRuntimeModeStops` |
| Default `offline` / `recast` config is loaded | Runtime obstacle set and rebuild queue are not registered | `GameEngine_NavBootstrap_OfflineRecastDoesNotRegisterRuntimeIncrementalQueue` |
| Store revision changes during query | Query retries and can return `NotReady` instead of mixed data | `NavTileStore_StableReadRejectsMixedRevision` |
| Recast obstacle filtering sees layer id | Uses exact `Ordinal` layer match through shared `NavObstacleGeometry` | `NavBakeServiceContractTests` plus repository scans |

## Merge And Reuse

Reused:

- `ManifestationObstacleBridge2DSystem` and its materialized shape state.
- `ShapeDataStorage2D`, `ShapeWorldTransform2D`, `ManifestationObstacleIntent2D`, and `CompoundObstacle2DState`.
- `INavObstacleSource` (`NavObstacleSet` cold/offline, `RuntimeNavObstacleSnapshot` runtime) and `NavObstacleGeometry` for CDT/Recast obstacle filtering.
- `NavBakeService`, the exact-one-input `NavBakeContext`, and registered `INavBakeAlgorithm` capabilities.
- `NavQueryServiceRegistry`, `NavTileStore`, and the existing `.ntil` query format.
- `GameEngine` service registration and phase ordered system registration.

NAV-10 does not merge another branch. It builds on the NAV-3/NAV-5 code already in this Epic branch.

## DoD

- Data-driven: runtime rebuild budget and build tuning come from `Navigation/navmesh.json`.
- No fallback: missing adapters and unsupported mode/input capabilities fail fast; failed tiles do not trigger another algorithm or a full-map bake.
- No duplicate source: structural obstacle geometry comes from the Physics2D bridge SSOT, not MassNavigationFlow approximation or a private loader.
- Strict casing: layer ids are matched with `StringComparison.Ordinal`.
- Contract tests cover active-input dirty AABB mapping, selected-algorithm preservation, fixed work budgeting, generation-wide atomic publication, failed-generation zero publication, valid empty tiles, bootstrap registration gating, obstacle layer strictness, SSOT dirty capture, and stable reads.
- Remaining gaps: 0GC dirty collection, runtime triangle-surface host composition, and the two headed showcases are tracked by the dynamic 3D bake architecture. Current NAV-10 verification is contract-level only.
- GitBook indexes link this page, and this page links back to #281 and #304.
