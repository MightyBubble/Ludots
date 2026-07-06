# Structure Collision Surfaces

Parent: GitHub issue #591.

## 结论

`StructureCollisionAsset` 是建筑、桥面、坡道、平台、墙体、门洞、gate 等静态/半静态结构碰撞与可站表面的单一事实来源。

`VisualHeightmap` 继续只负责地形高度采样与视觉贴地基础数据。结构 surface 可以被投影成视觉缓存，但缓存不是 gameplay authority，不能反写成建筑碰撞真相。

## 正式结构

Core 资产位于 `Ludots.Core.StructureCollision`：

- `StructureCollisionAsset`：cooked runtime asset，包含 header、layer、agent mask、surface SoA、shape SoA、chunk index。
- `StructureSurfaceSoA`：surface kind、layer id、flags、agent mask、bounds、normal、slope、height band、shape ref、source prefab/part id。
- `StructureShapeSoA`：convex prism、oriented box、cylinder、ramp plane、walkable polygon、wall segment、portal link 的查询形状数据。
- `StructureDirtyChunkState` / `StructureCollisionRuntimeState`：door、gate、destroyed structure、temporary blocker 等 semi-static mutation 的 chunk revision 与 surface enabled state。
- `IGroundSurfaceSampler`：terrain plus structure 的 batch grounding contract。

Chunk index 同时保留 surface span、blocker span、portal span。运行时查询从 chunk span 取候选，不扫描全量 surface。

## Map Load Contract

Map config 新增：

- `structureCollisionAsset`
- `structureAwareGrounding`
- `structureAwareNavigation`

如果 map 或 board 声明 `structureAwareGrounding=true` 或 `structureAwareNavigation=true`，但没有声明 `structureCollisionAsset`，map load 必须失败。terrain-only map 可以不声明该资产，但消费者必须知道自己处于 terrain-only 模式。

`CoreServiceKeys` 发布：

- `StructureCollisionAsset`
- `StructureCollisionRuntimeState`
- `GroundSurfaceSampler`

## Grounding Contract

`IGroundSurfaceSampler` 暴露三条批处理路径：

- `SampleTerrainBatch(...)`
- `SampleStructureSurfaceBatch(...)`
- `ResolveGroundBatch(...)`

调用方提供 position spans 和输出 spans。输出包括 height、normal、surface id、layer id、hit mask。`ResolveGroundBatch` 先采样 terrain，再按 relevant structure chunk 查询 surface candidate，并按 layer、agent mask、walkability、slope、height band 过滤。

桥面与桥下地形是两个 surface truth：桥面来自 `StructureCollisionAsset`，桥下地形来自 `VisualHeightmap` 或 logic terrain。选择哪个 surface 由 caller policy 显式决定，不能静默 fallback。

## Derived Consumers

派生消费者只能读 `StructureCollisionAsset`：

- `StructureCollisionPhysicsAdapter`
- `StructureCollisionNavigationAdapter`
- `StructureCollisionSelectionAdapter`
- `StructureCollisionCameraGroundAdapter`
- `StructureCollisionDebugAdapter`

这些 adapter 输出 derived view，不能拥有第二份 authored shape truth。debug 记录必须带 surface id、layer id、agent mask、source chunk 和 selected height。

## UAT Coverage

Acceptance fixture:

- `assets/StructureCollision/issue591_structure_collision.scoll.json`

Tests:

- `StructureCollisionAsset_LoadsCookedChunkedSoaContract`
- `MapStructureCollisionLoader_FailsFastOnlyWhenStructureAwareMapDeclaresTheNeed`
- `StructureCollisionAssetLoader_RejectsUnknownShapeLayerAndAgentMask`
- `StructureCollisionAssetLoader_RejectsMissingOrInvalidHeaderFieldsInsteadOfDefaulting`
- `StructureCollisionAsset_RejectsOutOfRangeChunkSpan`
- `ResolveGroundBatch_SelectsBridgeDeckWithoutMutatingTerrainHeightTruth`
- `ResolveGroundBatch_GroundLevelPolicyRejectsBridgeDeckAndKeepsLowerSurface`
- `ResolveGroundBatch_RampReturnsStableHeightNormalAndSurfaceIds`
- `StructureFlagsDriveSeparateMovementProjectilePhysicsAndDebugViews`
- `AgentMaskSelectsTraversalResultForInfantryAndMountedUnits`
- `GateMutationUpdatesOnlyAffectedChunkAndInvalidatesDerivedConsumers`
- `PickingAndCameraResolveSameStructureSurfacePolicy`
- `StructureGroundingStressBenchmark_ReportsBoundedZeroAllocationHotPath`

The stress benchmark uses at least 30,000 surfaces and 50,000 samples per frame for 100 frames. It reports total surfaces, loaded chunks, sampled points, visited chunks, tested candidate surfaces, elapsed time, p95 frame time, and managed allocations. After warmup, the tested hot path must allocate zero managed bytes and candidate checks must stay bounded by chunk spans.

## Gherkin SSOT

```gherkin
Feature: Structure-aware grounding and collision surfaces
  Structure collision is the source for building surfaces, blockers, and traversal rules.
  VisualHeightmap remains terrain and visual sampling truth.

  Scenario: Unit stands on a bridge deck above terrain
    When grounding resolves a bridge-layer policy at the bridge position
    Then the result uses the bridge deck surface id
    And terrain height data is unchanged

  Scenario: Unit below bridge stays on the lower surface
    When grounding resolves a ground-level height band under the bridge
    Then the bridge deck is rejected
    And the lower terrain surface is selected

  Scenario: Ramp returns stable height and normal
    When batch grounding samples points along the ramp
    Then each result has finite height and normal
    And repeated samples produce stable surface ids

  Scenario: Structure flags drive separate blockers
    When navigation queries movement blockers
    Then the gate surface is blocked
    When projectile logic queries projectile blockers
    Then the same gate surface is passable

  Scenario: Agent mask selects traversal result
    When infantry samples the narrow platform
    Then the platform surface is selected
    When mounted units sample the same point
    Then the platform surface is rejected

  Scenario: Gate mutation updates only affected chunk
    When the gate opens
    Then only the gate chunk revision changes
    And navigation and physics invalidations reference that chunk

  Scenario: Picking and camera agree with presentation grounding
    When picking and camera resolve the same point with the same policy
    Then both report the same source surface id
```

## 红线

- 不把 building collision 写入 `VisualHeightmap`。
- 不让 Physics2D、MassNavigation、selection、debug 成为 authored shape truth。
- 不对 declared structure-aware map 做 terrain-only silent fallback。
- 不把 render mesh triangle collision 作为第一版 gameplay authority。
