# 空间尺度配置查表

本页是 `gitbook/architecture/spatial-scale-and-resolution-ssot.md` 的快速查表入口。权威概念、owner 与现状映射以架构页为准；本页只解释“看到某个配置/常量时，它是什么意思、用在哪里、不能和什么混用”。

交互式关系图见 [`spatial-scale-explorer.html`](spatial-scale-explorer.html)，用于点击查看“谁用谁做单位”、哪些尺度必需、哪些尺度可配置。Mod 作者地图尺度入门见 [`map-scale-authoring-guide.md`](map-scale-authoring-guide.md) 与 [`map-scale-authoring-starter.html`](map-scale-authoring-starter.html)。

## 单位依赖图

```mermaid
flowchart TD
    CellCm["CellCm<br/>唯一 cm 基准<br/>1 sim cell = 100 cm"]

    subgraph Authoring["世界 authoring / runtime 边界"]
        MacroOwner["MapTile.Size<br/>MacroTileCells = 256 cells<br/>IO/寻址 owner"]
        MacroCount["WidthInMacroTiles / HeightInMacroTiles<br/>单位：MacroTile 个数"]
        WorldExtent["WorldExtentSpec<br/>MacroTile 个数 * 256 cells * CellCm"]
        WorldSize["WorldSizeSpec<br/>runtime 世界边界"]
    end

    subgraph PartitionTerrain["分区 / 地形 / bake"]
        Partition["PartitionChunkCells<br/>单位：cells<br/>空间分区、AOI、query"]
        Terrain["TerrainChunkCells<br/>单位：cells<br/>逻辑地形块"]
        NavFootprint["Nav bake tile footprint<br/>复用 TerrainChunk footprint<br/>不是独立尺度 owner"]
        Streaming["StreamingChunk<br/>单位：cells / cm<br/>流式加载与 loaded graph rebuild 窗口"]
    end

    subgraph MassFlow["MassFlow 执行层"]
        FlowWindow["FlowWindow<br/>单位：cm<br/>fieldWidthCm / fieldHeightCm"]
        FlowCell["FlowCell<br/>单位：cm<br/>流场网格分辨率"]
        AvoidHash["AvoidanceHashCell<br/>单位：cm<br/>separation / hard resolve hash"]
    end

    subgraph Physics["Physics2D"]
        Broadphase["PhysicsBroadphaseCell<br/>单位：cm<br/>broadphase spatial hash"]
    end

    HeightLevels["LogicTerrainHeightLevels<br/>单位：levels<br/>垂直高度档位，不参与 cm/cells 换算"]
    IndependentOwner["FlowCell / AvoidanceHashCell / PhysicsBroadphaseCell<br/>默认值可等于 CellCm，但 owner 独立"]

    CellCm --> MacroOwner
    HexEdge["HexEdgeLengthCm<br/>单位：cm<br/>HexGrid 专属 hex 边长"]
    MacroOwner --> WorldExtent
    MacroCount --> WorldExtent
    CellCm --> WorldExtent
    WorldExtent --> WorldSize

    CellCm --> Partition
    CellCm --> Terrain
    Terrain --> NavFootprint
    Partition --> Streaming
    Terrain --> Streaming
    CellCm --> Streaming
    HexEdge -.-> Terrain

    CellCm --> FlowWindow
    FlowCell --> FlowWindow
    AvoidHash --> FlowWindow
    CellCm -.-> FlowCell
    CellCm -.-> AvoidHash

    CellCm -.-> Broadphase
    IndependentOwner -.-> FlowCell
    IndependentOwner -.-> AvoidHash
    IndependentOwner -.-> Broadphase
```

