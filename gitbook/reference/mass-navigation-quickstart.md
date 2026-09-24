# 给单位加寻路：一页纸

这一页解决一个问题：我有单位模板和一张地图，怎么让它会走路、能被框选、能接受右键移动命令。写完这一页的步骤，不需要读引擎源码。

键的必填性、默认值和废弃状态查 [MassNavigationConfig 键级参考表](mass-navigation-config-keys.md)。集群移动的完整玩法设计（anchor/member、命令链、失败语义）读 [MassNavigation RTS 上手书](mass-navigation-user-book.md)。地图与 board 尺度读 [Mod 作者地图尺度入门](map-scale-authoring-guide.md)。

## 开始前：三条合同

1. **地图必须有 Grid board**。`Maps/<map>.json` 的 `Boards[0]` 声明 `SpatialType: "Grid"` 与 `WidthInMacroTiles / HeightInMacroTiles / GridCellSizeCm / ChunkSizeCells`。board 是世界尺寸与 streaming chunk 尺寸的 SSOT；game.json 的 `worldWidthInMacroTiles / worldHeightInMacroTiles` 只在与 board 一致时才允许出现。
2. **玩家可指挥的单位必须走 GAS 订单**。`massNavigationMove` 订单类型由 `MassNavigationMod/assets/GAS/order_types.json` 定义，依赖 `MassNavigationMod` 即得；自己定义也行，但输入映射要指到同一个 orderType key。
3. **引擎侧系统自动安装**。solver、绑定、移动执行、动画参数桥都由引擎在 `config.mapId` 与当前地图一致时自动装好，不需要写 C# 安装代码。

## 路径 A：叠加基座（推荐）

依赖 `MassNavigationMod`，自己的 `MassNavigationConfig.json` 只写差异键，未写的键按 mod 链 DeepObject 合并继承基座。

最小差异实样：`mods/showcases/capability_standard/CapabilityStandardCrowdPhysicsArenaMod/assets/MassNavigationConfig.json`，11 个键——`mapId`、`world.hotZones`（1 个热区）、`scenario`（规模 + 出生布局）、`presentation.teams`（复用基座 presenter）、`teamRelationships`、`scenarioRuntime.runtimeCapacity.displacedAgentCapacity`。加上 game.json 与模板，一个可玩的 RTS 场景就齐了。

步骤：

1. `mod.json` 的 `dependencies` 加 `"MassNavigationMod": "^1.0.0"`（可参照 CrowdPhysicsArena：`LudotsCoreMod + CoreInputMod + MassNavigationMod`）。
2. 写 `assets/game.json`：`startupMapId` 指向自己的地图，只写与基线不同的键；世界尺寸键不要写，board 是 SSOT。
3. 写 `assets/MassNavigationConfig.json` 差异键：至少 `mapId`（必须等于你的地图 id，运行时靠它激活）；要改规模的加 `scenario`，要换模板的加 `presentation.teams`。
4. 地图 `Maps/<map>.json` 声明 Grid board；`config.mapId` 与地图 `Id` 一致。
5. 单位模板加组件（见下节）。

继承面提醒：叠加继承的是基座 133 键的全部现值（10km solver 窗口、全部 cadence/semantics 常数、10K 大世界的 hotZones 坐标）。改一个键前先查 [键级参考表](mass-navigation-config-keys.md) 里它的现值；与基座不同的数组（如 `teamRelationships.relationships`）是整替不是合并。

## 路径 B：外部 authoring（自管生成，autoSpawn=false）

不想要引擎按 `scenario` 自动铺兵，而是用自己的触发图 / 业务代码生成单位时，把 `scenarioRuntime.autoSpawnConfiguredScenario` 设为 `false`。实样：`mods/showcases/formation_capability/FormationCapabilityShowcaseMod/assets/MassNavigationConfig.json`（20 个差异键，用 `"teams": []` 与 `"spawnLayout": null` 抹掉基座继承值）。

外部路径有两条硬规则：

- `agentsPerTeam = 0` 时没有规模可推导，`runtimeCapacity` 的 `groupMembershipAgentCapacity / groupMemberCapacity / movePlanExecutionMemberCapacity` 必须显式写。
- `autoSpawn = false` 时 `presentation.blockerPresenterId / requiredMeshAssetIds`、`scenario.spawnLayout`、hotspot 两键全部免填；`world.hotZones` 整节可省。

不想继承基座、要一份完全自管的配置时，下面这份独立模板可以直接改。125 个叶子键（数组按 1 计），已用 `MassNavigationConfig.Load` 全量校验通过；分节含义与逐键档位见 [键级参考表](mass-navigation-config-keys.md)。

