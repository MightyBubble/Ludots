# CrowdSimulation 数据结构：通行矩阵、结构模板与 .navsurface v2

> 状态：设计稿，等评审。仓库里的代码和数据还是旧格式，本页不改任何运行时代码。实现顺序见第 10 节。

群体模拟的通行规则、结构模板和导航面资产，改成全部由数据描述。核心只认一组固定的词：区域、移动类型、链接种类、两个层、三种足迹、三种效果、两种放置规则。`open`、`deep`、`jump`、`bridge`、`building`、`foot` 这些都是数据里的 id，核心和工具里不出现。

两条硬规则：

- 内核、烘焙器、规划器在通行和结构上用到的每个数，都在配置里有位置。
- 内核不读的键一律是装载错误，包括未知键和已经删掉的旧键。

| 核心认的词 | 含义 | 当前数据里的 id |
|---|---|---|
| 区域（area） | 一格地面的通行类别，按下标存进栅格 | `open` `shallow` `rough` `sheer` `deep` `road` `deck` `flooded` `burning` |
| 移动类型（agent type） | 通行矩阵的一行 | `foot` `hull` `climber` `amphib` `leaper` |
| 链接种类（link kind） | 两格之间不走地面的连接方式 | `jump` |
| 层（layer） | 固定两层：`base` 地面栅格，`upper` 条带上层 | — |
| 足迹（footprint） | `square` / `disc` / `path` | — |
| 效果（effect） | `block` / `setArea` / `surface` | — |
| 放置规则（placement） | `unrestricted` / `spanEnds` | — |
| 摆放手势（gesture，只在表现层） | `point` / `fixedPath` / `twoPoint` | — |

## 1 文件一览

| 文件 | 谁写 | 合并方式 | 内容 |
|---|---|---|---|
| `CrowdSimulation/traversal.json` | StandardMod 写基础，DemoContent 追加 | DeepObject，目录项登记在 `CrowdSimulationMod` | 区域、地形到区域、链接种类、通行矩阵 |
| `CrowdSimulationConfig.json` 的 `structures.templates` | StandardMod、DemoContent、地图 Mod | DeepObject（不变） | 结构模板 |
| `Navigation/navmesh.json` 的 `maps.<mapId>` | 地图 Mod | DeepObject（不变） | `solver`、`logicTerrainAsset`、`navCellSizeCm`、`terrainTypes`、`linkCandidateBake` |
| `Navigation/agent_profiles.json` | 不变 | ArrayById（不变） | 体型 |
| `Presentation/crowd_structure_palette.json` | DemoContent，S4 追加 | DeepObject，目录项登记在 DemoContent | 摆放手势条目 |
| `CrowdSimulationDebug.json` 的 `presentation.deploy.palette` | S4 | DeepObject（不变） | 面板顺序和选择键 |
| `terrain/<map>.navsurface` | 烘焙产物 | — | v2 格式，见第 6 节 |

删掉的文件：StandardMod 的 `Navigation/pathing.json` 和 `Navigation/navmesh.json`。Core 自带的 `assets/Navigation/navmesh.json` 已经有根上的 Recast 键（`mode`、`algorithm`、`profiles`、`layers`、`areas`、`runtimeIncremental`），StandardMod 不用再写一份。

删掉的键：`movement.jumpSpeedRatio`、`structures.blockCoverage`、`structures.portalReachNavCells`、`structures.anchorTerrain`、`structures.spanTerrain`、结构模板的 `name` / `blocker` / `area` / `priority` / `layered`、`maps.<mapId>` 的 `navAreas` / `terrainAreas` / `navProfiles` / `jumpLinkBake`（群体地图）、`tools.placeTemplate` / `rectSizeCm` / `discRadiusCm` / `pathWidthCm`、`presentation.deploy.structures`、实体组件 `CrowdSimulationNavArea` 和 `CrowdSimulationBridgeSpan`。新格式上线后再写这些键，装载报错并指出新位置。区域的 `slopeFree` 改名为 `ignoresSlope`。

示例文件按发布时的样子放在 [`crowdsimulation-data-schema/`](crowdsimulation-data-schema/)，目录结构和 Mod 里一致：

| 示例 | 对应 Mod 路径 |
|---|---|
| [CrowdSimulationMod/assets/config_catalog.json](crowdsimulation-data-schema/CrowdSimulationMod/assets/config_catalog.json) | `mods/capabilities/navigation/CrowdSimulationMod/assets/config_catalog.json` |
| [CrowdSimulationStandardMod/assets/CrowdSimulation/traversal.json](crowdsimulation-data-schema/CrowdSimulationStandardMod/assets/CrowdSimulation/traversal.json) | 新文件 |
| [CrowdSimulationStandardMod/assets/CrowdSimulationConfig.json](crowdsimulation-data-schema/CrowdSimulationStandardMod/assets/CrowdSimulationConfig.json) | 全文 |
| [CrowdSimulationDemoContentMod/assets/CrowdSimulation/traversal.json](crowdsimulation-data-schema/CrowdSimulationDemoContentMod/assets/CrowdSimulation/traversal.json) | 新文件 |
| [CrowdSimulationDemoContentMod/assets/CrowdSimulationConfig.json](crowdsimulation-data-schema/CrowdSimulationDemoContentMod/assets/CrowdSimulationConfig.json) | 全文 |
| [CrowdSimulationDemoContentMod/assets/Presentation/crowd_structure_palette.json](crowdsimulation-data-schema/CrowdSimulationDemoContentMod/assets/Presentation/crowd_structure_palette.json) | 新文件 |
| [CrowdSimulationDemoContentMod/assets/config_catalog.json](crowdsimulation-data-schema/CrowdSimulationDemoContentMod/assets/config_catalog.json) | 全文 |
| [CrowdSimulationMinimalMod/assets/CrowdSimulationConfig.json](crowdsimulation-data-schema/CrowdSimulationMinimalMod/assets/CrowdSimulationConfig.json) | 全文 |
| [CrowdSimulationMinimalMod/assets/Navigation/navmesh.json](crowdsimulation-data-schema/CrowdSimulationMinimalMod/assets/Navigation/navmesh.json) | 全文 |
| [CrowdSimulationMinimalMod/assets/Entities/templates.json](crowdsimulation-data-schema/CrowdSimulationMinimalMod/assets/Entities/templates.json) | 全文 |
| [CrowdSimulationS1Terrain1337Mod/assets/Navigation/navmesh.json](crowdsimulation-data-schema/CrowdSimulationS1Terrain1337Mod/assets/Navigation/navmesh.json) | 全文；2024、7 两张图只差 mapId 和资产路径 |
| [CrowdSimulationS1Terrain1337Mod/assets/CrowdSimulationConfig.json](crowdsimulation-data-schema/CrowdSimulationS1Terrain1337Mod/assets/CrowdSimulationConfig.json) | 全文 |
| [CrowdSimulationS1Terrain1337Mod/assets/Entities/templates.json](crowdsimulation-data-schema/CrowdSimulationS1Terrain1337Mod/assets/Entities/templates.json) | 全文 |

Minimal 的 `agent_profiles.json`、`CrowdSimulationDebug.json`、`config_catalog.json` 不变，没有放副本。

## 2 通行矩阵 `traversal.json`

### 2.1 结构

```json
{
  "areas": { "open": { "ignoresSlope": false }, "deep": { "ignoresSlope": true } },
  "terrainAreas": { "land": "open", "water": "deep" },
  "linkKinds": { "jump": { "traversalSpeedRatio": 1.3 } },
  "agentTypes": {
    "leaper": {
      "maxSlopeDeg": 30,
      "links": {
        "jump": { "upCm": 2500, "downCm": 20000, "rangeNavCells": 7, "costPerNavCell": 2 }
      },
      "cells": {
        "open": { "cost": 1, "speedDivisor": 1, "depart": ["jump"], "arrive": ["jump"] },
        "deep": false
      }
    }
  }
}
```

