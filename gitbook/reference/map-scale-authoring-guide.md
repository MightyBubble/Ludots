# Mod 作者地图尺度入门

本页写给要做真实地图的 Mod 作者。它不替代 [空间尺度与分辨率 SSOT](../architecture/spatial-scale-and-resolution-ssot.md)，而是把 SSOT 翻译成“我要做多大的地图、要多细的地形/导航/避障/表现，该从哪些配置入口下手”。

交互式入门页见 [`map-scale-authoring-starter.html`](map-scale-authoring-starter.html)。如果你只想先调几个数看世界有多大、网格有多密、FlowWindow 会不会整除、全量/局部 nav bake 大概要多少操作和时间，先打开 HTML；真正落配置前再回到本页查 owner 和约束。Terrain/obstacle/area/agent/bake/editor/Raylib debug 的完整工具链设计见 [`navmesh-authoring-bake-toolchain.md`](navmesh-authoring-bake-toolchain.md)。

## 先分三层

| 层 | 你在问什么 | 主要配置 | 不要混用 |
|---|---|---|---|
| 世界范围 | 地图到底有多大，坐标能走到哪里 | map `Boards[].WidthInMacroTiles` / `HeightInMacroTiles` / `GridCellSizeCm` | 不要拿 `FlowWindow` 或 `TerrainChunk` 当世界大小 |
| 拓扑几何 | Grid/Hex/NodeGraph 的几何语义是什么 | `SpatialType`、Grid 的 `GridCellSizeCm`、HexGrid 的 `HexEdgeLengthCm` | 不要把 Hex edge 当 Grid cell，也不要把 NodeGraph 节点间距塞进 CellCm |
| 空间组织 | 查询、地形、bake、streaming 的块多粗 | `ChunkSizeCells`、`TerrainChunkCells` owner、`Navigation/navmesh.json`、streaming/window 配置 | `PartitionChunk` 不是 navmesh tile，`NavTile footprint` 不是新尺度 owner |
| 运行精度 | 单位移动、避障、路径、表现更新多细 | `MassNavigationConfig.json` solver/cadence/agent profiles、`Navigation/agent_profiles.json`、`Navigation/pathing.json` | `FlowCell` 默认可等于 `CellCm`，但不是 board cell 的别名 |

核心公式：

```text
worldWidthCm  = WidthInMacroTiles  * MacroTileCells(256) * GridCellSizeCm
worldHeightCm = HeightInMacroTiles * MacroTileCells(256) * GridCellSizeCm
```

`GridCellSizeCm = 100` 时，1 个 `MacroTile` 是 `256m x 256m`。`WidthInMacroTiles = 250` 的地图宽度约为 `64km`。

编辑器里的正式 board 创建入口不要求作者填写 chunk 数量，也不要求作者直接填写 `WidthInMacroTiles`。作者输入“目标米数 + `GridCellSizeCm` / `HexEdgeLengthCm` 等尺度参数”，编辑器按 `MacroTileCells` 向上对齐分配范围，再派生出 grid cells、`WidthInMacroTiles` / `HeightInMacroTiles` 与 Terrain/NavTile 数量。`assets/Maps/<map>.json` 仍保存 `WidthInMacroTiles` / `HeightInMacroTiles`，因为这是 runtime 与 IO 的配置真相；米数只是 authoring UI。

## 配置入口速查

