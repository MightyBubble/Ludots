# 空间尺度配置查表

本页是 `gitbook/architecture/spatial-scale-and-resolution-ssot.md` 的配置查表入口。权威概念、owner 与映射以架构页为准。

| 配置 / 常量 | 概念 | 单位 | 当前默认 | 约束 |
|---|---|---:|---:|---|
| `SpatialScaleDefaults.CellCm` | `CellCm` | cm | 100 | 唯一基准单位 |
| `BoardConfig.GridCellSizeCm` | `CellCm` | cm | 100 | > 0 |
| `MapTile.Size` | `MacroTileCells` | cells | 256 | owner，不复制定义 |
| `BoardConfig.WidthInTiles` | `WidthInMacroTiles` | macro tiles | 64 | #283 正名，旧键 fail-fast |
| `BoardConfig.HeightInTiles` | `HeightInMacroTiles` | macro tiles | 64 | #283 正名，旧键 fail-fast |
| `BoardConfig.ChunkSizeCells` | `PartitionChunkCells` | cells | 64 | > 0 且 2 的幂 |
| `VertexChunk.ChunkSize` | `TerrainChunkCells` | cells | 64 | 当前固定；#286 解耦拓扑 |
| `MassFlowSolverConfig.fieldWidthCm` / `fieldHeightCm` | `FlowWindow` | cm | preset 显式配置 | 被 flow/hash cell 整除 |
| `MassFlowSolverConfig.flowCellSizeCm` | `FlowCell` | cm | preset 常用 100 | > 0 |
| `MassFlowSolverConfig.separationHashCellSizeCm` | `AvoidanceHashCell` | cm | preset 常用 100 | > 0 |
| `MassFlowSolverConfig.hardResolveHashCellSizeCm` | `AvoidanceHashCell` | cm | preset 常用 50 | > 0 |
| future physics broadphase config | `PhysicsBroadphaseCell` | cm | 100 | 显式配置，无 fallback |

迁移规则：

- `WidthInTiles` / `HeightInTiles` 当前只是历史字段名；它们的实际含义是 `WidthInMacroTiles` / `HeightInMacroTiles`。
- #283 进行破坏式迁移，旧键出现即 fail-fast，不提供别名兼容。
- `WorldExtentSpec` 是 authoring/计算对象，产出既有 `WorldSizeSpec`；不要替换 `WorldSizeSpec`。
- 障碍数据源仍使用 `ManifestationObstacleIntent2D` + `ShapeDataStorage2D` + `CompoundObstacle2DState`。