行是移动类型，列是区域，格是这一类单位在这一区域上的通行参数。完整文件见 StandardMod 和 DemoContent 两份示例。

| 键 | 类型 | 规则 | 谁读 |
|---|---|---|---|
| `areas.<areaId>` | 对象 | 以 id 为键；下标 = 合并后的属性顺序；最多 256 个 | 栅格的 u8 区域值、每行代价数组 |
| `areas.<areaId>.ignoresSlope` | bool，必写 | 为 true 时这一区域不做坡度判定 | `NavContextBaker` |
| `terrainAreas.<terrainId>` | 区域 id，必须在 `areas` 里 | 地形类型到区域的全局对应 | 地面栅格初值、链接候选掩码、放置判定 |
| `linkKinds.<kind>.traversalSpeedRatio` | 数，> 0 | 走这种链接时速度 = 单位速度 × 它 | `CrowdMotor` |
| `agentTypes.<type>.maxSlopeDeg` | 数或 null，必写 | 数取 (0, 90)；null 表示不限坡度 | `NavContextBaker`（按 `Fix64Math.Tan(deg × Deg2Rad)` 比较） |
| `agentTypes.<type>.links.<kind>` | `false` 或对象，必写 | `false` 表示这一行没有这种链接能力 | — |
| `links.<kind>.upCm` | 整数，> 0 | 向上能走的落差；落差 ≤ 它才建反向边 | `NavContextBaker` |
| `links.<kind>.downCm` | 整数，≥ `upCm` | 向下能走的落差 | `NavContextBaker` |
| `links.<kind>.rangeNavCells` | 整数，> 0 | 候选的格距超过它就不用 | `NavContextBaker` |
| `links.<kind>.costPerNavCell` | 数，> 0 | 链接代价 = 格距 × 它 | `NavContextBaker`、HPA 启发式 |
| `agentTypes.<type>.cells.<areaId>` | `false` 或对象，必写 | `false` 表示不可通行 | — |
| `cells.<areaId>.cost` | 数，> 0 | 寻路代价 | 规划器、流场、HPA |
| `cells.<areaId>.speedDivisor` | 数，> 0 | 移动速度除以它；不再默认等于 `cost` | `CrowdLeader`、`CrowdMotor` |
| `cells.<areaId>.depart` | 链接种类 id 数组 | 这一行能从这种区域出发走哪些链接 | `NavContextBaker`、链接候选掩码 |
| `cells.<areaId>.arrive` | 链接种类 id 数组 | 这一行能落到这种区域的链接 | 同上 |

### 2.2 校验

装载时全部检查，任何一条不满足都报错，报文带文件、键路径和 Mod：

1. 每一行的 `cells` 正好覆盖合并后的全部区域，`links` 正好覆盖全部链接种类。缺一项不当成代价 1 或不可通行，直接报错。
2. 所有行都校验，不只校验被单位模板引用的行。运行时仍然只装被引用的行，`AgentTypeIndex` 仍按合并后的属性顺序在被引用的行里编号。
3. `depart` / `arrive` 只能写本行 `links` 里是对象的种类，不能重复。本行某个种类是对象、但没有任何一格在 `depart` 或 `arrive` 里写它，按死配置报错。
4. `costPerNavCell` 不能低于本行所有可通行格 `cost` 的最小值，保证启发式可采纳。最小值按合并后的整行算，DemoContent 加进 `road`（0.8）后 `leaper` 的下限从 1 变成 0.8，`2` 仍然合法。
5. 上层区域是被某个 `surface` 效果引用的区域。它的格 `depart` / `arrive` 必须为空，它不能出现在 `terrainAreas` 里，也不能被 `setArea` 引用。当前只有 `deck` 是上层区域。
6. `terrainAreas` 的值必须是 `areas` 里的 id。群体地图 `terrainTypes` 里的每个地形都必须在 `terrainAreas` 里。
7. 链接种类最多 255 个（`.navsurface` 记录里是 u8）。移动类型 × 半径级组合仍然 ≤ 255。
8. 对象位置写 null、写数组，或出现未知键，都报错。

### 2.3 链接怎么判

`.navsurface` 里每条候选记录了种类 k、高端格 a、低端格 b、格距 L、落差 drop（烘焙保证 a 不低于 b）。对第 r 行：

- 建 a → b 的边：`r.links[k]` 是对象，a、b 两格在这一行可通行，`k ∈ r.cells[area(a)].depart`，`k ∈ r.cells[area(b)].arrive`，`L ≤ rangeNavCells`，`drop ≤ downCm`。
- 再建 b → a 的边：上面成立，`drop ≤ upCm`，并且 `k ∈ r.cells[area(b)].depart`、`k ∈ r.cells[area(a)].arrive`。
- 两个方向的代价都是 `L × costPerNavCell`。

`area(x)` 是这一格当前的地面区域，也就是 `setArea` 结构改过之后的值，和今天判 `passable` 用的是同一份栅格。可通行指这一格不是阻挡格，并且这一行在这个区域上不是 `false`。

当前数据给 `leaper` 的每个可通行地面区域都写了 `depart: ["jump"]` 和 `arrive: ["jump"]`，`deck` 两项为空。这时上面的条件就退化成今天的 `passable[a] && passable[b]`，链接集合不变。

### 2.4 烘焙掩码从矩阵推导

旧格式每张图手写 `endpointTerrain` 和 `gapTerrain`。新格式由矩阵推出来，地图只写几何参数：

- K = `links[k]` 是对象的所有行。
- 端点区域 = K 里所有行的所有格中，`depart` 或 `arrive` 含 k 的区域的并集。
- 硬缝区域 = 对 K 里每一行都是 `false` 的区域。
- 端点地形、硬缝地形 = 这张图 `terrainTypes` 里，经 `terrainAreas` 落到上面两组区域的地形。

当前数据推出的结果：端点 `{shore, land, mountain}`，硬缝 `{cliff, water}`，和 S1 三张图手写的两份列表一致。K 为空、地图却写了 `linkCandidateBake.<k>`，按死配置报错。

地图上每个种类只写几何参数，`maps.<mapId>.linkCandidateBake.<k>`：

| 键 | 规则 | 烘焙器怎么用 |
|---|---|---|
| `maxGapCm` | 整数 > 0，必须是 `navCellSizeCm` 的整数倍 | 每个方向最多扫 `maxGapCm / navCellSizeCm + 1` 格 |
| `linkSpacingCm` | 整数 > 0，必须是 `navCellSizeCm` 的整数倍 | 按候选中点分桶，桶边长 = 它换算的格数 |
| `endpointMaxSlopeDeg` | 数，(0, 90) | 端点格坡度不超过它 |

不写 `linkCandidateBake` 的图不烘候选，Minimal 就是这样。

这样做的代价：以后给某一行加上链接能力、或改了 `depart` / `arrive`，所有带这个种类的地图候选都会变，必须重烘。`.navsurface` v2 的 `linkBakeDigest` 把这件事变成装载报错（第 6 节）。

### 2.5 两个层

`base` 是地面栅格：每格一个区域下标，加一张阻挡位图。`upper` 是 `surface` 效果铺出的条带。上层格的代价和速度取同一行 `cells[<surface 的区域>]`。

层本身没有参数，`traversal.json` 里没有层这一节。以前看起来像层参数的两个数，描述的其实是一个结构怎么往栅格上盖，所以放进结构模板的效果里（4.1）：阻挡的覆盖份额写在 `block.coverage`，入口格数写在 `surface.portalReachNavCells`，每个模板各写各的。层的个数固定为两个，是内核合同（5.1），不做成可配置的层列表。