| 文件 | 字段 | 作用 |
|---|---|---|
| `assets/Maps/<map>.json` | `Boards[].SpatialType` | `Grid` / `HexGrid` / `NodeGraph`，决定地图 board 类型 |
| `assets/Maps/<map>.json` | `Boards[].WidthInMacroTiles` / `HeightInMacroTiles` | 世界范围，单位是 256-cell MacroTile 数量 |
| `assets/Maps/<map>.json` | `Boards[].GridCellSizeCm` | `CellCm` 输入，决定每个 sim cell 的厘米边长 |
| `assets/Maps/<map>.json` | `Boards[].HexEdgeLengthCm` | HexGrid 专属 hex 边长；Grid / NodeGraph 不受它影响 |
| `assets/Maps/<map>.json` | `Boards[].ChunkSizeCells` | `PartitionChunkCells`，只控制 spatial query/AOI 分区 |
| asset game.json | `startupMapId` | 启动地图 id |
| asset game.json | `presentation.*Capacity` | 表现层容量，跟实体/marker/overlay 数量相关 |
| asset game.json | `presentation.cameraCulling.*DistanceCm` | 近中远 LOD 裁剪距离 |
| asset game.json | `presentation.minimap.*` | 小地图缩放、full-map/follow-camera 等表现策略 |
| MassNavigationConfig asset | `world.solverWindowWidthCm` / `solverWindowHeightCm` | MassFlow 工作窗口，必须和 solver field 宽高一致 |
| MassNavigationConfig asset | `solver.fieldWidthCm` / `fieldHeightCm` | FlowWindow 尺寸，单位 cm |
| MassNavigationConfig asset | `solver.flowCellSizeCm` | FlowCell 分辨率，单位 cm |
| MassNavigationConfig asset | `solver.separationHashCellSizeCm` / `hardResolveHashCellSizeCm` | 避障 hash 分辨率，单位 cm |
| `assets/Configs/Navigation/agent_profiles.json` | `radiusCm` / `heightCm` / `clearanceCm` / `draftCm` / `beamCm` / `mass` / `layer` | agent 几何、避障身份与 NodeGraph 运输容量 SSOT |
| `assets/Configs/Navigation/navmesh.json` | `mode` / `algorithm` / `profiles[].maxClimbCm` / `maxSlopeDeg` | bake/runtime incremental 的导航网格参数 |
| `assets/Configs/Navigation/pathing.json` | `agentTypes[].profileId` / `selection.mode` | 哪些 profile 走精确 route，哪些继续 MassFlow |

生产 Mod 建议在 map `Boards[]` 里显式写 board 尺度。极小 demo 可以沿用默认，但一旦涉及导航、streaming、minimap 或性能验收，就不要靠隐式默认。

## 设计流程

1. 定玩家尺度：玩家同屏看到 50m、500m、5km，还是整个大陆？
2. 定 `GridCellSizeCm`：1 cell 是 1m、2m、5m，还是更粗。越小越精细，cells 总数越大。
3. 定拓扑几何：Grid 用 `GridCellSizeCm` 做 cell 边长；HexGrid 另用 `HexEdgeLengthCm` 做 hex 边长；NodeGraph 的节点/边由图数据表达。
4. 定世界范围：用 `WidthInMacroTiles * 256 * GridCellSizeCm` 算出世界厘米/米/公里。
5. 定分区粒度：`ChunkSizeCells` 影响 spatial query/AOI，不改变世界大小。当前默认 64 cells，必须为 2 的幂。
6. 定导航粒度：agent 半径、clearance、`maxClimbCm`、`maxSlopeDeg` 决定哪些地方可走。
6. 估 bake 预算：用 [`nav-bake-budget-and-estimation.md`](nav-bake-budget-and-estimation.md) 或 HTML 入门页算 full/dirty/window target tiles、layer/profile 乘数、Recast voxel 粒度和耗时区间。
7. 定执行窗口：MassFlow 不是全世界每格都算，通常用 `FlowWindow` 覆盖当前战区、相机焦点或热区。
8. 定运行精度：`flowCellSizeCm` 控流场网格，hash cell 控拥挤/硬解析邻居搜索。
9. 定表现容量：大地图不等于所有 performer 都常驻；用 camera culling、view residency、minimap 策略控制看见什么。

## RTS / 战场型

目标：同屏大量单位，局部战区很细，世界可以很大但每次只在几个战区发生密集互动。

常见思路：

