# Mod 作者地图尺度入门

本页写给要做真实地图的 Mod 作者。它不替代 [空间尺度与分辨率 SSOT](../architecture/spatial-scale-and-resolution-ssot.md)，而是把 SSOT 翻译成“我要做多大的地图、要多细的地形/导航/避障/表现，该从哪些配置入口下手”。

> **状态**：本页 schema 与键位是 [#1567 空间配置四域归位](https://github.com/MightyBubble/Ludots/issues/1567)的合同。地图可以无板；有板图由根板锚定 host world（`RootBoard` 指定，缺省第一块板），无板图用自己的 `World` 节；预算挂 map 级 `Tuning`。板的范围写厘米矩形，格子只写 `Grid.CellSizeCm`，格数由矩形除以格子边长向下取整。摆放写 `Anchor`：`Local` 是从铺格子那个角量起的厘米，`World` 是这个点在 Ludots 世界里的厘米。根板的 `Anchor.World` 必须是 `(0, 0)`，这就是 Ludots 原点。拓扑原点不在 Ludots 原点的板，要等每板导航寻址落地后才能进导航。

交互式入门页见 [`map-scale-authoring-starter.html`](map-scale-authoring-starter.html)。如果你只想先调几个数看世界有多大、网格有多密、FlowWindow 会不会整除、全量/局部 nav bake 大概要多少操作和时间，先打开 HTML；真正落配置前再回到本页查 owner 和约束。Terrain/obstacle/area/agent/bake/editor/Raylib debug 的完整工具链设计见 [`navmesh-authoring-bake-toolchain.md`](navmesh-authoring-bake-toolchain.md)。

## 先分四域

配置分四个域，每个域一份声明、一个家。写地图时按这个顺序问自己：

| 域 | 你在问什么 | 主要配置 | 不要混用 |
|---|---|---|---|
| 世界 | 世界到底多大，坐标能走到哪里 | map `World.WidthCm` / `World.HeightCm` | 世界尺寸是唯一的，不由任何板决定；不要拿 `FlowWindow` 或板范围当世界大小 |
| 板 | 世界上有几块业务区域，各是什么拓扑、多大、摆在哪 | `Boards[].SpatialType`、`WidthCm`/`HeightCm`、`Anchor`、`Grid.CellSizeCm`、`Hex.EdgeLengthCm` | 板是业务区域（战棋区、hex 港口、路网层），不是性能分区；分区块数不是板的属性 |
| 导航 | 可行走网从哪来、瓦片多粗 | `Navigation/navmesh.json`：source（.height/.grid/.hex）、`tileWorldWidthCm`/`tileWorldHeightCm`、profiles/layers | 瓦片颗粒度是 nav 自己的预算，不从板或地形块推导 |
| 运行精度 | 单位移动、避障、路径、表现更新多细 | `MassNavigationConfig.json` solver/cadence/agent profiles、`Navigation/agent_profiles.json`、`Navigation/pathing.json` | `FlowCell` 默认可等于 `CellCm`，但不是 board cell 的别名 |

核心公式（#1567 目标态）：

```text
hostWorld     = 根板厘米矩形                     # RootBoard 指定，缺省第一块板
topologyOrigin = Anchor.World - Anchor.Local   # 铺格子那个角在 Ludots 世界的位置
cellCount     = floor(WidthCm / CellSizeCm)    # 除不尽的厘米留在局部坐标大的一侧
```

作者请求的米数是编辑器 UI 的输入；JSON 里存的是分配后的尺寸，磁盘即运行时真相。地形数据页（page = 256 cells）是世界 IO 的内部寻址单位，由引擎从世界尺寸派生，作者不需要知道它。

编辑器里的正式 board 创建入口不要求作者填写 chunk 数量或地形数据页数量。作者输入“目标米数 + `CellSizeCm` / `HexEdgeLengthCm` 等尺度参数”，编辑器按预算规则分配范围并预览（分配后世界尺寸、板摆放、nav 瓦片数与内存），再派生出 grid cells 与 Terrain/NavTile 数量落盘。

## 配置入口速查

| 文件 | 字段 | 作用 |
|---|---|---|
| `assets/Maps/<map>.json` | `RootBoard` | host world 根板指定，缺省第一块板 |
| `assets/Maps/<map>.json` | `Boards[].SpatialType` | `Grid` / `HexGrid` / `NodeGraph`，决定板拓扑 |
| `assets/Maps/<map>.json` | `Boards[].WidthCm` / `HeightCm` | 板的厘米矩形 |
| `assets/Maps/<map>.json` | `Boards[].Grid.CellSizeCm` | 方格边长；格数是矩形除边长向下取整 |
| `assets/Maps/<map>.json` | `Boards[].Hex.EdgeLengthCm` | 六边形边长；能放进矩形的整圈六边形留下 |
| `assets/Maps/<map>.json` | `Boards[].Anchor` | `Local` 从铺格子的角量起，`World` 是 Ludots 坐标；根板 `World` 为 `(0, 0)` |
| `assets/Maps/<map>.json` | `Tuning.PartitionChunkCells` / `LoadedChunkCapacity` | map 级分区与 streaming 预算；声明后为唯一预算，容量回填未声明的板（#1567 切 4 已落地，缺省自动推导随切 4b） |
| `assets/Navigation/navmesh.json` | `boards.<name>.source` / `tileWorldWidthCm` / `tileWorldHeightCm` | nav 烘焙源与瓦片颗粒度（#1567 切 3 引入） |
| `assets/Navigation/navmesh.json` | `mode` / `algorithm` / `profiles[].maxClimbCm` / `maxSlopeDeg` | bake/runtime incremental 的导航网格参数 |
| Mod-local assets/game.json | `startupMapId` | 启动地图 id |
| Mod-local assets/game.json | `presentation.*Capacity` | 表现层容量，跟实体/marker/overlay 数量相关 |
| Mod-local assets/game.json | `presentation.cameraCulling.*DistanceCm` | 近中远 LOD 裁剪距离 |
| Mod-local assets/game.json | `presentation.minimap.*` | 小地图缩放、full-map/follow-camera 等表现策略 |
| Mod-local assets/MassNavigationConfig.json | `world.solverWindowWidthCm` / `solverWindowHeightCm` | MassNavigationFlow 工作窗口，必须和 solver field 宽高一致 |
| Mod-local assets/MassNavigationConfig.json | `solver.fieldWidthCm` / `fieldHeightCm` | FlowWindow 尺寸，单位 cm |
| Mod-local assets/MassNavigationConfig.json | `solver.flowCellSizeCm` | FlowCell 分辨率，单位 cm |
| Mod-local assets/MassNavigationConfig.json | `solver.separationHashCellSizeCm` / `hardResolveHashCellSizeCm` | 避障 hash 分辨率，单位 cm |
| `assets/Navigation/agent_profiles.json` | `radiusCm` / `heightCm` / `clearanceCm` / `draftCm` / `beamCm` / `mass` / `layer` | agent 几何、避障身份与 NodeGraph 运输容量 SSOT |
| `assets/Navigation/pathing.json` | `agentTypes[].profileId` / `selection.mode` | 哪些 profile 走精确 route，哪些继续 MassNavigationFlow |

生产 Mod 建议在 map 里显式写 `World` 尺寸与板声明。极小 demo 可以沿用默认，但一旦涉及导航、streaming、minimap 或性能验收，就不要靠隐式默认。

## 设计流程

1. 定世界：这个世界多大？玩家活动范围是 50m、500m、5km 还是整个大陆？写 `World.WidthCm` / `World.HeightCm`。
2. 定玩家尺度：同屏看到什么量级的对抗？由此定 Grid 板的 `CellSizeCm`（1 cell 是 1m、2m、5m 还是更粗）或 Hex 板的 `HexEdgeLengthCm`。越小越精细，cells 总数越大。
3. 摆板：世界上需要几块业务区域？每块什么拓扑、矩形多大、格子角钉在 Ludots 的哪一点？战棋区、hex 港口、战略路网各一块板。
4. 定导航：从哪张源数据烘焙（.height 直采或某板的 .grid/.hex）？瓦片颗粒度多大？agent 半径、clearance、`maxClimbCm`、`maxSlopeDeg` 决定哪些地方可走。
5. 估 bake 预算：用 [`nav-bake-budget-and-estimation.md`](nav-bake-budget-and-estimation.md) 或 HTML 入门页算 full/dirty/window target tiles、layer/profile 乘数、Recast voxel 粒度和耗时区间。
6. 定执行窗口：MassNavigationFlow 不是全世界每格都算，通常用 `FlowWindow` 覆盖当前战区、相机焦点或热区。
7. 定运行精度：`flowCellSizeCm` 控流场网格，hash cell 控拥挤/硬解析邻居搜索。
8. 定表现容量：大地图不等于所有 presenter 都常驻；用 camera culling、view residency、minimap 策略控制看见什么。

## RTS / 战场型

目标：同屏大量单位，局部战区很细，世界可以很大但每次只在几个战区发生密集互动。

常见思路：

- `CellSizeCm = 100`：1 cell = 1m，适合人/士兵/小车级别。
- 世界可很大，例如 `World.WidthCm = 6400000`（64km）；战区板放在行军走廊上。
- `MassNavigationConfig.world.solverWindowWidthCm` / `HeightCm` 和 `solver.fieldWidthCm` / `fieldHeightCm` 先取 `10000cm` 到 `48000cm` 这类战区窗口，而不是全图。
- `flowCellSizeCm = 100` 常作为 1m 流场；密集微操可降到 50cm，但 grid 数量会翻倍。
- `hardResolveHashCellSizeCm = 50` 用于硬解析细邻居，允许小于 `CellCm`，但必须显式配置并整除 FlowWindow。
- 需要小队/道路/门洞等精确移动时，用 `Navigation/pathing.json` 只让特定 profile 走 `PreferGraph` / `PreferMesh`；大军继续 MassNavigationFlow。

配置片段（#1567 目标态）：

```json
{
  "Boards": [
    {
      "Name": "default",
      "SpatialType": "Grid",
      "WidthCm": 40000,
      "HeightCm": 40000,
      "Grid": { "CellSizeCm": 100 },
      "Anchor": { "LocalXCm": 0, "LocalYCm": 0, "WorldXCm": 0, "WorldYCm": 0 }
    }
  ]
}
```

MassNavigationFlow 起点：

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

- 世界用 `World.WidthCm` 按大陆范围定。
- 玩法域分层摆板：地块、城市、道路节点用 NodeGraph 板表达，局部战棋对决用 Grid/Hex 板；“玩法格”不等于 engine cell，不要为了 4X 地块把 `CellSizeCm` 改成 10km。
- 重点调 `Navigation/pathing.json`：商队、军队、船只可按 profile 选择 `PreferGraph` 或 `AutoCheapest`。
- MassNavigationFlow 通常只用于局部战斗或拥挤区域，不要把整个大陆做成单个 FlowWindow。
- 小地图一般用 full-map preset，表现容量按城市/军队/marker 数量估算。

配置片段（#1567 目标态）：

```json
{
  "Boards": [
    {
      "Name": "strategic",
      "SpatialType": "NodeGraph",
      "WidthCm": 6400,
      "HeightCm": 6400,
      "Grid": { "CellSizeCm": 100 },
      "Anchor": { "LocalXCm": 0, "LocalYCm": 0, "WorldXCm": 0, "WorldYCm": 0 }
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
- streaming 预算挂世界层（`World.Tuning`），缺省由引擎按世界尺寸推导；禁止私有 loader fallback。
- `MassNavigationConfig.world.streamingChunkSizeCm` 可从分区尺寸起步，例如 64 cells × 100cm = `6400cm`。
- `streamingRadiusCm` 覆盖相机/玩家周围几圈 streaming chunk。
- 动态门、桥、建筑等持久结构变化走 `Navigation/navmesh.json` 的 `runtime-incremental` + `cdt`，并给实体加 `RuntimeNavMeshStructuralObstacle`。
- 临时人群拥堵、短寿命 blocker 仍归 MassNavigationFlow runtime avoidance，不应触发 navmesh rebuild。

Board 起点（#1567 目标态）：

```json
{
  "Boards": [
    {
      "Name": "default",
      "SpatialType": "Grid",
      "WidthCm": 40000,
      "HeightCm": 40000,
      "Grid": { "CellSizeCm": 100 },
      "Anchor": { "LocalXCm": 0, "LocalYCm": 0, "WorldXCm": 0, "WorldYCm": 0 }
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
| 世界坐标/地形采样更细 | 降低板的 `CellSizeCm` | 同样米数下 cells 变多，board/query/bake 负担变大 |
| 流场更细 | 降低 `flowCellSizeCm` | Flow grid 宽高增加，流场迭代成本增加 |
| 避障邻居更细 | 降低 `separationHashCellSizeCm` / `hardResolveHashCellSizeCm` | hash bucket 增加，邻居搜索/硬解析成本增加 |
| navmesh / NodeGraph 通过性更细 | 调 `agent_profiles.radiusCm/clearanceCm/draftCm/beamCm` 与 `navmesh.profiles[].maxClimbCm/maxSlopeDeg` | bake 产物或 graph 容量可达性变化，需要重新验证 |
| 远景表现更丰富 | 增加 `presentation.*Capacity` 和 LOD 距离 | 内存、提交、culling、minimap marker 压力增加 |

## 必须遵守的边界

- host world 由根板锚定（`RootBoard`，缺省第一块板）；无板图沿用 game.json `world`。
- 板的范围是厘米矩形。格数是 `WidthCm / Grid.CellSizeCm` 向下取整，除不尽的厘米留在局部坐标大的一侧，不新开一格。卫星板必须整块落在根板矩形里，越界在加载期失败。格子角的世界坐标是 `Anchor.World − Anchor.Local`。根板的 `Anchor.World` 必须是 `(0, 0)`，这是 Ludots 原点。拓扑原点不在 Ludots 原点的板还不能进导航。
- 一图一张 nav 网格：同图多板在 `Navigation/navmesh.json` 声明的瓦片尺寸必须一致（两轴分别比较），混合粒度加载期 fail-fast——按板粒度寻址是 #1567 切 2 后续。瓦片与板尺寸两轴独立，非正方形（板、世界、瓦片）均为一等公民。
- nav 瓦片颗粒度在 `Navigation/navmesh.json` 显式声明；不要从板的 cell/chunk 推导，也不要把 `PartitionChunk` 当 navmesh tile。
- `PartitionChunk` 只用于世界层空间分区/AOI；`TerrainChunk` 是逻辑地形块，两者都不是 nav 瓦片尺度。
- `FlowCell` / `AvoidanceHashCell` / `PhysicsBroadphaseCell` 默认可等于 `CellCm`，但 owner 独立。
- MassNavigationFlow `world.solverWindowWidthCm/HeightCm` 必须匹配 `solver.fieldWidthCm/fieldHeightCm`。
- `FlowWindow` 必须能被 `flowCellSizeCm`、`separationHashCellSizeCm`、`hardResolveHashCellSizeCm` 整除。
- 所有 profile id、routing mode、navmesh mode/algorithm 的大小写都严格；不要写别名兼容。
- 缺字段、坏 casing、未知 profile/layer 应 fail-fast，不要在 Mod 私有逻辑里补 fallback。

## 迁移对照（#1567）

对照与迁移动作（切 1 已执行，旧键现行加载即 fail-fast）：

| 现状键 | 目标键 | 迁移动作 |
|---|---|---|
| `Boards[].WidthInMacroTiles` / `HeightInMacroTiles`、`WidthCells` / `HeightCells`、`GridCellSizeCm` | `Boards[].WidthCm` / `HeightCm` + `Grid.CellSizeCm` | 范围改写厘米矩形。格数 = 矩形 ÷ 格子边长，向下取整。host world 由根板（`RootBoard`，缺省首板）锚定 |
| 板恒居中，或 `Boards[].OriginXCm` / `OriginYCm` | `Boards[].Anchor` | 四个厘米数。根板 `World` 为 `(0, 0)`。没写过原点的存量图，格子角留在 Ludots `(0, 0)`，矩形从这一点向局部坐标增大的方向铺开 |
| `Boards[].WidthHexes` / `HeightHexes`、`HexEdgeLengthCm` | `Boards[].WidthCm` / `HeightCm` + `Hex.EdgeLengthCm` | 六边形个数改为矩形里能放下的整圈 |
| `Boards[].ChunkSizeCells` / `LoadedChunkCapacity` | map `Tuning.PartitionChunkCells` / `LoadedChunkCapacity` | 切 4 迁入 map 级，缺省可推导（4b） |
| `Boards[].NavTileGrid`（含 `originXcm/originZcm`、`widthChunks/heightChunks`） | `Navigation/navmesh.json` `maps.<mapId>.boards.<name>` 条目 | 已迁移；板级出现即 fail-fast |
| game.json `gridCellSizeCm` / `worldWidthInMacroTiles` / `worldHeightInMacroTiles` | game.json `world`（仅 boot 占位）+ 根板 | 升格迁移；运行时 host world 出自根板，boot 占位只在进图前生效 |

旧键在新键生效后加载即失败并指向新键，不提供别名兼容。没写过原点的存量图，格子角仍在 Ludots `(0, 0)`；矩形从这一点向局部坐标增大的方向铺开。原先落在旧居中范围里的坐标，按根板半宽、半高平移进这张矩形，人和镜头还站在原来的相对位置上。

## 推荐阅读顺序

1. [`spatial-scale-configuration.md`](spatial-scale-configuration.md)：先查每个尺度名是什么意思。
2. [`spatial-scale-explorer.html`](spatial-scale-explorer.html)：看单位依赖和尺度板。
3. [`map-scale-authoring-starter.html`](map-scale-authoring-starter.html)：按游戏类型调起点参数。
4. [`agent-profile.md`](agent-profile.md)：配置 agent 半径、质量、layer。
5. [`nav-bake-budget-and-estimation.md`](nav-bake-budget-and-estimation.md)：估 full/dirty/window bake 的参数、操作数、耗时和大图风险。
6. [`navmesh-authoring-bake-toolchain.md`](navmesh-authoring-bake-toolchain.md)：设计地形高度、areaId、障碍物、agent cost、CLI/Web/Raylib debug 的生产工具链。
7. [`nav-bake-context.md`](nav-bake-context.md)：配置 navmesh bake / runtime incremental。
8. [`routing-to-mass-execution.md`](routing-to-mass-execution.md)：让小队走精确路径，大军走 MassNavigationFlow。
