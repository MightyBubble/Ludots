# CrowdSimulation 配置合同参考

群体模拟的配置以代码为准：`CrowdSimulationConfig`、`CrowdTraversalConfig`、各 `*Facts` / `Validate`、`StrictJsonOptions.CreateCamelCase()`。缺键、未知键、类型不对都在装载时报错，报文带文件路径和键路径。世界距离用厘米（`Cm`）；一部分长度在 `CrowdSimulationConfigLoader.Load` 里除以 `maps.<mapId>.navCellSizeCm` 变成导航格。

核心只认一组固定词：**区域**（area）、**移动类型**（agent type，通行矩阵的一行）、**链接种类**（link kind）、两层地表（`base` / `upper`）、三种足迹（`square` / `disc` / `path`）、三种效果（`block` / `setArea` / `surface`）、两种放置（`unrestricted` / `spanEnds`）。`open`、`deep`、`jump`、`foot` 等都是数据里的 id，不是硬编码枚举。

## 文件一览

| 文件 | 合并策略 | 典型提供方 | 主要读取方 |
| --- | --- | --- | --- |
| `CrowdSimulation/traversal.json` | DeepObject | `CrowdSimulationStandardMod`；`CrowdSimulationDemoContentMod` 追加区域与列 | `CrowdTraversalConfig` → `CrowdSimulationConfigLoader` |
| `CrowdSimulationConfig.json` | DeepObject | `CrowdSimulationStandardMod`；地图 Mod 写 `world` / `deploy` / 本地结构模板 | `CrowdSimulationConfig` → `CrowdSimulationConfigValidator` → `CrowdSimulationConfigLoader` |
| `Navigation/navmesh.json` 的 `maps.<mapId>` | DeepObject | 地图 Mod | `NavMeshBakeConfigLoader`；群体只读 `solver=crowd-simulation` 的键集 |
| `Navigation/agent_profiles.json` | ArrayById | `CrowdSimulationStandardMod`；地图 Mod 追加体型 | `AgentProfileRegistry`；移动类型取 `navProfileId` |
| `Presentation/crowd_structure_palette.json` | DeepObject | `CrowdSimulationDemoContentMod`；S4 等追加条目 | `CrowdStructurePalette`（表现层，内核不读） |
| `Presentation/crowd_player_colors.json` | DeepObject | 各地图 Mod（`Players` 每名玩家一条） | `CrowdPlayerColors` → 会话激活时 `TeamColorPalette` |
| `CrowdSimulationDebug.json` | DeepObject | `CrowdSimulationUiMod` 默认；演示 Mod 覆盖 `presentation` / `session` 等 | `CrowdSimulationDebugConfig` |
| `CrowdSimulationPlayerView.json` | DeepObject | `CrowdSimulationUiMod` 默认；演示 Mod 可覆盖 | `CrowdPlayerViewConfig` |
| `CrowdSimulationDebugView.json` | DeepObject | 同上 | `CrowdSimulationDebugViewConfig` |
| `CrowdSimulationPresentationPalette.json` | DeepObject | `CrowdSimulationUiMod` | `CrowdPresentationPalette`（合并进两份视图的 `colors`） |
| `terrain/<map>.navsurface` | 烘焙产物 | 工具 `nav bake-crowd-navsurface` | `NavSurfaceAsset`；`NavSurfaceContract` 对照配置 |

正本 JSON 在仓库 Mod 里，例如：

- `mods/capabilities/navigation/CrowdSimulationMod/assets/config_catalog.json`
- `mods/capabilities/navigation/CrowdSimulationUiMod/assets/config_catalog.json`
- `mods/capabilities/navigation/CrowdSimulationStandardMod/assets/CrowdSimulation/traversal.json`
- `mods/capabilities/navigation/CrowdSimulationStandardMod/assets/CrowdSimulationConfig.json`
- `mods/showcases/crowd_simulation/CrowdSimulationDemoContentMod/assets/…`
- `mods/showcases/crowd_simulation/CrowdSimulationMinimalMod/assets/…`
- `mods/showcases/crowd_simulation/CrowdSimulationS1Terrain1337Mod/assets/Navigation/navmesh.json`

## `CrowdSimulation/traversal.json`

解析：`CrowdTraversalConfig.Parse`。根对象只允许 `areas`、`terrainAreas`、`linkKinds`、`agentTypes`。