```json
{
  "mapId": "your_map",
  "world": {
    "commandFocusHoldTicks": 90,
    "workAreaPaddingCm": 4000,
    "workAreaMaxWidthCm": 48000,
    "workAreaMaxHeightCm": 48000
  },
  "solver": {
    "fieldWidthCm": 10000,
    "fieldHeightCm": 10000,
    "flowCellSizeCm": 100,
    "maxObstacleCount": 64,
    "parallelWorkerCount": 8,
    "separationHashCellSizeCm": 100,
    "separationHashMinSearchRadiusCells": 2,
    "hardResolveHashCellSizeCm": 50,
    "hardResolveHashMinSearchRadiusCells": 1,
    "playAreaMinXCm": 50,
    "playAreaMaxXCm": 9950,
    "playAreaMinYCm": 50,
    "playAreaMaxYCm": 9950
  },
  "presentation": {
    "blockerTemplateId": "your_blocker",
    "teams": []
  },
  "scenario": {
    "agentsPerTeam": 0,
    "teams": [{ "id": 1, "name": "Player" }]
  },
  "scenarioRuntime": {
    "autoSpawnConfiguredScenario": false,
    "runtimeCapacity": {
      "navigationGroupCapacity": 512,
      "groupMembershipAgentCapacity": 512,
      "movePlanExecutionGroupCapacity": 512,
      "groupMemberCapacity": 512,
      "movePlanExecutionMemberCapacity": 512,
      "routeStateCapacity": 10000,
      "routeMaxExpandedPerRequest": 2048,
      "routeWaypointCapacityPerAgent": 64,
      "displacedAgentCapacity": 64
    }
  },
  "cadence": {
    "simulationHz": 15,
    "targetUpdateHz": 15,
    "flowStepHz": 5,
    "flowCrowdStampHz": 5,
    "flowObstacleStampHz": 2,
    "hardResolveHz": 10,
    "entitySyncHz": 15,
    "maxStepsPerFixedTick": 1,
    "hardResolveCandidateThresholdAgents": 1
  },
  "agentProfiles": {
    "defaultProfileId": "light",
    "profiles": [
      { "id": "light", "heavy": false, "visualScale": 0.22, "speedCmPerSecond": 800, "everyNth": 0, "nthOffset": 0 }
    ]
  },
  "teamRelationships": { "defaultRelationship": "Hostile", "relationships": [] },
  "relationshipPolicy": { "cooperativeStance": "Friendly" },
  "flow": { "enabled": false, "iterationsPerStep": 4096 },
  "arrival": {
    "enabled": true,
    "timeoutMs": 1500,
    "progressDistanceCm": 60,
    "wakePushDistanceCm": 80,
    "maxRetryCount": 2
  },
  "avoidance": {
    "mode": "Separation",
    "dominantMassRatio": 2.25,
    "friendlyResponseScale": 1.1,
    "friendlyResponseMin": 0.35,
    "friendlyResponseMax": 2.75,
    "nonFriendlyResponseScale": 1.25,
    "nonFriendlyResponseMin": 0.25,
    "nonFriendlyResponseMax": 3.25,
    "dominantPushResponseScale": 1.6,
    "dominantPushResponseMin": 0.15,
    "dominantPushResponseMax": 4.5,
    "friendlyCorrectionShareMin": 0.18,
    "friendlyCorrectionShareMax": 0.82,
    "dominantCorrectionOtherMassWeight": 1.8,
    "dominantCorrectionShareMin": 0.05,
    "dominantCorrectionShareMax": 0.95,
    "nonFriendlyCorrectionOtherMassWeight": 1.2,
    "nonFriendlyCorrectionShareMin": 0.08,
    "nonFriendlyCorrectionShareMax": 0.92
  },
  "semantics": {
    "obstacle": {
      "hardResolveCandidateDistanceCm": 100,
      "softPushPaddingCm": 350,
      "softPushForceScale": 8
    },
    "targetProjection": {
      "teamTargetClearanceCm": 60,
      "groupCenterClearanceCm": 60,
      "teamSlotClearanceCm": 45,
      "groupSlotClearanceCm": 50,
      "looseTargetClearanceCm": 50
    },
    "group": {
      "spawnSpacingCm": 46,
      "spawnJitterCm": 12,
      "teamSlotSpacingCm": 90,
      "pullDeadZoneCm": 50,
      "pullClampCm": 2000,
      "arrivedRadiusCm": 150,
      "groupedAgentArriveThresholdCm": 200,
      "looseArriveThresholdCm": 300,
      "unitTargetStopThresholdCm": 50,
      "groupedAgentFlowSlowRadiusCm": 400,
      "nearSlotBlend": 0.82,
      "farSlotBlend": 0.38,
      "nearSlotBlendDistanceSq": 4000000
    },
    "route": {
      "waypointAdvanceStopThresholdScale": 2,
      "waypointAdvanceBodyRadiusScale": 1.5
    },
    "steering": {
      "separationRadiusCm": 200,
      "goalArrivalRadiusCm": 1200,
      "flowObstacleAvoidanceScale": 1.2,
      "groupedAgentSeparationScale": 2,
      "looseSeparationScale": 4,
      "velocityBlendPerSecond": 5
    },
    "solver": {
      "minNavMass": 0.001,
      "minVisualScale": 0.01,
      "maxStepDtSeconds": 0.05,
      "parallelStepMinAgents": 2048,
      "directionEpsilonSq": 0.0001,
      "normalizationEpsilonSq": 0.000001,
      "inverseSqrtMinValue": 0.00000001,
      "entitySyncPositionEpsilonSq": 0.25,
      "entitySyncVelocityEpsilonSq": 0.01,
      "facingVelocityEpsilonSq": 0.01,
      "flowBlockedCellCost": 99999,
      "flowBlockedCellThreshold": 9999,
      "flowTargetStopDistanceSq": 1,
      "flowObstacleNeighborRadiusCells": 4,
      "flowObstacleNeighborWeight": 5,
      "flowObstacleAvoidanceWeight": 1.5,
      "crowdStampCenterCost": 8,
      "crowdStampNeighborCost": 3,
      "hardResolveMaxSeparatesPerAgentPass": 2,
      "coincidentPairHashBucketCount": 1024,
      "coincidentPairHashPrimeA": 73856093,
      "coincidentPairHashPrimeB": 19349663
    }
  },
  "streaming": { "retainSeconds": 6, "radiusCm": 16000 }
}
```