- `GridCellSizeCm = 100`：1 cell = 1m，适合人/士兵/小车级别。
- `WidthInMacroTiles` / `HeightInMacroTiles` 可很大，例如 MassNavigation 示例使用 `250 x 250`，约 `64km x 64km`。
- `ChunkSizeCells = 64`：1 个 PartitionChunk 约 `64m`，适合作为空间查询分区起点。
- `MassNavigationConfig.world.solverWindowWidthCm` / `HeightCm` 和 `solver.fieldWidthCm` / `fieldHeightCm` 先取 `10000cm` 到 `48000cm` 这类战区窗口，而不是全图。
- `flowCellSizeCm = 100` 常作为 1m 流场；密集微操可降到 50cm，但 grid 数量会翻倍。
- `hardResolveHashCellSizeCm = 50` 用于硬解析细邻居，允许小于 `CellCm`，但必须显式配置并整除 FlowWindow。
- 需要小队/道路/门洞等精确移动时，用 `Navigation/pathing.json` 只让特定 profile 走 `PreferGraph` / `PreferMesh`；大军继续 MassFlow。

配置片段：

```json
{
  "Boards": [
    {
      "Name": "default",
      "SpatialType": "Grid",
      "WidthInMacroTiles": 250,
      "HeightInMacroTiles": 250,
      "GridCellSizeCm": 100,
      "ChunkSizeCells": 64
    }
  ]
}
```

MassFlow 起点：

```json
{
  "world": {
    "solverWindowWidthCm": 10000,
    "solverWindowHeightCm": 10000,
    "streamingChunkSizeCm": 6400,
    "streamingRadiusCm": 16000,
    "workAreaPaddingCm": 4000,
    "workAreaMaxWidthCm": 48000,
    "workAreaMaxHeightCm": 48000
  },
  "solver": {
    "fieldWidthCm": 10000,
    "fieldHeightCm": 10000,
    "flowCellSizeCm": 100,
    "separationHashCellSizeCm": 100,
    "hardResolveHashCellSizeCm": 50
  }
}
```

## 大战略 / 4X 型

目标：世界范围大，局部互动少，路径通常更偏图/区域/道路，单位不是每米都要精确避让。

常见思路：

- 世界可以仍用 `GridCellSizeCm = 100` 保持 cm 坐标一致，但“玩法格”不一定等于 engine cell。
- 地块、城市、道路节点可以是业务数据或 NodeGraph，不要为了 4X 地块把 `CellCm` 改成 10km。
- `WidthInMacroTiles` 可以按大陆范围定；如果只做菜单/棋盘 demo，可以不急着开巨大 board。
- 重点调 `Navigation/pathing.json`：商队、军队、船只可按 profile 选择 `PreferGraph` 或 `AutoCheapest`。
- MassFlow 通常只用于局部战斗或拥挤区域，不要把整个大陆做成单个 FlowWindow。
- 小地图一般用 full-map preset，表现容量按城市/军队/marker 数量估算。

配置片段：

```json
{
  "Boards": [
    {
      "Name": "strategic",
      "SpatialType": "NodeGraph",
      "WidthInMacroTiles": 64,
      "HeightInMacroTiles": 64,
      "GridCellSizeCm": 100,
      "ChunkSizeCells": 64
    }
  ]
}
```

Routing 起点：

```json
{
  "agentTypes": [
    {
      "id": "caravan",
      "profileId": "light",
      "selection": { "mode": "PreferGraph" }
    },
    {
      "id": "army",
      "profileId": "heavy",
      "selection": { "mode": "AutoCheapest" }
    }
  ]
}
```

## 开放大世界 / Streaming 型

目标：世界大、相机/玩家局部活动，必须控制 loaded window、表现驻留、运行时结构变化。

常见思路：

- 先定世界范围，再定玩家活动半径。不要让每个系统都尝试覆盖整张地图。
- `StreamingChunk` 应由正式配置或 board 分区推导；禁止私有 loader fallback。
- `MassNavigationConfig.world.streamingChunkSizeCm` 可从 `ChunkSizeCells * GridCellSizeCm` 起步，例如 64 cells * 100cm = `6400cm`。
- `streamingRadiusCm` 覆盖相机/玩家周围几圈 streaming chunk。
- 动态门、桥、建筑等持久结构变化走 `Navigation/navmesh.json` 的 `runtime-incremental` + `cdt`，并给实体加 `RuntimeNavMeshStructuralObstacle`。
- 临时人群拥堵、短寿命 blocker 仍归 MassFlow runtime avoidance，不应触发 navmesh rebuild。