### 2.6 地形到区域

`terrainAreas` 是全局的一张表，写在 StandardMod。5 种地形（`water`、`shore`、`land`、`mountain`、`cliff`）和 StandardMod 的 `terrain_type_colors.json` 是同一组词，各张图的对应今天完全一致。

### 2.7 地形顺序只有一处作者源

地形顺序决定格里的字节是什么意思。今天和它有关的有四处，逐一看过代码：

| 位置 | 里面有什么 | 证据 |
|---|---|---|
| 地图配置 `maps.<mapId>.terrainAreas[]`（新格式改名 `terrainTypes`） | 有序的地形 id 列表 | 唯一由人写的那一份 |
| 地编资产 `logicTerrainAsset`（`map_data.bin`） | 每格一个 `AreaId` 字节，没有任何地形名字 | `ReactGridTerrainBinary` 只读写区块和逐格通道；`CrowdNavSurfaceAuthoring.Bake` 把 `cell.AreaId` 原样拷进格，只检查它小于地形个数；`init-crowd-terrain` 用 `PaintTerrainAreas` 按配置顺序把地形 id 换成下标写进去 |
| `.navsurface` 地形表 | 有序的地形 id 列表 | 烘焙器 `Bake` 直接拿配置的 `TerrainTypeIds` 写进去；运行时 `NavSurfaceContract` 逐项比对配置，不同就报错 |
| `terrain_type_colors.json` | 以地形 id 为键的颜色 | 和顺序无关；运行时和工具只用 `RequireCovers` 检查覆盖 |

所以作者源只有地图配置这一份。地编资产里的字节是它的下标，`.navsurface` 地形表是烘焙时抄下的一份，用来在装载时发现“资产是按另一个顺序烘的”，颜色表只要求覆盖。新格式保持这个关系，只把键名从 `terrainAreas[]` 改成 `terrainTypes`，因为区域对应已经搬到全局表。

顺序不能改由全局 `terrainAreas` 的属性顺序决定：Minimal 的顺序是 `land, water`，S1 是 `water, shore, land, mountain, cliff`，统一成一个顺序会改掉 Minimal 地编资产和 `.navsurface` 里的每个字节。

## 3 Mod 拆分与合并

### 3.1 谁写什么

| Mod | traversal.json 里写什么 |
|---|---|
| `CrowdSimulationMod` | 不写数据，只在 `config_catalog.json` 登记 `CrowdSimulation/traversal.json`（DeepObject） |
| `CrowdSimulationStandardMod` | 区域 `open` `shallow` `rough` `sheer` `deep`（下标 0–4）；5 条地形对应；链接种类 `jump`；5 行，每行 5 格 |
| `CrowdSimulationDemoContentMod` | 追加区域 `road` `deck` `flooded` `burning`（下标 5–8），以及这 4 列在 5 行里的格 |
| 地图 Mod | 不写；只写 `navmesh.json` 的 `maps.<mapId>` |

DemoContent 依赖 StandardMod，所以合并后的区域顺序是 `open, shallow, rough, sheer, deep, road, deck, flooded, burning`，和今天 `navmesh.json` 里的 `areaId` 0–8 一一对应（`Default` 改名为 `open`）。只加载 StandardMod 的 Minimal 拿到前 5 个区域，下标同样是 0–4。今天 StandardMod 的 `navmesh.json` 写了全部 9 个区域，Minimal 的运行时区域表也是 9 个；新格式下变成 5 个。Minimal 的格子只落在 `open`（0）和 `deep`（4），没有结构改区域，寻路结果不变；但凡是把区域个数或整行代价数组算进摘要、缓存键的地方，Minimal 的值会变。第 6b 步要先查清这些地方，再决定 Minimal 的 truth 是否需要重录。

### 3.2 现有合并已经够用（对照过代码）

- 顺序：`ConfigPipeline` 先收 Core 资产，再按 `LoadedModIds` 收各 Mod。`LoadedModIds` 是依赖优先的拓扑序，同层按 `priority` 降序、再按创建顺序。
- `ConfigMerger.MergeObject`：新键追加在末尾；已有键原位替换，下标不变；对象遇对象递归合并；数组整份替换（除非键名在 `ArrayAppendFields` 里）；值为 null 时直接覆盖成 null。
- 格的 `false` 和对象类型不同，合并时整份替换。所以覆盖时“可通行改不可通行”写 `false`，反过来写完整对象，只改代价写 `{ "cost": 2 }` 就行。这也是不可通行写成 `false`、不写成 `{ "passable": false }` 的原因：后者和已有对象递归合并后会留下 `cost`、`depart` 等键，严格 DTO 会拒绝，而现有合并没有删键的手段。
- `links.<kind>` 同理，`false` 和对象互相覆盖都成立。
- `maxSlopeDeg`、`lifetimeSec` 写 null 能覆盖数，数也能覆盖 null。
- `traversal.json` 不需要 `ArrayAppendFields`。`depart` / `arrive` 覆盖时整份替换，这是想要的行为。

### 3.3 缺口（只提方案，这一步不做）

1. **共享导航装载器的地图键白名单。** `NavMeshBakeConfigLoader.ValidateRaw` 对 `maps.<mapId>` 只认 `solver`、`boards`、`terrainAreas`、`navProfiles`、`navAreas`、`logicTerrainAsset`、`jumpLinkBake`。要加 `navCellSizeCm`、`terrainTypes`、`linkCandidateBake`，删 `jumpLinkBake`。方案：白名单按 `solver` 分，`crowd-simulation` 的图只收 `solver`、`boards`、`logicTerrainAsset`、`navCellSizeCm`、`terrainTypes`、`linkCandidateBake`；`navAreas` 等键留给其他求解器（`CapabilityStandardCrowdPhysicsArenaMod` 的 `mass-navigation` 图还在用 `navAreas`）。这是 Core 共享装载器的改动，按规范在实现前单独说明。
2. **兄弟 Mod 追加区域的顺序。** 两个都依赖 StandardMod、彼此没有依赖的 Mod 各自追加区域时，区域下标由 `priority` 和创建顺序决定，换一组启动 Mod 下标就会变，瓦片缓存键和 S7 区域真相跟着漂。方案：群体装载器用 `ConfigFragment.SourceUri` 找出每个区域来自哪个 Mod，两个追加区域的 Mod 之间没有依赖路径就报错。需要装载器能拿到依赖图，属于基建改动。今天只有 DemoContent 追加区域，不受影响。
3. **`agent_profiles.json` 共享 schema。** 群体只读 `id`、`radiusCm`、`navProfileId`。`heightCm`、`clearanceCm`、`draftCm`、`beamCm`、`mass`、`layer` 由共享 schema 要求必写，群体不读。方案：群体要求 `navProfileId` 等于单位模板的 `agentTypeId`（取代今天经 navmesh 能力档的那次比对），让这个字段继续有人读；其余字段要么由共享 schema 按消费方放宽，要么另起群体自己的体型表。本计划不解决，列为已知例外。
4. **效果种类不能靠覆盖改。** `effect` 是对象，DeepObject 会把新旧两份递归合并，`setArea` 改 `block` 会留下 `area`、`priority`，严格 DTO 拒绝。要换效果种类就换一个模板 id。不需要改基建，写进作者文档。
5. **不能用覆盖删掉区域、行或模板。** DeepObject 没有删键。当前数据不需要；需要时再讨论是否把 `EntityTemplateInheritance` 的 `__replace` 约定搬进 `ConfigMerger`。

## 4 结构模板

