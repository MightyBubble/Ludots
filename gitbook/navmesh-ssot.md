# NavMesh 导航体系 SSOT

> 本页是 Ludots 导航网格（NavMesh）体系的产品与架构总合同。它定义目标产品形态、运行时边界、作者配置、编辑器行为、showcase 体验和验收口径。
>
> `gitbook/reference/` 下的 NavBake、运行时重烤、预算和编辑器页面是本页的实现细节与操作材料。它们不得另行定义导航体系的产品边界；目标产品边界以本页为准，当前实现范围以末节证据为准；代码未达到目标时记录差距，不把现状悄悄改写为目标。
>
> 按功能拆分的编辑器、工具、Runtime 和 Showcase 设计见 [NavMesh 功能目录](navmesh-features/README.md)。

## 1. 概述

Ludots 的导航体系要解决一件事：**地图作者声明“哪里能走、谁能走、走哪里代价高、结构变化后怎样更新”，玩家下达移动命令后，系统给出可解释、可验证、能随着世界变化更新的路径。**

NavMesh 不是一个孤立的寻路算法，也不是编辑器里一层绿色网格。它是一条从地图数据到玩家行为的正式产品链：

```text
地图与地形源
    ↓
每板导航声明
    ↓
语义与障碍快照
    ↓
离线烘焙 / 运行时局部重烤
    ↓
带版本的 NavTile 产物
    ↓
查询服务与 MassNavigation 执行
    ↓
玩家可见的路径、绕行、调试显示与验收证据
```

目标产品形态有四个使用者：

| 使用者 | 他要完成的事 | 产品应给出的结果 |
|---|---|---|
| 地图作者 | 声明地形、板、单位 profile、区域语义和结构障碍 | 一套配置、一条作者命令、可重复的 NavTile 产物 |
| 工具使用者 | 在 Web/Raylib 编辑器里调整并验证地图 | 估算、烘焙、脏区重烤、路径模拟和明确错误反馈 |
| 游戏运行时 | 查询路径、处理门墙桥等结构变化 | 版本一致的查询、原子换入的新瓦片和可追踪的重算 |
| 新玩家/评审者 | 看懂导航解决了什么问题 | 可启动、可操作、有消融对照和真实状态 HUD 的 showcase |

### 产品原则

- **单一事实源**：地图板、地形源、导航语义、profile、layer 和障碍各有明确 owner；下游只消费上游产物。
- **正式管线**：CLI、Editor Bridge、Web 编辑器、Raylib showcase 和运行时重烤都调用同一套 Core 服务。
- **无 fallback**：缺配置、来源冲突、产物过期、容量不足和不支持的组合必须 fail-fast，并给出下一步；不能悄悄改用平地、直线或另一种算法。
- **数据驱动**：尺寸、原点、 profile、layer、预算和运行时档位来自配置与 manifest，不写在 UI 或 showcase 代码里。
- **可解释**：每条路径都能回到 map、board、profile、layer、tile revision 和 source revision；每次重烤都能说明为什么发生、影响哪些瓦片、何时发布。
- **产品与验收分开**：测试证明系统成立，showcase 让没有读过代码的人看懂它为什么有用。

## 2. 结构

### 2.1 领域分层

```text
Board / WorldExtent
  ├─ boardId、拓扑、原点、世界范围、tile 几何
  └─ NavigationEnabled、NavTileGrid 声明

NavBakeSource
  ├─ ContinuousHeightmapSource  (.height)
  ├─ GridDataSource             (.grid)
  └─ HexDataSource              (.hex)

NavigationIntent
  ├─ AgentProfile               半径、爬坡、坡度等几何能力
  ├─ Layer                      地面、水面、特殊层
  ├─ AreaSemantic               区域含义与通行语义
  └─ StructuralObstacleIntent   墙、门、桥、建筑等拓扑变化

NavBakeContext / NavBakeService
  ├─ source snapshot
  ├─ board-local tile targets
  ├─ profile / layer / semantic / obstacle snapshot
  └─ build budget and algorithm capability

NavTileArtifact
  ├─ tile identity: map + board + layer + profile + local coordinates
  ├─ geometry / portals / area ids
  ├─ build hash / source revision / tile revision
  └─ query payload and presentation payload from one authority

NavQueryServiceRegistry
  └─ board + layer + profile → versioned NavTileStore → query service

PathingConfig / AutoPathService
  ├─ Graph / Mesh / Auto route-domain selection
  └─ dynamic graph edge cost overlay (Graph only)

MassNavigation
  ├─ route selection and move plan
  ├─ path execution and arrival
  └─ temporary crowd avoidance (does not trigger NavMesh rebuild)
```