### `areas.<areaId>`

| 键 | 类型 | 必填 | 约束 | 读取方 |
| --- | --- | --- | --- | --- |
| `ignoresSlope` | bool | 是 | — | 烘焙坡度判定（`NavContextBaker`） |

合并后属性顺序 = 区域下标（栅格 u8，最多 256 个）。

### `terrainAreas.<terrainId>`

| 类型 | 必填 | 约束 |
| --- | --- | --- |
| 区域 id 字符串 | 是 | 必须是 `areas` 里的 id；不能指向上层区域（见下文） |

全局一张表。各地形顺序在 `maps.<mapId>.terrainTypes`；格值 = 该数组下标。

### `linkKinds.<kindId>`

| 键 | 类型 | 必填 | 约束 |
| --- | --- | --- | --- |
| `traversalSpeedRatio` | number | 是 | > 0；走链接时速度 × 该值 |

最多 255 种。群体内核目前只消费一种（`CrowdSimulationConfigLoader` 装载时检查）。

### `agentTypes.<typeId>`

| 键 | 类型 | 必填 | 约束 |
| --- | --- | --- | --- |
| `maxSlopeDeg` | number 或 `null` | 是 | 数 ∈ (0, 90)；`null` 表示不限坡度 |
| `links.<kindId>` | `false` 或对象 | 是 | 必须覆盖 `linkKinds` 每一项 |
| `cells.<areaId>` | `false` 或对象 | 是 | 必须覆盖 `areas` 每一项 |

`links.<kindId>` 为对象时：

| 键 | 类型 | 约束 |
| --- | --- | --- |
| `upCm` | int | > 0 |
| `downCm` | int | ≥ `upCm` |
| `rangeNavCells` | int | > 0 |
| `costPerNavCell` | number | > 0；且 ≥ 本行所有可通行格 `cost` 的最小值 |

`cells.<areaId>` 为对象时：

| 键 | 类型 | 约束 |
| --- | --- | --- |
| `cost` | number | > 0 |
| `speedDivisor` | number | > 0 |
| `depart` | string[] | 链接种类 id，不重复；种类须在 `links` 里为对象 |
| `arrive` | string[] | 同上 |

若 `links.<kind>` 是对象，本行至少一格在 `depart` 或 `arrive` 里引用它，否则装载报错。

**上层区域**：被某个 `surface` 效果引用的区域。其格 `depart` / `arrive` 必须为空；不能出现在 `terrainAreas`；不能作为 `setArea` 或 `spanEnds` 的区域（`ValidateUpperLayerAreas`）。

运行时只装载被单位模板引用的移动类型行；`AgentTypeIndex` 在被引用行里按合并顺序编号。

## `CrowdSimulationConfig.json`

反序列化：`CrowdSimulationConfig.Load`（`StrictJsonOptions`，`formations` / `structures.templates` 禁止 null 与数组形态）。数值范围：`CrowdSimulationConfigValidator.Validate`；与板宽、导航格相关的规则在 `CrowdSimulationConfigLoader.Load`。

### 顶层节