| 配置 / 常量 | 目标概念 | 单位 | 当前默认 | 含义 / 应用场景 | owner / 约束 |
|---|---|---:|---:|---|---|
| `SpatialScaleDefaults.CellCm` | `CellCm` | cm | 100 | 全局原子尺度。board 世界尺寸、nav bake cm 换算、默认 flow/hash cell 都从它解释。 | 唯一基准单位，必须 > 0。 |
| `BoardConfig.GridCellSizeCm` | `CellCm` | cm | 100 | board authoring 输入，决定 grid board 每个 sim cell 的厘米边长，并进入 `WorldExtentSpec`。 | 仍保留历史字段名；语义按 `CellCm` 解释。 |
| `BoardConfig.HexEdgeLengthCm` | `HexEdgeLengthCm` | cm | 400 | HexGrid board 的 hex 边长，影响 `HexMetrics`、HexGrid 坐标/查询/渲染布局。 | 仅 HexGrid 生效；Grid / NodeGraph 不受它影响，必须 > 0。 |
| `MapTile.Size` | `MacroTileCells` | cells | 256 | 256-cell IO/寻址宏块。世界大小的 `WidthInMacroTiles` / `HeightInMacroTiles` 以它为倍率。 | `MapTile.Size` 是 owner；`SpatialScaleDefaults.MacroTileCells` 只引用它。 |
| `BoardConfig.WidthInMacroTiles` | `WorldExtent` width | macro tiles | 64 | board/world 宽度 authoring 数量，不是 TerrainChunk 数量，也不是 NavTile 数量。 | #283 正名；旧 `WidthInTiles` fail-fast。 |
| `BoardConfig.HeightInMacroTiles` | `WorldExtent` height | macro tiles | 64 | board/world 高度 authoring 数量，不是 TerrainChunk 数量，也不是 NavTile 数量。 | #283 正名；旧 `HeightInTiles` fail-fast。 |
| `WorldExtentSpec` | `WorldExtent` | cells / cm | derived | 用 `WidthInMacroTiles * MacroTileCells * CellCm` 计算世界范围，产出 runtime `WorldSizeSpec`。 | 是计算对象，不替换 `WorldSizeSpec`。 |
| `BoardConfig.ChunkSizeCells` | `PartitionChunkCells` | cells | 64 | 空间分区、AOI、query backend 的分区块边长。只描述查询分区，不描述地形或 navmesh。 | 必须 > 0 且为 2 的幂。 |
| `VertexChunk.ChunkSize` | `TerrainChunkCells` | cells | 64 | 逻辑地形块边长。当前 navmesh tile footprint 等于 `TerrainChunk` footprint。 | 当前固定；#286 已把 grid/hex 地形输入统一到 `LogicTerrainField`。 |
| Nav bake tile footprint | `TerrainChunk` footprint | cells / cm | 64 cells | navmesh `.ntil` 的 tile 覆盖一个 `TerrainChunk`。 | 不再单独命名为尺度 owner；不要把 `NavTile` 当第二个 chunk 尺度。 |
| streaming / loaded graph window | `StreamingChunk` | cells / cm | derived | 流式加载、loaded graph rebuild 的空间窗口。 | 从 board 分区或显式配置推导；禁止私有 loader fallback。 |
| `MassFlowSolverConfig.fieldWidthCm` / `fieldHeightCm` | `FlowWindow` | cm | preset 显式配置 | MassFlow 执行层滑窗/工作区尺寸。 | 必须 > 0，并被 `FlowCell` 与 `AvoidanceHashCell` 整除。 |
| `MassFlowSolverConfig.flowCellSizeCm` | `FlowCell` | cm | preset 常用 100 | MassFlow 流场网格分辨率。 | 必须 > 0；不要和 board `CellCm` 混成同一个配置 owner。 |
| `MassFlowSolverConfig.separationHashCellSizeCm` | `AvoidanceHashCell` | cm | preset 常用 100 | MassFlow 分离邻居哈希 cell。 | 必须 > 0；属于 avoidance，不属于 navmesh bake。 |
| `MassFlowSolverConfig.hardResolveHashCellSizeCm` | `AvoidanceHashCell` | cm | preset 常用 50 | MassFlow 硬解析候选哈希 cell。 | 必须 > 0；可小于 `CellCm`，但必须由配置显式给出。 |
| `SpatialScaleDefaults.PhysicsBroadphaseCellCm` | `PhysicsBroadphaseCell` | cm | 100 | Physics2D broadphase spatial hash 默认尺度。 | 显式配置 / 命名常量，禁止缺失时静默 fallback。 |
| `SpatialScaleDefaults.LogicTerrainHeightLevels` | logic terrain height levels | levels | 16 | 逻辑地形高度档位数量，当前为 4-bit 高度域。 | owner 在 `SpatialScaleDefaults`；最大值为 `LogicTerrainMaxHeightLevel`。 |

迁移规则：

- `WidthInTiles` / `HeightInTiles` 是历史字段名；实际含义是 `WidthInMacroTiles` / `HeightInMacroTiles`。
- #283 进行破坏式迁移，旧键出现即 fail-fast，不提供别名兼容。
- Authoring UI 可以让作者输入目标米数，但写入 MapConfig 时必须显式反推为 `WidthInMacroTiles` / `HeightInMacroTiles`；不要让作者手填 TerrainChunk/NavTile 个数。
- `WorldExtentSpec` 是 authoring/计算对象，产出既有 `WorldSizeSpec`；不要替换 `WorldSizeSpec`。
- `HexEdgeLengthCm` 只用于 HexGrid board 的 hex 几何；不要拿它解释 Grid board cell、FlowCell 或 NavTile footprint。
- `NavTile footprint` 是 `TerrainChunk` 的用途，不是独立尺度 owner。
- `PartitionChunk` 只用于空间分区/AOI/query；不要拿它解释 terrain/navmesh tile。
- `MacroTile` 只用于 IO/寻址宏块和世界范围 authoring；不要拿它解释 streaming chunk 或 terrain chunk。
- 障碍数据源仍使用 `ManifestationObstacleIntent2D` + `ShapeDataStorage2D` + `CompoundObstacle2DState`。