### 4.1 字段

```json
"bridge": {
  "layer": "upper",
  "footprint": "path",
  "effect": { "kind": "surface", "area": "deck", "portalReachNavCells": 4 },
  "placement": {
    "kind": "spanEnds",
    "endpointAreas": ["shallow", "open"],
    "interiorAreas": ["deep"]
  },
  "lifetimeSec": null
}
```

| 键 | 取值 | 说明 |
|---|---|---|
| `layer` | `base` / `upper` | 结构落在哪一层 |
| `footprint` | `square` / `disc` / `path` | 尺寸每次放置时给：`square` 边长 = `sizeCm`，`disc` 半径 = `sizeCm`，`path` 宽 = `sizeCm`，两端点另给 |
| `effect.kind = block` | `coverage`（数，(0, 1]） | 一格被足迹盖住的面积份额（X、Y 两向之积）≥ `coverage` 时变阻挡格；阻挡格同时遮挡视线 |
| `effect.kind = setArea` | `area`（区域 id），`priority`（整数 ≥ 0） | 格心落在足迹里的地面格改成这个区域；重叠时高优先级覆盖低优先级 |
| `effect.kind = surface` | `area`（区域 id），`portalReachNavCells`（整数 ≥ 1） | 铺一条上层条带，格的通行参数取这个区域；离两端不到 `portalReachNavCells` 格的条带格是入口；没有优先级，重叠时按现行的实体顺序 |
| `placement.kind = unrestricted` | 无其他键 | 不看地形 |
| `placement.kind = spanEnds` | `endpointAreas`、`interiorAreas`（区域 id 数组） | 两个端点格的地形区域必须在 `endpointAreas` 里，其余被盖住的格必须在 `interiorAreas` 里；两个端点格都要落在条带上 |
| `lifetimeSec` | 数 > 0，或 null，必写 | null 表示永久；到期 tick 仍是 `ceil(lifetimeSec × FixedHz / simSecondsPerTickSecond)` |

`spanEnds` 看的“地形区域”是这一格静态地形经 `terrainAreas` 得到的区域，不是被 `setArea` 改过的当前区域。今天按地形 id 判，`terrainAreas` 在当前数据里是一一对应，所以按区域判结果相同。改成区域的原因是模板是全局数据，而地形是地图事实；区域在所有图上意思一样。

模板没有 `name`：运行时不读，面板文字在摆放手势条目的 `label` 里。模板没有视野字段：迷雾的遮挡来自阻挡格（`CrowdFog.BuildOpaque`），`block` 就意味着挡视线。把它做成开关会是新的内核行为，不在本次范围。

### 4.2 组合规则

| layer | footprint | effect | placement |
|---|---|---|---|
| `base` | `square` | `block` 或 `setArea` | `unrestricted` |
| `base` | `disc` | `setArea` | `unrestricted` |
| `base` | `path` | `setArea` | `unrestricted` |
| `upper` | `path` | `surface` | `spanEnds` |

表外的组合装载报错。这四行正好是今天数据和内核支持的范围。`placement` 虽然现在由 `layer` 决定，仍然显式写出来，内核按 `placement.kind` 分派判定，编辑器也不用记隐含规则。以后放开 `upper + unrestricted` 或 `base path + spanEnds`，只需在这张表里加行和对应的判定代码。

`coverage` 和 `portalReachNavCells` 改成按模板之后，内核这几处都能按结构取值，现有代码里它们本来就是逐个结构循环：

- `CrowdStructuresStore.RasterRect` 的阻挡循环和 `GroundCellHit` 逐个实体判格，`SurfaceGrid.Build` 逐个地图阻挡判格，改成读这个实体所属模板的 `coverage`。
- `UpperLayerBake.RasterizeDecks` 逐个地图条带算 `DeckSpanGeometry`，`CrowdStructuresStore.PaintLayered` 逐个分层实体算，两处的入口距离都改成取这个条带模板的值；地图条带记录 `BridgeDeckRecord` 带上模板下标。
- `CrowdStructurePlacement.JudgeBridge` 判的就是这一个模板，直接读它的值。

现有数据里两个阻挡模板都是 0.5，两个条带模板都是 4，和今天的全局值相同，栅格逐位不变。

其他规则：`setArea.area` 必须是地面区域（不是上层区域）；`surface.area` 必须是上层区域（见 2.2 第 5 条）；`disc + block` 不支持，和今天“阻挡只能是正方形”一致。旧格式允许 `blocker` 和 `area` 同时写，现有数据没有这种模板，新格式不提供这个组合。

### 4.3 地图实体

地图上预置的结构用实体组件点名模板：

```json
{
  "id": "crowd_simulation_blocker_140m",
  "components": {
    "Name": { "Value": "CrowdSimulation.Blocker.140m" },
    "WorldPositionCm": { "Value": { "X": 0, "Y": 0 } },
    "ManifestationObstacleIntent2D": {
      "shape": "Box", "sinkPhysicsCollider": false, "sinkNavigationObstacle": true,
      "halfWidthCm": 7000, "halfHeightCm": 7000, "navRadiusCm": 0,
      "localOffsetXCm": 0, "localOffsetYCm": 0
    },
    "CrowdSimulationStructure": { "template": "building" }
  }
}
```

```json
{
  "InstanceId": "bridge_0001",
  "Template": "crowd_simulation_bridge",
  "Overrides": {
    "CrowdSimulationPathSpan": {
      "fromXCm": 1078125, "fromYCm": 1321875,
      "toXCm": 1140625, "toYCm": 1321875,
      "widthCm": 18750
    }
  }
}
```

- `CrowdSimulationStructure { template }`：模板 id，必写。
- `square + block` 的模板：尺寸取 `ManifestationObstacleIntent2D`，必须 `shape = "Box"`、`sinkNavigationObstacle = true`、`halfWidthCm = halfHeightCm`，边长 = 2 × 半边长；覆盖份额取模板的 `coverage`。
- `path + surface` 的模板：入口格数取模板的 `portalReachNavCells`，烘焙工具算候选时排除的桥面格也按它画。
- `path + surface` 的模板：尺寸和端点取 `CrowdSimulationPathSpan { fromXCm, fromYCm, toXCm, toYCm, widthCm }`。
- 地图实体只接受上面两种组合，模板的 `lifetimeSec` 必须是 null。其他组合今天的地图里没有，装载报错。
- 地图实体不走放置判定，和今天一样。
- `sinkNavigationObstacle = true` 却没有 `CrowdSimulationStructure` 的实体报错，不再默认按 `building` 处理。
- 入仓顺序不变：先按实体顺序装阻挡，再按实体顺序装条带，结构 id 依次续号。
- `CrowdSimulationMapSurfaceSource` 里的 `?? 0`、`?? "Box"`、`?? false` 删掉，缺字段即报错。

### 4.4 旧字段和硬编码去哪了