| 节 | 用途 |
| --- | --- |
| `world` | `seed`（int，地图可覆盖） |
| `worker` | `planTimeoutMs`（int，> 0，毫秒） |
| `agents` | `avoidanceRadiusScale`（> 0）；个人半径 = 体型 `radiusCm` × 该值 |
| `navmesh` | 瓦片烘焙参数（`minRegionNavCellArea`、`maxSimplificationErrorNavCells`、`maxEdgeLenNavCells`、`maxVertsPerPoly` 3–12、`portalInsetNavCells`） |
| `hpa` | `maxEntranceWidthNavCells`（≥ 1） |
| `navtile` | `sizeCm`（须整除 `navCellSizeCm`）、`cacheCapacity` |
| `flowfield` | `cacheCapacity`、`poolCapacity`、`corridorPaddingNavTiles`、`maxRebuildsPerTick` |
| `planning` | `latencyTicks` 1–60、`refreshLatencyTicks`、`maxRefreshesPerTick`、`rebakeSlices` 1–3 |
| `fog` | `rateHz`、`cellSizeCm`（≥ 100 cm 且整除导航格）、`visionCm`、`revealTicks`、`slotCacheCapacity`、`variantCapacity`、`lineOfSight`、`eyeCm` |
| `formation` | 阵型尺度、领队、槽位、magic box、点积阈值等（见 `CrowdSimulationConfigValidator` 各 `Rule`） |
| `formations.<id>` | `spacingDiameterScale`、`aspect`（均 > 0）、`wedge`；至少一条 |
| `movement` | 子步、混合、转向、停滞等；`maxStepNavCells` ∈ (0, 1) |
| `avoidance` | `rateHz`、`hashRings` 1–3、`maxNeighbors`、`maxScan` ≥ `maxNeighbors` 等 |
| `push` | `movingPriorityBonus`（≥ 0）、`dominantShare` ∈ [0, 0.5] |
| `pairRules` | `yieldsTo.relation`、`sharesVisionWith.relation`（非空关系类型 id） |
| `sim` | `maxUnits` 1–200000、`simSecondsPerTickSecond`（> 0）、`parallelWorkerCount` |
| `spawn` | `placementTries`（≥ 1）、`groupSpacingScale`、`navCellInsetFraction` ∈ [0, 0.5) |
| `structures.templates.<id>` | 见下节 |
| `deploy` | `initialUnits`、`spreadCm`、`baseJitterCm`、`centersPerGroup`、`bases[]`（`playerId`、`xCm`、`yCm`） |
| `telemetry` | `turnEpsRad`（> 0，运动质量采样） |

### `structures.templates.<id>`

| 键 | 取值 | 说明 |
| --- | --- | --- |
| `layer` | `base` / `upper` | |
| `footprint` | `square` / `disc` / `path` | 放置时尺寸由 `sizeCm` 或实体组件给出 |
| `effect` | 带 `kind` 的对象 | `block`：`coverage` ∈ (0, 1]；`setArea`：`area`、`priority` ≥ 0；`surface`：`area`、`portalReachNavCells` ≥ 1 |
| `placement` | `unrestricted` 或 `spanEnds` | `spanEnds`：`endpointAreas`、`interiorAreas`（非空区域 id 数组） |
| `lifetimeSec` | number 或 `null` | 必写；`null` 永久；> 0 为秒 |

允许的组合（其它组合装载报错）：

- `base` + `square` + `block` 或 `setArea` + `unrestricted`
- `base` + `disc` 或 `path` + `setArea` + `unrestricted`
- `upper` + `path` + `surface` + `spanEnds`

`spanEnds` 按格的**静态**地形区域（经 `terrainAreas`）判定，不是 `setArea` 改过的当前区域。

### 地图实体上的结构

- `CrowdSimulationStructure`：`template`（模板 id）
- `square` + `block`：配合 `ManifestationObstacleIntent2D`（`Box`、`sinkNavigationObstacle: true`、等边半边长）
- `path` + `surface`：`CrowdSimulationPathSpan`（`fromXCm`、`fromYCm`、`toXCm`、`toYCm`、`widthCm`）

## `Navigation/navmesh.json`（`solver: crowd-simulation`）

群体地图的 `maps.<mapId>` 必填键（其它求解器另有白名单）：

| 键 | 类型 | 约束 |
| --- | --- | --- |
| `solver` | string | `crowd-simulation` |
| `boards` | object | 可选；有则恰好一块求解器板 |
| `logicTerrainAsset` | string | React 地编网格；烘焙与编辑器用，运行时不装 LogicTerrain |
| `navCellSizeCm` | int | > 0；板宽须整除 |
| `terrainTypes` | string[] | 有序、非空；下标 = `.navsurface` 格值 |
| `linkCandidateBake.<kindId>` | object | 可选；`kindId` 须在 `traversal.linkKinds`；字段 `maxGapCm`、`linkSpacingCm`（均为 `navCellSizeCm` 正整数倍）、`endpointMaxSlopeDeg` ∈ (0, 90) |

端点 / 硬缝地形掩码不由地图手写，由 `traversal` 矩阵在装载时推导（`DeriveLinkCandidateBakes`）。

`.navsurface` 路径：地图 `ContinuousHeightmap.asset` 去掉 `assets/` 前缀，`.height` → `.navsurface`。

## `Navigation/agent_profiles.json`