Board 起点：

```json
{
  "Boards": [
    {
      "Name": "default",
      "SpatialType": "Grid",
      "WidthInMacroTiles": 250,
      "HeightInMacroTiles": 250,
      "GridCellSizeCm": 100,
      "ChunkSizeCells": 64,
      "NavigationEnabled": true
    }
  ]
}
```

Runtime incremental 起点：

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

## 精度取舍表

| 你想变得更细 | 改哪里 | 代价 |
|---|---|---|
| 世界坐标/地形采样更细 | 降低 `GridCellSizeCm` | 同样米数下 cells 变多，board/query/bake 负担变大 |
| 空间查询更细 | 降低 `ChunkSizeCells`，仍必须为 2 的幂 | chunk 数量增加，query 管理开销增加 |
| 流场更细 | 降低 `flowCellSizeCm` | Flow grid 宽高增加，流场迭代成本增加 |
| 避障邻居更细 | 降低 `separationHashCellSizeCm` / `hardResolveHashCellSizeCm` | hash bucket 增加，邻居搜索/硬解析成本增加 |
| navmesh / NodeGraph 通过性更细 | 调 `agent_profiles.radiusCm/clearanceCm/draftCm/beamCm` 与 `navmesh.profiles[].maxClimbCm/maxSlopeDeg` | bake 产物或 graph 容量可达性变化，需要重新验证 |
| 远景表现更丰富 | 增加 `presentation.*Capacity` 和 LOD 距离 | 内存、提交、culling、minimap marker 压力增加 |

## 必须遵守的边界

- `WidthInMacroTiles` / `HeightInMacroTiles` 统计 MacroTile 数量，不是 `TerrainChunk` 或 NavTile 数量。
- full nav bake 的目标 tile 数按 `ceil(worldCells / TerrainChunkCells)` 计算；不要把 MacroTile 数当 NavTile 数。
- `WorldExtentSpec` 产出 `WorldSizeSpec`；不要新增第二套世界范围对象。
- `PartitionChunk` 只用于 spatial query/AOI；不要拿它解释 terrain/navmesh tile。
- `TerrainChunk` 当前等于 NavTile footprint；`NavTile footprint` 不是独立尺度 owner。
- `FlowCell` / `AvoidanceHashCell` / `PhysicsBroadphaseCell` 默认可等于 `CellCm`，但 owner 独立。
- MassFlow `world.solverWindowWidthCm/HeightCm` 必须匹配 `solver.fieldWidthCm/fieldHeightCm`。
- `FlowWindow` 必须能被 `flowCellSizeCm`、`separationHashCellSizeCm`、`hardResolveHashCellSizeCm` 整除。
- 所有 profile id、routing mode、navmesh mode/algorithm 的大小写都严格；不要写别名兼容。
- 缺字段、坏 casing、未知 profile/layer 应 fail-fast，不要在 Mod 私有逻辑里补 fallback。

## 推荐阅读顺序

1. [`spatial-scale-configuration.md`](spatial-scale-configuration.md)：先查每个尺度名是什么意思。
2. [`spatial-scale-explorer.html`](spatial-scale-explorer.html)：看单位依赖和尺度板。
3. [`map-scale-authoring-starter.html`](map-scale-authoring-starter.html)：按游戏类型调起点参数。
4. [`agent-profile.md`](agent-profile.md)：配置 agent 半径、质量、layer。
5. [`nav-bake-budget-and-estimation.md`](nav-bake-budget-and-estimation.md)：估 full/dirty/window bake 的参数、操作数、耗时和大图风险。
6. [`navmesh-authoring-bake-toolchain.md`](navmesh-authoring-bake-toolchain.md)：设计地形高度、areaId、障碍物、agent cost、CLI/Web/Raylib debug 的生产工具链。
7. [`nav-bake-context.md`](nav-bake-context.md)：配置 navmesh bake / runtime incremental。
8. [`routing-to-mass-execution.md`](routing-to-mass-execution.md)：让小队走精确路径，大军走 MassFlow。