## 单位模板要加什么组件

寻路单位模板（templates.json）的核心组件，照抄 `MassNavigationMod/assets/Entities/templates.json` 的 agent 模板再改：

```json
{
  "id": "your_agent",
  "components": {
    "WorldPositionCm": { "Value": { "X": 0, "Y": 0 } },
    "OrderBuffer": {},
    "CommandSourceSelectableTag": {},
    "CommandSourceSelectableState": { "IsEnabled": true },
    "GameplayTagContainer": { "tags": ["Unit.Commandable"] },
    "MassNavigationAgent": { "profileId": "light" }
  }
}
```

- `MassNavigationAgent.profileId` 引用 config 的 `agentProfiles.profiles[].id`——字符串约定，写错在运行时才报错，起名保持一致。
- `OrderBuffer + CommandSourceSelectableTag/State + Unit.Commandable` 四件套决定单位能被框选并接收右键命令；纯 AI 驱动、不进玩家 command source 的单位可以不带。
- 想有血条 / HUD / 小地图，参照基座模板补 `AttributeBuffer`、`EntityLayer`、presenter 绑定；引擎的自动安装不依赖这些。

autoSpawn 路径下，模板 id 由 `presentation.teams[]` 的 `lightTemplateId / heavyTemplateId` 按队绑定，presenter id 同理四键对齐——config teams、templates.json、presenters.json 三方 id 要一致，这是叠加路径里剩下的最后一处手工对齐。

## 常见失败对照

| 报错关键字 | 原因 |
| --- | --- |
| `requires explicit '...' property` | 缺必填键。查 [键级参考表](mass-navigation-config-keys.md) 该键的档位与条件。 |
| `must be explicitly configured when the scenario does not auto-spawn agents` | 外部路径没写 agent 档容量（见路径 B 硬规则）。 |
| `conflicts with startup map board` | game.json 世界尺寸键与地图 board 不一致。删键或对齐，board 是 SSOT。 |
| `is smaller than one streaming window chunk count` | 显式 `loadedChunkCapacity` 低于 streaming 窗口在 board chunk 尺寸下的 chunk 数。 |
| `MassNavigation runtime requires board-owned WorldGridLoadedChunks` | 地图没有 Grid board，或 board 声明不是 `SpatialType: "Grid"`。 |
| `unknown` / `Disallow` 反序列化错误 | 键名拼错或写了已删除的键（如 `solverWindowWidthCm`、`streamingChunkSizeCm`），拼错键名会立刻报错，没有静默忽略。 |