群体装载读 `id`、`radiusCm`、`navProfileId`。`navProfileId` 必须等于 `traversal.agentTypes` 里某一行的 id。单位模板 `CrowdSimulationAgent.profileId` 指向档案 id；移动类型来自档案的 `navProfileId`，不在模板上写 `agentTypeId`。

## 单位模板 `CrowdSimulationAgent`

| 键 | 类型 | 约束 |
| --- | --- | --- |
| `profileId` | string | 须在 `agent_profiles.json` |
| `speedCmPerSec` | number | > 0 |
| `pushPriority` | number | ≥ 0 |
| `autoDeploy` | bool | `false` 时不参与批量部署 / 随机测试 |

生成时写入组件：`ProfileIndex`、`AgentTypeIndex`、`SpeedCmPerSec`、`PersonalRadiusCm`（半径 × `agents.avoidanceRadiusScale`）、`PushPriority`、`AutoDeploy`。

## `Presentation/crowd_structure_palette.json`

条目 id 为键；内核不读。

| 键 | 约束 |
| --- | --- |
| `template` | 存在的结构模板 id |
| `label` | 非空 |
| `sizeCm` | > 0 |
| `gesture.kind` | `point` / `fixedPath` / `twoPoint` |
| `gesture.toOffsetCm` | 仅 `fixedPath`；`xCm`、`yCm` 不能全 0 |

`path` 足迹模板必须用 `fixedPath` 或 `twoPoint`；其它足迹必须用 `point`。

## `Presentation/crowd_player_colors.json`

目录在 `CrowdSimulationMod` 的 `config_catalog.json` 登记（DeepObject 合并）。解析：`CrowdPlayerColors.Load` / `LoadMerged`；进图激活时 `RequirePlayers` 对照地图 `Players[].PlayerId`。

| 键 | 类型 | 约束 |
| --- | --- | --- |
| `<playerId>` | string（十进制玩家号） | ≥ 1；值 `#rrggbb` 或 `#rrggbbaa` |

地图 `Players` 里每个 `PlayerId` 都必须有颜色；表内不能有多余玩家号。正本示例：`mods/showcases/crowd_simulation/CrowdSimulationS1Terrain1337Mod/assets/Presentation/crowd_player_colors.json`。

## 表现与调试配置（D 类）

三份 JSON 不必在合同里逐键罗列；装载一律严格 DTO（缺键、未知键、越界即失败）。分工如下。

| 职责 | 默认提供方 | 演示常见覆盖 |
| --- | --- | --- |
| 共用底色 `plan` / `ink` / `paper` | `CrowdSimulationUiMod` · `CrowdSimulationPresentationPalette.json` | 一般不改 |
| 玩家 HUD 与点选玩法 | `CrowdSimulationUiMod` · `CrowdSimulationPlayerView.json` | 演示 Mod 换相机、座位、模板列表、`labels`、`shapeLabels` 等 |
| 调试叠层与检视 | `CrowdSimulationUiMod` · `CrowdSimulationDebugView.json` | 演示 Mod 改默认叠层、检视 agentType 等 |
| 调试工具条 / 脚本 / HUD 文案 | `CrowdSimulationUiMod` · `CrowdSimulationDebug.json`（`tools`、`rates`、`fuzz`） | 演示 Mod 的 `presentation`（`deploy` 或 `path` 二选一）、`session.script` |

合同要点：

