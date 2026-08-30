# Runtime Incremental Navmesh Rebuild

Parent: [Epic #281](https://github.com/MightyBubble/Ludots/issues/281). Subissue: [NAV-10 #304](https://github.com/MightyBubble/Ludots/issues/304). Depends on [NAV-3 #285](https://github.com/MightyBubble/Ludots/issues/285) obstacle authoring and [NAV-5 #287](https://github.com/MightyBubble/Ludots/issues/287) unified bake service.

## Background

Before NAV-10, navmesh was an offline artifact. If a persistent structural obstacle appeared at runtime, such as a wall, door, bridge, or building footprint, the existing navmesh still described the old walkable topology. Precise mesh paths could therefore cross the new wall until a full offline bake regenerated the tiles.

Temporary dynamic avoidance is not part of this page. Unit-to-unit avoidance, short lived blockers, and crowd pressure remain MassNavigationFlow runtime behavior.

## Target

Runtime incremental rebuild adds a production path for sparse structural changes:

- `RuntimeNavMeshObstacleDirtySystem` reads the same SSOT materialized by Physics2D: `ManifestationObstacleIntent2D`, `ManifestationObstacleBridge2DState`, `CompoundObstacle2DState`, and `ShapeDataStorage2D`.
- Runtime obstacles are stored in the shared `NavObstacleSet` service at `CoreServiceKeys.RuntimeNavMeshObstacles`.
- Only entities marked with `RuntimeNavMeshStructuralObstacle` enter the runtime dirty queue. `SinkNavigationObstacle` still materializes navigation geometry, but the marker distinguishes persistent topology changes from temporary dynamic avoidance.
- Dirty AABBs are aggregated into chunk-aligned `NavTile` coordinates by `RuntimeIncrementalNavMeshRebuildQueue`.
- The queue processes a fixed number of tiles per fixed tick using FIFO order.
- Runtime rebuild is `runtime-incremental` + `cdt`; offline Recast remains the full bake path.
- Successful rebuilt tiles are atomically published through `NavTileStore.Replace`.
- `NavQueryService.TryFindPath` runs under a store revision guard and returns `NotReady` instead of a mixed-revision path if a rebuild changes tiles mid-query.

The implementation intentionally does not create `ObstacleGeometryProfile2D`, a MassNavigationFlow obstacle sidecar, a private map loader, or a fallback full-bake path.

## User Story

As a player, I want units to stop planning through a wall that was built during gameplay, so structural map changes affect precise pathing without restarting the map.

Given a unit uses `PreferMesh`, when a structural wall is created on its previous path, then after the dirty tile budget runs the next precise path avoids the wall.

As an engine maintainer, I want runtime rebuilds to publish only complete tiles, so path queries never observe a half-rebuilt navmesh.

Given a path query overlaps a tile replacement, when the store revision changes during the query, then the query retries and returns `NotReady` if it cannot complete on a stable revision.

## UAT Showcase

Current verified command:

```powershell
dotnet test src\Tests\ArchitectureTests\ArchitectureTests.csproj --filter "NavMeshDebugShowcaseLauncherTests|Launcher_ResolvesNavMeshDirtyUpdateShowcases_AsMapSpecificEntrypoints|GameEngine_NavBootstrap_RuntimeIncrementalCdtRegistersRuntimeQueue" /m:1 /nr:false --no-restore --logger "console;verbosity=minimal"
dotnet test src\Tests\GasTests\GasTests.csproj --filter "RuntimeNavMeshObstacleDirtySystem_UsesBridgeStateAsStructuralDirtySource|RuntimeNavMeshObstacleDirtySystem_ClearsTrackedStateWhenRuntimeModeStops" /m:1 /nr:false --no-restore --logger "console;verbosity=minimal"
```

Playable entries:

| Entry | Launch preset | Terrain source | Player feedback |
|---|---|---|---|
| NavMesh Dirty Update · Hex | `navmesh_debug_raylib` | `HexGrid` + VertexMap | Press `P` to place a structural wall; the overlay shows the rebuilt hole on the dirty tiles. |
| NavMesh Dirty Update · Grid | `navmesh_debug_grid_raylib` | `Grid` DataFile | The same `P` / `O` controls prove runtime rebuild is independent of the map data source. |
| NavMesh Dirty Update · ContinuousHeightmap | `navmesh_debug_vhtm_raylib` | `.height` projected into `LogicTerrain` | The visible relief and the navigation overlay stay aligned while the dirty tile rebuilds. |

| Operation | Feedback |
|---|---|
| Create a structural obstacle entity with `RuntimeNavMeshStructuralObstacle` + `ManifestationObstacleIntent2D` | `ManifestationObstacleBridge2DSystem` materializes the shape through `ShapeDataStorage2D` |
| Run `RuntimeNavMeshObstacleDirtySystem` in `runtime-incremental` mode | One runtime obstacle is captured, the dirty tile is rebuilt, queue pending count returns to zero |
| Move an unmarked navigation obstacle | Runtime obstacle count and tile store revision do not change |
| Move the marked structural obstacle | Previous and current AABBs dirty the tile, `NavTileStore.Revision` increments |
| Run architecture queue contracts | Dirty AABB mapping, FIFO budget, failed bake preservation, layer strictness, and stable revision read all pass |

### Player UAT

```gherkin
Feature: Runtime navmesh dirty update showcase

  Scenario: Place a structural wall on the hex map
    Given I launch the Hex navmesh dirty update showcase
    When I press P
    Then the navmesh overlay marks the touched tile area as blocked
    And the log reports a spawned obstacle and a runtime rebuild queue state

  Scenario: Clear a structural wall on the grid map
    Given I launch the Grid navmesh dirty update showcase
    And I have pressed P once
    When I press O
    Then the spawned wall is removed
    And the navmesh overlay can return to the open tile shape after the dirty queue runs

  Scenario: Rebuild over ContinuousHeightmap terrain
    Given I launch the ContinuousHeightmap navmesh dirty update showcase
    When I press P
    Then the visible relief and the navmesh overlay still describe the same walkable ground
    And only the dirty tiles are rebuilt
```

## Configuration

`assets/Navigation/navmesh.json` must include an explicit `runtimeIncremental` object. Runtime dirty services are registered only when the top-level config selects runtime mode:

```json
{
  "mode": "runtime-incremental",
  "algorithm": "cdt",
  "runtimeIncremental": {
    "tileBudgetPerFixedTick": 1,
    "includeNeighborTiles": true,
    "heightScaleMeters": 1.0,
    "minWalkableUpDot": 0.6,
    "cliffHeightThreshold": 1
  }
}
```

Runtime map load creates a separate `NavBakeContext` with:

| Context field | Runtime value |
|---|---|
| `Mode` | `NavBakeMode.RuntimeIncremental` |
| `Algorithm` | `NavBakeAlgorithmKind.Cdt` |
| `Terrain` | Current map `LogicTerrainField` |
| `Obstacles` | `CoreServiceKeys.RuntimeNavMeshObstacles` |
| `Targets` | Replaced by the queue with each dirty tile |
| `BuildConfig` | Derived from `runtimeIncremental` |
| `Execution` | Single-threaded, one dirty tile context at a time |

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
| Dirty AABB touches a tile border with `includeNeighborTiles = true` | Neighbor tiles are enqueued and processed FIFO | `RuntimeIncrementalNavMeshRebuildQueue_DirtyAabbMapsToNeighborTilesAndIgnoresOutOfWorld` |
| Runtime rebuild bake fails | Previous tile remains readable; store revision does not advance | `RuntimeIncrementalNavMeshRebuildQueue_FailedBakeKeepsReadablePreviousTile` |
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
- `NavObstacleSet` and `NavObstacleGeometry` for CDT/Recast obstacle filtering.
- `NavBakeService`, `NavBakeContext`, and `CdtNavBakeAlgorithm`.
- `NavQueryServiceRegistry`, `NavTileStore`, and the existing `.ntil` query format.
- `GameEngine` service registration and phase ordered system registration.

NAV-10 does not merge another branch. It builds on the NAV-3/NAV-5 code already in this Epic branch.

## DoD

- Data-driven: runtime rebuild budget and build tuning come from `Navigation/navmesh.json`.
- No fallback: runtime incremental only accepts CDT, bad config fails fast, failed tiles do not trigger full-map bake.
- No duplicate source: structural obstacle geometry comes from the Physics2D bridge SSOT, not MassNavigationFlow approximation or a private loader.
- Strict casing: layer ids are matched with `StringComparison.Ordinal`.
- Contract tests cover dirty AABB mapping, budget FIFO, failed bake preservation, bootstrap registration gating, obstacle layer strictness, SSOT dirty capture, and revision guarded reads.
- Runtime dirty showcase entries cover HexGrid, Grid DataFile, and ContinuousHeightmap terrain sources with the same player controls: `N` overlay, `P` place wall, `O` clear wall.
- GitBook indexes link this page, and this page links back to #281 and #304.
