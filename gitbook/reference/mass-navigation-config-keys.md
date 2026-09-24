# MassNavigationConfig 键级参考表

解析与校验的单一来源是 `src/Core/MassNavigation/Runtime/MassNavigationConfig.cs`（必填清单在 `ValidateRequiredTopLevelProperties`，各节取值约束在各 tuning 类的 `Validate`）。基座配置是 `mods/capabilities/navigation/MassNavigationMod/assets/MassNavigationConfig.json`，当前 133 个叶子键（数组按 1 计）。mod 侧同路径文件按 mod 链 DeepObject 合并：标量覆盖、对象递归、**数组整替不合并**。

反序列化是 strict camelCase + `UnmappedMemberHandling.Disallow`：拼错键名、写已删除的键，加载时立刻报错；不存在静默忽略的键。

## 四档定义

| 档位 | 含义 | 不写会怎样 |
| --- | --- | --- |
| 必填 | 无条件必填 | 加载即报 `requires explicit '...' property` |
| 条件 | 特定开关下才必填（autoSpawn / hotZones / avoidance.mode） | 条件成立时同样报错；不成立时写了也合法 |
| 推导 | 可选 override，缺省由引擎推导或取默认 | 引擎按推导规则补值 |
| 废弃 | 已删除或语义已废弃 | 写了直接 Disallow 报错（MassNavigationConfig 键）或启动冲突报错（game.json 键） |

## world（基座 6 键）

| 键 | 档位 | 说明 |
| --- | --- | --- |
| `commandFocusHoldTicks` | 必填 | 命令焦点保持的 tick 数（基座 90） |
| `workAreaPaddingCm` | 必填 | 工作区外扩边距 |
| `workAreaMaxWidthCm` / `workAreaMaxHeightCm` | 必填 | 工作区上限，必须 ≥ solver field 尺寸 |
| `hotZones` | 推导（可选节） | debug 用热点地标；不写整节则无 hotspot。autoSpawn 且有 hotZones 时触发 hotspot 出生与 presenter 需求 |
| `activeHotZoneId` | 条件（hotZones 非空时） | 初始激活热区 id；hotZones 为空时写了反而报错 |

## solver（13 键，全必填）

| 键 | 说明 |
| --- | --- |
| `fieldWidthCm` / `fieldHeightCm` | 流场窗口尺寸，**同时就是 MassNavigationFlow 工作窗口**（单一来源，本分支前在 world 节双写） |
| `flowCellSizeCm` | 流场格边长，必须整除 field |
| `maxObstacleCount` / `parallelWorkerCount` | 障碍上限 / 并行 worker 数 |
| `separationHashCellSizeCm` / `separationHashMinSearchRadiusCells` | 分离 hash 分辨率与最小搜索半径 |
| `hardResolveHashCellSizeCm` / `hardResolveHashMinSearchRadiusCells` | 硬解析 hash 分辨率与最小搜索半径 |
| `playAreaMinXCm` / `playAreaMaxXCm` / `playAreaMinYCm` / `playAreaMaxYCm` | 场内可走区边界；常规写法是 field 内缩一个 agent 半径（基座 50/9950 对 10000 field） |

## presentation（基座 6 键）

| 键 | 档位 | 说明 |
| --- | --- | --- |
| `blockerTemplateId` | 必填 | 障碍实体模板 id |
| `teams` | 必填 | 每队 4 键：`teamId / lightTemplateId / heavyTemplateId / lightPresenterId / heavyPresenterId`。外部路径可写 `[]` 抹掉继承 |
| `blockerPresenterId` | 条件（autoSpawn） | 障碍 presenter id |
| `requiredMeshAssetIds` | 条件（autoSpawn） | mesh 资产 id 数组，≥1 |
| `hotspotPresenterId` / `hotspotTemplateId` | 条件（autoSpawn 且 hotZones 非空） | 热点地标 presenter / 模板 |

## scenario（基座 7 键）

| 键 | 档位 | 说明 |
| --- | --- | --- |
| `agentsPerTeam` | 必填 | 每队自动生成数；0 = 外部 authoring |
| `teams` | 必填 | 每队 `id / name`；team 数 × agentsPerTeam 是规模 SSOT |
| `spawnLayout` | 条件（autoSpawn 时必填，含 `kind / orbitRadiusCm / randomSeed` 3 键） | autoSpawn=false 时可省略或写 null 抹掉继承 |

## scenarioRuntime（基座 10 键）

| 键 | 档位 | 说明 |
| --- | --- | --- |
| `autoSpawnConfiguredScenario` | 必填 | true = 引擎按 scenario 铺兵 |
| `runtimeCapacity.navigationGroupCapacity` | 必填 | 导航组上限 |
| `runtimeCapacity.movePlanExecutionGroupCapacity` | 必填 | 移动计划执行组上限 |
| `runtimeCapacity.routeStateCapacity` / `routeMaxExpandedPerRequest` / `routeWaypointCapacityPerAgent` | 必填 | route 状态 / 单请求展开上限 / 每 agent 航点数 |
| `runtimeCapacity.displacedAgentCapacity` | 必填 | 被挤开 agent 记录上限 |
| `runtimeCapacity.groupMembershipAgentCapacity` / `groupMemberCapacity` / `movePlanExecutionMemberCapacity` | 推导 | 缺省按 team 数 × agentsPerTeam 推导；**外部路径（agentsPerTeam=0）无规模可推导，必须显式写** |
| `runtimeCapacity.relationshipDomainCapacity` | 推导 | 缺省 = team 数 |
| `runtimeCapacity.loadedChunkCapacity` | 推导 | 缺省在 board 绑定期按 streaming 窗口 × board chunk 尺寸推导；显式值低于窗口 chunk 数即拒绝（board 是 chunk 尺寸 SSOT） |

