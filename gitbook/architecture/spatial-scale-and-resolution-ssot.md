# 空间尺度与分辨率 SSOT

所属总单：[Epic #281](https://github.com/MightyBubble/Ludots/issues/281)。本页落实 [NAV-0 #282](https://github.com/MightyBubble/Ludots/issues/282)，是后续 NAV-1 到 NAV-10 的尺度词汇唯一来源。[空间配置四域归位 #1567](https://github.com/MightyBubble/Ludots/issues/1567)（世界/板/导航/执行分离）带来的 owner 与键位变更同样以本页层级表和映射表为唯一来源；本页标注「#1567 目标态」的行在 #1567 各切合入前仍是现状语义。

## 背景（现状）

基线 `origin/main` @ `f58bd23d` 中，`tile`、`grid`、`chunk`、`cell` 被多个系统复用，`256`、`64`、`100`、`50`、`16` 等数字散落在 board、bake 与 MassNavigationFlow 代码中。典型现状：

| 名称 | 实际含义 | 值 | 位置 |
|---|---:|---:|---|
| `GridCellSizeCm` | sim 原子 cell 边长 | 100 cm | `src/Core/Map/Board/BoardConfig.cs` |
| `HexEdgeLengthCm` | hex 边长 | 400 cm | `src/Core/Map/Board/BoardConfig.cs` / `src/Core/Map/Hex/HexMetrics.cs` |
| `MapTile.Size` / `WorldMap.TileSize` | 256-cell IO/寻址地形数据页 | 256 cells | `src/Core/Map/MapTile.cs` / `src/Core/Map/WorldMap.cs` |
| `BoardConfig.WidthInTiles` / `HeightInTiles` | 名为 tiles，实为 256-cell 地形数据页数量 | 默认 64 | `src/Core/Map/Board/BoardConfig.cs` |
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
- `WidthInTiles` / `HeightInTiles` 实际是地形数据页数量，不是 cell、TerrainChunk 或 NavTile 数量。

## 目标（预期）

唯一基准单位是 **`CellCm`**。`Cell` 是所有 board、bake、flow、avoidance、broadphase 尺度换算的原子单位。NAV-0 新增 `src/Core/Spatial/SpatialScaleDefaults.cs`，集中命名现有尺度默认值；后续配置项必须引用本文档中的目标名和 owner。

#1567 在此之上把空间配置归位为四个域，每个域一份声明、一个家：

| 域 | 管什么 | 配置的家 | 与 board 的关系 |
|---|---|---|---|
| 世界（host world） | 坐标基准；实体空间分区、AOI/streaming、越界边界、相机/minimap 全图 | 有板图：根板锚定（map `RootBoard`，缺省第一块板）；无板图：map `World` 节（boot 世界来自宿主图 startupMapId） | 根板即自己 |
| 板 | 业务区域：拓扑、度量、摆放（世界系 origin）、格子语义 | map JSON `Boards[]` | 就是自己 |
| 导航 | 烘焙源选择、瓦片颗粒度（显式两轴）、层/profile/语义/障碍 | `Navigation/navmesh.json` | board 只是可选源 + 寻址 scope |
| 执行 | FlowWindow/FlowCell/避障 hash | `MassNavigationConfig.json` | 无 |

世界的地形数据页数、板的 AABB、nav 格网覆盖是引擎派生值，不进 authoring JSON。

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
| `Cell` / `CellCm` | 1 | `CellCm`，默认 100 | `SpatialScaleDefaults.CellCm`，板配置写 `Boards[].Grid.CellSizeCm` | sim 原子单位、板内格子、bake cm 换算、默认 flow/hash cell | 是，当前经 `BoardConfig.Grid.CellSizeCm` 与 MassNavigationFlow solver 配置显式给出 | 必须 > 0；后续所有派生值必须是整数倍或显式说明 |
| `HexEdgeLengthCm` | hex 边长，非 cell 派生 | 默认 400 | `SpatialScaleDefaults.DefaultHexEdgeLengthCm`，板配置写 `Boards[].Hex.EdgeLengthCm` | HexGrid board 的 hex 轴长、world position / AOI / render layout | 是，仅 Hex / HexGrid board 生效 | 必须 > 0；不影响 Grid / NodeGraph board |
| `PartitionChunk` | `PartitionChunkCells`，默认 64 | `PartitionChunkCells * CellCm` | `BoardConfig.ChunkSizeCells` / `SpatialScaleDefaults.PartitionChunkCells`；#1567 目标态归 `World.Tuning.PartitionChunkCells`，分区是世界层预算，不挂板 | 空间分区、AOI、query backend | 是 | 必须 > 0 且为 2 的幂 |
| `TerrainChunk` | `TerrainChunkCells`，固定 64 | `TerrainChunkCells * CellCm` 的逻辑足迹；hex bake 还会乘 hex metric | `VertexChunk.ChunkSize` / `SpatialScaleDefaults.TerrainChunkCells` | 逻辑地形块；当前等于 NavTile 足迹 | 否 | 当前固定 64；grid/hex 共用抽象在 #286 落地 |
| `NavTile footprint` | = `TerrainChunk` | = `TerrainChunk` footprint | NavMesh bake (`NavTileBuilder` / `BakePipeline`)；#1567 目标态解除与 `TerrainChunk` 的焊接，颗粒度见 `NavTileGranularity` | navmesh 产物 tile 足迹 | 否 | 现状只作为 `TerrainChunk` 的用途；#1567 切 3 后此行废止，由 navmesh.json 显式声明取代 |
| `NavTileGranularity`（#1567 目标态） | 由声明值与世界 cm 决定 | `tileWorldWidthCm` × `tileWorldHeightCm` | `Navigation/navmesh.json` 每板寻址条目 | nav 烘焙/重烤的瓦片颗粒度预算，独立于板与地形块 | 是 | 必须 > 0；不从 `CellSizeCm × ChunkSizeCells` 推导；hex 两轴（如 44340×38400）由此表达 |
| `TerrainPage`（地形数据页） | `TerrainPageCells` = `MapTile.Size` = 256 | `TerrainPageCells * CellCm` | `MapTile.Size` / `SpatialScaleDefaults.TerrainPageCells` | 世界层 IO/寻址地形数据页；#1567 目标态为派生值（由 `World.WidthCm/HeightCm` 换算），不再进 authoring | 否，数量可配 | `TerrainPageCells` 引用 `MapTile.Size`；数量键 `WidthInMacroTiles` / `HeightInMacroTiles` 自 #1567 切 1 起 fail-fast |
| `StreamingChunk` | N x `PartitionChunk` | N x `PartitionChunkCells * CellCm` | streaming/loaded chunk owner；NodeGraph 当前通过 `WorldGridLoadedChunks` 消费；#1567 目标态容量归 `World.Tuning` | 流式加载、loaded graph rebuild | 是 | 必须显式配置或由分区推导；禁止私有 loader fallback |
| `WorldExtent` | 根板 `floor(WidthCm / CellSizeCm)` / boot `WidthCm / CellCm` | 根板 `WidthCm` / `HeightCm` | 运行时 host world = 根板 `BoardExtentSpec`；引擎 boot 占位 = `GameConfig.World`（`WorldExtentSpec`） | 世界范围、坐标转换、minimap/full-map bounds、越界校验 | 是 | 旧地形数据页数量键加载即失败，无别名兼容 |
| `BoardExtent` | `floor(WidthCm / CellSizeCm)` | `WidthCm` / `HeightCm` | map JSON `Boards[]` | 板的厘米矩形。格数是矩形除以 `Grid.CellSizeCm` 向下取整，余数留在局部坐标大的一侧 | 是 | 宽高必须盖住至少一格；六边形板另写 `Hex.EdgeLengthCm`，只保留能放进矩形的整圈 |
| `BoardAnchor` | —— | `Anchor.LocalXCm/LocalYCm` 与 `Anchor.WorldXCm/WorldYCm` | map JSON `Boards[]` | 板上一个点在 Ludots 世界的位置。`Local` 从铺格子的角量起，`World` 是 Ludots 厘米。格子角的世界坐标 = `World − Local` | 是 | 根板 `World` 必须是 `(0, 0)`，这是 Ludots 原点；矩形必须落在根板内；拓扑原点不在 Ludots 原点的板暂不参与导航 |
| `FlowWindow` | `FieldWidthCm / CellCm` by `FieldHeightCm / CellCm` | `FieldWidthCm` x `FieldHeightCm` | MassNavigationFlow solver config | 执行层滑窗/工作区 | 是 | 宽高必须 > 0；必须能被 `FlowCell`、`AvoidanceHashCell` 整除 |
| `FlowCell` | `FlowCellSizeCm / CellCm` | 默认 100 | MassNavigationFlow solver `flowCellSizeCm` / `SpatialScaleDefaults.FlowCellCm` | 流场网格 cell | 是 | 必须 > 0；`FlowWindow` 宽高必须整除它 |
| `AvoidanceHashCell` | `separationHashCellSizeCm / CellCm` 或 `hardResolveHashCellSizeCm / CellCm` | separation 默认 100；hard-resolve 默认 50 | MassNavigationFlow solver / `SpatialScaleDefaults.Avoidance*HashCellCm` | 分离邻居哈希、硬解析候选哈希 | 是 | 必须 > 0；`FlowWindow` 宽高必须整除它 |
| `PhysicsBroadphaseCell` | `PhysicsBroadphaseCellCm / CellCm` | 默认 100 | Physics2D / future physics broadphase config | broadphase spatial hash | 是 | 必须显式配置；禁止缺失时静默 fallback |

每个概念恰好一个名字、一个 owner。`NavTile footprint` 是 `TerrainChunk` 的用途，不再作为独立尺度 owner。

## 板域查询（切 2 服务层已落地）

引擎 `TryGetBoardScopedSpatialQueries(boardName)`：共享世界分区 + 按板语义的查询 facade——实体索引保持板盲（世界系），板域换入该板的转换器/HexMetrics/范围，hex 卫星板以自身拓扑语义作答；缓存随地图会话切换清理。图 op 节点级 `board` 参数（VM 编译面）与 effect preset 的 `boardRef` 参数为后续薄层。

## 命名 Taxonomy

- `Cell`：sim 原子格。唯一基准字段名为 `CellCm`；历史 `GridCellSizeCm` 仍作为 board config 输入。
- `HexEdgeLengthCm`：HexGrid board 的 hex 边长。它只影响 `HexMetrics`、`HexGridBoard`、hex 位置/查询/渲染，不改 Grid / NodeGraph board。
- `PartitionChunk`：空间分区块。只描述 query/AOI 分区，不描述地形或 navmesh。
- `TerrainChunk`：逻辑地形块。当前 hex owner 是 `VertexChunk`；#286 后 grid/hex 共用地形抽象仍沿用此名。
- `TerrainPage`（地形数据页）：256-cell IO/寻址地形数据页。owner 是 `MapTile.Size`，常量模块只引用它；#1567 目标态为世界层派生值，不是 authoring 词汇。
- `StreamingChunk`：流式加载块。不要用 `chunk` 裸词。
- `WorldExtent`：世界范围 authoring/计算概念。`WorldExtentSpec` 产出 `WorldSizeSpec`（仅引擎 boot 占位）；host world 由根板锚定，地图可以无板。
- `BoardExtent`：板的厘米矩形。格数是矩形除以 `Grid.CellSizeCm` 向下取整。
- `BoardAnchor`：板上一个点在 Ludots 世界的位置。格子角 = `Anchor.World − Anchor.Local`。根板 `World` 为 `(0, 0)`。见 `gitbook/navmesh-features/board-addressing.md`。
- `NavTileGranularity`（#1567）：nav 瓦片颗粒度，navmesh.json 显式两轴世界尺寸，独立于 `TerrainChunk` 与板。
- `FlowWindow` + `FlowCell`：MassNavigationFlow 执行层滑窗与流场分辨率。
- `AvoidanceHashCell`：MassNavigationFlow 分离/硬解析哈希 cell。
- `PhysicsBroadphaseCell`：physics broadphase cell。

## 现状名到目标名映射

| 现状名 | 目标名 | NAV-0 动作 | 后续动作 |
|---|---|---|---|
| `BoardConfig.GridCellSizeCm` | `CellCm` | 默认值引用 `SpatialScaleDefaults.CellCm` | 继续作为 `WorldExtentSpec` 输入；是否改字段名另立 |
| `BoardConfig.HexEdgeLengthCm` | `HexEdgeLengthCm` | 默认值引用 `SpatialScaleDefaults.DefaultHexEdgeLengthCm` | 仅 HexGrid board 生效；由 `HexMetrics` / `HexGridBoard` 消费 |
| `MapTile.Size` | `TerrainPageCells` | `SpatialScaleDefaults.TerrainPageCells` 引用它 | 保持 owner，不复制新 owner |
| `WorldMap.TileSize` | `TerrainPageCells` | 引用 `SpatialScaleDefaults.TerrainPageCells` | 后续可移除重复旧名 |
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

#1567 四域归位追加映射（现状 → 目标态）：

| 现状名 | 目标名 | 迁移动作 |
|---|---|---|
| `BoardConfig.WidthInMacroTiles` / `HeightInMacroTiles` | `World.WidthCm` / `World.HeightCm` 派生地形数据页数 | 切 1：旧键 fail-fast，机器迁移 `World = 旧板范围居中` |
| `BoardConfig.ChunkSizeCells` | `World.Tuning.PartitionChunkCells` | 切 4：预算归世界层，缺省由引擎按世界尺寸推导 |
| `BoardConfig.LoadedChunkCapacity` | `World.Tuning.LoadedChunkCapacity` | 切 4：同上 |
| `BoardConfig.NavTileGrid`（含 `OriginXcm/Zcm`、`widthChunks/heightChunks`、`chunkSizeCells`、`cellSizeCm`） | `Navigation/navmesh.json` 每板寻址条目（`source` + `tileWorldWidthCm/HeightCm`） | 切 3（依赖 PR #1484 合入）：迁出 BoardConfig，颗粒度显式化 |
| 板恒居中（无 origin 字段），以及 `OriginXCm` / `OriginYCm` | `Boards[].Anchor` | 格子角钉在作者写的点上。根板 `World` 为 `(0, 0)`。没写过原点的存量图，格子角留在 Ludots `(0, 0)` |
| `GameConfig.WorldWidthInMacroTiles` 三件套 | map `World` 节 | 切 1：升格迁入，消灭双真相源 |

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
| 在 `src/Core/Map/Board/GridBoard.cs` 将 `SpatialScaleDefaults.TerrainPageCells` 临时改回字面 `256` 后重跑上述命令 | 测试失败，输出 `src/Core/Map/Board/GridBoard.cs:<line>: literal 256` |
| 撤销临时改动后重跑 | 测试恢复通过 |
| 打开本页对照 `src/Core/Spatial/SpatialScaleDefaults.cs` | 层级表中每个默认尺度都有命名常量或明确 owner |

## 配置指南

NAV-0 不新增配置 schema。现有配置项按本文口径解释：

| 配置项 | 目标概念 | 单位 | 范围 / 约束 | 归属 |
|---|---|---|---|---|
| `BoardConfig.GridCellSizeCm` | `CellCm` | cm | > 0 | 运行时读取；JSON 写 `Boards[].Grid.CellSizeCm` |
| `BoardConfig.HexEdgeLengthCm` | `HexEdgeLengthCm` | cm | > 0；仅 Hex / HexGrid | 运行时读取；JSON 写 `Boards[].Hex.EdgeLengthCm` |
| `WidthInMacroTiles` / `WidthCells` | 历史键 | —— | 出现即加载失败 | 现行写法 `Boards[].WidthCm` |
| `HeightInMacroTiles` / `HeightCells` | 历史键 | —— | 出现即加载失败 | 现行写法 `Boards[].HeightCm` |
| `BoardConfig.ChunkSizeCells` | `PartitionChunkCells`（#1567 切 4 前） | cells | > 0 且 2 的幂 | spatial partition |
| map `RootBoard`（#1567） | host world 锚定 | 板名 | 缺省第一块板；必须匹配存在的板 | world authoring |
| `Boards[].WidthCm` / `HeightCm` | `BoardExtent` | cm | 宽高必须盖住至少一格 | 板的厘米矩形 |
| `Boards[].Grid.CellSizeCm` | `CellCm` | cm | > 0 | 格数 = 矩形 ÷ 边长，向下取整 |
| `Boards[].Hex.EdgeLengthCm` | `HexEdgeLengthCm` | cm | > 0；仅 Hex / HexGrid | 能放进矩形的整圈六边形 |
| `Boards[].Anchor` | `BoardAnchor` | cm | 根板 `World` 为 `(0, 0)`；卫星矩形必须落在根板内 | 格子角 = `World − Local` |
| navmesh.json `tileWorldWidthCm/HeightCm`（#1567 目标态） | `NavTileGranularity` | cm | > 0；不从板推导 | nav authoring（切 3 引入） |
| map `Tuning.PartitionChunkCells` / `LoadedChunkCapacity`（#1567 切 4 已落地） | `PartitionChunk` / streaming 容量 | cells / 个 | 可空；声明后为唯一预算，冲突的板级字段 fail-fast，容量回填未声明的板 | map 级预算；显式写默认值当前与未声明不可区分，歧义消除随切 4b |
| `MassNavigationFlowSolverConfig.fieldWidthCm` / `fieldHeightCm` | `FlowWindow` | cm | > 0；被 FlowCell/hash cell 整除 | MassNavigationFlow solver |
| `MassNavigationFlowSolverConfig.flowCellSizeCm` | `FlowCell` | cm | > 0 | MassNavigationFlow solver |
| `MassNavigationFlowSolverConfig.separationHashCellSizeCm` | `AvoidanceHashCell` | cm | > 0 | MassNavigationFlow solver |
| `MassNavigationFlowSolverConfig.hardResolveHashCellSizeCm` | `AvoidanceHashCell` | cm | > 0 | MassNavigationFlow solver |

更短的查表入口见 `gitbook/reference/spatial-scale-configuration.md`。

## 配置到行为联动

| 改动 | 预期行为 | 自动化钉死 |
|---|---|---|
| 修改板的 `Grid.CellSizeCm` | 厘米矩形不变，格数按除法向下取整变化 | 板范围 contract |
| 修改 `PartitionChunkCells` 或 `ChunkSizeCells` | spatial query/AOI 分区粒度变化，世界范围不变化 | 现有 spatial partition tests + #283 补充 |
| 修改 `FlowCellSizeCm` | MassNavigationFlow grid 宽高按 `FieldWidthCm / FlowCellSizeCm` 改变 | 现有 `MassNavigationFlowSolverStateConfigurationTests` + #288 补充 |
| 在 board/bake/MassNavigationFlow 代码内联 `256` / `64` / `100` | 不允许 | `NavigationSpatialScaleMagicNumberContractTests` 失败并打印文件行号 |
| 修改 map `World.WidthCm/HeightCm`（#1567 切 1 后） | 世界边界、越界校验、minimap 全图按声明变化；地形数据页数为派生值 | #1567 切 1 contract：World 派生链 + 全 mod 扫描 |
| 修改 `Boards[].Anchor` | 格子角移到 `World − Local`，矩形跟着走。根板世界范围仍是根板自己的厘米矩形 | 锚点 contract：根板 `World` 必须是 `(0, 0)`；拓扑原点不在 Ludots 原点的板暂不参与导航 |
| 修改 navmesh.json `tileWorldWidthCm/HeightCm`（#1567 切 3 后） | nav 瓦片颗粒度与重烤预算变化，板范围与地形块不变 | #1567 切 3 contract |

## 合并 / 复用

NAV-0 不合并任何外部分支，不试合 PR #235/#186。复用项：

- `MapTile.Size` 作为 `TerrainPageCells` owner。
- `WorldSizeSpec` 作为 board runtime 世界范围产物。
- `VertexChunk.ChunkSize` 作为当前 `TerrainChunkCells` owner，后续 #286 解耦。
- 既有 MassNavigationFlow solver config 的显式校验，继续 fail-fast。
- ArchitectureTests 作为全仓 contract 测试承载点。

## DoD

- 数据驱动：尺度默认值集中在 `SpatialScaleDefaults`，配置项继续显式输入。
- 无 fallback：NAV-0 不新增任何缺失配置兜底。
- 无重复数据源：`TerrainPageCells` 引用 `MapTile.Size`；`WorldExtentSpec` 产出 `WorldSizeSpec`，不替换。
- 大小写严格 fail-fast：本步不改变 loader；后续 #283/#285/#287 迁移时沿用严格 loader。
- 附 contract test：`NavigationSpatialScaleMagicNumberContractTests` 扫描 board/bake/MassNavigationFlow 代码。
- 更新 GitBook：本页加入 `gitbook/architecture/README.md` 与 `gitbook/SUMMARY.md`，配置查表加入 `gitbook/reference/`。
- 回链总单：本文回链 #281 与 #282。
- #1567 回链：四域归位每一切的 owner、键位与派生规则变更，以本页层级表/映射表为唯一词汇来源，切 PR 回链 #1567。
