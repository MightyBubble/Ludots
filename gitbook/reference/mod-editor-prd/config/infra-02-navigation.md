# infra-02 配置说明 · 导航配置

> 配置写法与行为。第一性需求见 [infra-02 PRD](../prd/infra-02-navigation.md)；编辑器需求见 [UXD](../uxd/infra-02-navigation.md)；现状见 [reference](../reference/infra-02-navigation.md)。

## 1. 示例配置

引擎真实三件（`assets/Navigation/`，节选）：

```json
[
  {
    "id": "Small",
    "radiusCm": 30.0, "heightCm": 180.0,
    "clearanceCm": 40.0, "draftCm": 0.0, "beamCm": 0.0,
    "mass": 1.0, "layer": 0, "navProfileId": "Small"
  }
]
```

```json
{
  "agentTypes": {
    "Humanoid": {
      "profileId": "Small",
      "selection": {
        "mode": "PreferMesh",
        "graphBias": 0.0, "meshBias": 0.0,
        "graphCostWeight": 1.0, "meshCostWeight": 1.0
      },
      "navMesh": { "areaCosts": [ { "areaId": 0, "cost": 1.0 } ] },
      "nodeGraph": { }
    }
  }
}
```

```json
{
  "mode": "offline",
  "algorithm": "recast",
  "profiles": { "Small": { "maxClimbCm": 40, "maxSlopeDeg": 45, "agentProfileId": "Small" } },
  "layers": [ { "id": "Ground", "layer": 0 } ],
  "areas": { "Default": { "areaId": 0, "slopeFree": false } },
  "runtimeIncremental": {
    "tileBudgetPerFixedTick": 1,
    "includeNeighborTiles": true,
    "heightScaleMeters": 1.0,
    "minWalkableUpDot": 0.6,
    "cliffHeightThreshold": 1
  }
}
```

## 2. 字段与行为

| 表 | 字段 | 这样配会产生什么效果 |
|---|---|---|
| agent_profiles | `radiusCm`/`heightCm` | 体型半径与身高，参与网格生成与避让 |
| agent_profiles | `clearanceCm` | 净空需求，通道加宽 |
| agent_profiles | `draftCm`/`beamCm` | 纵向/横向附加体量（载具类） |
| agent_profiles | `mass`/`layer` | 推挤质量与导航层 |
| pathing | `agentTypes.<id>.profileId` | 绑定体型档案 |
| pathing | `selection.mode` + 权重 | 网格/图选路偏好（如 PreferMesh） |
| pathing | `navMesh.areaCosts` | 面积通过代价（泥地贵、大路便宜）；每个 agent type 必须列出每一个已声明区域 |
| pathing | `nodeGraph.*` | 图投影：projectionMaxRadiusCm、useDynamicOverlay、tag 规则 |
| navmesh | `mode`/`algorithm` | offline + recast（现状唯一组合） |
| navmesh | `profiles.<id>.maxClimbCm` / `maxSlopeDeg` | 能力档可爬高度；`maxSlopeDeg` 省略表示不限坡度，显式 0 或负数拒绝，Recast 烘焙要求正数 |
| navmesh | `profiles.<id>.agentProfileId` | 这个能力档烘焙使用的体型，必填 |
| navmesh | `layers`/`areas` | 导航层与面积类型定义 |
| navmesh | `runtimeIncremental.tileBudgetPerFixedTick` | 每固定步瓦片重建预算（增量分摊） |

## 3. 文件结构

`assets/Navigation/` 三件：`agent_profiles.json`（ArrayById）、`pathing.json`（DeepObject，`agentTypes` 以 id 为键）、`navmesh.json`（DeepObject，`profiles` 与 `areas` 以 id 为键）。带导航标签的地图在 `maps.<mapId>.navProfiles` 列出要烘焙的能力档。每个 agent type 的 `areaCosts` 必须覆盖每一个已声明区域。能力档必写 `agentProfileId`。

## 4. 运行时加载效果

档案表先注册（至少一条）；pathing 解析 agentTypes 并校验 profileId；navmesh 挂接烘焙管线（离线模式 + 运行期增量预算）。**生效级别：重启**；navmesh 烘焙产物随地图变化重建。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| agent_profiles 为空 | 启动失败（至少一体型） |
| agentTypes 为空/缺 profileId | 启动失败，指明条目 |
| areaCosts 引用未声明 area，或少了某个已声明区域 | 启动失败 |
| 带导航标签的地图缺 navProfiles，或列出的能力档没有瓦片 | 启动失败 |
| 显式 maxSlopeDeg 为 0 或负数，或 Recast 要烘焙的能力档没有正坡度 | 启动失败 |

## 6. 实例

- `assets/Navigation/agent_profiles.json`（Small/Medium/Large + light/heavy/formation 六档案）
- `assets/Navigation/pathing.json`、`assets/Navigation/navmesh.json`（引擎默认三档案烘焙参数）

**相关文档**：[infra-02 PRD](../prd/infra-02-navigation.md) · [map-01 配置说明](map-01-definition.md)