| 旧 | 新 |
|---|---|
| `structures.blockCoverage` | 每个 `block` 模板的 `effect.coverage` |
| `structures.portalReachNavCells` | 每个 `surface` 模板的 `effect.portalReachNavCells` |
| `structures.anchorTerrain`（地图 Mod 和 DemoContent） | 模板的 `placement.endpointAreas`（区域 id） |
| `structures.spanTerrain` | 模板的 `placement.interiorAreas` |
| 模板 `name` | 删掉；面板文字在手势条目 `label` |
| `footprint: "rect"` | `footprint: "square"`；`CrowdStructureShape.Rect` 改名 `Square` |
| `blocker: true` | `effect: { kind: "block" }` |
| `area` + `priority`（非分层） | `effect: { kind: "setArea", area, priority }` |
| `area` + `layered: true`（`priority` 不生效） | `layer: "upper"` + `effect: { kind: "surface", area }` |
| `lifetimeSec` 可省 | 必写，null 表示永久 |
| 装载器 `Priority ?? 0`、`Name ?? id` | 删掉 |
| `CrowdStructuresStore.Build` 里的 `"building"` | 实体 `CrowdSimulationStructure.template` |
| `InstallMapBridges` 里的 `TemplateIndexOf("bridge")` | 实体 `CrowdSimulationStructure.template` |
| `CrowdSimulationNavArea { area, priority, layered }` | 删掉，模板里已经有 |
| `CrowdSimulationBridgeSpan { x0Cm, y0Cm, x1Cm, y1Cm, widthCm }` | `CrowdSimulationPathSpan { fromXCm, fromYCm, toXCm, toYCm, widthCm }` |
| 放置拒因 `AnchorTerrain` / `SpanTerrain` | `EndpointArea` / `InteriorArea` |
| 文案 `placeAnchorTerrain` / `placeSpanTerrain` | `placeEndpointArea` / `placeInteriorArea` |
| 颜色 `placeAnchor` | `placeEndpoint`（`placePortal` 不变） |
| `movement.jumpSpeedRatio` | `linkKinds.<kind>.traversalSpeedRatio` |
| `presentation.deploy.structures[] { id, label, shape, sizeCm, roadLengthCm?, selectAction }` | 手势条目（4.5）+ `presentation.deploy.palette[] { entry, selectAction }` |
| `S4DeployDemoRuntime.ShapePoint / ShapeDisc / ShapeRoad / ShapeSpan`、`IsRoad` 的固定 +X | 手势 `point` / `point` / `fixedPath` / `twoPoint`；+X 长度进 `toOffsetCm` |
| `tools.placeTemplate` | `tools.placeEntry`（手势条目 id） |
| `tools.rectSizeCm` / `discRadiusCm` / `pathWidthCm` | 删掉。它们只在 `CrowdSimulationDebugConfig.RequirePlaceTemplate` 里和面板行做相等校验，没有别的读者；尺寸以条目的 `sizeCm` 为准 |
| `fuzz.bridgeTemplate` / `bridgeWidthCm` / `bridgeSpanCm` | `fuzz.pathTemplate` / `pathWidthCm` / `pathLengthCm` |
| fuzz op `placeBridge` / `removeBridge` | `placePath` / `removePath` |

### 4.5 摆放手势（DemoContent）

手势只在表现层，内核不读。DemoContent 新增 `Presentation/crowd_structure_palette.json`，以条目 id 为键：

```json
"roadFixed": {
  "template": "road",
  "label": "道路 path 30m×250m(+X)",
  "gesture": { "kind": "fixedPath", "toOffsetCm": { "xCm": 25000, "yCm": 0 } },
  "sizeCm": 3000
}
```

| 手势 | 键 | 适用足迹 | 鼠标怎么用 |
|---|---|---|---|
| `point` | 无 | `square`、`disc` | 点一下，以光标为中心 |
| `fixedPath` | `toOffsetCm { xCm, yCm }`，不能同时为 0 | `path` | 点一下作起点，终点 = 起点 + 偏移 |
| `twoPoint` | 无 | `path` | 先点起点，再点终点 |

条目规则：`template` 必须存在；手势和模板足迹要匹配；`sizeCm` 为正整数。条目 id 和模板 id 是两套名字，同一个模板可以有多个条目（`roadFixed`、`roadTwoPoint`）。

S4 只写顺序和选择键，并在自己的条目片段里加上 S1 图专有的路障：

```json
// CrowdSimulationS4DeployMod/assets/Presentation/crowd_structure_palette.json
{
  "s7barrier": {
    "template": "s7barrier",
    "label": "路障 square 70m(4s 寿命)",
    "gesture": { "kind": "point" },
    "sizeCm": 7000
  }
}
```

```json
// CrowdSimulationS4DeployMod/assets/CrowdSimulationDebug.json（相关片段）
"tools": {
  "boomRadiusCm": 12500, "ringScale": 2, "impulse": 4,
  "spawnCount": 24, "spawnUnitType": 0, "spawnRadiusClass": 0,
  "placeEntry": "building",
  "speedStepsPermille": [500, 1000, 2000, 4000]
},
"presentation": {
  "deploy": {
    "placeEndpointArea": "(桥端不可锚)",
    "placeInteriorArea": "(跨越段地形不可放)",
    "placeEndpoint": [255, 214, 64, 242],
    "palette": [
      { "entry": "building", "selectAction": "CrowdSimulation.S4.Template1" },
      { "entry": "s7barrier", "selectAction": "CrowdSimulation.S4.Template2" },
      { "entry": "roadFixed", "selectAction": "CrowdSimulation.S4.Template3" },
      { "entry": "flood", "selectAction": "CrowdSimulation.S4.Template4" },
      { "entry": "roadTwoPoint", "selectAction": "CrowdSimulation.S4.Template5" },
      { "entry": "bridge", "selectAction": "CrowdSimulation.S4.Template6" }
    ]
  }
},
"fuzz": {
  "pathTemplate": "bridge", "pathWidthCm": 6250, "pathLengthCm": 62500,
  "ops": [
    { "id": "placePath", "structure": true, "probeHandles": false, "reserved": false },
    { "id": "removePath", "structure": true, "probeHandles": false, "reserved": false }
  ]
}
```

`palette` 数组覆盖时整份替换，这和今天 `structures` 数组一样。`ops` 只列了改名的两项，其余不变。

## 5 形状规则和对照

### 5.1 内核固定合同（不进配置）

这些是算法本身的约定或数值容差，改了就是另一个内核，不做成配置：

- `square` 轴对齐，中心 ± 边长/2。`disc` 用 Int128 比较距离平方，避开 Q31.32 平方在约 46340 厘米处溢出。`path` 是没有圆角的条带，长度不超过 1e-7 厘米时方向取 (1, 0)。
- 层固定两个：`base` 地面栅格（区域下标 + 阻挡位图），`upper` 条带上层，只在入口格和地面相连。层没有参数。
- `setArea` 按格心是否在足迹内；`block` 按 X、Y 两向覆盖份额之积 ≥ 这个模板的 `coverage`。
- 上层条带用 `DeckSpanGeometry` 判格，端点容差 1e-6 米；入口是离端点距离 < 这个模板的 `portalReachNavCells × navCellSizeCm` 的格；条带和岸边合并半径 = 净空 − 1。
- 坡度判定 `slope > tan(maxSlopeDeg)`，`ignoresSlope` 区域跳过。
- 链接按直线走，速度 = 单位速度 × `traversalSpeedRatio`；走链接时避让半径记 0；落点取不到时退回格心；链接长度下限 1e-3。
- 候选烘焙走 8 个方向，长度到 `maxGapCm / navCellSizeCm + 1` 格；按中点和 `linkSpacingCm` 分桶去重，同桶保留更短的；落差四舍五入到厘米。

### 5.2 作者侧检查（建议，另行决定）

下面几条会改变现有行为，不进第 7 节的等价范围，实现时单独评审：

- 放置后一格都没盖到的结构拒收，新增拒因。
- 上层条带宽度小于一个导航格时拒收。
- `maxGapCm / navCellSizeCm + 1` 小于带 k 的行里最大的 `rangeNavCells` 时给出警告，因为超出扫描范围的那部分 `rangeNavCells` 永远用不上。当前数据是 6 + 1 = 7，等于 `leaper` 的 7，不触发。

### 5.3 和常见引擎的对照