- **阵型文案**：`CrowdSimulationPlayerView.json` 的 `shapeLabels` 以 `CrowdSimulationConfig.json` 里 `formations.<id>` 为键，必须全覆盖；`defaultShapeId` 必须是其中之一。HUD 用这些字符串，不在调试 JSON 里重复。
- **调试视图文案**：`CrowdSimulationDebug.json` → `presentation.viewLabels` 按视图 id 写全（如 `terrain` / `walkable` / `navmesh`）；每个带 `presentation.deploy` 的演示自己写完整表，供 V 键巡回与 HUD 占位符 `{view}`。
- **`presentation` 块**：根下恰好 `deploy` 或 `path` 之一（无 `kind`）。`deploy` 含 `palette`、`actions`、放置预览色等；`path` 用于路径演示。带 `deploy` 时必须同时有 `tools`、`rates`、`fuzz`。
- **`fuzz.ops`**：字符串 id 数组，只列操作名（如 `spawn`、`order`、`place`、`remove`、`placePath`、`removePath`、`kill`）；字段含义见 `CrowdSimRegressCatalog`。
- **颜色对象**：玩家视图、调试视图、`presentation.deploy` 里的预览色统一 `{ "red", "green", "blue", "alpha" }`（0–255）。共用色只写在 `CrowdSimulationPresentationPalette.json`，视图 `colors` 不得与 palette 键重复（装载时 `CrowdPresentationPalette.MergeInto`）。
- **视线初始值**：群体迷雾是否做遮挡采样只认 `CrowdSimulationConfig.json` → `fog.lineOfSight`（bool）。HUD 的 `ToggleLos` 走运行时 `fogSight` 命令，不在 PlayerView / Debug JSON 里再写一份初始开关。
- **散开（scatter）范围**：`scatter` 指令给每名有单位的出生玩家随机 box 目标；坐标均匀取自 `[0, NavCellsPerSide × navCellSizeCm)`，由当前地图求解器板与 `navmesh` 推导，不在调试 JSON 里配置。
- **爆炸（kill）半径**：`CrowdSimulationDebug.json` → `tools.boomRadiusCm`（> 0）与 `ringScale`（> 1）；外圈冲量用 `impulse`。演示左键爆炸时把该半径写入 `kill` 指令。

## `CrowdSimulationDebug.json`（摘要）

严格 DTO。带 `presentation.deploy` 时须同时有 `tools`、`rates`、`fuzz`。

- `tools`：`boomRadiusCm`、`ringScale` > 1、`impulse` ≥ 0、`spawnCount` ≥ 1、`spawnTemplate`（单位模板 id）、`placeEntry`（须在 `presentation.deploy.palette[].entry`）、`speedStepsPermille`（严格递增且含 1000）
- `rates`：`avoidHzMin` / `avoidHzMax`（0 < min ≤ max）、`avoidStep` > 0
- `fuzz`：`sliceMs`、`opCount`、`stepsPerOp`、`placeTemplate`、`pathTemplate` / `pathWidthCm` / `pathLengthCm`（若 `ops` 含 `placePath`）、`ops`（字符串 id 列表）
- `presentation`：恰好 `deploy` 或 `path` 之一（无 `kind` 字段）；`deploy.palette[]` 的 `entry`、`selectAction`（不重复）

## `.navsurface`

布局与 `NavSurfaceAsset.Layout` 一致（小端）；`NavSurfaceContract.BakeInputsDigest` 把该布局字符串纳入指纹。

| 偏移 | 类型 | 字段 |
| --- | --- | --- |
| 0 | u32 | magic `LNSF` |
| 4 | u16 | `headerBytes` = 30 |
| 6 | i32 | `cellsX` |
| 10 | i32 | `cellsY` |
| 14 | i32 | `navCellSizeCm` |
| 18 | u16 | `terrainCount` |
| 20 | u16 | `linkKindCount` |
| 22 | i32 | `linkCandidateCount` |
| 26 | u32 | `bakeInputsDigest` |

之后：地形 id 表、链接种类 id 表、`cellsX×cellsY` 字节地形下标、候选记录（每条 16 字节：`kindIndex`、`flags`（bit0 斜向）、`steps`、`fromX/Y`、`toX/Y`、`dropCm`）。

进图：`NavSurfaceContract.Validate` 对照 `terrainTypes` 与 `linkCandidateBake` 种类序；`ValidateBakeInputs` 重算指纹，不符时报错并提示 `nav bake-crowd-navsurface` 重烘。

指纹含：`navCellSizeCm`；各链接种类的几何与推导地形掩码；地图上层条带实体顺序与 `CrowdSimulationPathSpan`；逐格高度与坡度（Fix64 原始值）。不含地编网格字节（格值已在文件里）。

## 装载顺序（数据流）

1. `ConfigPipeline` 按 Mod 依赖合并各 JSON。
2. `CrowdSimulationConfigLoader.LoadConfig` + `CrowdTraversalConfig.Load` + `agent_profiles` + `navmesh`。
3. `CrowdSimulationConfigLoader.Load` 绑定地图、模板、时钟，产出 `CrowdSimulationRuntimeConfig`。
4. 进图读 `.navsurface` / `.height`，`CrowdSimulationWorldIngest` 校验指纹与尺寸。

架构分层与演示说明见 [CrowdSimulation 与 NavSurface](crowdsimulation-and-navsurface.md)。
