# 空间尺度与分辨率 SSOT

所属总单：[Epic #281](https://github.com/MightyBubble/Ludots/issues/281)。本页落实 [NAV-0 #282](https://github.com/MightyBubble/Ludots/issues/282)，是后续 NAV-1 到 NAV-10 的尺度词汇唯一来源。地形预算单位、owner 与 dense-equivalent / 存盘 / bake-cost 口径见 [Terrain Data Budget SSOT](terrain-data-budget-ssot.md)。

## 背景（现状）

基线 `origin/main` @ `f58bd23d` 中，`tile`、`grid`、`chunk`、`cell` 被多个系统复用，`256`、`64`、`100`、`50`、`16` 等数字散落在 board、bake 与 MassNavigationFlow 代码中。典型现状：

| 名称 | 实际含义 | 值 | 位置 |
|---|---:|---:|---|
| `GridCellSizeCm` | sim 原子 cell 边长 | 100 cm | `src/Core/Map/Board/BoardConfig.cs` |
| `HexEdgeLengthCm` | hex 边长 | 400 cm | `src/Core/Map/Board/BoardConfig.cs` / `src/Core/Map/Hex/HexMetrics.cs` |
| `MapTile.Size` / `WorldMap.TileSize` | 256-cell IO/寻址宏块 | 256 cells | `src/Core/Map/MapTile.cs` / `src/Core/Map/WorldMap.cs` |
| `BoardConfig.WidthInTiles` / `HeightInTiles` | 名为 tiles，实为 256-cell 宏块数量 | 默认 64 | `src/Core/Map/Board/BoardConfig.cs` |
| `BoardConfig.ChunkSizeCells` | 空间分区/AOI 块边长 | 默认 64 cells | `src/Core/Map/Board/BoardConfig.cs` |
| `VertexChunk.ChunkSize` | hex 逻辑地形块边长，也是当前 NavTile 足迹 | 64 cells | `src/Core/Map/Hex/VertexChunk.cs` |
| `VertexMap.WidthInChunks` / `HeightInChunks` | 逻辑地形块数量 | 默认 64 | `src/Core/Map/Hex/VertexMap.cs` |
| `flowCellSizeCm` | MassNavigationFlow 流场 cell | 配置值，现有 preset 为 100 cm | `mods/.../MassNavigationConfig.json` |
| `separationHashCellSizeCm` | MassNavigationFlow 分离哈希 cell | 配置值，现有 preset 为 100 cm | `mods/.../MassNavigationConfig.json` |
| `hardResolveHashCellSizeCm` | MassNavigationFlow 硬解析哈希 cell | 配置值，现有 preset 为 50 cm | `mods/.../MassNavigationConfig.json` |
| `Spatial.CellSizeCm` | retired physics broadphase cell | 配置/默认 100 cm | `src/Core/Ludots.Physics2D/...` |

主要漂移点：

- `64` 同时表示 `PartitionChunk` 边长、`TerrainChunk` 边长、默认地形 chunk 数、portal/list 容量和 bit word 宽度。
- 三个 board 构造函数曾直接写 `WidthInTiles * 256 * GridCellSizeCm`。
- `100` 同时用于 board cell、bake 米到厘米转换、MassNavigationFlow flow/hash cell 默认值、NodeGraph 投影 cell 最小值。
- `WidthInTiles` / `HeightInTiles` 实际是宏块数量，不是 cell、TerrainChunk 或 NavTile 数量。

## 目标（预期）

唯一基准单位是 **`CellCm`**。`Cell` 是所有 board、bake、flow、avoidance、broadphase 尺度换算的原子单位。NAV-0 新增 `src/Core/Spatial/SpatialScaleDefaults.cs`，集中命名现有尺度默认值；后续配置项必须引用本文档中的目标名和 owner。

In scope：

- 本页定义尺度 taxonomy、owner、约束和现状名到目标名映射。
- 新增命名常量模块，集中 `256` / `64` / `100` / `50` / `16` 等尺度默认值。
- 添加全仓扫描 contract，禁止 board、bake、MassNavigationFlow 代码重新内联 `256` / `64` / `100` 尺度字面量。
- 更新 GitBook 索引。

Out of scope：

- NAV-0 不改 board 计算语义，不改 map json 键名。
- `WidthInTiles` / `HeightInTiles` 的破坏式迁移由 #283 完成；迁移后目标键是 `WidthInMacroTiles` / `HeightInMacroTiles`，旧键 fail-fast、无别名兼容。
- `WorldExtentSpec` 由 #283 引入；它是 authoring/计算对象，产出既有 `src/Core/Spatial/WorldSizeSpec.cs`，不替换后者。
- 逻辑地形高度保持 4-bit 共 16 档，拓扑解耦在 #286 做。
- 障碍数据源使用主线已有 `ManifestationObstacleIntent2D` + `ShapeDataStorage2D` + `CompoundObstacle2DState`，不要新建 `ObstacleGeometryProfile2D`。

## 层级表

| 名称 | cells | cm | owner | 用途 | 是否可配 | 约束 |
|---|---:|---:|---|---|---|---|
| `Cell` / `CellCm` | 1 | `CellCm`，默认 100 | `SpatialScaleDefaults.CellCm`，board 配置字段仍为 `GridCellSizeCm` | sim 原子单位、board 世界尺寸、bake cm 换算、默认 flow/hash cell | 是，当前经 `BoardConfig.GridCellSizeCm` 与 MassNavigationFlow solver 配置显式给出 | 必须 > 0；后续所有派生值必须是整数倍或显式说明 |
| `HexEdgeLengthCm` | hex 边长，非 cell 派生 | 默认 400 | `SpatialScaleDefaults.DefaultHexEdgeLengthCm`，board 配置字段为 `HexEdgeLengthCm` | HexGrid board 的 hex 轴长、world position / AOI / render layout | 是，仅 HexGrid board 生效 | 必须 > 0；不影响 Grid / NodeGraph board |
| `PartitionChunk` | `PartitionChunkCells`，默认 64 | `PartitionChunkCells * CellCm` | `BoardConfig.ChunkSizeCells` / `SpatialScaleDefaults.PartitionChunkCells` | 空间分区、AOI、query backend | 是 | 必须 > 0 且为 2 的幂 |
| `TerrainChunk` | `TerrainChunkCells`，固定 64 | `TerrainChunkCells * CellCm` 的逻辑足迹；hex bake 还会乘 hex metric | `VertexChunk.ChunkSize` / `SpatialScaleDefaults.TerrainChunkCells` | 逻辑地形块；当前等于 NavTile 足迹 | 否 | 当前固定 64；grid/hex 共用抽象在 #286 落地 |
| `NavTile footprint` | = `TerrainChunk` | = `TerrainChunk` footprint | NavMesh bake (`NavTileBuilder` / `BakePipeline`) | navmesh 产物 tile 足迹 | 否 | 只作为 `TerrainChunk` 的用途，不再单独命名尺度 |
| `MacroTile` | `MacroTileCells` = `MapTile.Size` = 256 | `MacroTileCells * CellCm` | `MapTile.Size` / `SpatialScaleDefaults.MacroTileCells` | IO/寻址宏块、世界范围 authoring 数量 | 否，数量可配 | `MacroTileCells` 引用 `MapTile.Size`；数量字段正名为 `WidthInMacroTiles` / `HeightInMacroTiles` |
| `StreamingChunk` | N x `PartitionChunk` | N x `PartitionChunkCells * CellCm` | streaming/loaded chunk owner；NodeGraph 当前通过 `WorldGridLoadedChunks` 消费 | 流式加载、loaded graph rebuild | 是 | 必须显式配置或由 board 分区推导；禁止私有 loader fallback |
| `WorldExtent` | `WidthInMacroTiles * MacroTileCells` / `HeightInMacroTiles * MacroTileCells` | cells x `CellCm` | `WorldExtentSpec`，产出既有 `WorldSizeSpec` | board 世界范围、坐标转换、minimap/full-map bounds | 是 | 旧 `WidthInTiles` / `HeightInTiles` 出现时 fail-fast |
| `FlowWindow` | `FieldWidthCm / CellCm` by `FieldHeightCm / CellCm` | `FieldWidthCm` x `FieldHeightCm` | MassNavigationFlow solver config | 执行层滑窗/工作区 | 是 | 宽高必须 > 0；必须能被 `FlowCell`、`AvoidanceHashCell` 整除 |
| `FlowCell` | `FlowCellSizeCm / CellCm` | 默认 100 | MassNavigationFlow solver `flowCellSizeCm` / `SpatialScaleDefaults.FlowCellCm` | 流场网格 cell | 是 | 必须 > 0；`FlowWindow` 宽高必须整除它 |
| `AvoidanceHashCell` | `separationHashCellSizeCm / CellCm` 或 `hardResolveHashCellSizeCm / CellCm` | separation 默认 100；hard-resolve 默认 50 | MassNavigationFlow solver / `SpatialScaleDefaults.Avoidance*HashCellCm` | 分离邻居哈希、硬解析候选哈希 | 是 | 必须 > 0；`FlowWindow` 宽高必须整除它 |
| `PhysicsBroadphaseCell` | `PhysicsBroadphaseCellCm / CellCm` | 默认 100 | Physics2D / future physics broadphase config | broadphase spatial hash | 是 | 必须显式配置；禁止缺失时静默 fallback |

每个概念恰好一个名字、一个 owner。`NavTile footprint` 是 `TerrainChunk` 的用途，不再作为独立尺度 owner。

## 命名 Taxonomy

- `Cell`：sim 原子格。唯一基准字段名为 `CellCm`；历史 `GridCellSizeCm` 仍作为 board config 输入。
- `HexEdgeLengthCm`：HexGrid board 的 hex 边长。它只影响 `HexMetrics`、`HexGridBoard`、hex 位置/查询/渲染，不改 Grid / NodeGraph board。
- `PartitionChunk`：空间分区块。只描述 query/AOI 分区，不描述地形或 navmesh。
- `TerrainChunk`：逻辑地形块。当前 hex owner 是 `VertexChunk`；#286 后 grid/hex 共用地形抽象仍沿用此名。
- `MacroTile`：256-cell IO/寻址宏块。owner 是 `MapTile.Size`，常量模块只引用它。
- `StreamingChunk`：流式加载块。不要用 `chunk` 裸词。
- `WorldExtent`：世界范围 authoring/计算概念。`WorldExtentSpec` 产出 `WorldSizeSpec`。
- `FlowWindow` + `FlowCell`：MassNavigationFlow 执行层滑窗与流场分辨率。
- `AvoidanceHashCell`：MassNavigationFlow 分离/硬解析哈希 cell。
- `PhysicsBroadphaseCell`：physics broadphase cell。

## 现状名到目标名映射

| 现状名 | 目标名 | NAV-0 动作 | 后续动作 |
|---|---|---|---|
| `BoardConfig.GridCellSizeCm` | `CellCm` | 默认值引用 `SpatialScaleDefaults.CellCm` | 继续作为 `WorldExtentSpec` 输入；是否改字段名另立 |
| `BoardConfig.HexEdgeLengthCm` | `HexEdgeLengthCm` | 默认值引用 `SpatialScaleDefaults.DefaultHexEdgeLengthCm` | 仅 HexGrid board 生效；由 `HexMetrics` / `HexGridBoard` 消费 |
| `MapTile.Size` | `MacroTileCells` | `SpatialScaleDefaults.MacroTileCells` 引用它 | 保持 owner，不复制新 owner |
| `WorldMap.TileSize` | `MacroTileCells` | 引用 `SpatialScaleDefaults.MacroTileCells` | 后续可移除重复旧名 |
| `BoardConfig.WidthInTiles` | `WidthInMacroTiles` | 仅文档映射，不改 JSON/API | #283 破坏式迁移；旧键 fail-fast，无别名兼容 |
| `BoardConfig.HeightInTiles` | `HeightInMacroTiles` | 仅文档映射，不改 JSON/API | #283 破坏式迁移；旧键 fail-fast，无别名兼容 |
| `BoardConfig.ChunkSizeCells` | `PartitionChunkCells` | 默认值引用 `SpatialScaleDefaults.PartitionChunkCells` | #283 配置 schema 正名 |
| `VertexChunk.ChunkSize` | `TerrainChunkCells` | 引用 `SpatialScaleDefaults.TerrainChunkCells` | #286 将 owner 从 hex 专属实现解耦 |
| `VertexMap.WidthInChunks` / `HeightInChunks` | `TerrainWidthChunks` / `TerrainHeightChunks` | 默认值引用 `DefaultTerrain*Chunks` | #286 与 topology-neutral terrain authoring 对齐 |
| `NavTile` tile footprint | `TerrainChunk` footprint | bake 代码用 `TerrainChunkCells` / `VertexChunk` owner | #286 grid/hex 共用 |
| `NodeGraphBoard` 内联 loaded chunk | `StreamingChunk` | 文档命名；当前仍从 `ChunkSizeCells * CellCm` 推导 | 后续配置显式化 |
| `MassNavigationFlowSolverConfig.FieldWidthCm` / `FieldHeightCm` | `FlowWindow` | 文档命名 | #288/#290 与执行 showcase 对齐 |
| `flowCellSizeCm` | `FlowCell` | 常量提供默认名；配置仍显式 | 后续 profile/preset 文档回链本文 |
| `separationHashCellSizeCm` / `hardResolveHashCellSizeCm` | `AvoidanceHashCell` | 常量提供默认名；配置仍显式 | #288 动态障碍/避障调参回链本文 |
| `Spatial.CellSizeCm` | `PhysicsBroadphaseCell` | 仅文档映射 | #285 合 PR #186 后去 fallback、显式配置 |

## User Story

US-0.1：作为接手导航的开发者，我要一份现状清单、目标命名和映射表，以便不再逆向猜每个尺度数字的含义。

Given 仓库存在 board/bake/MassNavigationFlow 多套尺度词；When 我打开本页；Then 我能查到任一尺度概念的定义、单位、owner、约束与现状名到目标名映射。

US-0.2：作为评审者，我要尺度魔数集中到常量模块，以便新增代码不能重新散落 `256` / `64` / `100`。

Given `SpatialScaleDefaults` 已落地；When 有人在 board/bake/MassNavigationFlow 代码中写入字面尺度；Then `NavigationSpatialScaleMagicNumberContractTests` 失败并输出文件与行号。

## UAT Showcase（钉死）

NAV-0 的产物是文档、常量和扫描 contract，不新增可玩 preset。后续 #283 起共用 `NavDomainShowcaseMod` 多 preset 做可视化；NAV-0 先用真实测试与人工审查验收。

| 命令 / 操作 | 可见反馈 |
|---|---|
| `dotnet test src/Tests/ArchitectureTests/ArchitectureTests.csproj --filter NavigationSpatialScaleMagicNumberContractTests` | contract 测试通过 |
| 在 `src/Core/Map/Board/GridBoard.cs` 将 `SpatialScaleDefaults.MacroTileCells` 临时改回字面 `256` 后重跑上述命令 | 测试失败，输出 `src/Core/Map/Board/GridBoard.cs:<line>: literal 256` |
| 撤销临时改动后重跑 | 测试恢复通过 |
| 打开本页对照 `src/Core/Spatial/SpatialScaleDefaults.cs` | 层级表中每个默认尺度都有命名常量或明确 owner |

## 配置指南

NAV-0 不新增配置 schema。现有配置项按本文口径解释：

| 配置项 | 目标概念 | 单位 | 范围 / 约束 | 归属 |
|---|---|---:|---|---|
| `BoardConfig.GridCellSizeCm` | `CellCm` | cm | > 0 | board authoring |
| `BoardConfig.HexEdgeLengthCm` | `HexEdgeLengthCm` | cm | > 0；仅 HexGrid 生效 | HexGrid board authoring |
| `BoardConfig.WidthInMacroTiles` | `WidthInMacroTiles` | macro tiles | 旧键 fail-fast | board/world extent authoring |
| `BoardConfig.HeightInMacroTiles` | `HeightInMacroTiles` | macro tiles | 旧键 fail-fast | board/world extent authoring |
| `BoardConfig.ChunkSizeCells` | `PartitionChunkCells` | cells | > 0 且 2 的幂 | spatial partition |
| `MassNavigationFlowSolverConfig.fieldWidthCm` / `fieldHeightCm` | `FlowWindow` | cm | > 0；被 FlowCell/hash cell 整除 | MassNavigationFlow solver |
| `MassNavigationFlowSolverConfig.flowCellSizeCm` | `FlowCell` | cm | > 0 | MassNavigationFlow solver |
| `MassNavigationFlowSolverConfig.separationHashCellSizeCm` | `AvoidanceHashCell` | cm | > 0 | MassNavigationFlow solver |
| `MassNavigationFlowSolverConfig.hardResolveHashCellSizeCm` | `AvoidanceHashCell` | cm | > 0 | MassNavigationFlow solver |

更短的查表入口见 `gitbook/reference/spatial-scale-configuration.md`。

## 配置到行为联动

| 改动 | 预期行为 | 自动化钉死 |
|---|---|---|
| 修改 `CellCm` 或 board `GridCellSizeCm` | board `WorldSizeSpec.Bounds` 按相同 macro tile 数成比例变化 | #283 尺度 contract |
| 修改 `PartitionChunkCells` 或 `ChunkSizeCells` | spatial query/AOI 分区粒度变化，世界范围不变化 | 现有 spatial partition tests + #283 补充 |
| 修改 `FlowCellSizeCm` | MassNavigationFlow grid 宽高按 `FieldWidthCm / FlowCellSizeCm` 改变 | 现有 `MassNavigationFlowSolverStateConfigurationTests` + #288 补充 |
| 在 board/bake/MassNavigationFlow 代码内联 `256` / `64` / `100` | 不允许 | `NavigationSpatialScaleMagicNumberContractTests` 失败并打印文件行号 |

## 合并 / 复用

NAV-0 不合并任何外部分支，不试合 PR #235/#186。复用项：

- `MapTile.Size` 作为 `MacroTileCells` owner。
- `WorldSizeSpec` 作为 board runtime 世界范围产物。
- `VertexChunk.ChunkSize` 作为当前 `TerrainChunkCells` owner，后续 #286 解耦。
- 既有 MassNavigationFlow solver config 的显式校验，继续 fail-fast。
- ArchitectureTests 作为全仓 contract 测试承载点。

## DoD

- 数据驱动：尺度默认值集中在 `SpatialScaleDefaults`，配置项继续显式输入。
- 无 fallback：NAV-0 不新增任何缺失配置兜底。
- 无重复数据源：`MacroTileCells` 引用 `MapTile.Size`；`WorldExtentSpec` 产出 `WorldSizeSpec`，不替换。
- 大小写严格 fail-fast：本步不改变 loader；后续 #283/#285/#287 迁移时沿用严格 loader。
- 附 contract test：`NavigationSpatialScaleMagicNumberContractTests` 扫描 board/bake/MassNavigationFlow 代码。
- 更新 GitBook：本页加入 `gitbook/architecture/README.md` 与 `gitbook/SUMMARY.md`，配置查表加入 `gitbook/reference/`。
- 回链总单：本文回链 #281 与 #282。