### 2.2 能力归属

| 能力 | 唯一归属 | 不应放在哪里 |
|---|---|---|
| 跨域区域分类 | Map/Terrain 的 areaId/tags（#372）；导航只消费 | 地形 cost、Nav 私有词汇表 |
| Agent 通行代价 | `PathingConfig.agentTypes[].navMesh.areaCosts` | 地图分类、sidecar 或几何 profile |
| 地形源读取与快照 | `NavBakeSource` adapter | Web 私有格式、showcase 私有 loader |
| 配置合并与校验 | `ConfigPipeline` + `NavMeshBakeConfigLoader` | CLI 手写默认值、编辑器自行解释 JSON |
| 烘焙 | `NavBakeContext` + `NavBakeService` + `INavBakeAlgorithm` | CLI/Bridge 各写一套 bake loop |
| 障碍几何 | Navigation obstacle SSOT → `NavObstacleSet` | 另建一份编辑器障碍表 |
| 运行时重烤 | `RuntimeIncrementalNavMeshRebuildQueue` | 临时拥堵系统、showcase 回调 |
| 查询 | `NavQueryServiceRegistry` + `NavQueryService` | Mod 直接组装 Detour |
| 路由域选择 | `PathServiceRouter` + `AutoPathService` + `PathingConfig` | Mod 私有 Graph/Mesh 选择器 |
| 图动态边权 | `GraphEdgeCostOverlay` | 地图 area cost 或 NavTile 几何 |
| 导航显示 | `NavMeshPresentationBuffer/State/System` + Raylib renderer | Web 画近似三角、showcase 自画另一套网格 |
| 路线执行 | `MassNavigationMovePlanExecutionSink` + MassNavigationFlow | NavMesh 查询服务直接移动实体 |
| 临时避障 | MassNavigation avoidance | 触发 NavTile 重烤 |
| 作者工作台 | Editor Bridge / Web / Raylib adapter | 复制 Core 数据模型和规则 |

### 2.3 生命周期

```text
Authoring
  读取地图与 board 声明
  校验 source / profile / layer / semantic / obstacle
  生成 estimateHash 与 bake plan

Bake
  冻结 source + intent snapshot
  按 board-local targets 构建 NavTile
  写入唯一权威 artifact 和 manifest

Load
  校验 buildHash、sourceRevision、formatVersion、board identity
  注册 NavTileStore 和 query service
  presentation 从同一 artifact 派生

Runtime change
  structural obstacle intent 变更
  dirty AABB → board-local tile set
  queue 按预算重烤 → revision check → 主线程原子发布
  query 使用新 snapshot；旧任务不得覆盖新 revision

Retire
  旧 artifact 被明确拒载并指向重烤命令
  不保留静默兼容格式或第二条查询路径
```

## 3. 详情

### 3.1 Runtime 运行时

#### 查询合同

运行时查询必须先确定：`mapId`、`boardId`、`layerId`、`profileId` 和目标点。查询服务从 registry 得到对应的 `NavTileStore`，按 board 的原点和两轴 tile 尺寸定位局部瓦片，再组装或复用查询实例。

查询结果至少包含：

| 字段 | 含义 |
|---|---|
| `status` | 沿用 `PathStatus` 粗状态并附详细原因：成功、输入无效、缺瓦片、过期、不可达、容量不足或系统错误 |
| `path` | 世界坐标路径点，首尾与请求目标可追溯 |
| `cost` | 按本次 Agent 通行策略与 area 分类计算的路径代价；几何 profile/layer 另行决定可行性 |
| `tileRevisions` | 本次查询使用的瓦片版本 |
| `buildHash` | 产物与配置/源输入的对应关系 |
| `diagnostics` | 缺瓦片、过期、容量或系统异常的明确原因 |

查询缓存属于查询基础设施：几何缓存绑定 store 的 board/layer/profile 身份、加载集合版本和 build hash；结构变更发布后使旧几何缓存失效。路径结果另带代价策略版本，单改 area cost 不重建几何。缓存不能绕开 `NavTileStore`，也不能把陈旧路径静默交给执行系统。

#### 运行时重烤合同