| 概念 | 本方案 | Recast / Detour | Unity AI Navigation | Unreal |
|---|---|---|---|---|
| 区域 | 最多 256 个，按 id 合并 | area id 0–63 | 32 个 area | `UNavArea` 子类 |
| 代价 | 每行每区域一个 `cost` | `dtQueryFilter::setAreaCost`，查询时给 | 每个 area 一个 cost，agent 可用 area mask 排除 | `DefaultCost`、`FixedAreaEnteringCost`，查询过滤器可覆盖 |
| 速度 | 每行每区域一个 `speedDivisor`，和代价分开 | 没有，由上层自己做 | 没有，`NavMeshAgent.speed` 全局 | 没有，由移动组件决定 |
| 不可通行 | 格写 `false` | include / exclude 标志 | area mask | 查询过滤器排除 |
| 移动类型 | 一份地面栅格，每行一套代价和净空变体 | 每套 agent 参数单独烘一份网格 | 每个 Agent Type 单独烘一份 | 每个 Supported Agent 单独一份 RecastNavMesh |
| 坡度 | 每行 `maxSlopeDeg`，区域可 `ignoresSlope` | `walkableSlopeAngle`，烘焙时定 | Agent Type 的 Max Slope | Agent 的 Max Slope |
| 链接 | 候选由地形几何自动烘焙，每行按 `upCm` / `downCm` / `rangeNavCells` 过滤，按格的 `depart` / `arrive` 选端点 | off-mesh connection，手放，带半径、方向、area、flags | `NavMeshLink` 手放；旧版烘焙有 Drop Height / Jump Distance 自动生成 | `NavLinkProxy` 手放 |
| 多层 | 固定 `base` + `upper` 两层，上层只有条带 | 高度场原生支持重叠层 | 烘焙几何决定 | 烘焙几何决定 |
| 运行时改地面 | `setArea` / `block` 结构，脏 tile 增量重烘 | TileCache 障碍物、凸体标 area | `NavMeshObstacle` carve、`NavMeshModifierVolume` | 动态障碍、`NavModifierVolume` |
| 放置约束 | `spanEnds` 按地形区域判两端和中段 | 无 | 无 | 无 |

差别主要在两处。一是代价按“移动类型 × 区域”写成整张矩阵，不是“区域一个代价、单位挑选区域”，因为五类单位在同一区域上的代价各不相同，而且速度和代价要分开。二是链接自动生成、每行过滤，不用一条条手放；这和 Unity 旧版的自动下落/跳跃链接接近，但端点由矩阵决定，不由烘焙参数决定。

## 6 `.navsurface` v2

小端。v1 资产直接拒绝，报文提示重烘；4 个现有资产（S1 1337、2024、7 和 Minimal）在第 8 步一起重烘。

| 偏移 | 类型 | 字段 | 规则 |
|---|---|---|---|
| 0 | u32 | magic `"LNSF"` | |
| 4 | u16 | version = 2 | |
| 6 | u16 | headerBytes = 32 | |
| 8 | i32 | cellsX | > 0 |
| 12 | i32 | cellsY | > 0 |
| 16 | i32 | navCellSizeCm | 等于 `maps.<mapId>.navCellSizeCm` |
| 20 | u16 | terrainCount | 1–256 |
| 22 | u16 | linkKindCount | 0–255 |
| 24 | i32 | linkCandidateCount | ≥ 0 |
| 28 | u32 | linkBakeDigest | 见下 |

头之后依次是：

1. 地形表：terrainCount 项 `{ u16 len, utf8 id }`，必须和 `maps.<mapId>.terrainTypes` 逐字同序。
2. 链接种类表：linkKindCount 项 `{ u16 len, utf8 id }`，等于 `traversal.json` 的 `linkKinds` 合并顺序里、这张图 `linkCandidateBake` 写了的那些种类。
3. 格：cellsX × cellsY 个 u8，值是地形表下标。
4. 候选：linkCandidateCount 条，每条 16 字节，按种类下标升序分组：

| 偏移 | 类型 | 字段 | 规则 |
|---|---|---|---|
| 0 | u8 | kindIndex | < linkKindCount |
| 1 | u8 | flags | bit0 = 斜向；其余位必须为 0 |
| 2 | u16 | steps | ≥ 1 |
| 4 | u16 | fromX | 高端格 |
| 6 | u16 | fromY | |
| 8 | u16 | toX | 低端格 |
| 10 | u16 | toY | |
| 12 | i32 | dropCm | ≥ 0 |

读取时检查：两端在图内；`to − from` 的每个分量是 0 或 ±steps；两个分量都非 0 当且仅当 bit0 为 1；总长度和头里的计数精确相符，多一字节少一字节都拒绝。

v1 的 `f32 LengthCells` 去掉，改由 steps 和斜向位算：`diagonal ? (float)(steps * Math.Sqrt(2.0)) : steps`。这和今天烘焙器（`CrowdJumpCandidateBake` 第 137 行）写进 v1 的值是同一个 float，后面 `Fix64.FromFloat` / `FromDouble` 的结果逐位相同。

`linkBakeDigest` 是 32 位 FNV-1a，对下面的字节序列求值，烘焙器写入，运行时按当前配置重算并比对，不同就报错“链接候选和配置不一致，重烘”：

- i32 navCellSizeCm
- 对链接种类表里的每个种类，按表顺序：utf8 id、一个 0x00、i32 maxGapCm、i32 linkSpacingCm、endpointMaxSlopeDeg 的 IEEE 754 双精度位、再按地形表顺序每个地形一个字节（bit0 = 端点地形，bit1 = 硬缝地形）

链接种类表为空时，摘要就是只含 navCellSizeCm 的那份值。烘焙时排除的地图上层条带覆盖格来自地图实体，不进摘要；今天改桥也要重烘，这一点不变。

## 7 等价表

实现完成后，下面每一项都要和今天逐位相同。