## cadence（10 键）

| 键 | 档位 | 说明 |
| --- | --- | --- |
| `simulationHz` / `targetUpdateHz` / `flowStepHz` / `flowCrowdStampHz` / `flowObstacleStampHz` / `hardResolveHz` / `entitySyncHz` / `maxStepsPerFixedTick` / `hardResolveCandidateThresholdAgents` | 必填 | 各环节调度频率与步数上限（9 键，均有物理含义） |
| `agentSliceCount` | 推导 | 默认 1；agent 档切片数 |

## agentProfiles（基座 2 键）

| 键 | 档位 | 说明 |
| --- | --- | --- |
| `defaultProfileId` | 必填 | 缺省 profile |
| `profiles` | 必填 | 每 profile 6 键：`id / heavy / visualScale / speedCmPerSecond / everyNth / nthOffset`；≥1 个；模板的 `MassNavigationAgent.profileId` 按字符串引用 `id` |

## teamRelationships / relationshipPolicy / streaming（5 键，全必填）

| 键 | 说明 |
| --- | --- |
| `teamRelationships.defaultRelationship` / `relationships` | 默认关系（`relationships` 数组整替：每条 `teamA / teamB / attitude / symmetric`） |
| `relationshipPolicy.cooperativeStance` | 关系域投影的合作立场 |
| `streaming.retainSeconds` / `radiusCm` | streaming 窗口保留秒数与半径；窗口 chunk 数 = 以 board chunk 尺寸折算 radius 的方阵 |

## flow（2 键）/ arrival（5 键），全必填

| 键 | 说明 |
| --- | --- |
| `flow.enabled` / `iterationsPerStep` | 流场开关与每步迭代上限；enabled=false 时整节惰性 |
| `arrival.enabled` / `timeoutMs` / `progressDistanceCm` / `wakePushDistanceCm` / `maxRetryCount` | 到达判定开关、超时、推进距离、唤醒推离、重试上限 |

## avoidance（基座 19 键 = mode + 共享 18）

| 键 | 档位 | 说明 |
| --- | --- | --- |
| `mode` | 必填 | `Separation` / `Orca` / `Sonar` |
| 共享 18 键（`dominantMassRatio`、`friendly/nonFriendly/dominantPush` 各 Scale/Min/Max、`friendly/dominant/nonFriendly` Correction 系列） | 必填 | 分离响应与修正权重，全档位生效 |
| `orca.timeHorizonSeconds` / `orca.maxNeighbors` | 条件（mode=Orca） | 仅 Orca 分支消费 |
| `sonar.*` 8 键（`maxSteerAngleDeg / backwardPenaltyAngleDeg / predictionTimeScale / ignoreBehindMovingAgents / blockedStop / usePreferredVelocityWhenBlocked / timeHorizonSeconds / maxNeighbors`） | 条件（mode=Sonar） | 仅 Sonar 分支消费 |

## semantics（51 键，全必填；引擎常数集中地）

| 子节 | 键数 | 说明 |
| --- | --- | --- |
| `obstacle` | 3 | `hardResolveCandidateDistanceCm / softPushPaddingCm / softPushForceScale` |
| `targetProjection` | 5 | 各类目标投影间距 |
| `group` | 13 | 出生间距、槽位、到达阈值、远近槽混合 |
| `route` | 2 | 航点推进阈值 |
| `steering` | 6 | 分离半径、到达半径、流速混合 |
| `solver` | 22 | 引擎内部常数（epsilon、hash 质数、flow 代价）。通常整段继承基座，不要自己发明值。两个注记：`hardResolveMaxSeparatesPerAgentPass` 缺省会静默变成 0（无上限），保持显式；`facingVelocityEpsilonSq` 当前零运行时消费，属待清理键，在删除 issue 落地前仍必填 |

## 废弃档

| 键 | 状态 | 替代 |
| --- | --- | --- |
| `world.solverWindowWidthCm` / `world.solverWindowHeightCm` | 已删除，写入即 Disallow 报错 | `solver.fieldWidthCm / fieldHeightCm` 单一来源 |
| `world.streamingChunkSizeCm` | 已删除，写入即 Disallow 报错 | 地图 board 的 `ChunkSizeCells × GridCellSizeCm` |
| game.json `worldWidthInMacroTiles` / `worldHeightInMacroTiles` 作为独立声明 | 语义废弃 | 启动地图 board 是世界尺寸 SSOT；game.json 显式值只作一致性校验的对照（不一致启动即报错），无 board 的地图才作为唯一来源 |

## 与其他页的关系

- 三步上手与最小模板：[给单位加寻路：一页纸](mass-navigation-quickstart.md)
- 集群移动玩法与命令链：[MassNavigation RTS 上手书](mass-navigation-user-book.md)
- board 尺度与 streaming 合同：[Mod 作者地图尺度入门](map-scale-authoring-guide.md)