只有持久结构变化进入 NavMesh 重烤：墙、门、桥、建筑 footprint 等会改变远程连通性的变化。单位拥挤、短寿命 blocker 和局部避让仍由 MassNavigation avoidance 处理。

```text
StructuralObstacleIntent changed
  → Navigation obstacle SSOT materializes NavObstacleSet
  → RuntimeNavMeshObstacleDirtySystem emits dirty AABB
  → board-local tile coordinates + neighbors
  → immutable source/intent snapshot
  → bounded runtime bake tier
  → stale generation check
  → NavTileStore.Replace atomically
  → query cache invalidation
  → route refresh / execution continues
```

运行时重烤必须有显式档位。离线 authoring 可以使用高精度源；运行时使用有界分辨率、瓦片边长和体素预算。不能因为运行时来不及，就偷偷降低精度、跳过障碍或改成直线。

#### 版本与线程边界

- source snapshot、obstacle snapshot 和 semantic snapshot 在 worker 开始前固定。
- worker 只构建不可变结果，不直接修改运行中的 `NavTileStore`。
- 主线程发布时检查 `(boardId, layerId, profileId, tileCoord, generation)`；旧 generation 的结果丢弃并记录原因。
- `NavTileStore.Replace` 是原子边界；查询要么看到旧完整瓦片，要么看到新完整瓦片。
- 发布后通知 query cache、MassNavigation route refresh 和 presentation state。
- 任何失败都保留失败原因，不生成“看起来能走”的替代产物。

#### 与 MassNavigation 的边界

NavMesh 负责“哪里能走”和“路径是什么”；MassNavigation 负责“单位怎样沿路径走”。路径刷新不应直接改写 authored order payload。临时避障只影响执行层，不触发 NavTile 变更。

### 3.2 配置结构

目标产品使用“一张地图、多块 board、每块 board 声明自己的源和导航输出”。下面是目标形态示例，字段名表达合同；未实现字段在“现状和 TODO”中列出：

```json
{
  "mapId": "coastline",
  "boards": [
    {
      "id": "mainland",
      "topology": "grid",
      "navigationEnabled": true,
      "originCm": { "x": -3200000, "z": -1800000 },
      "tile": { "widthCm": 64000, "heightCm": 64000, "cells": 64 },
      "source": {
        "kind": "height",
        "uri": "World:Terrain/coastline.height",
        "revision": "sha256:..."
      }
    },
    {
      "id": "harbor",
      "topology": "hex",
      "navigationEnabled": true,
      "originCm": { "x": 120000, "z": 45000 },
      "tile": { "widthCm": 44340, "heightCm": 38400 },
      "source": {
        "kind": "hex",
        "uri": "World:Terrain/coastline.hex",
        "revision": "sha256:..."
      }
    }
  ],
  "profiles": [
    { "id": "infantry", "radiusCm": 35, "maxClimbCm": 40, "maxSlopeDeg": 45 },
    { "id": "ship", "radiusCm": 180, "maxClimbCm": 0, "maxSlopeDeg": 4 }
  ],
  "layers": [
    { "id": "ground", "layer": 0, "boardIds": ["mainland"] },
    { "id": "water", "layer": 1, "boardIds": ["mainland", "harbor"] }
  ],
  "semantics": {
    "areas": [
      { "id": "road", "areaId": 2 },
      { "id": "mud", "areaId": 3 },
      { "id": "water", "areaId": 4 }
    ],
    "sidecars": ["World:Navigation/coastline.semantic.json"]
  },
  "runtime": {
    "enabled": true,
    "tier": "bounded",
    "tileBudgetPerFixedTick": 4,
    "includeNeighborTiles": true,
    "snapshot": "immutable",
    "maxTileMilliseconds": 25,
    "maxVoxelColumns": 33500000
  }
}
```