| 数值或不变量 | 今天在哪 | 新位置 | 值 |
|---|---|---|---|
| 区域顺序与 ignoresSlope（旧名 slopeFree） | StandardMod `navmesh.json` `areas.<id>.areaId` | `traversal.json` `areas` 合并顺序 | 0 open(Default) / 1 shallow / 2 rough / 3 sheer / 4 deep（忽略坡度） / 5 road / 6 deck（忽略坡度） / 7 flooded（忽略坡度） / 8 burning |
| 移动类型顺序 | `pathing.json` `agentTypes` 属性顺序 | `traversal.json` `agentTypes` 属性顺序 | foot, hull, climber, amphib, leaper |
| 45 格的 cost / speedDivisor / 可通行 | `pathing.json` `areaCosts`（`speedDivisor` 省略 = cost，未列 = 不可通行） | `cells`，两项都显式写 | 见两份 traversal 示例 |
| maxSlopeDeg | `navmesh.json` `profiles.<id>.maxSlopeDeg`，经体型 `navProfileId` | `agentTypes.<id>.maxSlopeDeg` | 24 / null / 38 / 20 / 30 |
| 跳跃能力 | `profiles.leaper.jump { upCm, downCm, rangeNavCells, cost }` | `agentTypes.leaper.links.jump { upCm, downCm, rangeNavCells, costPerNavCell }` | 2500 / 20000 / 7 / 2 |
| 链接速度倍率 | `movement.jumpSpeedRatio` | `linkKinds.jump.traversalSpeedRatio` | 1.3 |
| 阻挡覆盖份额 | `structures.blockCoverage`（全局一份） | `building`、`s7barrier` 的 `effect.coverage` | 0.5 |
| 入口格数 | `structures.portalReachNavCells`（全局一份） | `bridge`、`timedBridge` 的 `effect.portalReachNavCells` | 4 |
| 导航格边长 | 各能力档 `cellSizeCm`（必须相同） | `maps.<mapId>.navCellSizeCm` | 6250 |
| 地形到区域 | 每图 `terrainAreas[]` | 全局 `terrainAreas` | water→deep, shore→shallow, land→open, mountain→rough, cliff→sheer |
| 地形顺序 | 每图 `terrainAreas[]` 的下标 | 每图 `terrainTypes` | S1：water, shore, land, mountain, cliff；Minimal：land, water |
| 候选几何参数 | `jumpLinkBake { maxGapCm, linkSpacingCm, endpointMaxSlopeDeg }` | `linkCandidateBake.jump` | 37500 / 31250 / 22 |
| 端点 / 硬缝地形 | `jumpLinkBake.endpointTerrain` / `gapTerrain` | 由矩阵推导 | {shore, land, mountain} / {cliff, water} |
| 每行链接过滤 | `passable[a] && passable[b]` | `depart` / `arrive` 判定 | 当前数据下集合相同（2.3） |
| 结构模板 | 旧字段 | 4.4 表 | building、s7barrier、bridge、timedBridge、road、flood、fire 的形状、效果、优先级、寿命逐项相同 |
| 桥的放置 | `anchorTerrain [shore, land]`、`spanTerrain [water]` | `endpointAreas [shallow, open]`、`interiorAreas [deep]` | 地形到区域一一对应，判定相同 |
| 模板下标 | 按模板 id 排序 | 不变（id 没改） | |
| 地图结构 id | 阻挡按实体顺序，再桥按实体顺序 | 不变 | |
| 摆放面板 | `presentation.deploy.structures[0..5]` | `palette[0..5]` → 条目 | 模板、尺寸、手势、+X 25000 逐项相同 |
| `.navsurface` 格字节 | v1 | v2 | 同一份地形表和栅格，逐字节相同 |
| 链接候选 | v1 记录 | v2 记录 | 同一组端点、落差，长度 float 由同一公式得出 |
| 校验域常量 | `0x6A756D70` 等 | 不变 | 只改名，不改值 |
| truth 数值 | S1 / S2 / S7 truth | 不重新生成 | 只改键名：`jumpCandidates` → `linkCandidates`，`bridges` → `layeredPaths` |

已知例外只有一处：Minimal 的运行时区域表从 9 个变成 5 个（3.1），寻路结果相同，摘要和缓存键要在第 6b 步核对。

表里配置部分（区域、矩阵、坡度、链接、地形对应、推导掩码、模板含覆盖份额和入口格数、放置集合、面板）已经用脚本对示例文件和当前 Mod 数据逐项比过，149 项全部一致。`.navsurface` 和 truth 两行要到第 8 步重烘后再比。

## 8 C# DTO 草图

只给结构，不是最终代码。全部用 `StrictJsonOptions.CreateCamelCase()` 反序列化，目标框架是 net9.0。`false` 与对象二选一的格和链接用自定义转换器；带 `kind` 的联合类型用 `JsonPolymorphic`，并打开 `AllowOutOfOrderMetadataProperties`，因为合并后的对象里 `kind` 不一定在第一个。

```csharp
namespace Ludots.Core.CrowdSimulation.Config;

public sealed record TraversalFile
{
    [JsonRequired] public required OrderedIdMap<NavAreaDef> Areas { get; init; }
    [JsonRequired] public required Dictionary<string, string> TerrainAreas { get; init; }
    [JsonRequired] public required OrderedIdMap<LinkKindDef> LinkKinds { get; init; }
    [JsonRequired] public required OrderedIdMap<TraversalRow> AgentTypes { get; init; }
}

public sealed record NavAreaDef
{
    [JsonRequired] public required bool IgnoresSlope { get; init; }
}

public sealed record LinkKindDef
{
    [JsonRequired] public required double TraversalSpeedRatio { get; init; }
}

public sealed record TraversalRow
{
    // 必写；null 表示不限坡度。
    [JsonRequired] public required double? MaxSlopeDeg { get; init; }
    [JsonRequired] public required OrderedIdMap<LinkAbility> Links { get; init; }
    [JsonRequired] public required OrderedIdMap<TraversalCell> Cells { get; init; }
}

[JsonConverter(typeof(FalseOrObjectConverter<LinkAbility, LinkAbility.Capable>))]
public abstract record LinkAbility
{
    public sealed record None : LinkAbility;

    public sealed record Capable : LinkAbility
    {
        [JsonRequired] public required int UpCm { get; init; }
        [JsonRequired] public required int DownCm { get; init; }
        [JsonRequired] public required int RangeNavCells { get; init; }
        [JsonRequired] public required double CostPerNavCell { get; init; }
    }
}

[JsonConverter(typeof(FalseOrObjectConverter<TraversalCell, TraversalCell.Passable>))]
public abstract record TraversalCell
{
    public sealed record Blocked : TraversalCell;

    public sealed record Passable : TraversalCell
    {
        [JsonRequired] public required double Cost { get; init; }
        [JsonRequired] public required double SpeedDivisor { get; init; }
        [JsonRequired] public required string[] Depart { get; init; }
        [JsonRequired] public required string[] Arrive { get; init; }
    }
}

public sealed record StructureTemplateDef
{
    [JsonRequired] public required StructureLayer Layer { get; init; }
    [JsonRequired] public required StructureFootprint Footprint { get; init; }
    [JsonRequired] public required StructureEffect Effect { get; init; }
    [JsonRequired] public required StructurePlacement Placement { get; init; }
    // 必写；null 表示永久。
    [JsonRequired] public required double? LifetimeSec { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<StructureLayer>))]
public enum StructureLayer { Base, Upper }

[JsonConverter(typeof(JsonStringEnumConverter<StructureFootprint>))]
public enum StructureFootprint { Square, Disc, Path }

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Block), "block")]
[JsonDerivedType(typeof(SetArea), "setArea")]
[JsonDerivedType(typeof(Surface), "surface")]
public abstract record StructureEffect
{
    public sealed record Block : StructureEffect
    {
        [JsonRequired] public required double Coverage { get; init; }
    }

    public sealed record SetArea : StructureEffect
    {
        [JsonRequired] public required string Area { get; init; }
        [JsonRequired] public required int Priority { get; init; }
    }

    public sealed record Surface : StructureEffect
    {
        [JsonRequired] public required string Area { get; init; }
        [JsonRequired] public required int PortalReachNavCells { get; init; }
    }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Unrestricted), "unrestricted")]
[JsonDerivedType(typeof(SpanEnds), "spanEnds")]
public abstract record StructurePlacement
{
    public sealed record Unrestricted : StructurePlacement;

    public sealed record SpanEnds : StructurePlacement
    {
        [JsonRequired] public required string[] EndpointAreas { get; init; }
        [JsonRequired] public required string[] InteriorAreas { get; init; }
    }
}

public sealed record CrowdMapNavEntry
{
    [JsonRequired] public required string Solver { get; init; }
    [JsonRequired] public required string LogicTerrainAsset { get; init; }
    [JsonRequired] public required int NavCellSizeCm { get; init; }
    [JsonRequired] public required string[] TerrainTypes { get; init; }
    public Dictionary<string, LinkCandidateBakeDef>? LinkCandidateBake { get; init; }
    public JsonObject? Boards { get; init; }
}

public sealed record LinkCandidateBakeDef
{
    [JsonRequired] public required int MaxGapCm { get; init; }
    [JsonRequired] public required int LinkSpacingCm { get; init; }
    [JsonRequired] public required double EndpointMaxSlopeDeg { get; init; }
}

public sealed record StructurePaletteEntry
{
    [JsonRequired] public required string Template { get; init; }
    [JsonRequired] public required string Label { get; init; }
    [JsonRequired] public required PlacementGesture Gesture { get; init; }
    [JsonRequired] public required int SizeCm { get; init; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Point), "point")]
[JsonDerivedType(typeof(FixedPath), "fixedPath")]
[JsonDerivedType(typeof(TwoPoint), "twoPoint")]
public abstract record PlacementGesture
{
    public sealed record Point : PlacementGesture;

    public sealed record FixedPath : PlacementGesture
    {
        [JsonRequired] public required OffsetCm ToOffsetCm { get; init; }
    }

    public sealed record TwoPoint : PlacementGesture;
}

public readonly record struct OffsetCm([property: JsonRequired] int XCm, [property: JsonRequired] int YCm);

public sealed record DeployPaletteSlot
{
    [JsonRequired] public required string Entry { get; init; }
    [JsonRequired] public required string SelectAction { get; init; }
}
```