示例表达目标概念，不是可直接复制的当前 schema。区域分类按 [#372](https://github.com/MightyBubble/Ludots/issues/372) 只存 `areaId/tags`；代价沿用现有 `Navigation/pathing.json` 的 `agentTypes[].navMesh.areaCosts[]`，逐 Agent 解释区域，不放在地图声明里。示例和双 Agent 验收见 [区域分类与 Agent 通行代价](navmesh-features/area-costs.md)。单改某 Agent 的代价不重烘焙几何，也不改变另一 Agent 的策略。时间预算用于估算和诊断，提交数量不保证后台工作在同一 tick 完成。

配置规则：

1. `board.id`、`layer.id`、`profile.id` 和 source `uri` 区分大小写，重复项直接失败。
2. board 的原点、tile 宽高和拓扑由 board 声明；registry、路径和查询都消费同一份值。
3. profile 只描述单位几何和可通行能力；展示尺寸属于 presentation binding，不进入 profile。
4. area 的语义来自地图/地形源；pathing 只消费成本，不反向修改地形分类。
5. structural obstacle 必须带 layer、几何和语义；临时 crowd obstacle 不写入烘焙输入。
6. estimate、bake、runtime rebuild 使用同一份 source/profile/layer/semantic/obstacle snapshot，并把 hash 写入 artifact manifest。
7. 配置缺字段、路径绝对化、来源冲突、目标瓦片越界和预算超限必须失败；不填默认值掩盖问题。

当前代码中的 `Navigation/navmesh.json` 已经具备 `mode`、`algorithm`、`profiles`、`layers`、`areas` 和 `runtimeIncremental` 的严格校验；正式目标还需要把 board 身份、source 角色、多板寻址和语义 sidecar 纳入同一份声明。

### 3.3 产物与存储

目标路径必须能从 manifest 唯一推导：

```text
assets/Data/Nav/{mapId}/{boardId}/layer{layer}/profile_{profile}/x{cx}/navtile_{cx}_{cy}.ntil
```

每个 artifact 附带：

- `formatVersion`：reader 能识别的格式版本；
- `mapId / boardId / layerId / profileId / tileCoord`：完整身份；
- `sourceRevision / semanticRevision / obstacleRevision`：输入版本；
- `buildHash`：配置、算法能力和输入快照的组合 hash；
- `tileRevision`：瓦片持久版本；`generation`：在途任务代次；store 发布计数只属于当前运行会话，冷启动后重新建立；
- query payload、presentation payload 的派生关系。

`.ntil` 是持久化产物的唯一权威。查询和显示不应各维护一份互不验证的数据；如果显示需要三角形、portal 或 area 信息，应从同一权威产物派生。

### 3.4 编辑器

编辑器不是另一套导航引擎，而是 Core 服务的操作面：

```text
地图选择
  → Board / source / profile / layer / semantic inspector
  → paint or place structural obstacle
  → dirty preview
  → estimate
  → bake / runtime rebake
  → artifact + diagnostics
  → path simulation
  → save and reload
```

编辑器至少需要四个可读面板：

| 面板 | 用户能做什么 | 必须显示什么 |
|---|---|---|
| Source & Board | 选择 board、源、原点和 tile 范围 | source kind、revision、覆盖范围、冲突错误 |
| Navigation Config | 编辑 profile、layer、area 和 runtime tier | 真实配置值、来源、影响范围 |
| Bake | 选择 full/dirty/neighbor、运行 estimate/bake | tile 数、预算、hash、成功/空/失败和失败原因 |
| Path Simulation | 选择 profile/layer，设置起终点 | status、路径点、cost、耗时、tile revision、诊断 |

编辑器的硬边界：

- 不在 Web 端实现 Recast、Detour 或第二套障碍模型。
- 不把 `.ntil` 的显示快照当作可查询的另一种权威格式。
- 不允许编辑器用隐式默认值补全缺配置。
- 保存后必须能冷启动重新加载并继续查询；同一进程内重新读内存对象不算完成。
- 错误要在面板上出现，不能只写后台日志。

### 3.5 Showcase

总演示以“城门落下后，全队沿新路径到达 B 营”为核心。运行时冻结时要显示导航已过期，单位在不安全路段前等待；恢复后发布新路并继续行军。不能将穿门或只更新 revision 当作成功。

具体八步设计、运行中控件和证据要求只在 [NavMesh Showcase 交付](navmesh-features/showcase-delivery.md) 维护；多层、半径、水面、Link 等分别使用各功能页的独立场景，不塞进同一主演示。

## 4. 场景

### 场景 A：作者制作一张陆地地图

作者选择一个 `height` 源，声明一个 grid board、步兵 profile、ground layer 和 road/mud area，运行 estimate 后执行 bake。工具生成带 board 身份和 build hash 的 `.ntil`，启动地图后编辑器和游戏查询使用同一套瓦片。

### 场景 B：同一地图包含陆地和水面

作者声明 ground/water 两个 layer，语义 sidecar 标出海岸和水域，步兵与航船分别绑定 profile。两类单位查询不同 layer；航船不能靠“直线示例”绕过水域 NavMesh 合同。

### 场景 C：多板地图

大陆和港口各自有原点、拓扑和 tile 尺寸。它们的瓦片路径、registry 和查询互不覆盖。板内寻路由 NavMesh 服务完成，跨板连接由更高层 board graph 负责，不把不兼容的板硬塞进一个 Detour 网格。

### 场景 D：运行中落门

门的实体变化产生结构障碍意图，dirty 系统只标记受影响 board-local tiles。worker 使用冻结快照重烤；主线程检查 generation 后原子发布；MassNavigation 刷新路径，单位继续行军。若预算不足或重烤失败，HUD 显示明确状态，不能假装已经绕行。

### 场景 E：作者编辑后重载

作者在编辑器修改区域或障碍，保存配置和产物，关闭并重新启动地图。重新加载时校验 source/build/artifact revision；通过后可继续查询，失败时指出是哪个版本或文件不匹配。

## 5. 边界

### 做什么

- NavMesh 描述结构性可通行空间、area 语义和 profile 约束。
- Runtime rebuild 处理稀疏、持久、改变连通性的结构变化。
- MassNavigation 执行路径并处理临时人群避障。
- Editor/Bridge/CLI 复用同一 Core 服务和配置合同。
- Raylib presentation 读取权威 NavTile 产物显示真实几何和状态。

### 不做什么

- 不把短寿命拥堵、单位互相挤压和局部避让烘焙进 NavMesh。
- 不为 CLI、Bridge、Web、showcase 分别写 bake、query 或 obstacle 管线。
- 不保留 `.ltrn`、旧 CDT、双 payload 或“缺数据就走平地/直线”的兼容旁路。
- 不让纯 Raylib 引擎编辑器和 Ludots 地图编辑器共享一套错误的领域模型；前者编辑 `scene.json`，后者编辑地图与导航声明。
- 不把测试成功、静态截图或门户条目当作可玩 showcase 交付。
- 不把目标产品 schema 中尚未实现的字段写成当前可用命令。

## 6. UAT

以下验收从玩家和作者行为出发，使用 Cucumber BDD 描述。每个场景都需要真实入口、真实状态和故障可见性。

```gherkin
Feature: 作者从地图声明生成可查询的 NavMesh

  Scenario: 同一套配置在 CLI 和编辑器产生同一份瓦片
    Given 作者已声明一个启用导航的 board、source、profile、layer 和障碍
    When 作者分别从 CLI 和 Editor Bridge 执行同一份 estimate 与 bake
    Then 两边报告的 build hash 相同
    And 对应 NavTile 的字节内容相同
    And 任何配置缺失或大小写错误都明确失败

  Scenario: 编辑器保存后冷启动仍能查询
    Given 作者在编辑器中修改地形区域和结构障碍并保存
    When 作者关闭并重新启动地图
    Then 编辑器显示保存后的 source、artifact 和 revision
    And 作者选择起点和终点能得到路径
    And 过期或缺失产物会显示重烤动作，而不是显示一条假路径

  Scenario: 运行中城门落下后小队绕行
    Given 小队正在从 A 营前往 B 营
    When 玩家让城门落下
    Then 受影响瓦片显示为 dirty
    And 重烤完成后 store revision 增加
    And 新路径绕开城门
    And 所有队员沿新路径抵达 B 营

  Scenario: 冻结运行时重烤的消融对照
    Given 玩家已冻结 runtime rebuild
    When 玩家让城门落下并让小队继续行军
    Then revision 保持不变
    And 旧路径仍反映未更新的 NavMesh
    And HUD 明确显示“重烤已冻结”
    And 单位在已关闭的门前等待，不穿过结构障碍

  Scenario: 多板地图不会互相覆盖瓦片
    Given 地图包含一个 grid board 和一个 hex board，且二者原点不同
    When 作者分别烘焙并启动地图
    Then 两块 board 的 artifact 路径和 query registry 身份不同
    And 修改一块 board 不会替换另一块 board 的瓦片

  Scenario: 陆地和水面使用不同导航语义
    Given 作者声明 ground 和 water 两个 layer，并为步兵和航船绑定不同 profile
    When 步兵与航船分别查询到各自目标
    Then 步兵只使用可通行陆地
    And 航船只使用可通行水面
    And 缺少水面语义时系统明确失败，不改走直线

  Scenario: showcase 让新玩家看懂导航变化
    Given 玩家从门户登记的 navmesh runtime showcase 启动地图
    When 玩家按下城门和冻结重烤的操作键
    Then 玩家能看到 dirty tile、revision、路径状态和行军结果
    And 玩家无需阅读日志就能解释“为什么路径改变”
```

UAT 证据至少包括：启动 preset、Agent Bridge `/health` 两次且 `pumpCount` 增长、`session.info` 确认目标 Mod/地图、操作前后日志、状态查询和截图。没有这些证据时，状态只能是“设计完成”或“已实现，待运行验收”。

## 7. 现状和 TODO

### 现状

截至最新 `origin/main` `9231f05fcf`：

- `NavBakeContext`、`NavBakeService`、配置严格校验、Recast 烘焙和 Editor Bridge/CLI 共用链路已经存在。
- `.height/.grid/.hex` 资产命名、连续高度图命名和 CHTM/HEXM magic 已完成迁移。
- 真实 NavTileStore overlay、N 开关、后台障碍重烤和生产帧 NavMesh pass 已进入 main。
- Agent Bridge 已有导航空间探针和查询工具。
- 此前导航专项审计记录为架构回归 91/91 通过（本次文档修改未重跑运行时测试）；`.height` 单瓦片烘焙及走性 PNG 导出通过。
- `PathingConfig` 与 `AutoPathService` 已按 Agent 编译区域代价表；`LogicTerrainCell.Cost`、`NavMeshBakeConfig.Areas[].Cost` 仍是待清理残留。纯 NavMesh 引导仍用首个 Agent 的 query adapter，不能宣称所有入口都按请求身份选择策略，详见 [区域代价现状](navmesh-features/area-costs.md)。
- 当前配置仍由 `NavMeshBakeConfig` 的 `mode/algorithm/profiles/layers/areas/runtimeIncremental` 主导，`NavBakeContext` 仍直接接收 `LogicTerrainField`。
- 多板 registry、board 身份路径、非零 origin、两轴 tile 尺寸和完整 source snapshot 尚未达到目标合同。
- 此前审计的运行时 4 个场景中 2 个通过、2 个失败（本次未重跑）：一个未在固定 tick 窗口内等到 revision，另一个路径绕行通过但小队未全部抵达。
- Web 编辑器仍存在 Artifact 版本选择和 `.ntil`/`detourBase64` 双 payload 边界；统一 `nav bake --map` 作者入口尚未完成。
- `navmesh_runtime_gate_showcase` 和多个 debug showcase 已登记；NavGate 仍处于“已实现，待运行验收”，不能称为可玩交付完成。

### 分支核查（2026-09-08）

这次核查同时覆盖本地分支和已抓取的 `origin/*` 分支。分支指针存在不等于成果已经进入主线；以下状态按“相对最新 `origin/main` 的提交和文件差异”判定。

| 分支 / PR | 与 TODO 的关系 | 结论 | 接收动作 |
|---|---|---|---|
| `nav/asset-ext-rename` / #1400、`nav/continuous-heightmap-naming` / #1397、`nav/wire-magic-migration` / #1401 | 旧资产命名、连续高度图命名、CHTM/HEXM 魔数 | **已进 main**；分支相对 main 无新增提交 | 不再作为待合功能；残留分支清理另行处理 |
| `nav/direct-heightfield-1344` / #1374、`nav/navtilegrid-per-board-1353` / #1362、`nav/bench-64km-1355` / #1367 | 高度场直灌、每板 `NavTileGrid` 声明、64km 基准 | **已进 main**；这些是现状证据，不等于 #1346 的完整 board registry/origin/path 合同 | 保留主线代码和基准，停止重复开发 |
| `codex/nav-domain-unification`、`codex/nav-domain-main-merge`、`navmesh-413-debug-slice` | 早期导航域统一、NavGate/debug view 和主线合并 | **已被 main 吸收或已是 main 的祖先**；远端分支没有相对最新 main 的新增提交 | 不再作为未完成 TODO；只保留主线入口和回归证据 |
| `codex/nav-perf-query-cache-rebake-workers` / #1164（本地同名分支） | TODO 2、6：Detour 查询缓存、并行 worker、generation 防旧结果覆盖 | **分支已实现，未进 main**；PR 仍为 open draft | 以当前 `NavTileStore` 版本合同重整后提取；不要把它写成已交付 |
| `codex/nav-bake-policy`（本地与远端同名） | TODO 2、3、4：board bake policy、连续高度和 authored obstacle 输入、fail-closed CLI/Bridge | **分支有大量实现，未进 main**；同时带有未收口的旧入口和文档/验收产物 | 只提取 `NavBakePolicy`、source/input 合同及严格校验；先做主线 API 对齐，再进入 #1342/#1347/#1348 |
| `codex/nav-authoring-showcase`（本地与远端同名） | TODO 4、8：编辑器直启入口和作者 showcase | **只有分支实现证据，不能算产品交付**；当前 `NavBakeSession` 仍使用 Cdt、内存 `NavTileStore` 和旧 `LogicTerrainDocument` | 可作为体验稿；改为 Core 统一服务、`.ntil` 权威产物和真实 registry 后再接收 |
| `codex/nav-bake-visualization`（本地与远端同名） | TODO 5：Web Nav 面板、Nav overlay、分块流送 | **分支实现证据，不能直接合入**；使用旧 `bake-recast-react`/`map_data.bin` payload，并另起 editor 渲染边界 | 只抽取与当前 Editor Bridge/Core 合同一致的显示适配；拒绝第二套 bake/query 管线 |
| `codex/navmesh-showcase-minimal`、`feat/navmesh-767-convergence` / #742、#846 | TODO 7、8：旧动态 showcase、LayeredSpan/CDT 收敛、旧编辑器材料 | **未合分支均为历史路线**；包含 `.vhtm/.vtxm`、大批重写和旧 CDT/展示边界 | 已被主线成果部分吸收；不回迁分支整体，只按缺口逐项核对 |
| `codex/terr-399-merge-main` / #1006（本地与远端） | TODO 3、7：中立分类、去地形 Cost、分类投影 | **分支已有实现，主线未吸收完整合同**；PR closed 且未合并，`80ddd77b7a` 贯通区域键 | 按 #372/#1345 提取分类与去 Cost 片段，不回迁旧 `.ltrn`/CDT 整包 |
| `nav/bake-island-showcase` | TODO 8 | **只有八步设计文档，没有实现证据** | 作为设计参考；不能标记为 showcase 已交付 |
| `codex/issue-1402`、`codex/issue-1402-core`、`codex/issue-1402-route-init`、`codex/issue-1402-height-ssot` | TODO 1、3、8：非零 origin 查询、`PathDomain.Auto` 路由缝、Raylib 场景容器 | **局部修复/场景基础设施，均未形成 #1402 完整 showcase**；#1402 仍 open | 本地 `5bd22c9e4e` / `1d2eb4a2ec` 另有纯 NavMesh 按 AgentTypeId 选 profile/代价的修复，应接入 #372；origin 修复归 #1346，场景容器不等于完整 UAT |

因此，当前没有找到一个“已经把全部 TODO 做完、只是忘了合并”的本地或远端分支。可提取候选包括 #1164 查询缓存/worker、`codex/nav-bake-policy` 的 source/policy、本地 #1402 的 AgentTypeId 路由修复和 #1006 的分类/去 Cost 片段；这些候选均需按最新 main 重整并补齐主线合同。`codex/nav-authoring-showcase`、`codex/nav-bake-visualization` 和 #1402 分支提供了可复用的体验或适配片段，但都没有达到生产级 SSOT、冷启动加载和真实 UAT 要求。

### TODO 顺序

每个细项对应独立功能文档，编辑器、工具、Runtime、Showcase 的职责及功能点在 [24 项功能目录](navmesh-features/README.md) 中逐项映射。以下 8 组保留交付顺序，具体设计只在功能页维护。

1. **P1：板与尺度（未收口）**，拆为 [每板寻址](navmesh-features/board-addressing.md) 与 [空间尺度](navmesh-features/spatial-scale-and-resolution.md)。#1362 只完成了每板声明；仍需 board identity、origin、两轴 tile 几何、manifest、路径和 registry，并通过 grid+hex 混板及非零 origin 的真实加载验收。尺度 SSOT 已有，但 meters-first 编辑器派生和跨层 owner 约束仍未全收口。
2. **P1：运行时合同（部分完成）**，拆为 [脏区更新](navmesh-features/runtime-dirty-rebake.md) 与 [快照、预算、代次](navmesh-features/runtime-bake-budget.md)。主线已有后台队列和主线程发布；#1164 分支已有并行 worker、generation 防旧结果覆盖和查询缓存，但尚未进入 main。仍需独立 runtime tier、不可变 source/intent snapshot 和完整 NavGate 到达验收。
3. **P1：新源、拓扑与语义（未收口）**，拆为 [拓扑与逻辑地形](navmesh-features/topology-and-logic-terrain.md)、[源接入](navmesh-features/source-and-semantics.md)、[多层/表面](navmesh-features/multi-layer-pathing.md)、[几何约束](navmesh-features/slope-clearance.md)、[结构障碍](navmesh-features/structural-obstacles.md)、[区域代价](navmesh-features/area-costs.md)、[水面](navmesh-features/water-semantics.md) 和 [Link](navmesh-features/navmesh-links.md)。`codex/nav-bake-policy` 有连续高度和 authored obstacle 输入实现，但未合入；仍需完成 `NavBakeSource`、grid/hex adapter、area/water sidecar，并让 CLI、Bridge、runtime 消费同一 source contract。
4. **P1：路由、执行与作者工具链（未收口）**，拆为 [路由域选择](navmesh-features/route-domain-selection.md)、[路线到 MassNavigation 执行](navmesh-features/route-execution.md)、[作者入口](navmesh-features/authoring-toolchain.md)、[profile/半径配置](navmesh-features/agent-profiles.md) 与 [烘焙估算](navmesh-features/bake-estimation-and-memory.md)。现有 CLI/Bridge 共享部分 Core bake 链路；`codex/nav-authoring-showcase` 和 `codex/nav-bake-visualization` 的作者面仍依赖旧 payload/入口。仍需统一 `nav bake --map <id>`、manifest 写入、错误提示、Graph/Mesh/Auto 解释和双 profile 执行验收。
5. **P2：产物与编辑器（部分完成）**，拆为 [产物/Manifest](navmesh-features/artifacts-and-manifest.md)、[投影贴图](navmesh-features/walkability-projection.md) 与 [真实调试显示](navmesh-features/navmesh-debug-view.md)。主线已有 `.ntil` NavTile 和真实 overlay；编辑器仍存在 Artifact 版本选择、`.ntil`/`detourBase64` 双 payload 边界，未证明保存后冷启动可查询。#1402 分支没有补齐这一合同。
6. **P2：查询性能与诊断（分支完成，主线未收口）**，拆为 [缓存](navmesh-features/query-cache.md) 与 [查询诊断](navmesh-features/query-diagnostics.md)。#1164 提供 Detour mesh cache、共享 LoadedVersion、并行 rebake worker 和异步测试修正；PR 仍是 draft，主线当前不能宣称已完成。接收时还需查询缓冲复用、容量合同、失败原因统计和零分配回归。
7. **P2：[旧类型清理](navmesh-features/legacy-retirement.md)（部分完成）**。命名迁移和部分旧 CDT 清理已经进 main；历史分支仍携带 `.vhtm/.vtxm`、旧 CDT 或重复 editor pipeline，不能整体回迁。待所有消费方迁移后再删除 LogicTerrain 持久化旁路和重复入口。
8. **P2：[Showcase 交付](navmesh-features/showcase-delivery.md)（未收口）**。主线已登记 NavGate/debug showcase；`nav/bake-island-showcase` 只有设计，`codex/nav-authoring-showcase`/#1402 只有局部实现。仍需 Agent Bridge 观察→驱动→验证闭环、HUD、故障反馈、消融对照、截图/录屏和门户验收证据。

### 关联入口

- 源架构与总顺序：[#1350](https://github.com/MightyBubble/Ludots/issues/1350)
- 每板寻址：[#1346](https://github.com/MightyBubble/Ludots/issues/1346)
- 运行时重烤：[#1347](https://github.com/MightyBubble/Ludots/issues/1347)
- 作者工具链：[#1348](https://github.com/MightyBubble/Ludots/issues/1348)
- 源适配：[#1342](https://github.com/MightyBubble/Ludots/issues/1342)、[#1356](https://github.com/MightyBubble/Ludots/issues/1356)
- 区域分类与 Agent 成本规则：[ #372](https://github.com/MightyBubble/Ludots/issues/372)；#412 已并入其范围，#479 消费编辑器合同。
- 语义 sidecar：[#1345](https://github.com/MightyBubble/Ludots/issues/1345)
- 查询债务：[#1129](https://github.com/MightyBubble/Ludots/issues/1129)
- 真实显示已交付：[#1021](https://github.com/MightyBubble/Ludots/pull/1021)、[#1352](https://github.com/MightyBubble/Ludots/pull/1352)
- Showcase 登记：[`showcase.registry.json`](../showcase.registry.json)