```csharp
namespace Ludots.Core.CrowdSimulation;

public struct CrowdSimulationStructure
{
    public string Template;
}

public struct CrowdSimulationPathSpan
{
    public int FromXCm;
    public int FromYCm;
    public int ToXCm;
    public int ToYCm;
    public int WidthCm;
}
```

`OrderedIdMap<T>` 是保持 JSON 属性顺序的只读字典（键 → 下标 → 值），区域、行、链接种类的下标都从它来。`CrowdSimulationStructure.Template` 是字符串，只在会话激活时换成模板下标，tick 里不读，和 `CrowdSimulationAgent` 的做法一样。

## 9 编辑器影响

- `CrowdConfigEditorPage.tsx` 的“代价矩阵”页改读写 `traversal.json`。列是合并后的全部区域，不再按某张图的 `navAreas` 截取。格子编辑器：可通行开关（`false` ↔ 对象）、`cost`、`speedDivisor`、按链接种类勾选 `depart` / `arrive`。行头加 `maxSlopeDeg`（带“不限”开关）和 `links` 编辑。
- `CrowdSimulationEditorApi.PathingFile` 换成 traversal 文件常量；`PatchCostMatrix` 改成按格写补丁，补丁可以是 `false` 或对象片段；`PreviousCost` 跟着改。补丁写进当前编辑的 Mod 的 `traversal.json` 片段，依赖 3.2 说的合并行为。
- `CrowdSimulationEditorSession.CostMatrix(pathing, navmesh, mapId)` 改成 `CostMatrix(traversal)`，地图只用来高亮这张图实际出现的地形区域。
- 结构模板检视器按 4.1 的字段重做，组合按 4.2 的表限制；摆放手势条目单独一页或并进模板页。
- `CrowdSimulationEditorEndpoints.cs` 的 RPC 名不变，改写目标文件。
- 工具里的 `CrowdModScaffold` 生成新地图时写 `navCellSizeCm` 和 `terrainTypes`，不再写 `terrainAreas`、`navAreas`。

## 10 实施步骤

在审计给的 1–9 步基础上调整。每步结束时全绿。“现在”指基于 `cursor/crowdsim-integrate-af78` 就能开工；其余要等对应分支合进集成分支。

| # | 内容 | 依赖 | 何时开工 |
|---|---|---|---|
| 0 | 本页：配置结构设计稿，评审通过后作为实现依据 | — | 本次 |
| 1 | 棘轮测试：扫描 `src/Core/CrowdSimulation` 主题词，当前计数为上限，只许减少 | 0 | 现在 |
| 2 | 导航层改名：`Deck*` → `UpperLayer*`，`Ground*` → `BaseLayer*`，`BridgeDeckRecord` → `LayeredPathRecord`；只动 `Nav/`、`Structures/`、`World/`，不动数据 | 1 | 现在；`Nav/Pathing/PathQueryService.cs` 那几处等 `cursor/crowdsim-stray-extend-parity-af78` |
| 3a | 运动、单位、诊断改名：`Jump*` → `Link*`，`CrowdUnitState.Traversing = 4`，校验域常量值不变 | 2 | 等 `cursor/crowdsim-stray-extend-parity-af78`、`cursor/crowdsim-showcases-af78`（都改 `CrowdSimPlanner.cs`） |
| 3b | 呈现、HUD、遥测改名（`Jumping` → `Traversing`，文案） | 2 | 等 `cursor/crowdsim-ui-infra-af78`、`cursor/crowdsim-showcases-af78`（重写 `S4DeployDemoPresentationSystem`、`CrowdPlayerViewHost`、`CrowdDebugPanels`，HUD 搬进 `CrowdSimulationUiMod`） |
| 4 | 模板 id 特例去掉：`CrowdSimulationStructure`、`CrowdSimulationPathSpan`，删 `CrowdSimulationNavArea`，改 S1 三张图和 Minimal 的实体模板与地图实例，删 `MapSurfaceSource` 的隐藏默认 | 2 | 现在 |
| 5 | 调试与工具键：fuzz `path*` 和 op id、`tools.placeEntry`、删 `tools` 三个尺寸键、手势条目文件、S4 `palette`、`waterXCm` → `probeXCm`、`jumpLinks` / `toggleJumpLinks` | 3a、3b、7 | 等 `cursor/crowdsim-ui-infra-af78`、`cursor/crowdsim-showcases-af78`（都改 `CrowdSimulationDebugConfig.cs` 或 S4 的调试 JSON） |
| 6a | 共享导航装载器：`maps.<mapId>` 白名单按 solver 分（3.3 第 1 条） | 0 | 现在；属于 Core 基建，按本页方案单独提交 |
| 6b | `traversal.json`、DTO、群体装载器重写，`Default` → `open`，删 `pathing.json` 和 StandardMod `navmesh.json`，删三处隐藏默认（装载器代价默认 1、`PathingAreaCosts` 的 speedDivisor 与 passable 默认、`CrowdLeader` 的速度除数回退 1），改 `TestDefaults` 和 `fixtures/demo-navmesh.json` | 2、6a | 装载器部分现在；编辑器部分等 `cursor/crowdsim-presenter-contract-af78`（改 `CrowdSimulationEditorApi.cs` 和 `CrowdConfigEditorPage.tsx`） |
| 7 | 结构模板新字段：`layer`、`effect`、`placement`，删 `anchorTerrain` / `spanTerrain`，拒因改名 | 4、6b | 内核与装载器现在；`S4DeployDemoPresentationSystem.JudgePreview` 那部分等 `cursor/crowdsim-ui-infra-af78` |
| 8 | `.navsurface` v2、候选烘焙改用推导掩码和每图几何参数、`linkBakeDigest`、重烘 4 个资产、truth 改键名 | 6b | 6b 之后即可，没有待合分支碰 `NavSurfaceAsset` 和烘焙工具 |
| 9 | 棘轮清零，更新 `crowdsimulation-and-navsurface.md`、`crowdsimulation-minimal-mod.md`、`spatial-scale-and-resolution-ssot.md`、`gitbook/reference/spatial-scale-configuration.md` | 7、8 | 最后 |

和审计原计划的差别：

- 第 5 步排到第 7 步之后，因为手势条目要引用新模板字段。
- 第 6a 步不再处理根上的 Recast 键：Core 自带的 `navmesh.json` 已经有，StandardMod 删掉自己那份即可。
- 第 6b 步明确列出要删的三处隐藏默认。
- 第 7 步里 `blockCoverage` 和 `portalReachNavCells` 进每个模板的效果；审计稿只把后者挪进全局层参数。
- 地图实体的组合限制（4.3）和 `tools` 三个尺寸键的删除是本页新增的。

`cursor/crowdsim-showcases-af78` 新增的 Fog、March、Stress50k 等展示 Mod 只写 `session` 和 `presentation` 的标题、镜头，不碰结构、面板和矩阵，合进来后不需要跟着改数据。
